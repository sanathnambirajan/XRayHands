"""Load CT stacks, crop to the object, pick a display window and downsample.

All functions are pure array transforms (no file I/O except the loaders) so they can be
unit-tested on synthetic volumes. ``pipeline.py`` wires them together per specimen.
"""
from __future__ import annotations

import logging
import math
from pathlib import Path

import numpy as np
from skimage.filters import threshold_otsu

LOG = logging.getLogger("preprocess")

# Display window from intensity percentiles of the object voxels: robust to the few
# extremely bright reconstruction artefacts that a plain min/max window would chase.
WINDOW_LOW_PERCENTILE = 0.5
WINDOW_HIGH_PERCENTILE = 99.8
CROP_MARGIN_VOXELS = 4
HISTOGRAM_SAMPLE_STRIDE = 4  # sample every 4th voxel per axis for histogram stats (64x fewer voxels)


# --------------------------------------------------------------------------- loaders

def load_tiff_stack(
    folder: Path,
    dtype: np.dtype = np.uint16,
    step: int = 1,
    crop: tuple[slice, slice, slice] | None = None,
) -> np.ndarray:
    """Load a folder of single-slice TIFFs into a [z, y, x] array.

    Slices are sorted by the trailing number in the file name (NIST naming: ``...-<n>.tif``,
    higher n = towards the top of the sample), not lexicographically, so slice 10 follows 9.
    ``step`` decimates all three axes (for quick statistics); ``crop`` selects a sub-volume
    while reading, so the full stack never has to fit in memory.
    """
    import re

    import tifffile

    files = [p for p in folder.rglob("*") if p.suffix.lower() in (".tif", ".tiff")]
    if not files:
        raise FileNotFoundError(f"no TIFF files under {folder}")

    def slice_index(p: Path) -> int:
        m = re.search(r"(\d+)(?!.*\d)", p.stem)
        return int(m.group(1)) if m else 0

    files.sort(key=slice_index)
    zs, ys, xs = crop if crop else (slice(None), slice(None), slice(None))
    files = files[zs][::step]
    first = tifffile.imread(files[0])[ys, xs][::step, ::step]
    vol = np.empty((len(files), *first.shape), dtype=dtype)
    for i, f in enumerate(files):
        vol[i] = tifffile.imread(f)[ys, xs][::step, ::step]
    LOG.info("loaded %s from %s (step %d)", vol.shape, folder, step)
    return vol


def load_tiff_stack_from_zip(
    zip_path: Path,
    dtype: np.dtype = np.uint16,
    crop: tuple[slice, slice, slice] | None = None,
) -> np.ndarray:
    """Like :func:`load_tiff_stack` but reads slices straight out of the zip.

    Avoids extracting ~1.8 GB of TIFFs to disk (the dev laptop has <20 GB free).
    """
    import io
    import re
    import zipfile

    import tifffile

    def slice_index(name: str) -> int:
        m = re.search(r"(\d+)(?!.*\d)", Path(name).stem)
        return int(m.group(1)) if m else 0

    with zipfile.ZipFile(zip_path) as zf:
        names = sorted((n for n in zf.namelist() if n.lower().endswith((".tif", ".tiff"))), key=slice_index)
        if not names:
            raise FileNotFoundError(f"no TIFF files in {zip_path}")
        zs, ys, xs = crop if crop else (slice(None), slice(None), slice(None))
        names = names[zs]
        first = tifffile.imread(io.BytesIO(zf.read(names[0])))[ys, xs]
        vol = np.empty((len(names), *first.shape), dtype=dtype)
        for i, n in enumerate(names):
            vol[i] = tifffile.imread(io.BytesIO(zf.read(n)))[ys, xs]
    LOG.info("loaded %d slices of %s from %s", len(names), first.shape, zip_path.name)
    return vol


