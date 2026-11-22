"""Builds study.html: explanation on the left, the real source code on the right.

Run from anywhere:  python docs/oral_exam/build_study.py
Re-run it after changing the code so the line numbers stay correct.
"""
import html
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
A = "Assets/XRayHands/"

# Each file: what it does, key spots (regex that finds the line, label, explanation), exam questions.
FILES = [
    {"group": "1 Data (Python)", "path": "tools/preprocess.py", "title": "Loading, thresholds, downsampling",
     "what": "Reads the CT slices (TIFF or DICOM), picks the threshold between air and metal, converts to 8-bit and shrinks the volume for the headset.",
     "points": [
         (r"def load_dicom", "load_dicom()", "Reads the Enigma DICOM slices and the voxel spacing from the file headers."),
         (r"def otsu_threshold", "otsu_threshold()", "The method we tried first and rejected: it needs two histogram peaks, but pores are under 1 % of voxels, so it found 103,094 fake pores."),
         (r"def iso50_threshold", "iso50_threshold()", "The one we use: the surface is halfway between the air and metal grey values. Standard in industrial CT. Gives 2,910 pores."),
         (r"def to_u8", "to_u8()", "Maps the 16-bit grey values to 0-255 for the GPU texture."),
         (r"def downsample_mean", "downsample_mean()", "Shrinks grey values by averaging blocks (the physically correct average)."),
         (r"def downsample_max", "downsample_max()", "Shrinks labels by taking the maximum, so every small pore keeps at least one voxel."),
     ],
     "qs": ["Why ISO50 and not Otsu?", "Why mean for grey values but max for labels?"]},
    {"group": "1 Data (Python)", "path": "tools/segment.py", "title": "Finding the pores",
     "what": "Turns the thresholded volume into separate pores, each with its own ID.",
     "points": [
         (r"def material_mask", "material_mask()", "Every voxel brighter than the threshold is metal."),
         (r"def enclosed_air", "enclosed_air()", "The definition of a pore: air completely enclosed by metal (3D hole filling). Air touching the outside is not a pore."),
         (r"def label_components", "label_components()", "26-connected components: each separate air blob gets an ID (16-bit). Blobs below a minimum voxel count are dropped as noise."),
         (r"def multi_threshold_components", "multi_threshold_components()", "For the Enigma: splits the parts into density classes (several thresholds) instead of pores."),
     ],
     "qs": ["What exactly is a pore in your code?", "How do you remove noise?"]},
    {"group": "1 Data (Python)", "path": "tools/features.py", "title": "Measuring each pore",
     "what": "Computes size, shape and position for every pore, classifies it, and compares our result with NIST's reference.",
     "points": [
         (r"def equivalent_diameter", "equivalent_diameter()", "Diameter of a sphere with the same volume."),
         (r"def sphericity", "sphericity()", "How round a pore is (1 = perfect sphere), from volume and surface area. Not meaningful for very small pores (8-30 voxels)."),
         (r"def elongation", "elongation()", "How stretched a pore is: sqrt(largest / smallest eigenvalue) of the voxel spread. Max/min (not max/mid) so flat disc-shaped cracks are caught too."),
         (r"def classify", "classify()", "Crack = sphericity < 0.5 and elongation > 3; lack of fusion = sphericity < 0.75; everything else = gas pore."),
         (r"def compute_features", "compute_features()", "Runs all measurements for every pore and builds the rows for features.csv."),
         (r"def compare_to_ground_truth", "compare_to_ground_truth()", "Matches our pores with NIST's: TP (correct), FP (false), FN (missed). Gives precision 0.90, recall 0.75, IoU 0.59."),
     ],
     "qs": ["How do you compute sphericity?", "What are precision, recall and IoU?"]},
    {"group": "1 Data (Python)", "path": "tools/pipeline.py", "title": "The whole pipeline",
     "what": "Runs every step in order and writes the 4 files Unity loads: volume_u8.raw, labels_u16.raw, features.csv, meta.json.",
     "points": [
         (r"def _write_specimen", "_write_specimen()", "Writes the 4 output files."),
         (r"def nist_crop", "nist_crop()", "The central 640-voxel cube (1.57 mm): the full scan does not fit in 16 GB RAM with all masks."),
         (r"def build_nist", "build_nist()", "Dataset A: crop, median filter, ISO50, pores, features, ground truth, downsample, write."),
         (r"def build_enigma", "build_enigma()", "Dataset B: DICOM, multi-threshold parts, features, write."),
     ],
     "qs": ["Walk us through the pipeline step by step.", "Why measure at full resolution and only then shrink?"]},

    {"group": "2 Loading & rendering", "path": A + "Core/VolumeLoader.cs", "title": "Loading data in Unity",
     "what": "Reads the files from StreamingAssets (works on PC and on the Quest) and builds a VolumeDataset.",
     "points": [
         (r"public static IEnumerator Load", "Load()", "Coroutine: loads meta, volume, labels and CSV without freezing the app."),
         (r"public static ushort\[\] DecodeLabels", "DecodeLabels()", "Turns the raw bytes back into 16-bit pore IDs."),
     ],
     "qs": ["Why StreamingAssets?"]},
    {"group": "2 Loading & rendering", "path": A + "Rendering/Shaders/XRayVolume.shader", "title": "The volume shader (ray marching)",
     "what": "Draws the CT scan as a see-through 3D object. All the visual tools (cut, peel, lens, pore colours) happen here on the GPU.",
     "points": [
         (r"float2 IntersectBox", "IntersectBox()", "Where the eye ray enters and leaves the cube."),
         (r"float rim = pow", "Peel rim term", "When peeled, metal is ~2 % opaque, but edges stay visible: (1-|n.v|)^3. That is why it looks like glass."),
         (r"half4 Frag", "Frag(): one pixel", "The main function, run for every pixel."),
         (r"float denom = dot\(_ClipPlane", "Cut plane", "The ray starts where it crosses the plane. If the first sample is metal, we show the real grey value: the CT slice."),
         (r"if \(_LensRadius > 0.0\)", "X-ray lens", "If this pixel's ray goes through the finger ring, use the lens peel for the whole ray."),
         (r"float jitter = InterleavedGradientNoise", "Jitter + opacity correction", "Each ray starts at a slightly different offset (removes stripes). Opacity is corrected for the step count, so quality tiers look alike."),
         (r"Empty-space skipping", "Empty-space skipping", "8x8x8 bricks store min/max. If nothing in a brick can be visible, jump straight over it."),
         (r"if \(acc.a > kOpaqueAlpha\) break", "Early ray termination", "Stop at 97 % opacity: nothing behind can be seen."),
     ],
     "qs": ["Explain ray marching.", "How did you make it fast enough for a mobile GPU?", "How does the cut show a real slice?"]},
    {"group": "2 Loading & rendering", "path": A + "Rendering/VolumeRenderer.cs", "title": "C# side of the renderer",
     "what": "Owns the material and passes every setting to the shader. The tools call these methods; they never touch the shader directly.",
     "points": [
         (r"public void SetDataset", "SetDataset()", "Uploads the 3D textures and the colour table."),
         (r"public void SetPeel", "SetPeel()", "Peel amount from the peel tool."),
         (r"public void SetWindow", "SetWindow()", "Contrast (window level/width), changed by the wrist twist."),
         (r"public void SetClipPlaneWorld", "SetClipPlaneWorld()", "Converts the palm plane from world space to the cube's own space for the shader."),
         (r"public void SetLens", "SetLens()", "Centre, direction and size of the finger ring."),
         (r"public void SetQuality", "SetQuality()", "Low / Medium / High = 96 / 160 / 256 steps."),
     ],
     "qs": ["Why convert the plane to object space?"]},

    {"group": "3 Hands & gestures", "path": A + "Input/HandFrame.cs", "title": "The hand model",
     "what": "One hand per frame: 26 joint positions (OpenXR order) plus derived values the gestures use.",
     "points": [
         (r"public void ComputeDerived", "ComputeDerived()", "Computes finger curls, palm position, palm normal and pinch distance from the joints."),
         (r"\? Vector3.Cross\(PalmForward, across\)", "Palm normal", "Cross product of 'along the hand' and 'across the knuckles'. The order flips for left vs right hand, otherwise one normal points out of the back of the hand."),
         (r"private float ChainCurl", "ChainCurl()", "Finger curl = sum of bend angles along the finger, normalised to 0 (straight) .. 1 (fist)."),
     ],
     "qs": ["How do you compute the palm normal?", "What is finger curl?"]},
    {"group": "3 Hands & gestures", "path": A + "Input/SimulatedHandSource.cs", "title": "Simulated hands (no headset)",
     "what": "Keyboard + mouse build full 26-joint hands, so the same gesture code runs in the editor as on the Quest.",
     "points": [
         (r"public void Tick", "Tick()", "Reads keys and mouse every frame and picks a hand shape (C = cut, P = peel, F = probe ...)."),
         (r"bool twoHandMode", "Scroll wheel", "Wheel = hand depth, or the distance between the two hands in two-hand mode."),
     ],
     "qs": ["How did you test without a headset?"]},
    {"group": "3 Hands & gestures", "path": A + "Gestures/Ramp.cs", "title": "Soft thresholds",
     "what": "Instead of a hard yes/no, each rule gives a value 0..1 on a linear ramp. A gesture takes the minimum of its rules (fuzzy AND).",
     "points": [
         (r"public static float Below", "Below()", "1 when the value is clearly below the limit, 0 when clearly above, linear in between."),
         (r"public static float Above", "Above()", "The mirror case."),
     ],
     "qs": ["Why soft thresholds instead of if/else?"]},
    {"group": "3 Hands & gestures", "path": A + "Gestures/Detectors.cs", "title": "Gesture detectors",
     "what": "One small class per gesture. Each turns hand data into a confidence 0..1, using geometric rules. No machine learning.",
     "points": [
         (r"class PinchDetector", "PinchDetector", "Thumb tip close to index tip (enter < 2 cm)."),
         (r"class FlatPalmDetector", "FlatPalmDetector", "All four finger curls below 0.25 and palm inside the part (+5 cm). Starts the cut."),
         (r"class PeelDetector", "PeelDetector", "Both palms flat and facing each other (dot of normals < -0.7), within 40 cm."),
         (r"class PointDetector", "PointDetector", "Index straight, other fingers curled: the probe."),
         (r"class GrabDetector", "GrabDetector", "Mean curl > 0.7 near the part. Fixed bug: a precision pinch used to count as a grab."),
         (r"class LensDetector", "LensDetector", "Thumb and index tips touching to make an 'O', facing your eye, held 250 ms."),
         (r"class ResetDetector", "ResetDetector", "Palm up, then close to a fist and hold."),
         (r"class TwistTracker", "TwistTracker", "Measures wrist roll for the contrast dial."),
     ],
     "qs": ["Why rules and not machine learning?", "Pick one gesture and explain its rules."]},
    {"group": "3 Hands & gestures", "path": A + "Gestures/GestureState.cs", "title": "Hysteresis (no flickering)",
     "what": "Turns a noisy confidence into a stable on/off state.",
     "points": [
         (r"public GestureState\(", "Constructor", "Enter threshold (0.95), exit threshold (0.05), hold time, minimum active time."),
         (r"public void Update\(float confidence", "Update()", "On only after confidence stays above 'enter' for the hold time; off only when below 'exit'. The gap stops flicker."),
     ],
     "qs": ["What is hysteresis and why do you need it?"]},
    {"group": "3 Hands & gestures", "path": A + "Gestures/GestureRecognizer.cs", "title": "Gesture recognizer",
     "what": "Runs all detectors every frame and keeps one GestureState per gesture and hand. It reports everything that is true; it does not decide which tool runs.",
     "points": [
         (r"public GestureRecognizer\(", "Constructor", "Registers every detector with its hold time (from GestureTuning)."),
         (r"public void Update\(in GestureContext", "Update()", "Every frame: detector confidence -> GestureState."),
     ],
     "qs": ["Where are all the threshold numbers? (Input/GestureTuning.cs)"]},

    {"group": "4 Tools", "path": A + "Interaction/ToolContracts.cs", "title": "What every tool looks like",
     "what": "The shared interface: every tool is a small state machine CanStart -> Begin -> Tick (each frame) -> End.",
     "points": [
         (r"public ToolFrame\(", "ToolFrame", "What a tool gets each frame: hands, gestures, time step."),
         (r"public static class HandPoses", "HandPoses", "Helper: hand rotation from palm forward and palm normal."),
     ],
     "qs": ["Why give every tool the same interface?"]},
    {"group": "4 Tools", "path": A + "Interaction/SpecimenTools.cs", "title": "Grab, scale, cut, peel, lens, reset",
     "what": "The tools that act on the scanned part.",
     "points": [
         (r"class GrabTool", "GrabTool", "Stores the hand-to-part offset at grab start, so the part doesn't jump."),
         (r"class ScaleTool", "ScaleTool", "Scales around the midpoint between the hands, within allowed limits."),
         (r"class SliceTool", "SliceTool (cut)", "Palm plane = cut plane, normal pointing away from your head, so the near half is removed."),
         (r"TwistDegreesPerOctave", "Contrast dial", "Each 45 degrees of wrist roll doubles or halves the contrast window."),
         (r"class PeelTool", "PeelTool", "Distance between palms 8-45 cm -> peel 0-100 %."),
         (r"class LensTool", "LensTool", "Runs alongside the main tool on the free hand."),
         (r"class ResetTool", "ResetTool", "Puts everything back."),
     ],
     "qs": ["Why does the part not jump when you grab it?", "Which half disappears when cutting, and why?"]},
    {"group": "4 Tools", "path": A + "Interaction/ChartTools.cs", "title": "Extract, select, brush + the controller",
     "what": "Chart tools and the InteractionModeController, which decides which single tool may run.",
     "points": [
         (r"class ExtractTool", "ExtractTool", "Pinch a pore and pull 12 cm: all pores fly into the chart."),
         (r"class ChartSelectTool", "ChartSelectTool", "Pinch a dot to select it; its pore lights up in the part."),
         (r"class BrushTool", "BrushTool", "Two-hand box in the chart selects a group of dots."),
         (r"class InteractionModeController", "InteractionModeController", "Only one main tool at a time. Two-hand tools are checked first; a running tool keeps control until its gesture ends."),
         (r"public void Tick\(in ToolFrame f\)\s*$", "Controller Tick()", "The decision logic each frame. Fixed bug: a running cut used to block the two-hand peel."),
     ],
     "qs": ["What happens if two gestures are true at once?"]},
    {"group": "4 Tools", "path": A + "Interaction/ProbeTool.cs", "title": "Probe",
     "what": "Reads the volume at the fingertip and finds the nearest pore.",
     "points": [
         (r"public bool CanStart", "CanStart()", "Starts when the point gesture is active."),
         (r"public static ProbeReading Measure", "Measure()", "Grey value, material class and distance to the nearest pore (simple linear scan: a few thousand pores take microseconds)."),
     ],
     "qs": ["Is the probe value a real density? (No: uncalibrated grey value.)"]},
    {"group": "4 Tools", "path": A + "Audio/ProbeSonifier.cs", "title": "Sound for the probe",
     "what": "Generates the sound live, at the fingertip, so it comes from where the finger is.",
     "points": [
         (r"public static float ClickRate", "ClickRate()", "Clicks speed up exponentially as the nearest pore gets closer (like a Geiger counter)."),
         (r"private void OnAudioFilterRead", "OnAudioFilterRead()", "Unity's audio callback: we write every sample ourselves. Pitch follows density over 2 octaves, glided to avoid clicks."),
     ],
     "qs": ["Why sound? (Multisensory; every click also has a visual ring.)"]},

    {"group": "5 Analytics & app", "path": A + "Analytics/SelectionModel.cs", "title": "Linked selection",
     "what": "The single source of truth for what is selected. The part, chart, link line and tooltip all listen to it, so linking works both ways.",
     "points": [
         (r"public void Focus", "Focus()", "Select one pore (from the part or the chart)."),
         (r"public void SetBrush", "SetBrush()", "Select a group from the brush."),
         (r"public bool IsSelected", "IsSelected()", "Asked by everyone who draws something."),
     ],
     "qs": ["How does linked selection work in both directions?"]},
    {"group": "5 Analytics & app", "path": A + "Analytics/DefectChart3D.cs", "title": "The 3D scatter plot",
     "what": "All pores as dots: x = log volume, y = sphericity, z = build height. One mesh for all dots = one draw call.",
     "points": [
         (r"public void Build", "Build()", "Creates the dot mesh and the axes."),
         (r"public void Extract", "Extract()", "Starts the fly-out animation from each pore's position."),
         (r"Quadratic B", "Arc animation", "Each dot travels on a quadratic Bezier arc, staggered by distance so the eye can follow."),
         (r"public int NearestDot", "NearestDot()", "Which dot the pinch is closest to."),
         (r"public List<int> IdsInBox", "IdsInBox()", "Dots inside the brush box."),
     ],
     "qs": ["What do the axes mean?", "Why one mesh?"]},
    {"group": "5 Analytics & app", "path": A + "Analytics/DefectPalette.cs", "title": "Pore colours",
     "what": "One place for colours, used by the shader (via a lookup table) and by the chart, so both always match.",
     "points": [
         (r"public static Color32 ColourOf", "ColourOf()", "Chooses the colour by mode: pore type, ground truth, selected / hidden."),
         (r"public static Color32 GroundTruthColour", "GroundTruthColour()", "Blue = correct, orange = false, pink = missed."),
     ],
     "qs": ["What do the colours mean?"]},
    {"group": "5 Analytics & app", "path": A + "App/Bootstrap.cs", "title": "Startup (composition root)",
     "what": "Creates every object and connects them. No class searches the scene for others.",
     "points": [
         (r"private void Awake", "Awake()", "Builds hands, recognizer, tools, renderer, chart."),
         (r"public IEnumerator LoadSpecimen", "LoadSpecimen()", "Loads dataset A or B and places it 55 cm in front, 15 cm below the eyes."),
         (r"private void Update\(\)", "Update()", "The per-frame loop, plus the keys T, H, X, 1, 2, Tab, Backspace."),
     ],
     "qs": ["Where does everything start?", "What is a composition root?"]},
    {"group": "5 Analytics & app", "path": "Assets/Tests/EditMode/GestureStateTests.cs", "title": "Example tests",
     "what": "Unit tests for hysteresis. 43 Unity tests and 28 Python tests pass in total.",
     "points": [], "qs": ["How did you test your gestures?"]},
]

