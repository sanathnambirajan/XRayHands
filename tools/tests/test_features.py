"""Feature extraction must recover known geometry of synthetic shapes."""
import math

import numpy as np
import pytest

from conftest import ellipsoid_mask
from features import (
    classify, compare_to_ground_truth, compute_features, depth_map_mm, elongation,
    equivalent_diameter, sphericity, surface_area_mm2,
)

ISO = (0.01, 0.01, 0.01)  # 10 µm isotropic voxels, typical after NIST downsampling


def test_sphericity_formula_is_one_for_ideal_sphere():
    r = 2.0
    v = 4 / 3 * math.pi * r**3
    a = 4 * math.pi * r**2
    assert sphericity(v, a) == pytest.approx(1.0)


def test_equivalent_diameter_of_sphere():
    assert equivalent_diameter(4 / 3 * math.pi * 1.5**3) == pytest.approx(3.0)


@pytest.mark.parametrize("radius", [8, 12, 20])
def test_voxelised_sphere_volume_and_sphericity(radius):
    shape = (2 * radius + 10,) * 3
    c = (shape[0] // 2,) * 3
    mask = ellipsoid_mask(shape, c, (radius,) * 3)
    labels = mask.astype(np.uint16)
    feats = compute_features(labels, np.zeros(shape, np.uint8), ISO, None)
    assert len(feats) == 1
    f = feats[0]
    true_vol = 4 / 3 * math.pi * (radius * 0.01) ** 3
    assert f.volume_mm3 == pytest.approx(true_vol, rel=0.03)
    assert f.sphericity >= 0.95, f"sphere r={radius} got sphericity {f.sphericity:.3f}"
    assert f.elongation == pytest.approx(1.0, abs=0.05)
    assert f.cls == "gas_pore"


def test_surface_area_of_sphere_within_five_percent():
    r = 20
    shape = (2 * r + 10,) * 3
    mask = ellipsoid_mask(shape, (shape[0] // 2,) * 3, (r,) * 3)
    area = surface_area_mm2(mask, (1.0, 1.0, 1.0))
    assert area == pytest.approx(4 * math.pi * r**2, rel=0.05)


def test_prolate_ellipsoid_elongation_matches_axis_ratio():
    shape = (40, 40, 100)
    mask = ellipsoid_mask(shape, (20, 20, 50), (5, 5, 40))
    labels = mask.astype(np.uint16)
    f = compute_features(labels, np.zeros(shape, np.uint8), (1.0, 1.0, 1.0), None)[0]
    # For a solid ellipsoid the second moments scale with the squared semi-axes,
    # so sqrt(λmax/λmin) equals the semi-axis ratio 40/5 = 8 (minus discretisation).
    assert f.elongation == pytest.approx(8.0, rel=0.1)
    assert f.sphericity < 0.75


def test_flat_disc_is_elongated_even_though_two_axes_are_equal():
    """Cracks are discs: max/min eigenvalue ratio must catch them (max/mid would not)."""
    coords = np.array([[0.0, y, x] for y in range(-20, 21) for x in range(-20, 21) if x * x + y * y <= 400])
    assert elongation(coords, (1.0, 1.0, 1.0)) > 10


@pytest.mark.parametrize(
    "sph, elong, expected",
    [(0.3, 5.0, "crack"), (0.3, 2.0, "lack_of_fusion"), (0.6, 5.0, "lack_of_fusion"), (0.9, 1.1, "gas_pore")],
)
def test_classification_rule(sph, elong, expected):
    assert classify(sph, elong) == expected


def test_depth_is_distance_to_outside():
    obj = np.zeros((40, 40, 40), bool)
    obj[5:35, 5:35, 5:35] = True
    depth = depth_map_mm(obj, (0.1, 0.1, 0.1), factor=1)
    # Centre voxel (20) is 15 voxels from the first outside voxel (index 4): 1.5 mm.
    assert depth[20, 20, 20] == pytest.approx(1.5, abs=0.11)
    assert depth[0, 0, 0] == 0


def test_ground_truth_comparison_counts():
    pred = np.zeros((10, 10, 10), np.uint16)
    gt = np.zeros_like(pred)
    pred[1:3, 1:3, 1:3] = 1  # overlaps gt 1 -> TP
    pred[6:8, 6:8, 6:8] = 2  # no gt -> FP
    gt[2:4, 2:4, 2:4] = 1
    gt[0, 8, 8] = 2  # missed -> FN
    res = compare_to_ground_truth(pred, gt, gt_count=2)
    assert res.tp_pred_ids == {1}
    assert res.matched_gt_ids == {1}
    assert res.precision == pytest.approx(0.5)
    assert res.recall == pytest.approx(0.5)
    inter, union = 1, 8 + 8 + 8 + 1 - 1
    assert res.iou == pytest.approx(inter / union)
