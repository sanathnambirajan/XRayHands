# Data pipeline

All code lives in `tools/` (Python 3.11, `tools/.venv`). Run order:

```
python fetch_data.py nist-segmented          # 5 reference segmentations, ~78 MB, SHA-256 verified
python inspect_ground_truth.py               # rank samples by pore population
python fetch_data.py nist-raw --sample set1sample2
python fetch_data.py enigma
python pipeline.py nist --sample set1sample2
python pipeline.py enigma
python validate.py                           # schema + consistency checks
python -m pytest -q                          # 28 tests
```

Output per specimen (`Assets/StreamingAssets/Specimens/<id>/`): `volume_u8.raw`,
`labels_u16.raw` (both headerless, x fastest = Unity `Texture3D` order), `features.csv`,
`meta.json`.

## Choosing the NIST sample

The brief asks for "the sample with the richest pore population". We ranked all five
samples on their **reference** segmentations (2× decimated so it fits in RAM; one
decimated voxel ≈ 8 full-res voxels, matching the noise rule). Results
(`data_raw/nist/ground_truth_ranking.json`):

| sample | pores | porosity | pores > ~1000 full-res voxels | median size |
|---|---|---|---|---|
| set1sample2 | 9 936 | **0.91 %** | **1 564** | 27 |
| set1sample3 | 12 625 | 0.23 % | 224 | 2 |
| set1sample4 | 2 341 | 0.34 % | 446 | 25 |
| set1sample5 | 4 142 | 0.22 % | 148 | 2 |
| set1sample6 | 2 354 | 0.04 % | 0 | 1 |

Sample 3 has the most components but a median size of 2 voxels (mostly noise-scale
specks); **sample 2** has by far the most substantial pores and the highest porosity.

## Specimen A processing (NIST set1sample2)

1. **Crop.** A central 640³ cube (1.57 mm) around the field-of-view centre from the NIST
   README (Table 3). Why: the full 900×1010×984 stack plus masks and labels does not fit in
   16 GB RAM at full resolution, and the brief requires full-resolution segmentation. The
   cube lies entirely inside the circular reconstruction.
2. **Denoise.** 3×3×3 median filter, as in NIST's own reference pipeline. Without it the
   raw noise produced ~10⁵ one-voxel "pores".
3. **Threshold: ISO50.** Halfway between the air grey value (median of the darkest 0.3 %
   of voxels = pore interiors) and the material grey value (median). Measured on the crop:
   air 35 660, material 51 044, threshold **43 352**. Otsu was tried first and failed: with
   <1 % pores the histogram is unimodal, and Otsu put the threshold at 50 061, inside the
   metal's noise distribution, producing 103 094 false components.
4. **Pores = air enclosed by material.** 3D hole filling of the material mask; the
   difference is enclosed air. Pores cut by the crop boundary are open to the border and
   are therefore excluded (we never show half pores).
5. **Components.** 26-connected; components **under 8 voxels are dropped as noise**
   (8 voxels ≈ a 2×2×2 cube ≈ 15 µm equivalent diameter at 2.45 µm voxels).
6. **Features at full resolution** (`features.py`): volume, equivalent diameter, surface
   area (marching cubes on a σ = 1 voxel smoothed mask, which removes the ~1.5× staircase
   overestimate of a raw voxel surface), Wadell sphericity ψ = π^(1/3)(6V)^(2/3)/A,
   elongation = √(λmax/λmin) of the inertia tensor (max/min, not max/mid, so flat
   crack-like discs are caught), centroid and bounding box in mm.
7. **Classes** (brief's rule): crack = ψ < 0.5 and elongation > 3; lack-of-fusion = ψ < 0.75;
   otherwise gas pore.
8. **Ground truth.** The NIST segmented stack is cropped identically and pores are defined
   the same way. Object-level match: a predicted pore is TP if it shares ≥ 1 voxel with a
   reference pore. Missed reference pores (FN) are appended to the label volume with new
   ids so the shader can show them in ground-truth mode.
9. **Window + downsample.** 8-bit window from the 0.5–99.8 % percentiles; intensities 3×
   area-mean, labels 3× max-pool (keeps every small pore visible) → 214³ at 7.35 µm.

**Results:** 2 910 pores (2 775 gas pores, 132 lack-of-fusion, 3 crack-like). Against the
NIST reference (3 176 pores): precision 0.90, recall 0.75, voxel IoU 0.59. The reference
used Bernsen *local* thresholding after NLM filtering, so a global ISO50 threshold is
expected to disagree on faint, small pores. That is the main source of the 800 FN.

**Chart axes:** log10 pore volume, sphericity, and **build height** (z). The brief asked for
"depth below surface", but this is an interior (local) tomography scan, cropped further:
the part's outer surface is not in the data, so that depth is undefined. Build height (z is
the build direction, per the NIST README) is physically meaningful for L-PBF porosity
(layer-dependent process conditions). `depth_mm` (distance to the crop boundary) is still in
the CSV but not charted.

**Display scale:** the crop is 1.57 mm, so 1:1 is invisible. The specimen opens at ×159
(25 cm) and the scale gesture ranges from ×79 to ×3179; the label always shows the true
magnification.

**What the probe reports:** grey values in this scan are *uncalibrated* attenuation, not
density in g/cm³. The probe shows the raw 16-bit grey value and the material bin
("air / pore" or "CoCr alloy"); it does not claim a physical density.

## Specimen B processing (Enigma rotors)

1. Load DICOM (549 slices, 784 × 551, 0.181 mm isotropic, from the headers).
2. Otsu air/object threshold (valid here: a real object in air is bimodal).
3. 8-bit window as above.
4. Multi-Otsu with 4 classes → thresholds 32 / 98 / 161 (8-bit). Bins are named **low /
   medium / high attenuation**, not by material, because grey values alone cannot identify
   an alloy or polymer.
5. 26-connected components per bin, minimum **64 voxels (~0.38 mm³)**; below that, parts
   are scan noise at this resolution. Result: 790 parts.
6. Same features; chart axes log10 part volume × elongation × mean attenuation.
7. 4× downsampling → 125 × 196 × 135 at 0.72 mm. 98 small parts share display blocks with
   larger neighbours after max-pooling; they remain selectable in the chart.
