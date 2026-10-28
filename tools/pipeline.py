"""Build a processed specimen (volume, labels, features, meta) from raw CT data.

Order of operations matters and is the same for both specimens:

1. load at full resolution
2. segment and measure at full resolution (small pores only exist here)
3. crop to the object, window to 8 bit
4. downsample intensities by area-mean and labels by max-pool to <= 256 voxels per axis
5. write ``volume_u8.raw``, ``labels_u16.raw``, ``features.csv``, ``meta.json``

Usage::

    python pipeline.py nist --sample set1sample4
    python pipeline.py enigma
"""
from __future__ import annotations

import argparse
import csv
import gc
import logging
import math
from pathlib import Path

import numpy as np
from scipy import ndimage
from skimage.filters import threshold_multiotsu

import features as feat
import preprocess as pre
import segment as seg
from common import (
    CLASS_COLORS, DATA_RAW, FEATURE_COLUMNS, FEATURES_FILE, LABEL_FILE, META_FILE, META_SCHEMA_VERSION,
    MIN_COMPONENT_VOXELS, SPECIMEN_OUT, TARGET_LONGEST_AXIS_VOXELS, VOLUME_FILE, write_meta, write_raw,
)

LOG = logging.getLogger("pipeline")

DEPTH_MAP_FACTOR = 4  # EDT runs on a 4x decimated mask (see features.depth_map_mm)
# Specimens are shown at a comfortable hand-held size; the label always shows the true
# magnification relative to the physical object, so nothing about scale is hidden.
DEFAULT_DISPLAY_SIZE_M = 0.25
NIST_CROP_VOXELS = 640
NIST_MEDIAN_SIZE = 3
# Field-of-view centre (x, y) in pixels, NIST README Table 3 "X/Y coordinate".
NIST_FOV_CENTER_XY = {"set1sample2": (491, 506), "set1sample3": (496, 507), "set1sample4": (492, 506),
                      "set1sample5": (494, 506), "set1sample6": (492, 507)}
ENIGMA_MATERIAL_BINS = 4  # air + three attenuation bins (multi-Otsu)
ENIGMA_MIN_COMPONENT_VOXELS = 64  # ~0.38 mm³ at 0.181 mm; below that, parts are scan noise


