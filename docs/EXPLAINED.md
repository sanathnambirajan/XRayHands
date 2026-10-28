# X-Ray Hands explained

One page per module: what it does, why it is built that way, and questions you are likely to
get in the Q&A, with answers. Read this together with the code; every class has XML docs.

## The big picture

```
CT data ──tools/ (Python)──► volume_u8.raw + labels_u16.raw + features.csv + meta.json
                                         │ StreamingAssets
Hands ─► IHandSource ─► HandFrame ─► GestureRecognizer ─► InteractionModeController ─► tools
        (XR Hands or simulator)       (detectors +        (one primary tool at a time)
                                       hysteresis)                │
                                                                  ▼
              VolumeRenderer (shader uniforms)  ◄── SelectionModel ──►  DefectChart3D
```

Everything is created in `App/Bootstrap.cs` (composition root) and handed to the classes
that need it. No class searches the scene.

---

## 1. Data pipeline (`tools/`)

**What:** downloads the scans, segments defects at full resolution, measures each defect, and
writes compact files the app loads.

**Why segment at full resolution, then downsample?** Pores are a few voxels across. If we
downsampled first, most would vanish or merge. We measure on the real 2.45 µm grid, then
shrink for display: intensities by area-mean (the physically correct average), labels by
max-pool (so every pore keeps at least one display voxel).

**Why ISO50 and not Otsu?** Otsu assumes two histogram peaks. Inside a metal part, pores are
<1 % of voxels, so there is effectively one peak, and Otsu split the metal's own noise in
half: 103 094 false "pores". ISO50 (the industrial-CT standard) puts the surface halfway
between the air and material grey values: 43 352, giving 2 910 pores.

**Why "air enclosed by material"?** That is the definition of a pore. 3D hole filling marks
every air pocket with no path to the outside; pores cut by the crop are excluded on purpose.

**How good is our segmentation?** Against NIST's reference: precision 0.90, recall 0.75,
voxel IoU 0.59. NIST used local (Bernsen) thresholding after denoising, which finds fainter
small pores; our global threshold misses some of them (800 FN). We show this honestly in
the ground-truth view.

> **Q: Why is sphericity 1.0 for so many small pores?** At 8–30 voxels the surface cannot be
> resolved; the smoothed marching-cubes area underestimates, ψ saturates. Our tests show
> ψ ≥ 0.95 for real spheres with radius ≥ 8 voxels; below that, ψ is not meaningful.
>
> **Q: Why not "depth below surface" on the z axis?** The NIST scan is an interior
> tomography of the part, cropped further; the outer surface is not in the data. Build
> height is physically meaningful for laser powder bed fusion (layer-wise process).
>
> **Q: Is the probe value a density?** No. CT grey values are uncalibrated attenuation. The
> probe shows the raw 16-bit grey value and the material bin, and says so.

---

## 2. Hands (`Input/`)

**HandFrame:** 26 joint positions (OpenXR order) plus derived values: finger curls (sum of
bend angles along each finger, normalised), palm position/normal/forward, pinch distance.

**Why one class for real and simulated hands?** `IHandSource` hides where hands come from.
The keyboard simulator builds full skeletons with `HandPoseBuilder`, so the gesture code we
test in the editor is exactly the code that runs on Quest.

> **Q: How do you compute the palm normal?** Cross product of "across the knuckles" and
> "along the hand". The order flips between left and right hands, otherwise one hand's
> normal points out of the back of the hand; a unit test checks both.

---

## 3. Gestures (`Gestures/`)

**Detectors** are pure functions: hand data → confidence 0..1. Each criterion is a linear
ramp ("soft threshold"); a gesture's confidence is the minimum of its criteria (fuzzy AND).

**GestureState** adds hysteresis: enter when confidence ≥ 0.95 (for a hold time), exit only
when ≤ 0.05 and after a minimum active time. The band between enter and exit is why a hand
hovering at a threshold does not flicker. A unit test feeds noisy confidence for 300 frames
and asserts at most one transition.

