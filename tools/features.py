"""Per-defect shape features, classification and ground-truth comparison.

Every feature is computed at full resolution in physical units (mm), before any
downsampling, so the chart shows the true geometry rather than display-resolution blobs.
"""
from __future__ import annotations

import logging
import math
from dataclasses import dataclass

import numpy as np
from scipy import ndimage
from skimage.measure import marching_cubes, mesh_surface_area

LOG = logging.getLogger("features")

# Classification thresholds (prompt section 3). Kept as named constants so the rule is
# visible in one place and quoted verbatim in docs/DATA.md.
CRACK_MAX_SPHERICITY = 0.5
CRACK_MIN_ELONGATION = 3.0
LACK_OF_FUSION_MAX_SPHERICITY = 0.75

# Smoothing before marching cubes. A binary voxel mask has a staircase surface whose area
# is ~1.5x the true area of the smooth object it samples (the "Manhattan" overestimate);
# a light Gaussian blur turns the staircase into a smooth iso-surface at the 0.5 level.
SURFACE_SMOOTHING_SIGMA_VOXELS = 1.0
SURFACE_PAD_VOXELS = 3


@dataclass
class DefectFeatures:
    """All per-defect values written to ``features.csv`` (units in field names)."""

    id: int
    voxel_count: int
    volume_mm3: float
    eq_diameter_mm: float
    surface_mm2: float
    sphericity: float
    elongation: float
    centroid_mm: tuple[float, float, float]  # (x, y, z)
    depth_mm: float
    bbox_min_mm: tuple[float, float, float]
    bbox_max_mm: tuple[float, float, float]
    mean_density_u8: float
    cls: str = ""
    material_bin: int = 0  # specimen B: attenuation bin the component was segmented from
    gt_status: str = "NA"


def equivalent_diameter(volume_mm3: float) -> float:
    """Diameter of the sphere with the same volume: d = (6V/π)^(1/3)."""
    return (6.0 * volume_mm3 / math.pi) ** (1.0 / 3.0)


def sphericity(volume_mm3: float, surface_mm2: float) -> float:
    """Wadell sphericity ψ = π^(1/3) (6V)^(2/3) / A. Equals 1 for a perfect sphere."""
    if surface_mm2 <= 0:
        return 0.0
    psi = math.pi ** (1.0 / 3.0) * (6.0 * volume_mm3) ** (2.0 / 3.0) / surface_mm2
    # Discretisation can push tiny defects slightly above 1; ψ > 1 is not physical.
    return float(min(psi, 1.0))


def surface_area_mm2(mask: np.ndarray, spacing_zyx: tuple[float, float, float]) -> float:
    """Surface area of a binary object via smoothed marching cubes (see module constants)."""
    padded = np.pad(mask.astype(np.float32), SURFACE_PAD_VOXELS)
    smooth = ndimage.gaussian_filter(padded, SURFACE_SMOOTHING_SIGMA_VOXELS)
    if smooth.max() < 0.5:
        # Object is so thin the blur never reaches the iso-level; fall back to the raw mask.
        smooth = padded
    verts, faces, _, _ = marching_cubes(smooth, level=0.5, spacing=spacing_zyx)
    return float(mesh_surface_area(verts, faces))


def elongation(coords_mm: np.ndarray, spacing_zyx: tuple[float, float, float]) -> float:
    """sqrt(λ_max / λ_min) of the second-moment (inertia) tensor of the voxel cloud.

    Why max/min and not max/mid: cracks are flat *discs* (two large axes, one tiny), so
    max/mid ≈ 1 would miss them; max/min is large for both needles and discs.
    Each voxel is a cube, not a point, so we add its own variance s²/12 per axis;
    otherwise a one-voxel-thick plate would have λ_min = 0 and infinite elongation.
    """
    cov = np.cov(coords_mm, rowvar=False) if coords_mm.shape[0] > 1 else np.zeros((3, 3))
    cov = cov + np.diag([s * s / 12.0 for s in spacing_zyx])
    eig = np.linalg.eigvalsh(cov)
    return float(math.sqrt(eig[-1] / eig[0]))


def classify(sph: float, elong: float) -> str:
    """Prompt rule: crack = ψ<0.5 and elongation>3; lack-of-fusion = ψ<0.75; else gas pore."""
    if sph < CRACK_MAX_SPHERICITY and elong > CRACK_MIN_ELONGATION:
        return "crack"
    if sph < LACK_OF_FUSION_MAX_SPHERICITY:
        return "lack_of_fusion"
    return "gas_pore"


def depth_map_mm(object_mask: np.ndarray, spacing_zyx: tuple[float, float, float], factor: int) -> np.ndarray:
    """Distance (mm) from each voxel to the outside of ``object_mask``, on a coarse grid.

    The Euclidean distance transform at ~900 M voxels needs ~7 GB of float64, so it runs on
    the mask decimated by ``factor``. The error is bounded by about one coarse voxel
    (factor × spacing), which is far below the depth range the chart shows.
    """
    coarse = object_mask[::factor, ::factor, ::factor]
    # Pad with background so the scan border counts as "outside" for the distance.
    padded = np.pad(coarse, 1, constant_values=False)
    edt = ndimage.distance_transform_edt(padded, sampling=[s * factor for s in spacing_zyx])
    return edt[1:-1, 1:-1, 1:-1].astype(np.float32)


