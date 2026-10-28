"""Rank the NIST samples by pore population using their reference segmentations.

The prompt asks for "the sample with the richest pore population". We measure it on the
NIST segmented stacks (255 = material, 0 = air / outside the circular field of view):
pores are air enclosed by material, counted as 26-connected components of >= 8 voxels.

Usage::  python inspect_ground_truth.py   (expects data_raw/nist/seg<N>/ from the zips)
"""
from __future__ import annotations

import gc
import json
import logging

import numpy as np

from common import DATA_RAW
from preprocess import load_tiff_stack
from segment import enclosed_air, label_components

LOG = logging.getLogger("inspect_ground_truth")
RANKING_STEP = 2


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    results = {}
    for n in (2, 3, 4, 5, 6):
        folder = DATA_RAW / "nist" / f"seg{n}"
        if not folder.exists():
            LOG.warning("missing %s", folder)
            continue
        # 2x decimation: ranking only needs relative pore counts, and the full stack does not
        # fit next to the Editor in 16 GB RAM. One decimated voxel ~ 8 full-res voxels, so
        # min size 1 here corresponds to the 8-voxel noise rule at full resolution.
        material = load_tiff_stack(folder, dtype=np.uint8, step=RANKING_STEP) == 255
        pores, filled = enclosed_air(material)
        del material
        gc.collect()
        labels, count = label_components(pores, min_voxels=1)
        sizes = np.bincount(labels.ravel())[1:]
        results[f"set1sample{n}"] = {
            "pores": int(count),
            "porosity_percent": float(100.0 * pores.sum() / max(filled.sum(), 1)),
            "largest_pore_voxels": int(sizes.max()) if sizes.size else 0,
            "median_pore_voxels": float(np.median(sizes)) if sizes.size else 0.0,
            "pores_over_125_voxels": int((sizes > 125).sum()),  # ~1000 full-res voxels
        }
        LOG.info("set1sample%d: %s", n, results[f"set1sample{n}"])
        del pores, filled, labels, sizes
        gc.collect()
    out = DATA_RAW / "nist" / "ground_truth_ranking.json"
    out.write_text(json.dumps(results, indent=2))
    print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
