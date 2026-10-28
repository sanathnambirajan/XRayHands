"""Validate processed specimens: file sizes, meta schema, CSV schema and label consistency.

Usage::  python validate.py                 # every specimen under Assets/StreamingAssets/Specimens
         python validate.py <specimen_dir>  # one specimen

Exit code 0 only if every check passes. Warnings (e.g. defects merged away by max-pool
downsampling) are printed but do not fail validation, because they are expected.
"""
from __future__ import annotations

import csv
import math
import sys
from pathlib import Path

import numpy as np

from common import DEFECT_CLASSES, FEATURE_COLUMNS, META_REQUIRED_KEYS, SPECIMEN_OUT, read_meta, read_raw

GT_STATUSES = {"TP", "FP", "FN", "NA"}


def validate_specimen(folder: Path) -> tuple[list[str], list[str]]:
    """Return ``(errors, warnings)`` for one specimen folder."""
    errors: list[str] = []
    warnings: list[str] = []

    meta_path = folder / "meta.json"
    if not meta_path.exists():
        return [f"{folder.name}: meta.json missing"], warnings
    meta = read_meta(meta_path)

    missing = [k for k in META_REQUIRED_KEYS if k not in meta]
    if missing:
        errors.append(f"meta.json missing keys: {missing}")
        return errors, warnings

    dims = tuple(int(d) for d in meta["dims"])
    if len(dims) != 3 or min(dims) <= 0 or max(dims) > 256:
        errors.append(f"dims {dims} must be 3 positive values <= 256")
    if len(meta["spacing_mm"]) != 3 or min(meta["spacing_mm"]) <= 0:
        errors.append(f"spacing_mm {meta['spacing_mm']} must be 3 positive values")

    n = dims[0] * dims[1] * dims[2]
    for key, bytes_per_voxel in (("volume_file", 1), ("label_file", 2)):
        p = folder / meta[key]
        if not p.exists():
            errors.append(f"{meta[key]} missing")
        elif p.stat().st_size != n * bytes_per_voxel:
            errors.append(f"{meta[key]}: {p.stat().st_size} bytes, expected {n * bytes_per_voxel}")

    mats = meta["materials"]
    if not mats or mats[0]["u8_min"] != 0 or mats[-1]["u8_max"] != 255:
        errors.append("materials must cover 0..255")
    for a, b in zip(mats, mats[1:]):
        if b["u8_min"] != a["u8_max"] + 1:
            errors.append(f"material bins not contiguous: {a['name']} -> {b['name']}")

    rows = []
    csv_path = folder / meta["features_file"]
    if not csv_path.exists():
        errors.append("features.csv missing")
    else:
        with csv_path.open(encoding="utf-8") as fh:
            reader = csv.DictReader(fh)
            if tuple(reader.fieldnames or ()) != FEATURE_COLUMNS:
                errors.append(f"features.csv columns {reader.fieldnames} != {list(FEATURE_COLUMNS)}")
            rows = list(reader)
    if len(rows) != meta["defect_count"]:
        errors.append(f"defect_count {meta['defect_count']} != {len(rows)} CSV rows")

    ids = []
    for r in rows:
        try:
            ids.append(int(r["id"]))
            if r["class"] not in DEFECT_CLASSES:
                errors.append(f"id {r['id']}: unknown class {r['class']!r}")
            if r["gt_status"] not in GT_STATUSES:
                errors.append(f"id {r['id']}: unknown gt_status {r['gt_status']!r}")
            v = float(r["volume_mm3"])
            s = float(r["sphericity"])
            if not (v > 0 and 0 <= s <= 1 and float(r["elongation"]) >= 1 - 1e-6):
                errors.append(f"id {r['id']}: out-of-range volume/sphericity/elongation")
            if not math.isclose(float(r["log10_volume_mm3"]), math.log10(v), abs_tol=1e-4):
                errors.append(f"id {r['id']}: log10_volume_mm3 inconsistent")
        except (KeyError, ValueError) as exc:
            errors.append(f"bad row {r}: {exc}")
            break
    if len(set(ids)) != len(ids):
        errors.append("duplicate ids in features.csv")

    label_path = folder / meta["label_file"]
    if label_path.exists() and not errors:
        labels = read_raw(label_path, dims, np.uint16)
        present = set(np.unique(labels).tolist()) - {0}
        unknown = present - set(ids)
        if unknown:
            errors.append(f"{len(unknown)} label ids in the volume have no CSV row (e.g. {sorted(unknown)[:5]})")
        hidden = set(ids) - present
        if hidden:
            warnings.append(f"{len(hidden)} of {len(ids)} defects not visible after max-pool downsampling "
                            "(merged into a neighbour's block); they stay in the chart")
    return errors, warnings


def main(argv: list[str]) -> int:
    folders = [Path(a) for a in argv] or sorted(p for p in SPECIMEN_OUT.iterdir() if p.is_dir())
    ok = True
    for f in folders:
        errs, warns = validate_specimen(f)
        status = "OK" if not errs else "FAIL"
        print(f"[{status}] {f.name}")
        for e in errs:
            print(f"   error: {e}")
        for w in warns:
            print(f"   warning: {w}")
        ok &= not errs
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
