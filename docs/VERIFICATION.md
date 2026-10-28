# Verification log

Only things that were actually run are listed. Screenshots were taken through MCP for Unity
from the Main Camera in Play mode, and inspected by eye.

## 2026-10-28

**Automated**
- `tools/`: 28 pytest tests pass (synthetic spheres/ellipsoids: volume within 3 %,
  sphericity ≥ 0.95 for r = 8/12/20 voxels, elongation of a 40:5 ellipsoid within 10 %,
  disc-shaped cracks caught, ISO50 on noisy metal with sparse pores, schema/size/layout checks).
- `validate.py`: both specimens OK (Enigma: 98 of 790 parts not visible after max-pool,
  expected and reported).
- Unity EditMode: 38/38 pass (curl round trip, palm normals for both hands, pinch, every
  detector positive + near-miss + negative, hysteresis, jitter does not toggle, hold times,
  recognizer flat-palm → fist transition).
- Console: 0 errors, 0 warnings after every compile.

**Visual (screenshots)**
| State | Result |
|---|---|
| Default view, specimen A | metal block with class-coloured pores throughout ✓ |
| Cut plane through centre, normal away from user | near half removed, face shows greyscale CT slice with tinted pores ✓; **finding:** face too bright with 0..1 window → added `VolumeDataset.AutoWindow()` (1st–99.5th percentile of material, air included) |
| Peel 0.6 | metal translucent, pore cloud visible ✓ |
| Extract → chart | dots land, size ∝ volume, colour = class; physically plausible trend (larger pores less spherical) ✓ |
| Focus largest pore | white dot + dashed link line to the defect + tooltip ✓ |
| Ground-truth mode | TP blue / FP orange / FN magenta; label "precision 0.90 recall 0.75 IoU 0.59" ✓ |

**Findings and fixes**
1. First screenshot empty: taken before the async load finished (gradient bake of 9.8 M
   voxels takes a few seconds in the editor). Not a bug; scripts now wait for `Dataset != null`.
2. Play mode does not advance while the editor window is unfocused, even with
   `runInBackground`; automated runs bring the window to the front first.
3. Chart axis labels overlapped and wrapped → titles at axis midpoints, ticks at ends, no wrap.
4. Small pores (8–30 voxels) saturate at sphericity 1.0: surface area cannot be resolved at
   that size. Kept (honest) and noted here; candidate fix: grey out ψ for pores < 64 voxels.

**Not yet verified:** gesture-driven end-to-end runs (PlayMode timelines), stereo/SPI
rendering, lens visual, probe audio, Enigma visuals, any on-device behaviour.
