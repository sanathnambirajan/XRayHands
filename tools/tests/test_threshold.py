"""ISO50 threshold on a noisy metal block with sparse pores.

The real-data reason for ISO50 (Otsu produced >10^5 false pores on the NIST crop) is
recorded in docs/DATA.md with the measured numbers; synthetic data is too clean to
reproduce that failure, so it is not asserted here.
"""
import numpy as np

from conftest import ellipsoid_mask
from preprocess import iso50_threshold


def _sparse_pores(seed=0):
    rng = np.random.default_rng(seed)
    shape = (64, 64, 64)
    vol = rng.normal(50000, 1500, shape)  # noisy metal
    for c in [(16, 16, 16), (40, 40, 20), (30, 50, 45), (50, 15, 50)]:
        vol[ellipsoid_mask(shape, c, (4, 4, 4))] = rng.normal(20000, 1500, (1,))[0]
    return vol.astype(np.uint16)


def test_iso50_sits_between_air_and_metal():
    vol = _sparse_pores()
    thr, air, metal = iso50_threshold(vol)
    assert 15000 < air < 25000
    assert 48000 < metal < 52000
    assert abs(thr - 0.5 * (air + metal)) < 1e-6
    # Almost no metal voxel falls below the threshold: noise is not segmented as pores.
    pores = vol < thr
    true = np.zeros_like(pores)
    for c in [(16, 16, 16), (40, 40, 20), (30, 50, 45), (50, 15, 50)]:
        true |= ellipsoid_mask(vol.shape, c, (4, 4, 4))
    assert (pores & ~true).sum() < 10
