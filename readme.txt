X-RAY HANDS - Group 07, Immersive Analytics (6172), University of Passau, WiSe 26/27
Mithulram Gunasekaran, Sanath Nambirajan

Look inside real CT-scanned objects with your bare hands: cut, peel, probe, see through,
and pull hidden defects out into a linked 3D chart. No menus, no controllers.

----------------------------------------------------------------------------------------
SETUP
----------------------------------------------------------------------------------------
1. Unity 6000.3.27f1 (Unity 6.3 LTS) with Android Build Support (for Quest).
2. Open the folder as a Unity project. Git LFS is required for the specimen volumes
   (git lfs install; git lfs pull).
3. Scene: Assets/XRayHands/Scenes/Main.unity. Press Play.
   - With a headset (Quest Link / Air Link) and OpenXR enabled: real hand tracking.
   - Without: keyboard + mouse simulated hands (below). Same gesture code either way.
4. Data pipeline (optional, to rebuild specimens): see docs/DATA.md.
   cd tools; uv venv --python 3.11 .venv; .venv\Scripts\python -m pip install numpy scipy
   scikit-image pydicom tifffile connected-components-3d pandas pytest requests
   python fetch_data.py nist-segmented | nist-raw --sample set1sample2 | enigma
   python pipeline.py nist --sample set1sample2 ; python pipeline.py enigma ; python validate.py

----------------------------------------------------------------------------------------
GESTURES (hand tracking)                      KEYBOARD/MOUSE FALLBACK (editor / PC)
----------------------------------------------------------------------------------------
Grab: fist near specimen, move/rotate         G + left mouse drag
Scale: pinch with both hands, pull apart      Shift + left mouse, mouse wheel = spread
Cut: flat palm through the specimen           hold C, move mouse; wheel = depth
Contrast dial: twist wrist while cutting      C + Q / E
Peel: two flat palms facing, spread apart     hold P, mouse wheel = palm distance
Probe: point with index finger                hold F, move mouse
Extract: pinch a glowing defect, pull away    left mouse on a defect, drag 12 cm; or X
Select dot: pinch a chart dot                 left mouse on a dot
Brush: pinch both hands inside the chart      Shift + left mouse inside the chart
X-ray lens: thumb-index "O" held up           hold O
Reset: palm up, close fist, hold 0.6 s        hold R  (or Backspace)
Ground-truth colours (TP/FP/FN)               T
Hide unselected defects                       H
Load specimen A / B                           1 / 2
Quality tier Low/Medium/High                  Tab

----------------------------------------------------------------------------------------
PARAMETERS
----------------------------------------------------------------------------------------
All gesture thresholds are in one asset type, GestureTuning (Assets/XRayHands/Input).
Defaults: pinch enter < 2.0 cm / exit > 3.5 cm; flat palm curl < 0.25 held 150 ms,
inside specimen + 5 cm; peel palm-normal dot < -0.7 within 40 cm; point index curl < 0.2,
others > 0.6; lens tips < 2.5 cm, ring facing eye < 35 deg, held 250 ms; grab mean curl
> 0.7 within 8 cm; reset palm up + fist held 0.6 s.
Rendering: Low/Medium/High = 96/160/256 steps across the cube diagonal.

----------------------------------------------------------------------------------------
DATA
----------------------------------------------------------------------------------------
A: NIST mds2-2162 CoCr L-PBF sample 2 (DOI 10.18434/M32162), NIST open license.
B: Enigma I rotors, Deutsches Museum Inv. 1993-522, CT by Fraunhofer IIS/EZRT,
   3D-Cipher project (BMBF eHeritage), Zenodo 8232721, CC BY-SA 4.0 (treated as BY-SA).
Full attribution: data/ATTRIBUTION.md. Processing details: docs/DATA.md.
