"""End-to-end: run the pipeline steps on a synthetic block, write a specimen, validate it."""
import csv

import numpy as np

import features as feat
import preprocess as pre
import segment as seg
from common import FEATURE_COLUMNS, read_raw
from pipeline import _display_block, _write_specimen
from validate import validate_specimen


def _build(tmp_path, porous_block):
    spacing = (0.01, 0.01, 0.01)
    thr = pre.otsu_threshold(porous_block)
    pores, filled = seg.enclosed_air(seg.material_mask(porous_block, thr))
    vol_u8 = pre.to_u8(porous_block, 0, 255)
    labels, _ = seg.label_components(pores)
    defects = feat.compute_features(labels, vol_u8, spacing, feat.depth_map_mm(filled, spacing, 1))
    f = 2
    vol_ds, lab_ds = pre.downsample_mean(vol_u8, f), pre.downsample_max(labels, f)
    dims = [vol_ds.shape[2], vol_ds.shape[1], vol_ds.shape[0]]
    meta = {
        "schema_version": 1, "id": "synthetic", "name": "synthetic", "dims": dims,
        "spacing_mm": [0.02] * 3, "volume_file": "volume_u8.raw", "label_file": "labels_u16.raw",
        "features_file": "features.csv", "value_range": {}, "window": {"level": 0.5, "width": 1.0},
        "materials": [{"name": "air", "u8_min": 0, "u8_max": 99}, {"name": "metal", "u8_min": 100, "u8_max": 255}],
        "defect_count": len(defects), "chart_axes": [], "source": {}, "license": "test",
        "attribution": "test", "processing": {}, "display": _display_block(dims, [0.02] * 3),
    }
    out = tmp_path / "synthetic"
    _write_specimen(out, vol_ds, lab_ds, feat.features_to_rows(defects), meta)
    return out, dims


def test_written_specimen_validates(tmp_path, porous_block):
    out, dims = _build(tmp_path, porous_block)
    errors, _ = validate_specimen(out)
    assert errors == []
    with (out / "features.csv").open() as fh:
        rows = list(csv.DictReader(fh))
    assert len(rows) == 2
    assert tuple(rows[0].keys()) == FEATURE_COLUMNS


def test_raw_layout_is_x_fastest(tmp_path, porous_block):
    out, dims = _build(tmp_path, porous_block)
    raw = np.fromfile(out / "volume_u8.raw", dtype=np.uint8)
    vol = read_raw(out / "volume_u8.raw", tuple(dims), np.uint8)
    # Element at x=1, y=0, z=0 must be the second byte in the file (Unity Texture3D order).
    assert raw[1] == vol[0, 0, 1]
    assert raw[dims[0]] == vol[0, 1, 0]


def test_validator_catches_size_mismatch(tmp_path, porous_block):
    out, _ = _build(tmp_path, porous_block)
    with (out / "volume_u8.raw").open("ab") as fh:
        fh.write(b"\0")
    errors, _ = validate_specimen(out)
    assert any("volume_u8.raw" in e for e in errors)


def test_display_scale_shows_true_magnification():
    d = _display_block([256, 256, 256], [0.006, 0.006, 0.006])  # 1.536 mm object
    assert d["physical_extent_m"] == np.float64(0.001536) or abs(d["physical_extent_m"] - 0.001536) < 1e-9
    assert abs(d["default_scale"] - 0.25 / 0.001536) < 1e-6
