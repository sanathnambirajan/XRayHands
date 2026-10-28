"""Defect and component segmentation at full resolution.

Pore definition (specimen A): a pore is *air enclosed by material*. We threshold material,
fill its holes in 3D, and the difference (filled minus material) is exactly the enclosed
air. Air that touches the outside of the scanned region is not a pore and is excluded
automatically, because ``binary_fill_holes`` only fills cavities with no path to the border.
"""
from __future__ import annotations

import logging

import cc3d
import numpy as np
from scipy import ndimage

from common import MIN_COMPONENT_VOXELS

LOG = logging.getLogger("segment")

CONNECTIVITY = 26  # full 3D neighbourhood: diagonal voxel contacts belong to the same pore


def material_mask(vol: np.ndarray, threshold: float) -> np.ndarray:
    """Voxels brighter than ``threshold`` (attenuating material)."""
    return vol > threshold


def enclosed_air(material: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Return ``(pores, filled)`` where ``filled`` is material with all cavities filled.

    ``filled`` is also the object mask used later for the depth-below-surface distance.
    """
    filled = ndimage.binary_fill_holes(material)
    pores = filled & ~material
    return pores, filled


def label_components(mask: np.ndarray, min_voxels: int = MIN_COMPONENT_VOXELS) -> tuple[np.ndarray, int]:
    """26-connected components of ``mask``; drops components under ``min_voxels``.

    Returns ``(labels, count)`` with labels renumbered 1..count in uint16 (0 = background).
    uint16 halves memory against cc3d's default uint32, which matters at ~900 M voxels;
    a ValueError is raised if a volume ever has more than 65535 components.
    """
    labels = cc3d.connected_components(mask, connectivity=CONNECTIVITY, out_dtype=np.uint32)
    if min_voxels > 1:
        labels = cc3d.dust(labels, threshold=min_voxels, connectivity=CONNECTIVITY, in_place=True)
    labels, count = _relabel_sequential(labels)
    if count > np.iinfo(np.uint16).max:
        raise ValueError(f"{count} components exceed the uint16 label range")
    LOG.info("labelled %d components (min size %d voxels)", count, min_voxels)
    return labels.astype(np.uint16), count


def _relabel_sequential(labels: np.ndarray) -> tuple[np.ndarray, int]:
    ids = np.unique(labels)
    ids = ids[ids != 0]
    if ids.size == 0:
        return np.zeros_like(labels), 0
    lut = np.zeros(int(ids.max()) + 1, dtype=labels.dtype)
    lut[ids] = np.arange(1, ids.size + 1, dtype=labels.dtype)
    return lut[labels], int(ids.size)


def multi_threshold_components(
    vol: np.ndarray, thresholds: list[float], min_voxels: int
) -> tuple[np.ndarray, int, np.ndarray]:
    """Specimen B: connected components per attenuation bin.

    Voxels are binned by ``thresholds`` (bin 0 = air is ignored); each bin is labelled
    separately so a steel pin touching a brass contact stays two components.
    Returns ``(labels, count, bin_of_label)`` where ``bin_of_label[i]`` is the bin of label i.
    """
    bins = np.digitize(vol, thresholds).astype(np.uint8)
    out = np.zeros(vol.shape, dtype=np.uint16)
    bin_of_label = [0]
    next_id = 1
    for b in range(1, len(thresholds) + 1):
        lab, n = label_components(bins == b, min_voxels=min_voxels)
        if n == 0:
            continue
        if next_id + n - 1 > np.iinfo(np.uint16).max:
            raise ValueError("too many components for uint16 labels")
        sel = lab > 0
        out[sel] = lab[sel] + (next_id - 1)
        bin_of_label.extend([b] * n)
        next_id += n
    return out, next_id - 1, np.asarray(bin_of_label, dtype=np.uint8)
