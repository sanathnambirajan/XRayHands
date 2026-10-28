using UnityEngine;
using XRayHands.Core;
using XRayHands.Gestures;
using XRayHands.Input;

namespace XRayHands.Interaction
{
    /// <summary>
    /// One-hand grab (fist or all-finger pinch near the specimen): the specimen follows the
    /// hand rigidly. The offset between hand and specimen is captured at grab time, so the
    /// specimen never jumps to the hand.
    /// </summary>
    public sealed class GrabTool : ITool
    {
        private readonly ISpecimenHost _host;
        private readonly IFeedbackSink _fx;
        private Handedness _hand;
        private Vector3 _localPos;
        private Quaternion _localRot;

        /// <summary>Creates the tool.</summary>
        public GrabTool(ISpecimenHost host, IFeedbackSink fx) { _host = host; _fx = fx; }

        /// <inheritdoc/>
        public string Name => "Grab";

        /// <inheritdoc/>
        public HandUsage Usage => HandPoses.Of(_hand);

        /// <inheritdoc/>
        public bool CanStart(in ToolFrame f) => f.Active(GestureId.GrabLeft, Handedness.Right) || f.Active(GestureId.GrabLeft, Handedness.Left);

        /// <inheritdoc/>
        public void Begin(in ToolFrame f)
        {
            _hand = f.Active(GestureId.GrabLeft, Handedness.Right) ? Handedness.Right : Handedness.Left;
            HandFrame h = f.Context.Hand(_hand);
            Quaternion handRot = HandPoses.Rotation(h);
            Transform root = _host.SpecimenRoot;
            _localRot = Quaternion.Inverse(handRot) * root.rotation;
            _localPos = Quaternion.Inverse(handRot) * (root.position - h.PalmPosition);
            _fx.PlayRecognised(h.PalmPosition);
        }

        /// <inheritdoc/>
        public bool Tick(in ToolFrame f)
        {
            HandFrame h = f.Context.Hand(_hand);
            if (!f.Active(GestureId.GrabLeft, _hand) || !h.IsTracked) return false;
            Quaternion handRot = HandPoses.Rotation(h);
            _host.SpecimenRoot.SetPositionAndRotation(h.PalmPosition + handRot * _localPos, handRot * _localRot);
            return true;
        }

        /// <inheritdoc/>
        public void End() { }
    }

    /// <summary>
    /// Two-hand pinch: scale about the midpoint between the hands. The magnification label
    /// ("×8.0") floats between the hands.
    /// </summary>
    public sealed class ScaleTool : ITool
    {
        /// <summary>Pinch midpoint must be within this distance of the specimen to start, metres.</summary>
        public const float MaxStartDistanceMeters = 0.25f;
        private const float MinHandDistanceMeters = 0.02f;
        private const string LabelKey = "scale";

        private readonly ISpecimenHost _host;
        private readonly IFeedbackSink _fx;
        private float _startDistance;
        private float _startScale;

        /// <summary>Creates the tool.</summary>
        public ScaleTool(ISpecimenHost host, IFeedbackSink fx) { _host = host; _fx = fx; }

        /// <inheritdoc/>
        public string Name => "Scale";

        /// <inheritdoc/>
        public HandUsage Usage => HandUsage.Both;

        /// <inheritdoc/>
        public bool CanStart(in ToolFrame f)
        {
            if (!f.Gestures.IsActive(GestureId.PinchLeft) || !f.Gestures.IsActive(GestureId.PinchRight)) return false;
            Vector3 mid = 0.5f * (f.Context.Left.PinchPoint + f.Context.Right.PinchPoint);
            return f.Context.DistanceToSpecimen(mid) <= MaxStartDistanceMeters;
        }

        /// <inheritdoc/>
        public void Begin(in ToolFrame f)
        {
            _startDistance = Mathf.Max(Vector3.Distance(f.Context.Left.PinchPoint, f.Context.Right.PinchPoint), MinHandDistanceMeters);
            _startScale = _host.DisplayScale;
            _fx.PlayRecognised(0.5f * (f.Context.Left.PinchPoint + f.Context.Right.PinchPoint));
        }

