# Data attribution

X-Ray Hands ships two processed specimens in `Assets/StreamingAssets/Specimens/`. Both are
**modified** versions of the published data: cropped, windowed to 8 bit, downsampled and
re-segmented by Group 07 (see `docs/DATA.md` for every processing step).

## Specimen A: `nist_set1sample2`

- **Source:** High-Resolution X-ray computed tomography (XCT) image data set of additively
  manufactured cobalt chrome samples produced with varying laser powder bed fusion
  processing parameters. National Institute of Standards and Technology (NIST).
- **DOI:** 10.18434/M32162. **Landing page:** https://data.nist.gov/od/id/mds2-2162
- **Files used:** `raw/set1sample2raw.zip`, `segmented/set1sample2segmented.zip`
  (SHA-256 verified against the publisher's sidecar files on 2026-10-28).
- **License:** NIST open license, https://www.nist.gov/open/license. Works of NIST
  employees are not subject to copyright protection in the United States; NIST asks that
  it be acknowledged as the source and that modified works carry a notice of the change.
- **Modification notice:** central 640³-voxel crop (1.57 mm cube), 3×3×3 median filter,
  ISO50 re-segmentation of pores, 8-bit windowing, 3× area-mean downsampling. Changed by
  Group 07, 28 Oct 2026.
- **References cited by the dataset README:**
  1. Kim, F. H., Moylan, S. P., Garboczi, E. J., and Slotwinski, J. A., 2017. "Investigation
     of pore structure in cobalt chrome additively manufactured parts using X-ray computed
     tomography and three-dimensional image analysis." *Additive Manufacturing*, 17, 23–38.
  2. Slotwinski, J. A., Garboczi, E. J., and Hebenstreit, K. M., 2014. "Porosity Measurements
     and Analysis for Metal Additive Manufacturing Process Control." *Journal of Research of
     the National Institute of Standards and Technology*, 119, 494–528.

## Specimen B: `enigma_1993_522_rotors`

- **Source:** Cipher Machine Enigma I (Inv. 1993-522), Zenodo record 8232721,
  https://zenodo.org/records/8232721
- **File used:** `1993-522_ROI_rotors.dcm.zip` (MD5 verified against the Zenodo API on 2026-10-28).
- **Attribution:** Deutsches Museum, Inv. 1993-522; CT by Fraunhofer IIS/EZRT; 3D-Cipher
  project (BMBF eHeritage).
- **License:** the Zenodo record metadata lists **CC BY 4.0**. Following the project brief,
  we treat it as **CC BY-SA 4.0** to be safe, so our derived specimen is shared under
  CC BY-SA 4.0 as well.
- **Modification notice:** cropped to the object, 8-bit windowed, 4× area-mean downsampled,
  segmented into connected components per multi-Otsu attenuation bin. Changed by Group 07,
  28 Oct 2026.

Raw downloads are kept in `data_raw/` (git-ignored) and are never redistributed by this repository.
