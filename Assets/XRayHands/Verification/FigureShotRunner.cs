using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using XRayHands.Analytics;
using XRayHands.App;
using XRayHands.Core;
using XRayHands.Gestures;
using XRayHands.Input;

namespace XRayHands.Verification
{
    /// <summary>
    /// Scripted session that produces the figures for the specification sheet. Hands are real
    /// 26-joint poses injected through <see cref="SimulatedHandSource.SetScriptedHand"/>: the
    /// gesture recognizer and the tools react exactly as they would to tracked hands, so each
    /// figure is also an end-to-end check that the gesture → tool → rendering path works.
    /// The <see cref="Log"/> records which tool was active at every capture.
    /// </summary>
    public sealed class FigureShotRunner : MonoBehaviour
    {
        /// <summary>Output folder (relative to the project).</summary>
        public static string OutputDir = "docs/spec_sheet/img/raw";
        /// <summary>Main figure size in pixels.</summary>
        public static int Width = 2400, Height = 1500;
        /// <summary>Gesture pictogram size in pixels.</summary>
        public static int IconSize = 700;

        /// <summary>True when the run finished.</summary>
        public static bool Done { get; private set; }

        /// <summary>Run log (tool active per shot, chosen defect ids, errors).</summary>
        public static readonly StringBuilder Log = new StringBuilder();

        private Bootstrap _app;
        private SimulatedHandSource _sim;
        private Camera _cam;

        // A loosely relaxed hand: neither flat (cut) nor pinching.
        private static readonly HandShape Relaxed = new HandShape(0.3f, 0.35f, 0.4f, 0.45f, 0.45f, 0f);

        private static readonly Quaternion PalmLeft = Quaternion.AngleAxis(-90f, Vector3.forward);  // right hand palm faces -x
        private static readonly Quaternion PalmRight = Quaternion.AngleAxis(90f, Vector3.forward);  // left hand palm faces +x

        /// <summary>Starts the runner (call in Play mode).</summary>
        public static void Launch()
        {
            Done = false;
            Log.Clear();
            new GameObject("FigureShotRunner").AddComponent<FigureShotRunner>();
        }

        private IEnumerator Start()
        {
            _app = FindFirstObjectByType<Bootstrap>();
            _sim = _app.Hands as SimulatedHandSource;
            _cam = Camera.main;
            if (_sim == null) { Log.AppendLine("ERROR: hands are not simulated"); Done = true; yield break; }
            while (_app.Dataset == null || _app.IsLoading) yield return null;

            yield return ShotCut();
            yield return ShotPeel();
            yield return ShotProbe();
            yield return ShotExtract();
            yield return ShotLens();
            yield return ShotGroundTruth();
            yield return ShotEnigma();
            yield return Pictograms();

            _sim.ClearScriptedHands();
            Log.AppendLine("DONE");
            Done = true;
        }

        // ------------------------------------------------------------------ shots

        private Vector3 S => _app.SpecimenRoot.position;

        private IEnumerator ShotCut()
        {
            yield return Release();
            Fresh();
            // Right hand flat, fingers up, palm facing away (+z), yawed 20° for an oblique cut.
            Quaternion rot = Quaternion.AngleAxis(-20f, Vector3.up) * Quaternion.LookRotation(Vector3.up, Vector3.back);
            _sim.SetScriptedHand(Handedness.Right, new Pose(S + new Vector3(0.0f, -0.11f, -0.005f), rot), HandShape.Open);
            Aim(S + new Vector3(0.2f, 0.14f, -0.42f), S + new Vector3(0.01f, -0.02f, 0.01f));
            yield return Wait(0.6f);
            Capture("fig_cut");
        }

        private IEnumerator ShotPeel()
        {
            yield return Release();
            Fresh();
            Quaternion fwd = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            _sim.SetScriptedHand(Handedness.Left, new Pose(S + new Vector3(-0.19f, -0.03f, -0.07f), fwd * PalmRight), HandShape.Open);
            _sim.SetScriptedHand(Handedness.Right, new Pose(S + new Vector3(0.19f, -0.03f, -0.07f), fwd * PalmLeft), HandShape.Open);
            Aim(S + new Vector3(0.0f, 0.16f, -0.52f), S + new Vector3(0f, -0.01f, 0f));
            yield return Wait(0.6f);
            Capture("fig_peel");
        }