        /// <inheritdoc/>
        public bool Tick(in ToolFrame f)
        {
            if (!f.Gestures.IsActive(GestureId.PinchLeft) || !f.Gestures.IsActive(GestureId.PinchRight)) return false;
            Vector3 a = f.Context.Left.PinchPoint, b = f.Context.Right.PinchPoint;
            Vector3 mid = 0.5f * (a + b);
            float distance = Mathf.Max(Vector3.Distance(a, b), MinHandDistanceMeters);

            float before = _host.DisplayScale;
            _host.SetDisplayScale(_startScale * distance / _startDistance);
            float ratio = _host.DisplayScale / before;
            // Scale about the hands' midpoint, not the specimen centre: the point between the
            // fingers stays put, which is what a stretch gesture should feel like.
            Transform root = _host.SpecimenRoot;
            root.position = mid + (root.position - mid) * ratio;

            _fx.ShowLabel(LabelKey, mid + Vector3.up * 0.03f, PhysicalUnits.FormatScale(_host.DisplayScale));
            return true;
        }

        /// <inheritdoc/>
        public void End() => _fx.HideLabel(LabelKey);
    }

    /// <summary>
    /// Flat palm through the specimen = cutting plane. The near side (between plane and user)
    /// is removed and the cut face shows the real CT slice. A wrist twist while cutting
    /// changes the slice window width (contrast dial). The last cut stays when the hand leaves,
    /// so the face can be inspected and probed; reset removes it.
    /// </summary>
    public sealed class SliceTool : ITool
    {
        /// <summary>Wrist roll for a doubling/halving of window width, degrees.</summary>
        public const float TwistDegreesPerOctave = 45f;
        private const string LabelKey = "cut";

        private readonly ISpecimenHost _host;
        private readonly IFeedbackSink _fx;
        private readonly TwistTracker _twist = new TwistTracker();
        private Handedness _hand;
        private float _startWidth;

        /// <summary>Creates the tool.</summary>
        public SliceTool(ISpecimenHost host, IFeedbackSink fx) { _host = host; _fx = fx; }

        /// <inheritdoc/>
        public string Name => "Slice";

        /// <inheritdoc/>
        public HandUsage Usage => HandPoses.Of(_hand);

        /// <summary>Current plane position along its normal, mm from the volume centre (for tests and the label).</summary>
        public float PlaneOffsetMm { get; private set; }

        /// <inheritdoc/>
        public bool CanStart(in ToolFrame f) => f.Active(GestureId.FlatPalmLeft, Handedness.Right) || f.Active(GestureId.FlatPalmLeft, Handedness.Left);

        /// <inheritdoc/>
        public void Begin(in ToolFrame f)
        {
            _hand = f.Active(GestureId.FlatPalmLeft, Handedness.Right) ? Handedness.Right : Handedness.Left;
            _twist.Begin(f.Context.Hand(_hand));
            _startWidth = _host.Volume.Window.y;
            _fx.PlayRecognised(f.Context.Hand(_hand).PalmPosition);
        }

        /// <inheritdoc/>
        public bool Tick(in ToolFrame f)
        {
            HandFrame h = f.Context.Hand(_hand);
            if (!f.Active(GestureId.FlatPalmLeft, _hand) || !h.IsTracked) return false;

            Vector3 p = h.PalmPosition;
            Vector3 n = h.PalmNormal;
            // Keep the half-space away from the user: flip the normal if it points at the head.
            if (Vector3.Dot(n, p - f.Context.Head.position) < 0f) n = -n;
            _host.Volume.SetClipPlaneWorld(p, n);

            // Contrast dial: each 45° of roll doubles (or halves) the window width.
            float width = _startWidth * Mathf.Pow(2f, _twist.Degrees(h) / TwistDegreesPerOctave);
            _host.Volume.SetWindow(_host.Volume.Window.x, width);

            Transform proxy = _host.Volume.ProxyTransform;
            Vector3 centre = proxy.position;
            float metresFromCentre = Vector3.Dot(p - centre, n);
            float mmPerMetre = PhysicalUnits.MillimetresPerMetre / _host.DisplayScale; // world metres → physical mm
            PlaneOffsetMm = metresFromCentre * mmPerMetre;
            float ringRadius = 0.5f * proxy.lossyScale.magnitude;
            _fx.ShowCutRing(centre + n * metresFromCentre, n, ringRadius);
            _fx.ShowLabel(LabelKey, p + Vector3.up * 0.06f, $"cut {PlaneOffsetMm:+0.000;-0.000} mm  w {width:0.00}");
            return true;
        }

        /// <inheritdoc/>
        public void End()
        {
            _twist.End();
            _fx.ShowCutRing(null, Vector3.up, 0f);
            _fx.HideLabel(LabelKey);
        }
    }

