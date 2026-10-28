"""Make the pipeline modules in tools/ importable from the tests and provide synthetic volumes."""
import sys
from pathlib import Path

import numpy as np
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))


def ellipsoid_mask(shape, center, radii):
    """Boolean [z, y, x] mask of an axis-aligned ellipsoid; radii in voxels (rz, ry, rx)."""
    z, y, x = np.ogrid[: shape[0], : shape[1], : shape[2]]
    cz, cy, cx = center
    rz, ry, rx = radii
    return ((z - cz) / rz) ** 2 + ((y - cy) / ry) ** 2 + ((x - cx) / rx) ** 2 <= 1.0


@pytest.fixture
def porous_block():
    """A 'metal' block (value 200) in air (value 20) containing three known pores.

    Pores: sphere r=10, prolate ellipsoid (16, 4, 4), tiny 1-voxel speck (noise, must be dropped),
    plus one surface-breaking notch that touches the outside air (must NOT count as a pore).
    """
    shape = (80, 80, 120)
    vol = np.full(shape, 20, dtype=np.uint16)
    vol[8:72, 8:72, 8:112] = 200
    vol[ellipsoid_mask(shape, (40, 40, 30), (10, 10, 10))] = 20
    vol[ellipsoid_mask(shape, (40, 40, 80), (4, 4, 16))] = 20
    vol[40, 20, 100] = 20  # 1-voxel speck
    vol[0:20, 60:66, 60:66] = 20  # notch open to the top face
    return vol