def load_dicom(folder: Path) -> tuple[np.ndarray, tuple[float, float, float]]:
    """Load a DICOM volume (multi-frame file or one file per slice).

    Returns the [z, y, x] array and the voxel spacing (sx, sy, sz) in mm read from the headers.
    """
    import pydicom

    files = sorted(p for p in folder.rglob("*") if p.is_file() and p.suffix.lower() in (".dcm", ""))
    datasets = []
    for f in files:
        try:
            datasets.append(pydicom.dcmread(f))
        except Exception:  # noqa: BLE001 - non-DICOM files in the zip are skipped on purpose
            continue
    if not datasets:
        raise FileNotFoundError(f"no DICOM files under {folder}")

    if len(datasets) == 1 and int(getattr(datasets[0], "NumberOfFrames", 1)) > 1:
        ds = datasets[0]
        vol = ds.pixel_array
        sy, sx = (float(v) for v in _pixel_spacing(ds))
        sz = _slice_thickness(ds, default=sx)
    else:
        datasets.sort(key=lambda d: float(getattr(d, "ImagePositionPatient", [0, 0, getattr(d, "InstanceNumber", 0)])[2]))
        vol = np.stack([d.pixel_array for d in datasets])
        sy, sx = (float(v) for v in _pixel_spacing(datasets[0]))
        if len(datasets) > 1 and hasattr(datasets[0], "ImagePositionPatient"):
            sz = abs(float(datasets[1].ImagePositionPatient[2]) - float(datasets[0].ImagePositionPatient[2])) or sx
        else:
            sz = _slice_thickness(datasets[0], default=sx)
    LOG.info("loaded DICOM volume %s, spacing %.4f/%.4f/%.4f mm", vol.shape, sx, sy, sz)
    return vol, (sx, sy, sz)


def _pixel_spacing(ds) -> tuple[float, float]:
    if hasattr(ds, "PixelSpacing"):
        return ds.PixelSpacing
    shared = getattr(ds, "SharedFunctionalGroupsSequence", None)
    if shared:
        return shared[0].PixelMeasuresSequence[0].PixelSpacing
    raise ValueError("DICOM has no PixelSpacing")


def _slice_thickness(ds, default: float) -> float:
    if hasattr(ds, "SpacingBetweenSlices"):
        return float(ds.SpacingBetweenSlices)
    if hasattr(ds, "SliceThickness"):
        return float(ds.SliceThickness)
    shared = getattr(ds, "SharedFunctionalGroupsSequence", None)
    if shared:
        pm = shared[0].PixelMeasuresSequence[0]
        if hasattr(pm, "SpacingBetweenSlices"):
            return float(pm.SpacingBetweenSlices)
        if hasattr(pm, "SliceThickness"):
            return float(pm.SliceThickness)
    return default


# --------------------------------------------------------------------------- transforms

def otsu_threshold(vol: np.ndarray) -> float:
    """Otsu air/material threshold on a strided sample of the volume (fast, same answer)."""
    s = HISTOGRAM_SAMPLE_STRIDE
    return float(threshold_otsu(vol[::s, ::s, ::s]))


ISO50_AIR_FRACTION = 0.003


def iso50_threshold(vol: np.ndarray, air_fraction: float = ISO50_AIR_FRACTION) -> tuple[float, float, float]:
    """ISO50 surface threshold for a volume that is mostly material with sparse pores.

    ISO50 (standard surface determination in industrial CT) places the material/air
    boundary halfway between the air and the material grey values. Here the material value
    is the median voxel and the air value is the median of the darkest ``air_fraction`` of
    voxels (the pore interiors). Otsu cannot be used on such data: with <1 % pores the
    histogram is unimodal and Otsu splits the metal's own noise in two.

    Returns ``(threshold, air_value, material_value)``.
    """
    s = HISTOGRAM_SAMPLE_STRIDE // 2  # denser sample: the air tail is tiny
    sample = vol[::s, ::s, ::s].ravel()
    material = float(np.median(sample))
    cutoff = np.percentile(sample, 100.0 * air_fraction)
    air = float(np.median(sample[sample <= cutoff]))
    return 0.5 * (air + material), air, material