> **Q: Why not machine learning for gestures?** Six static poses with clear geometric
> definitions; rules are transparent, need no training data, are unit-testable, and every
> threshold is tunable in one asset (`GestureTuning`).
>
> **Q: How do you avoid conflicts, e.g. two pinches vs one pinch?** Recognition reports
> everything that is true; `InteractionModeController` decides. Two-hand tools are checked
> first, and a running tool keeps control until its own gesture ends.

---

## 4. Rendering (`Rendering/`, `XRayVolume.shader`)

**Ray marching:** draw the back faces of a unit cube; per pixel, cast a ray from the eye
(per eye, so stereo is correct), intersect the box, step through the volume front to back,
and stop when opacity reaches 0.97.

**Speed-ups (all mobile-GPU motivated):**
- gradient baked into the RGB of the density texture (1 fetch per sample instead of 7);
- empty-space skipping with an 8³ min/max brick grid (jump over air);
- jittered start per pixel (interleaved gradient noise) instead of more steps against banding;
- opacity correction `1-(1-α)^(ref/steps)` so quality tiers look the same, only smoother.

**Cut plane:** the ray starts where it crosses the plane; if the first sample is material,
we output the windowed grey value opaquely. That is why the cut face looks like a real CT slice.

**Peel:** below the peel threshold, opacity drops to ~2 % plus a rim term
`(1-|n·v|)^3 × |∇|`: silhouettes stay, so the shape reads as glass.

**Lens:** per pixel, intersect the view ray with the lens disc; if it hits, the whole ray uses
the lens peel. Focus + context without any extra pass.

**Defect colours:** a 256×N lookup texture indexed by label id. All logic (class colours,
TP/FP/FN, selection, hide unselected) is C# (`DefectPalette`), so it is unit-testable and the
chart uses the same colours.

> **Q: Why RG8 labels and not R16?** R16 sampling is optional on some mobile GPUs; RG8 is
> universal and still encodes 65 535 ids exactly.
>
> **Q: How do hands occlude the volume?** Each ray stops at the scene depth (URP depth
> texture), so opaque geometry in front of or inside the volume wins.

---

## 5. Interaction (`Interaction/`)

Each tool is a small state machine (`CanStart / Begin / Tick / End`). The controller runs at
most one primary tool; lens and reset run alongside on the free hand.

- **Grab** stores the hand→specimen offset at grab time, so nothing jumps.
- **Scale** scales about the midpoint between the hands (what a stretch should feel like),
  clamped to the specimen's allowed magnification; the label shows the true factor.
- **Slice** orients the plane normal away from the head (the near side is removed); wrist roll
  changes window width, one octave per 45°.
- **Peel** maps palm distance 8–45 cm linearly to 0–100 %.
- **Probe** samples density/label at the fingertip; nearest defect by linear scan (a few
  thousand defects: microseconds, no allocation).
- **Extract** pinch on a defect + pull 12 cm → the chart animation.

---

## 6. Analytics (`Analytics/`)

**SelectionModel** is the single source of truth for linked selection. The volume, chart,
link line and tooltip only listen to it, so linking is bidirectional without views calling
each other.

**DefectChart3D:** one mesh for all dots (one draw call on Quest). Dot radius ∝ cube root
of volume, i.e. proportional to physical size. Extraction: quadratic Bézier arc from the
defect to its chart position, staggered by distance, so the eye can follow the flow.

---

## 7. Audio (`Audio/ProbeSonifier.cs`)

Synthesised sample by sample in `OnAudioFilterRead` on an AudioSource at the fingertip, so
Unity's spatialiser places it at the finger. Pitch = density (2 octaves, glided to avoid
clicks). Geiger clicks: rate rises exponentially as the nearest defect gets closer; every
click also triggers a visual ring pulse, so it works without sound.