        private IEnumerator ShotProbe()
        {
            yield return Release();
            Fresh();
            _app.Volume.SetPeel(0.55f); // show the pores around the fingertip
            Vector3 tip = S + new Vector3(0.02f, 0.01f, -0.03f);
            Quaternion rot = Quaternion.LookRotation(new Vector3(0.45f, -0.35f, 1f).normalized, Vector3.up);
            PlaceJoint(Handedness.Right, HandJoint.IndexTip, tip, rot, HandShape.Point);
            // View from the left so the label above the fingertip is not behind the hand.
            Aim(S + new Vector3(-0.2f, 0.12f, -0.3f), S + new Vector3(0.01f, 0.03f, -0.02f));
            yield return Wait(1.2f);
            Capture("fig_probe");
        }

        /// <summary>Removes both hands long enough for every gesture and tool to end.</summary>
        private IEnumerator Release()
        {
            HideLeft(); HideRight();
            yield return Wait(0.3f);
        }

        private IEnumerator ShotExtract()
        {
            yield return Release();
            Fresh();
            DefectRecord target = LargestDetected();
            Vector3 defect = _app.Volume.MmToWorld(target.CentroidMm);
            Log.AppendLine($"extract: pinching defect #{target.Id} ({target.VolumeMm3:E2} mm3)");
            Quaternion rot = Quaternion.LookRotation(new Vector3(0.2f, -0.3f, 1f).normalized, Vector3.up);

            // Approach with a relaxed hand (an open hand inside the specimen would be a cut),
            // then pinch on the defect and pull away: the real extract gesture.
            PlaceJoint(Handedness.Right, HandJoint.IndexTip, defect, rot, Relaxed);
            HideLeft();
            yield return Wait(0.3f);
            PlacePinch(Handedness.Right, defect, rot);
            yield return Wait(0.2f);
            for (int i = 1; i <= 12; i++)
            {
                PlacePinch(Handedness.Right, defect + new Vector3(0.05f, 0.06f, -0.12f) * (i / 12f), rot);
                yield return null;
                yield return null;
            }
            Log.AppendLine($"extract: chart shown = {_app.Chart.IsShown}");
            HideRight(); // the hand would hide the flight
            Aim(S + new Vector3(0.19f, 0.12f, -0.62f), S + new Vector3(0.19f, 0.0f, 0.03f));
            yield return Wait(0.95f); // mid-flight: early dots have landed, late ones still on their arcs
            Capture("fig_extract_flight");

            yield return Wait(1.8f);
            // Pinch the dot of the target defect in the chart (chart → specimen selection).
            if (_app.Chart.TryGetDotWorld(target.Id, out Vector3 dot))
            {
                PlaceJoint(Handedness.Right, HandJoint.IndexTip, dot + new Vector3(0, 0, -0.03f), rot, Relaxed);
                yield return Wait(0.25f);
                PlacePinch(Handedness.Right, dot, rot);
                yield return Wait(0.4f);
            }
            Log.AppendLine($"extract: focused id = {_app.Selection.FocusedId} (target {target.Id})");
            HideRight();
            yield return Wait(0.3f);
            Capture("fig_chart_linked");
        }

        private IEnumerator ShotLens()
        {
            yield return Release();
            Fresh();
            // Eye closer to the specimen than the default head pose so the lens fills more of the view.
            Aim(Vector3.Lerp(new Vector3(0f, 1.6f, 0f), S, 0.35f), S);
            Vector3 eye = _cam.transform.position;
            Vector3 toSpec = (S - eye).normalized;
            // Ring held close to the specimen so the lens footprint on it is large and visible.
            Vector3 ringCentre = eye + toSpec * (Vector3.Distance(eye, S) - 0.2f) + _cam.transform.right * 0.02f;
            Quaternion rot = Quaternion.LookRotation(_cam.transform.up, -_cam.transform.right);
            // Solve the wrist position so that the thumb-index ring is centred on the view ray.
            var probe = new HandFrame(Handedness.Right);
            HandPoseBuilder.Build(probe, new Pose(Vector3.zero, rot), HandShape.OkRing);
            LensDetector.RingGeometry(probe, out Vector3 c0, out _, out _);
            _sim.SetScriptedHand(Handedness.Right, new Pose(ringCentre - c0, rot), HandShape.OkRing);
            HideLeft();
            yield return Wait(0.7f);
            Log.AppendLine($"lens: active gesture = {_app.Recognizer.IsActive(GestureId.LensRight)}");
            Capture("fig_lens");
        }