def _write_specimen(out_dir: Path, vol_u8: np.ndarray, labels: np.ndarray, rows: list[dict], meta: dict) -> None:
    write_raw(out_dir / VOLUME_FILE, vol_u8)
    write_raw(out_dir / LABEL_FILE, labels.astype(np.uint16))
    with (out_dir / FEATURES_FILE).open("w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=FEATURE_COLUMNS)
        w.writeheader()
        for r in rows:
            w.writerow({k: (f"{v:.6g}" if isinstance(v, float) else v) for k, v in r.items()})
    write_meta(out_dir / META_FILE, meta)
    LOG.info("wrote specimen to %s", out_dir)


def _display_block(dims_xyz: list[int], spacing_ds: list[float]) -> dict:
    extent_m = max(d * s for d, s in zip(dims_xyz, spacing_ds)) / 1000.0
    default_scale = DEFAULT_DISPLAY_SIZE_M / extent_m
    return {
        "physical_extent_m": extent_m,
        "default_scale": default_scale,
        # Grab/scale gesture range: from half the default up to 20x the default.
        "min_scale": max(1.0, default_scale * 0.5),
        "max_scale": default_scale * 20.0,
    }


def _crop_downsample(vol_u8, labels, filled, spacing_zyx):
    bbox = pre.object_bbox(filled)
    vol_u8, labels = vol_u8[bbox], labels[bbox]
    f = pre.downsample_factor(vol_u8.shape, TARGET_LONGEST_AXIS_VOXELS)
    vol_ds = pre.downsample_mean(vol_u8, f)
    lab_ds = pre.downsample_max(labels, f)
    offset_mm = [bbox[2].start * spacing_zyx[2], bbox[1].start * spacing_zyx[1], bbox[0].start * spacing_zyx[0]]
    return vol_ds, lab_ds, f, offset_mm


# --------------------------------------------------------------------------- specimen A

def nist_crop(sample: str, n_slices: int = 900) -> tuple[slice, slice, slice]:
    """Central NIST_CROP_VOXELS³ cube around the field-of-view centre (README Table 3).

    Why a crop: the full ~900 M-voxel stack plus masks and labels does not fit in 16 GB RAM
    at full resolution, and the prompt requires segmenting at full resolution. The cube
    lies entirely inside the circular FOV (inscribed square side = 964/√2 ≈ 681 px).
    """
    cx, cy = NIST_FOV_CENTER_XY[sample]
    h = NIST_CROP_VOXELS // 2
    z0 = (n_slices - NIST_CROP_VOXELS) // 2
    return slice(z0, z0 + NIST_CROP_VOXELS), slice(cy - h, cy + h), slice(cx - h, cx + h)


def build_nist(sample: str) -> Path:
    raw_zip = DATA_RAW / "nist" / f"{sample}raw.zip"
    gt_dir = DATA_RAW / "nist" / f"seg{sample[-1]}"
    voxel_um = {"set1sample2": 2.45, "set1sample3": 2.77, "set1sample4": 2.43,
                "set1sample5": 2.52, "set1sample6": 2.77}[sample]  # NIST README, Table 1
    s = voxel_um / 1000.0
    spacing = (s, s, s)
    n_slices = 750 if sample == "set1sample5" else 900  # NIST README, Table 1
    crop = nist_crop(sample, n_slices)
    LOG.info("full-resolution crop (z, y, x): %s", crop)

    vol = pre.load_tiff_stack_from_zip(raw_zip, crop=crop)
    # Raw XCT noise makes thresholding produce ~10^5 one-voxel "pores". NIST's own reference
    # segmentation used a 3D median filter first (README); we do the same. A median keeps
    # pore edges sharp, unlike a Gaussian, which would shrink small pores.
    vol = ndimage.median_filter(vol, size=NIST_MEDIAN_SIZE)
    LOG.info("applied %d^3 median filter", NIST_MEDIAN_SIZE)
    # The reconstruction is only valid inside the circular field of view; outside it the
    # values are zero. Thresholds and windows are computed on in-FOV voxels only.
    fov = vol > 0
    thr, air_value, material_value = pre.iso50_threshold(vol)
    LOG.info("ISO50 threshold %.1f (air %.1f, material %.1f)", thr, air_value, material_value)
    material = vol > thr
    pores, filled = seg.enclosed_air(material)
    del material
    gc.collect()
    low, high = pre.window_from_percentiles(vol, filled)
    vol_u8 = pre.to_u8(vol, low, high)
    del vol, fov
    gc.collect()

    labels, n_pred = seg.label_components(pores)
    del pores
    gc.collect()
    depth = feat.depth_map_mm(filled, spacing, DEPTH_MAP_FACTOR)
    defects = feat.compute_features(labels, vol_u8, spacing, depth, DEPTH_MAP_FACTOR)

    # Ground truth: NIST reference segmentation, pores defined exactly like ours.
    gt_material = pre.load_tiff_stack(gt_dir, dtype=np.uint8, crop=crop) == 255
    gt_pores, _ = seg.enclosed_air(gt_material)
    del gt_material
    gc.collect()
    gt_labels, n_gt = seg.label_components(gt_pores)
    del gt_pores
    gc.collect()
    gt = feat.compare_to_ground_truth(labels, gt_labels, n_gt)
    for d in defects:
        d.gt_status = "TP" if d.id in gt.tp_pred_ids else "FP"

    # Missed reference pores become extra labels so the shader can show them in GT mode.
    missed = [i for i in range(1, n_gt + 1) if i not in gt.matched_gt_ids]
    if missed:
        lut = np.zeros(n_gt + 1, dtype=np.uint16)
        lut[missed] = np.arange(n_pred + 1, n_pred + 1 + len(missed), dtype=np.uint16)
        fn_labels = lut[gt_labels]
        free = (labels == 0) & (fn_labels > 0)
        fn_only = np.zeros_like(labels)
        fn_only[free] = fn_labels[free]
        labels[free] = fn_labels[free]
        fn_feats = feat.compute_features(fn_only, vol_u8, spacing, depth, DEPTH_MAP_FACTOR)
        for d in fn_feats:
            d.gt_status = "FN"
        defects.extend(fn_feats)
        del fn_labels, fn_only, free
    del gt_labels, depth
    gc.collect()

    vol_ds, lab_ds, f, offset_mm = _crop_downsample(vol_u8, labels, filled, spacing)
    for d in defects:  # express positions relative to the cropped volume origin
        d.centroid_mm = tuple(c - o for c, o in zip(d.centroid_mm, offset_mm))
        d.bbox_min_mm = tuple(c - o for c, o in zip(d.bbox_min_mm, offset_mm))
        d.bbox_max_mm = tuple(c - o for c, o in zip(d.bbox_max_mm, offset_mm))
    dims = [vol_ds.shape[2], vol_ds.shape[1], vol_ds.shape[0]]
    spacing_ds = [s * f] * 3
    thr_u8 = int(np.clip((thr - low) * 255.0 / (high - low), 0, 255))

    meta = {
        "schema_version": META_SCHEMA_VERSION,
        "id": f"nist_{sample}",
        "name": f"CoCr L-PBF sample, central {NIST_CROP_VOXELS * s:.2f} mm cube ({sample}, NIST)",
        "dims": dims,
        "spacing_mm": spacing_ds,
        "volume_file": VOLUME_FILE, "label_file": LABEL_FILE, "features_file": FEATURES_FILE,
        "value_range": {"raw_low": low, "raw_high": high, "unit": "XCT grey value (16-bit, uncalibrated)"},
        "window": {"level": 0.5, "width": 1.0},
        "materials": [
            {"name": "air / pore", "u8_min": 0, "u8_max": thr_u8, "color": "#1A2230"},
            {"name": "CoCr alloy", "u8_min": thr_u8 + 1, "u8_max": 255, "color": "#B8C4CF"},
        ],
        "defect_count": len(defects),
        "defect_classes": CLASS_COLORS,
        "chart_axes": [
            {"column": "log10_volume_mm3", "label": "log10 pore volume (mm³)"},
            {"column": "sphericity", "label": "sphericity"},
            # Interior scan: there is no outer part surface in the data, so "depth below
            # surface" is undefined. Build height (z = build direction, NIST README) is the
            # physically meaningful third axis for L-PBF defects (layer-dependent porosity).
            {"column": "centroid_z_mm", "label": "build height in crop (mm)"},
        ],
        "ground_truth": {
            "precision": gt.precision, "recall": gt.recall, "iou": gt.iou,
            "reference_count": n_gt, "predicted_count": n_pred, "missed_count": len(missed),
            "match_rule": "object TP if it shares >= 1 voxel with a reference pore; IoU over all pore voxels",
        },
        "display": _display_block(dims, spacing_ds),
        "source": {
            "title": "High-Resolution X-ray computed tomography (XCT) image data set of additively manufactured "
                     "cobalt chrome samples produced with varying laser powder bed fusion processing parameters",
            "doi": "10.18434/M32162",
            "url": "https://data.nist.gov/od/id/mds2-2162",
            "files": [f"raw/{sample}raw.zip", f"segmented/{sample}segmented.zip"],
        },
        "license": "NIST open license, https://www.nist.gov/open/license",
        "attribution": "National Institute of Standards and Technology (NIST), mds2-2162; modified (cropped, "
                       "windowed, downsampled, re-segmented) by Group 07 for X-Ray Hands.",
        "processing": {
            "full_res_voxel_mm": s,
            "full_res_crop_zyx": [[c.start, c.stop] for c in crop],
            "material_threshold_raw": thr,
            "threshold_method": "ISO50: halfway between air (median of darkest 0.3 %) and material (median) grey value",
            "air_value_raw": air_value, "material_value_raw": material_value,
            "denoise": f"{NIST_MEDIAN_SIZE}x{NIST_MEDIAN_SIZE}x{NIST_MEDIAN_SIZE} median filter before thresholding "
                       "(as in the NIST reference pipeline); the displayed volume is the filtered one",
            "pore_rule": "air enclosed by material (3D hole filling), 26-connected",
            "min_component_voxels": MIN_COMPONENT_VOXELS,
            "downsample_factor": f,
            "intensity_downsample": "area mean", "label_downsample": "max pool",
            "depth_note": "interior (local) tomography + central crop: depth_mm is distance to the crop "
                          "boundary, not to the part surface, so it is not charted",
            "cut_pores": "pores touching the crop boundary are not enclosed and are excluded",
        },
    }
    out = SPECIMEN_OUT / meta["id"]
    _write_specimen(out, vol_ds, lab_ds, feat.features_to_rows(defects), meta)
    return out


# --------------------------------------------------------------------------- specimen B

def build_enigma() -> Path:
    vol, (sx, sy, sz) = pre.load_dicom(DATA_RAW / "enigma" / "rotors")
    spacing = (sz, sy, sx)
    thr_air = pre.otsu_threshold(vol)
    filled = vol > thr_air
    low, high = pre.window_from_percentiles(vol, filled)
    vol_u8 = pre.to_u8(vol, low, high)
    del vol
    gc.collect()

    sample = vol_u8[::2, ::2, ::2]
    thresholds = [float(t) for t in threshold_multiotsu(sample, classes=ENIGMA_MATERIAL_BINS)]
    LOG.info("multi-Otsu thresholds (u8): %s", thresholds)
    labels, n, bin_of_label = seg.multi_threshold_components(vol_u8, thresholds, ENIGMA_MIN_COMPONENT_VOXELS)
    depth = feat.depth_map_mm(filled, spacing, 2)
    parts = feat.compute_features(labels, vol_u8, spacing, depth, 2)
    for p in parts:
        p.cls = "component"
        p.material_bin = int(bin_of_label[p.id])

    vol_ds, lab_ds, f, offset_mm = _crop_downsample(vol_u8, labels, filled, spacing)
    for p in parts:
        p.centroid_mm = tuple(c - o for c, o in zip(p.centroid_mm, offset_mm))
        p.bbox_min_mm = tuple(c - o for c, o in zip(p.bbox_min_mm, offset_mm))
        p.bbox_max_mm = tuple(c - o for c, o in zip(p.bbox_max_mm, offset_mm))
    dims = [vol_ds.shape[2], vol_ds.shape[1], vol_ds.shape[0]]
    spacing_ds = [sx * f, sy * f, sz * f]
    edges = [0, *[int(t) for t in thresholds], 255]
    bin_names = ["air", "low attenuation", "medium attenuation", "high attenuation"]
    bin_colors = ["#1A2230", "#7C8C9C", "#B8C4CF", "#E0A64A"]

    meta = {
        "schema_version": META_SCHEMA_VERSION,
        "id": "enigma_1993_522_rotors",
        "name": "Enigma I rotors (Deutsches Museum Inv. 1993-522)",
        "dims": dims,
        "spacing_mm": spacing_ds,
        "volume_file": VOLUME_FILE, "label_file": LABEL_FILE, "features_file": FEATURES_FILE,
        "value_range": {"raw_low": low, "raw_high": high, "unit": "CT grey value (16-bit, uncalibrated)"},
        "window": {"level": 0.5, "width": 1.0},
        # Bins are named by attenuation, not by material: we cannot verify which alloy or
        # polymer each part is made of from grey values alone.
        "materials": [
            {"name": bin_names[i], "u8_min": edges[i] + (1 if i else 0), "u8_max": edges[i + 1], "color": bin_colors[i]}
            for i in range(ENIGMA_MATERIAL_BINS)
        ],
        "defect_count": len(parts),
        "defect_classes": CLASS_COLORS,
        "chart_axes": [
            {"column": "log10_volume_mm3", "label": "log10 part volume (mm³)"},
            {"column": "elongation", "label": "elongation"},
            {"column": "mean_density_u8", "label": "mean attenuation (8-bit)"},
        ],
        "ground_truth": None,
        "display": _display_block(dims, spacing_ds),
        "source": {
            "title": "Cipher Machine Enigma I (Inv. 1993-522)",
            "url": "https://zenodo.org/records/8232721",
            "files": ["1993-522_ROI_rotors.dcm.zip"],
        },
        "license": "CC BY-SA 4.0 (record metadata lists CC BY 4.0; treated as BY-SA to be safe)",
        "attribution": "Deutsches Museum, Inv. 1993-522; CT by Fraunhofer IIS/EZRT; 3D-Cipher project "
                       "(BMBF eHeritage). Cropped, windowed, downsampled and segmented by Group 07.",
        "processing": {
            "full_res_voxel_mm": [sx, sy, sz],
            "air_threshold_raw": thr_air,
            "material_thresholds_u8": thresholds,
            "component_rule": "26-connected components per multi-Otsu attenuation bin",
            "min_component_voxels": ENIGMA_MIN_COMPONENT_VOXELS,
            "downsample_factor": f,
            "intensity_downsample": "area mean", "label_downsample": "max pool",
        },
    }
    out = SPECIMEN_OUT / meta["id"]
    _write_specimen(out, vol_ds, lab_ds, feat.features_to_rows(parts), meta)
    return out


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("specimen", choices=["nist", "enigma"])
    p.add_argument("--sample", default="set1sample4")
    a = p.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    out = build_nist(a.sample) if a.specimen == "nist" else build_enigma()
    LOG.info("done: %s", out)


if __name__ == "__main__":
    main()