    /// <summary>
    /// Two facing flat palms: the distance between them sets the peel threshold. Low-density
    /// material fades to glass with silhouettes; dense material and defects stay. The value
    /// persists after the hands leave; reset clears it.
    /// </summary>
    public sealed class PeelTool : ITool
    {
        /// <summary>Peel amount 1.0 maps to this density threshold (keeps the densest metal visible).</summary>
        public const float MaxThreshold = 0.92f;
        private const string LabelKey = "peel";
        private const string LineKey = "peel";

        private readonly ISpecimenHost _host;
        private readonly IFeedbackSink _fx;

        /// <summary>Creates the tool.</summary>
        public PeelTool(ISpecimenHost host, IFeedbackSink fx) { _host = host; _fx = fx; }

        /// <inheritdoc/>
        public string Name => "Peel";

        /// <inheritdoc/>
        public HandUsage Usage => HandUsage.Both;

        /// <summary>Last peel amount 0..1.</summary>
        public float Amount { get; private set; }

        /// <inheritdoc/>
        public bool CanStart(in ToolFrame f) => f.Gestures.IsActive(GestureId.Peel);

        /// <inheritdoc/>
        public void Begin(in ToolFrame f) => _fx.PlayRecognised(0.5f * (f.Context.Left.PalmPosition + f.Context.Right.PalmPosition));

        /// <inheritdoc/>
        public bool Tick(in ToolFrame f)
        {
            if (!f.Gestures.IsActive(GestureId.Peel)) return false;
            HandFrame l = f.Context.Left, r = f.Context.Right;
            Amount = PeelDetector.PeelAmount(l, r, f.Tuning);
            _host.Volume.SetPeel(Amount * MaxThreshold);
            _fx.ShowDashedLine(LineKey, l.PalmPosition, r.PalmPosition);
            _fx.ShowLabel(LabelKey, 0.5f * (l.PalmPosition + r.PalmPosition) + Vector3.up * 0.04f, $"Peel {Amount * 100f:0}%");
            return true;
        }

        /// <inheritdoc/>
        public void End()
        {
            _fx.HideDashedLine(LineKey);
            _fx.HideLabel(LabelKey);
        }
    }

    /// <summary>
    /// "O" ring held up = X-ray magic lens: inside the circle (seen from the eye) the specimen
    /// renders peeled; outside it stays solid (focus + context). Runs alongside other tools on
    /// the free hand.
    /// </summary>
    public sealed class LensTool
    {
        private readonly ISpecimenHost _host;
        private bool _active;

        /// <summary>Creates the tool.</summary>
        public LensTool(ISpecimenHost host) { _host = host; }

        /// <summary>True while a lens is shown.</summary>
        public bool IsActive => _active;

        /// <summary>Updates the lens from whichever hand holds an "O" (and is not busy).</summary>
        public void Tick(in ToolFrame f, HandUsage busy)
        {
            for (int i = 0; i < 2; i++)
            {
                var hand = (Handedness)i;
                if ((busy & HandPoses.Of(hand)) != 0 || !f.Active(GestureId.LensLeft, hand)) continue;
                LensDetector.RingGeometry(f.Context.Hand(hand), out Vector3 centre, out Vector3 normal, out float radius);
                _host.Volume.SetLens(centre, normal, radius);
                _active = true;
                return;
            }
            if (_active) _host.Volume.DisableLens();
            _active = false;
        }
    }

    /// <summary>Palm-up fist held 0.6 s: reset the view, with a confirmation ripple.</summary>
    public sealed class ResetTool
    {
        private readonly ISpecimenHost _host;
        private readonly IFeedbackSink _fx;

        /// <summary>Creates the tool.</summary>
        public ResetTool(ISpecimenHost host, IFeedbackSink fx) { _host = host; _fx = fx; }

        /// <summary>Number of resets performed (tests).</summary>
        public int ResetCount { get; private set; }

        /// <summary>Fires the reset on the frame the gesture becomes active.</summary>
        public void Tick(in ToolFrame f)
        {
            for (int i = 0; i < 2; i++)
            {
                var hand = (Handedness)i;
                if (!f.Gestures.State(GestureRecognizer.ForHand(GestureId.ResetLeft, hand)).JustEntered) continue;
                _host.ResetView();
                _host.Selection.Clear();
                ResetCount++;
                _fx.Pulse(f.Context.Hand(hand).PalmPosition, 0.08f);
                _fx.PlayRecognised(f.Context.Hand(hand).PalmPosition);
                return;
            }
        }
    }
}
