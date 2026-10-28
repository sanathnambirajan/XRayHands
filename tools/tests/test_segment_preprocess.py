"""Segmentation and preprocessing on a synthetic porous block with known answers."""
import numpy as np
import pytest

from preprocess import downsample_factor, downsample_max, downsample_mean, object_bbox, otsu_threshold, to_u8
from segment import enclosed_air, label_components, material_mask, multi_threshold_components


def test_pores_are_enclosed_air_only(porous_block):
    material = material_mask(porous_block, otsu_threshold(porous_block))
    pores, filled = enclosed_air(material)
    labels, count = label_components(pores)
    # sphere + ellipsoid survive; the 1-voxel speck is dust; the open notch is not enclosed.
    assert count == 2
    assert labels.dtype == np.uint16
    assert not pores[0:20, 60:66, 60:66].any(), "surface-breaking notch must not be a pore"
    assert filled[40, 40, 30], "filled object mask must include the pore interior"


def test_label_ids_are_sequential(porous_block):
    material = material_mask(porous_block, 100)
    pores, _ = enclosed_air(material)
    labels, count = label_components(pores)
    assert set(np.unique(labels)) == set(range(count + 1))


def test_crop_bbox_contains_object_with_margin(porous_block):
    bbox = object_bbox(porous_block > 100, margin=2)
    assert bbox == (slice(6, 74), slice(6, 74), slice(6, 114))


def test_downsample_factor_targets_longest_axis():
    assert downsample_factor((900, 1010, 984), 256) == 4
    assert downsample_factor((100, 100, 100), 256) == 1


def test_mean_downsample_preserves_mean_and_shape():
    vol = np.random.default_rng(0).integers(0, 255, (64, 64, 64), dtype=np.uint8)
    out = downsample_mean(vol, 4)
    assert out.shape == (16, 16, 16)
    assert out.mean() == pytest.approx(vol.mean(), abs=0.6)


def test_max_downsample_keeps_tiny_defects():
    labels = np.zeros((32, 32, 32), np.uint16)
    labels[5, 5, 5] = 7  # single-voxel defect
    out = downsample_max(labels, 4)
    assert out.shape == (8, 8, 8)
    assert out[1, 1, 1] == 7


def test_downsample_handles_non_multiple_shapes():
    out = downsample_mean(np.ones((10, 9, 7), np.uint8) * 100, 4)
    assert out.shape == (3, 3, 2)
    assert (out == 100).all(), "edge padding must not darken border blocks"


def test_window_mapping_clamps():
    vol = np.array([[[0, 50, 100, 150]]], dtype=np.uint16)
    out = to_u8(vol, 50, 150)
    assert out.tolist() == [[[0, 0, 128, 255]]] or out.tolist() == [[[0, 0, 127, 255]]]


def test_multi_threshold_components_keep_touching_materials_apart():
    vol = np.zeros((10, 10, 20), np.uint8)
    vol[2:8, 2:8, 2:10] = 100  # medium material
    vol[2:8, 2:8, 10:18] = 200  # dense material touching it
    labels, count, bins = multi_threshold_components(vol, [50, 150], min_voxels=8)
    assert count == 2
    assert sorted(bins[1:].tolist()) == [1, 2]