KW = set("""abstract as base bool break case catch class const continue default do double else enum event explicit
false finally float for foreach get if in int interface internal is new null out override private protected public
readonly ref return sealed set static string struct switch this throw true try using var void while yield
def import from return None True False and or not with lambda pass raise elif self
float2 float3 float4 half half4 uniform sampler3D""".split())
TOKEN = re.compile(r'(//.*$|#(?!pragma|include|define).*$|"(?:\\.|[^"\\])*"|\b\d+(?:\.\d+)?f?\b|\b[A-Za-z_]\w*\b)')


def colour(line: str, py: bool) -> str:
    out, pos = [], 0
    for m in TOKEN.finditer(line):
        tok = m.group(0)
        if (tok.startswith("#") and not py) or (tok.startswith("//") and py):
            continue
        out.append(html.escape(line[pos:m.start()]))
        esc = html.escape(tok)
        if tok.startswith("//") or tok.startswith("#"):
            out.append(f'<span class="c">{esc}</span>')
        elif tok.startswith('"'):
            out.append(f'<span class="s">{esc}</span>')
        elif tok[0].isdigit():
            out.append(f'<span class="n">{esc}</span>')
        elif tok in KW:
            out.append(f'<span class="k">{esc}</span>')
        else:
            out.append(esc)
        pos = m.end()
    out.append(html.escape(line[pos:]))
    return "".join(out)