        private IEnumerator ShotGroundTruth()
        {
            yield return Release();
            Fresh();
            _app.SetColourMode(DefectColourMode.GroundTruth);
            _app.Volume.SetPeel(0.6f);
            _app.ExtractChart();
            yield return Wait(2.4f);
            Aim(S + new Vector3(0.19f, 0.16f, -0.66f), S + new Vector3(0.19f, 0.05f, 0.03f));
            yield return Wait(0.2f);
            Capture("fig_groundtruth");
            _app.SetColourMode(DefectColourMode.Class);
        }

        private IEnumerator ShotEnigma()
        {
            yield return Release();
            yield return _app.LoadSpecimen(1);
            Fresh();
            Quaternion fwd = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            _sim.SetScriptedHand(Handedness.Left, new Pose(S + new Vector3(-0.19f, -0.04f, -0.08f), fwd * PalmRight), HandShape.Open);
            _sim.SetScriptedHand(Handedness.Right, new Pose(S + new Vector3(0.19f, -0.04f, -0.08f), fwd * PalmLeft), HandShape.Open);
            Aim(S + new Vector3(0.12f, 0.2f, -0.52f), S + new Vector3(0f, 0f, 0f));
            yield return Wait(0.6f);
            Capture("fig_enigma_peel");
            HideLeft(); HideRight();
            yield return Wait(0.2f);
            _app.ResetView();
            Aim(S + new Vector3(0.12f, 0.2f, -0.52f), S);
            yield return Wait(0.3f);
            Capture("fig_enigma_solid");
            yield return _app.LoadSpecimen(0);
        }