def object_bbox(mask: np.ndarray, margin: int = CROP_MARGIN_VOXELS) -> tuple[slice, slice, slice]:
    """Bounding box of the True voxels of ``mask`` plus ``margin`` voxels, clamped to the array."""
    if not mask.any():
        raise ValueError("object mask is empty; threshold is wrong")
    slices = []
    for axis in range(3):
        other = tuple(a for a in range(3) if a != axis)
        hit = np.flatnonzero(mask.any(axis=other))
        lo = max(int(hit[0]) - margin, 0)
        hi = min(int(hit[-1]) + margin + 1, mask.shape[axis])
        slices.append(slice(lo, hi))
    return tuple(slices)  # type: ignore[return-value]


def window_from_percentiles(vol: np.ndarray, object_mask: np.ndarray | None = None) -> tuple[float, float]:
    """Return (low, high) raw intensities for the display window.

    Uses percentiles of the object voxels when a mask is given, otherwise of the whole volume.
    """
    s = HISTOGRAM_SAMPLE_STRIDE
    sample = vol[::s, ::s, ::s]
    if object_mask is not None:
        sample = sample[object_mask[::s, ::s, ::s]]
    lo, hi = np.percentile(sample, [WINDOW_LOW_PERCENTILE, WINDOW_HIGH_PERCENTILE])
    if hi <= lo:
        hi = lo + 1.0
    return float(lo), float(hi)


def to_u8(vol: np.ndarray, low: float, high: float) -> np.ndarray:
    """Linearly map [low, high] to [0, 255] with clamping. Processes slab-wise to bound memory."""
    out = np.empty(vol.shape, dtype=np.uint8)
    scale = 255.0 / (high - low)
    for z in range(vol.shape[0]):
        out[z] = np.clip((vol[z].astype(np.float32) - low) * scale, 0, 255).astype(np.uint8)
    return out


def downsample_factor(shape: tuple[int, ...], target: int) -> int:
    """Smallest integer factor that brings the longest axis to <= ``target`` voxels."""
    return max(1, math.ceil(max(shape) / target))


def _pad_to_multiple(vol: np.ndarray, f: int, mode: str) -> np.ndarray:
    pad = [(0, (-n) % f) for n in vol.shape]
    if not any(p[1] for p in pad):
        return vol
    return np.pad(vol, pad, mode=mode)


def downsample_mean(vol: np.ndarray, f: int) -> np.ndarray:
    """Area-mean (box filter) downsampling by integer factor ``f`` on all axes.

    Area-mean is the correct anti-aliasing filter for intensities: each output voxel is the
    average attenuation of the physical region it covers. Edge blocks are padded by
    replicating the border so they are not darkened.
    """
    if f == 1:
        return vol.copy()
    v = _pad_to_multiple(vol, f, mode="edge")
    nz, ny, nx = (n // f for n in v.shape)
    out = np.empty((nz, ny, nx), dtype=np.float32)
    # Slab-wise over z to keep peak memory at one slab of float32.
    for z in range(nz):
        block = v[z * f:(z + 1) * f].astype(np.float32)
        out[z] = block.reshape(f, ny, f, nx, f).mean(axis=(0, 2, 4))
    return np.clip(np.rint(out), 0, np.iinfo(vol.dtype).max if vol.dtype.kind in "ui" else None).astype(vol.dtype)


def downsample_max(labels: np.ndarray, f: int) -> np.ndarray:
    """Max-pool downsampling for label volumes.

    Why max and not mean or nearest: a 20 µm pore covers only a few full-res voxels; nearest
    sampling would drop most of them and averaging label IDs is meaningless. Max-pool keeps
    every defect that touches a block (if two defects share a block, the higher ID wins,
    which is acceptable at display resolution).
    """
    if f == 1:
        return labels.copy()
    v = _pad_to_multiple(labels, f, mode="constant")
    nz, ny, nx = (n // f for n in v.shape)
    out = np.empty((nz, ny, nx), dtype=labels.dtype)
    for z in range(nz):
        block = v[z * f:(z + 1) * f]
        out[z] = block.reshape(f, ny, f, nx, f).max(axis=(0, 2, 4))
    return out
