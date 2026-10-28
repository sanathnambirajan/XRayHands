"""Shared constants, file formats and schema definitions for the X-Ray Hands data pipeline.

Array convention used everywhere in ``tools/``: numpy arrays are indexed ``[z, y, x]``
and written in C order, so in the ``.raw`` files **x varies fastest**, then y, then z.
That is exactly the order Unity's ``Texture3D.SetPixelData`` expects, so the C# loader
can upload the bytes without reshuffling.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np

REPO_ROOT = Path(__file__).resolve().parent.parent
DATA_RAW = REPO_ROOT / "data_raw"
# Processed specimens live in StreamingAssets so the Unity app can load them at runtime
# on both PC and Quest (Android reads StreamingAssets through UnityWebRequest).
SPECIMEN_OUT = REPO_ROOT / "Assets" / "StreamingAssets" / "Specimens"

META_SCHEMA_VERSION = 1
VOLUME_FILE = "volume_u8.raw"
LABEL_FILE = "labels_u16.raw"
FEATURES_FILE = "features.csv"
META_FILE = "meta.json"

TARGET_LONGEST_AXIS_VOXELS = 256
MIN_COMPONENT_VOXELS = 8  # components smaller than this are treated as noise (documented in docs/DATA.md)

# Defect class names. Order matters: the index is stored in the shader's class LUT.
DEFECT_CLASSES = ("gas_pore", "lack_of_fusion", "crack", "component")
CLASS_COLORS = {
    "gas_pore": "#FFDF8A",
    "lack_of_fusion": "#FF9A3D",
    "crack": "#FF5546",
    "component": "#9FB4C8",
}

FEATURE_COLUMNS = (
    "id", "class", "voxel_count", "volume_mm3", "log10_volume_mm3", "eq_diameter_mm",
    "surface_mm2", "sphericity", "elongation",
    "centroid_x_mm", "centroid_y_mm", "centroid_z_mm", "depth_mm",
    "bbox_min_x_mm", "bbox_min_y_mm", "bbox_min_z_mm",
    "bbox_max_x_mm", "bbox_max_y_mm", "bbox_max_z_mm",
    "mean_density_u8", "material_bin", "gt_status",
)

META_REQUIRED_KEYS = (
    "schema_version", "id", "name", "dims", "spacing_mm", "volume_file", "label_file",
    "features_file", "value_range", "window", "materials", "defect_count", "chart_axes",
    "source", "license", "attribution", "processing",
)


def write_raw(path: Path, array: np.ndarray) -> None:
    """Write ``array`` (indexed [z, y, x]) as a headerless little-endian C-order file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    np.ascontiguousarray(array).astype(array.dtype.newbyteorder("<"), copy=False).tofile(path)


def read_raw(path: Path, dims_xyz: tuple[int, int, int], dtype: np.dtype) -> np.ndarray:
    """Read a ``.raw`` file written by :func:`write_raw` back into a [z, y, x] array."""
    nx, ny, nz = dims_xyz
    data = np.fromfile(path, dtype=np.dtype(dtype).newbyteorder("<"))
    expected = nx * ny * nz
    if data.size != expected:
        raise ValueError(f"{path.name}: expected {expected} values for dims {dims_xyz}, found {data.size}")
    return data.reshape(nz, ny, nx)


def write_meta(path: Path, meta: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding="utf-8")


def read_meta(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))