        /// <summary>One pictogram per gesture: hands only, specimen hidden, far from it.</summary>
        private IEnumerator Pictograms()
        {
            _app.SpecimenRoot.gameObject.SetActive(false);
            _app.Chart.Collapse();
            Vector3 p = new Vector3(3f, 1.5f, 3f);
            Quaternion fwd = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            Quaternion up = Quaternion.LookRotation(Vector3.up, Vector3.back);
            Quaternion side = Quaternion.LookRotation(Vector3.up, Vector3.left);
            var icons = new List<(string name, System.Action pose)>
            {
                ("g_grab", () => { One(p, fwd, HandShape.Fist); }),
                ("g_scale", () => { Two(p, fwd, HandShape.Pinching, 0.08f); }),
                ("g_cut", () => { One(p, Quaternion.AngleAxis(-25f, Vector3.up) * up, HandShape.Open); }),
                ("g_peel", () => { _sim.SetScriptedHand(Handedness.Left, new Pose(p + new Vector3(-0.09f, 0, 0), fwd * PalmRight), HandShape.Open);
                                   _sim.SetScriptedHand(Handedness.Right, new Pose(p + new Vector3(0.09f, 0, 0), fwd * PalmLeft), HandShape.Open); }),
                ("g_probe", () => { One(p, Quaternion.LookRotation(new Vector3(0.3f, -0.2f, 1f), Vector3.up), HandShape.Point); }),
                ("g_extract", () => { One(p, Quaternion.LookRotation(new Vector3(0.3f, -0.2f, 1f), Vector3.up), HandShape.Pinching); }),
                ("g_brush", () => { Two(p, Quaternion.LookRotation(new Vector3(0f, -0.3f, 1f), Vector3.up), HandShape.Pinching, 0.07f); }),
                ("g_lens", () => { One(p - new Vector3(0, 0.09f, 0), side, HandShape.OkRing); }),
                ("g_twist", () => { One(p, Quaternion.AngleAxis(35f, Vector3.forward) * up, HandShape.Open); }),
                ("g_reset", () => { One(p, fwd * Quaternion.AngleAxis(180f, Vector3.forward), HandShape.Fist); }),
            };
            foreach ((string name, System.Action pose) in icons)
            {
                HideLeft(); HideRight();
                pose();
                yield return null; // let the source rebuild the joints
                yield return null;
                FrameHands(new Vector3(0.35f, 0.55f, -1f));
                yield return Wait(0.1f);
                Capture(name, IconSize, IconSize);
            }
            _app.SpecimenRoot.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------ helpers

        private void Fresh()
        {
            // ResetView places the specimen relative to the head, so put the head back first.
            _cam.transform.SetPositionAndRotation(new Vector3(0f, Bootstrap.DesktopEyeHeightMeters, 0f), Quaternion.Euler(12f, 0f, 0f));
            _app.SetHideUnselected(false);
            _app.Selection.Clear();
            _app.ResetView();
        }

        private void One(Vector3 wrist, Quaternion rot, HandShape shape) =>
            _sim.SetScriptedHand(Handedness.Right, new Pose(wrist, rot), shape);

        private void Two(Vector3 centre, Quaternion rot, HandShape shape, float halfGap)
        {
            _sim.SetScriptedHand(Handedness.Left, new Pose(centre - new Vector3(halfGap, 0, 0), rot), shape);
            _sim.SetScriptedHand(Handedness.Right, new Pose(centre + new Vector3(halfGap, 0, 0), rot), shape);
        }

        private void HideLeft() => _sim.SetScriptedHand(Handedness.Left, Pose.identity, HandShape.Open, false);
        private void HideRight() => _sim.SetScriptedHand(Handedness.Right, Pose.identity, HandShape.Open, false);

        /// <summary>Places a hand so that one joint lands exactly on <paramref name="target"/>.</summary>
        private void PlaceJoint(Handedness h, HandJoint joint, Vector3 target, Quaternion rot, HandShape shape)
        {
            var f = new HandFrame(h);
            HandPoseBuilder.Build(f, new Pose(Vector3.zero, rot), shape);
            _sim.SetScriptedHand(h, new Pose(target - f.Joint(joint), rot), shape);
        }

        /// <summary>Places a pinching hand so its pinch point is at <paramref name="target"/>.</summary>
        private void PlacePinch(Handedness h, Vector3 target, Quaternion rot)
        {
            var f = new HandFrame(h);
            HandPoseBuilder.Build(f, new Pose(Vector3.zero, rot), HandShape.Pinching);
            _sim.SetScriptedHand(h, new Pose(target - f.PinchPoint, rot), HandShape.Pinching);
        }

        private DefectRecord LargestDetected()
        {
            DefectRecord best = default;
            foreach (DefectRecord r in _app.Dataset.Catalog.Records)
            {
                if (r.GroundTruth == GroundTruthStatus.FalseNegative) continue;
                if (best.Id == 0 || r.VolumeMm3 > best.VolumeMm3) best = r;
            }
            return best;
        }

        /// <summary>Points the camera at the tracked hands from <paramref name="viewDir"/>, fitting them in view.</summary>
        private void FrameHands(Vector3 viewDir)
        {
            var b = new Bounds();
            bool any = false;
            foreach (HandFrame f in new[] { _sim.Left, _sim.Right })
            {
                if (!f.IsTracked) continue;
                foreach (Vector3 j in f.Positions)
                {
                    if (!any) { b = new Bounds(j, Vector3.zero); any = true; }
                    else b.Encapsulate(j);
                }
            }
            float radius = Mathf.Max(b.extents.magnitude, 0.05f);
            float dist = radius / Mathf.Sin(0.5f * _cam.fieldOfView * Mathf.Deg2Rad) * 1.15f;
            Aim(b.center - viewDir.normalized * dist, b.center);
        }

        private void Aim(Vector3 position, Vector3 lookAt) =>
            _cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        private void Capture(string name, int w = 0, int h = 0)
        {
            string tool = _app.Controller.Active != null ? _app.Controller.Active.Name : "none";
            string path = CaptureUtility.Capture(_cam, $"{OutputDir}/{name}.png", w > 0 ? w : Width, h > 0 ? h : Height);
            Log.AppendLine($"{name}: tool={tool} peel={_app.Volume.Peel:0.00} clip={_app.Volume.ClipEnabled} -> {path}");
        }
    }
}