STRIP = re.compile(r'//.*|"(?:\\.|[^"\\])*"|\'.\'')  # comments, strings and chars, so their braces don't count


def block_end(lines: list[str], start: int, py: bool) -> int:
    """Last line (1-based) of the function/class/block that starts at line `start`."""
    i = start - 1
    if py:
        indent = len(lines[i]) - len(lines[i].lstrip())
        end = i
        for j in range(i + 1, len(lines)):
            t = lines[j]
            if t.strip() == "":
                continue
            if len(t) - len(t.lstrip()) <= indent and not t.lstrip().startswith((")", "]")):
                break
            end = j
        return min(end + 1, start + 80)
    first = STRIP.sub("", lines[i]).strip()
    if "=>" in first and first.endswith(";"):
        return start  # one-line member
    # A declaration opens a { within its first few lines; anything else is a spot inside a
    # function, so highlight its paragraph (up to the next blank line).
    opens_block = any("{" in STRIP.sub("", lines[k]) for k in range(i, min(len(lines), i + 4)))
    if not opens_block or not re.search(r"\b(class|struct|void|bool|float\d?|half\d?|int|IEnumerator|List<\w+>|Color32|ProbeReading)\b|\bif\b|\(.*\)", first):
        end = i
        while end + 1 < len(lines) and lines[end + 1].strip() and end - i < 14:
            end += 1
        return end + 1
    depth, opened = 0, False
    for j in range(i, min(len(lines), i + 200)):
        for ch in STRIP.sub("", lines[j]):
            if ch == "{":
                depth += 1
                opened = True
            elif ch == "}":
                depth -= 1
        if opened and depth <= 0:
            return min(j + 1, start + 80)
    return start + 5