def compute_features(
    labels: np.ndarray,
    intensity_u8: np.ndarray,
    spacing_zyx: tuple[float, float, float],
    depth_coarse_mm: np.ndarray | None,
    depth_factor: int = 1,
) -> list[DefectFeatures]:
    """Compute :class:`DefectFeatures` for every label > 0 in ``labels``."""
    sz, sy, sx = spacing_zyx
    voxel_mm3 = sx * sy * sz
    out: list[DefectFeatures] = []
    for idx, sl in enumerate(ndimage.find_objects(labels), start=1):
        if sl is None:
            continue
        mask = labels[sl] == idx
        n = int(mask.sum())
        if n == 0:
            continue
        zz, yy, xx = np.nonzero(mask)
        z0, y0, x0 = (s.start for s in sl)
        coords_mm = np.column_stack(((zz + z0 + 0.5) * sz, (yy + y0 + 0.5) * sy, (xx + x0 + 0.5) * sx))

        vol = n * voxel_mm3
        area = surface_area_mm2(mask, spacing_zyx)
        sph = sphericity(vol, area)
        elong = elongation(coords_mm, spacing_zyx)
        cz, cy, cx = coords_mm.mean(axis=0)

        if depth_coarse_mm is not None:
            gz = np.minimum((zz + z0) // depth_factor, depth_coarse_mm.shape[0] - 1)
            gy = np.minimum((yy + y0) // depth_factor, depth_coarse_mm.shape[1] - 1)
            gx = np.minimum((xx + x0) // depth_factor, depth_coarse_mm.shape[2] - 1)
            # Closest approach to the surface: the most critical point of the defect.
            depth = float(depth_coarse_mm[gz, gy, gx].min())
        else:
            depth = float("nan")

        out.append(DefectFeatures(
            id=idx,
            voxel_count=n,
            volume_mm3=vol,
            eq_diameter_mm=equivalent_diameter(vol),
            surface_mm2=area,
            sphericity=sph,
            elongation=elong,
            centroid_mm=(float(cx), float(cy), float(cz)),
            depth_mm=depth,
            bbox_min_mm=(x0 * sx, y0 * sy, z0 * sz),
            bbox_max_mm=(sl[2].stop * sx, sl[1].stop * sy, sl[0].stop * sz),
            mean_density_u8=float(intensity_u8[sl][mask].mean()),
            cls=classify(sph, elong),
        ))
    LOG.info("computed features for %d defects", len(out))
    return out


@dataclass
class GroundTruthResult:
    """Object- and voxel-level agreement between our pores and a reference segmentation."""

    precision: float
    recall: float
    iou: float
    tp_pred_ids: set[int]
    matched_gt_ids: set[int]
    gt_count: int


def compare_to_ground_truth(pred: np.ndarray, gt: np.ndarray, gt_count: int) -> GroundTruthResult:
    """Compare predicted pore labels with reference pore labels.

    * Object level: a predicted defect is a true positive if it overlaps any reference
      defect by at least one voxel; a reference defect with no overlap is a false negative.
      precision = TP_pred / N_pred, recall = matched_ref / N_ref.
    * Voxel level: IoU = |P ∩ G| / |P ∪ G| over all pore voxels.
    """
    p = pred > 0
    g = gt > 0
    inter = p & g
    union = int((p | g).sum())
    iou = float(inter.sum()) / union if union else 1.0

    # Pairs (pred id, gt id) that share at least one voxel, via a combined 32-bit key.
    keys = np.unique(pred[inter].astype(np.uint32) << 16 | gt[inter].astype(np.uint32))
    tp_pred = {int(k >> 16) for k in keys}
    matched_gt = {int(k & 0xFFFF) for k in keys}
    n_pred = int(pred.max()) if pred.size else 0
    precision = len(tp_pred) / n_pred if n_pred else 0.0
    recall = len(matched_gt) / gt_count if gt_count else 0.0
    return GroundTruthResult(precision, recall, iou, tp_pred, matched_gt, gt_count)


def features_to_rows(features: list[DefectFeatures]) -> list[dict]:
    """Flatten features into dicts keyed by ``common.FEATURE_COLUMNS``."""
    rows = []
    for f in features:
        rows.append({
            "id": f.id, "class": f.cls, "voxel_count": f.voxel_count,
            "volume_mm3": f.volume_mm3, "log10_volume_mm3": math.log10(f.volume_mm3),
            "eq_diameter_mm": f.eq_diameter_mm, "surface_mm2": f.surface_mm2,
            "sphericity": f.sphericity, "elongation": f.elongation,
            "centroid_x_mm": f.centroid_mm[0], "centroid_y_mm": f.centroid_mm[1], "centroid_z_mm": f.centroid_mm[2],
            "depth_mm": f.depth_mm,
            "bbox_min_x_mm": f.bbox_min_mm[0], "bbox_min_y_mm": f.bbox_min_mm[1], "bbox_min_z_mm": f.bbox_min_mm[2],
            "bbox_max_x_mm": f.bbox_max_mm[0], "bbox_max_y_mm": f.bbox_max_mm[1], "bbox_max_z_mm": f.bbox_max_mm[2],
            "mean_density_u8": f.mean_density_u8, "material_bin": f.material_bin, "gt_status": f.gt_status,
        })
    return rows