def build() -> None:
    data, missing = [], []
    for f in FILES:
        lines = (ROOT / f["path"]).read_text(encoding="utf-8", errors="replace").splitlines()
        pts = []
        for pattern, label, text in f["points"]:
            rx = re.compile(pattern)
            hit = next((i + 1 for i, l in enumerate(lines) if rx.search(l)), None)
            if hit is None:
                missing.append(f'{f["path"]}: {label}')
                continue
            pts.append({"line": hit, "label": label, "text": text})
        py = f["path"].endswith(".py")
        for pt in pts:
            pt["end"] = block_end(lines, pt["line"], py)
        data.append({**{k: f[k] for k in ("group", "path", "title", "what", "qs")}, "points": pts,
                     "code": [colour(l, py) for l in lines]})
    page = TEMPLATE.replace("/*DATA*/", json.dumps(data))
    out = Path(__file__).with_name("study.html")
    out.write_text(page, encoding="utf-8")
    print(f"wrote {out} ({len(data)} files)")
    for m in missing:
        print("MISSING ANCHOR:", m)


TEMPLATE = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>X-Ray Hands Code Study</title>
<style>
:root{--bg:#f6f8fb;--panel:#fff;--ink:#1a2230;--muted:#5b6676;--line:#dde3ea;--accent:#1b7fb0;--code:#0f1724;--codeink:#d6e2ef;--hl:#2a4b6b}
@media (prefers-color-scheme:dark){:root{--bg:#0d1117;--panel:#151b23;--ink:#e3e9f0;--muted:#94a1b2;--line:#2a333f;--accent:#4fb3e6}}
*{box-sizing:border-box}html,body{margin:0;height:100%}
body{font-family:"Segoe UI",Arial,sans-serif;background:var(--bg);color:var(--ink);display:flex;flex-direction:column}
header{display:flex;align-items:center;gap:12px;padding:8px 14px;border-bottom:1px solid var(--line);background:var(--panel)}
header b{font-size:15px}header span{color:var(--muted);font-size:12.5px}
main{flex:1;display:grid;grid-template-columns:230px minmax(320px,38%) 1fr;min-height:0}
nav{overflow:auto;border-right:1px solid var(--line);background:var(--panel);padding:6px 0}
nav h4{margin:12px 14px 4px;font-size:11px;letter-spacing:.6px;text-transform:uppercase;color:var(--muted)}
nav button{display:block;width:100%;text-align:left;border:0;background:none;color:var(--ink);padding:5px 14px;font:inherit;font-size:13px;cursor:pointer}
nav button:hover{background:var(--bg)}nav button.on{background:var(--accent);color:#fff}
nav small{display:block;font-size:10.5px;opacity:.7;font-family:Consolas,monospace}
#notes{overflow:auto;padding:16px 18px;border-right:1px solid var(--line)}
#notes h2{margin:0 0 2px;font-size:19px}#notes .p{font-family:Consolas,monospace;font-size:12px;color:var(--muted);margin-bottom:10px}
#notes .what{background:var(--panel);border-left:3px solid var(--accent);padding:9px 12px;border-radius:4px;margin-bottom:14px;line-height:1.5}
.pt{display:block;width:100%;text-align:left;font:inherit;color:inherit;background:var(--panel);border:1px solid var(--line);border-radius:6px;padding:8px 11px;margin-bottom:7px;cursor:pointer;line-height:1.45}
.pt:hover,.pt.on{border-color:var(--accent)}.pt b{color:var(--accent);font-family:Consolas,monospace;font-size:12.5px}
.pt i{float:right;font-style:normal;font-size:11px;color:var(--muted)}
#notes h3{font-size:13px;margin:16px 0 6px}#notes ul{margin:0;padding-left:18px;line-height:1.6}
#code{overflow:auto;background:var(--code);color:var(--codeink);font:12.5px/1.55 Consolas,"Cascadia Mono",monospace}
.l{display:flex;white-space:pre}.l .ln{flex:0 0 52px;text-align:right;padding-right:12px;color:#5d6b7c;user-select:none}
.l.hl{background:#1f3d5c}.l.hl .ln{color:#fff;background:#1b7fb0}.l.hlt{box-shadow:inset 0 2px 0 #4fb3e6}.l.hlb{box-shadow:inset 0 -2px 0 #4fb3e6}.l.key .ln{color:#ffcc80;font-weight:700}
.pt.on{background:color-mix(in srgb,var(--accent) 12%,var(--panel));box-shadow:0 0 0 2px var(--accent)}
.c{color:#7f8fa3;font-style:italic}.s{color:#a5d6a7}.k{color:#82b1ff}.n{color:#ffcc80}
.tip{font-size:12px;color:var(--muted);margin-top:14px}
#pick{display:none;font:inherit;font-size:13px;padding:3px 6px;max-width:60%}
@media (max-width:1100px){main{grid-template-columns:minmax(260px,40%) 1fr}nav{display:none}#pick{display:block}header span{display:none}#notes{padding:12px}#notes .what,.pt{font-size:13px}}
</style></head><body>
<header><b>X-Ray Hands &middot; code study</b><select id="pick"></select><span>Pick a file on the left &middot; click a point to jump to that code &middot; keys: &uarr;/&darr; previous/next file</span></header>
<main><nav id="nav"></nav><section id="notes"></section><section id="code"></section></main>
<script>
const D=/*DATA*/;let cur=0;
const nav=document.getElementById('nav'),notes=document.getElementById('notes'),code=document.getElementById('code');
const pick=document.getElementById('pick');pick.onchange=()=>show(+pick.value);
let g='';D.forEach((f,i)=>{const o=document.createElement('option');o.value=i;o.textContent=f.group.slice(0,1)+' · '+f.title;pick.appendChild(o);if(f.group!==g){g=f.group;const h=document.createElement('h4');h.textContent=g;nav.appendChild(h);}
 const b=document.createElement('button');b.innerHTML=f.title+'<small>'+f.path.split('/').pop()+'</small>';b.onclick=()=>show(i);nav.appendChild(b);});
function esc(s){return s.replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));}
function show(i){cur=i;const f=D[i];pick.value=i;[...nav.querySelectorAll('button')].forEach((b,j)=>b.classList.toggle('on',j===i));
 notes.innerHTML='<h2>'+esc(f.title)+'</h2><div class="p">'+esc(f.path)+'</div><div class="what">'+esc(f.what)+'</div>'+
  f.points.map((p,k)=>'<button class="pt" data-k="'+k+'"><i>lines '+p.line+'–'+p.end+'</i><b>'+esc(p.label)+'</b><br>'+esc(p.text)+'</button>').join('')+
  (f.qs.length?'<h3>They might ask</h3><ul>'+f.qs.map(q=>'<li>'+esc(q)+'</li>').join('')+'</ul>':'')+
  '<div class="tip">Answers to these are in the oral exam guide PDF, section 6.</div>';
 code.innerHTML=f.code.map((l,n)=>'<div class="l" id="L'+(n+1)+'"><span class="ln">'+(n+1)+'</span><span>'+(l||' ')+'</span></div>').join('');
 code.scrollTop=0;notes.scrollTop=0;
 notes.querySelectorAll('.pt').forEach(b=>b.onclick=()=>jump(+b.dataset.k));
 f.points.forEach(p=>{const e=document.getElementById('L'+p.line);if(e)e.classList.add('key');});
 if(f.points.length)jump(0);
 try{localStorage.setItem('xrh-file',i)}catch(e){}}
function jump(k){const p=D[cur].points[k];notes.querySelectorAll('.pt').forEach((b,j)=>b.classList.toggle('on',j===k));
 code.querySelectorAll('.hl,.hlt,.hlb').forEach(e=>e.classList.remove('hl','hlt','hlb'));
 for(let n=p.line;n<=p.end;n++){const e=document.getElementById('L'+n);if(e)e.classList.add('hl');}
 const t=document.getElementById('L'+p.line),b=document.getElementById('L'+p.end);if(t)t.classList.add('hlt');if(b)b.classList.add('hlb');
 if(t){const h=(b?b.offsetTop+b.offsetHeight:t.offsetTop)-t.offsetTop;code.scrollTo({top:h<code.clientHeight*0.8?t.offsetTop-(code.clientHeight-h)/3:t.offsetTop-20,behavior:'smooth'});}
 notes.querySelector('.pt[data-k="'+k+'"]').scrollIntoView({block:'nearest'});}
document.addEventListener('keydown',e=>{if(e.key==='ArrowDown'&&cur<D.length-1){e.preventDefault();show(cur+1)}if(e.key==='ArrowUp'&&cur>0){e.preventDefault();show(cur-1)}});
let start=0;try{start=+(localStorage.getItem('xrh-file')||0)}catch(e){}show(Math.min(start,D.length-1));
</script></body></html>"""

if __name__ == "__main__":
    build()
