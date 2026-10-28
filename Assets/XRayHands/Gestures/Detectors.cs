using UnityEngine;
using XRayHands.Input;

namespace XRayHands.Gestures
{
    /// <summary>
    /// A pure function from one frame of hand data to a confidence 0..1. Detectors hold no
    /// timing state; <see cref="GestureState"/> adds hysteresis and hold times on top.
    /// </summary>
    public interface IGestureDetector
    {
        /// <summary>Confidence that the gesture is being performed this frame.</summary>
        float Evaluate(in GestureContext ctx);
    }

    /// <summary>Base for single-hand detectors: returns 0 whenever that hand is not tracked.</summary>
    public abstract class HandDetector : IGestureDetector
    {
        /// <summary>Shared thresholds.</summary>
        protected readonly GestureTuning Tuning;

        /// <summary>Which hand this detector watches.</summary>
        public Handedness Hand { get; }

        /// <summary>Creates a detector for one hand.</summary>
        protected HandDetector(GestureTuning tuning, Handedness hand)
        {
            Tuning = tuning;
            Hand = hand;
        }

        /// <inheritdoc/>
        public float Evaluate(in GestureContext ctx)
        {
            HandFrame f = ctx.Hand(Hand);
            return f != null && f.IsTracked ? EvaluateHand(f, ctx) : 0f;
        }

        /// <summary>Confidence for a tracked hand.</summary>
        protected abstract float EvaluateHand(HandFrame f, in GestureContext ctx);
    }

    /// <summary>Thumb tip to index tip under 2.0 cm enters, over 3.5 cm releases.</summary>
    public sealed class PinchDetector : HandDetector
    {
        /// <summary>Creates a pinch detector.</summary>
        public PinchDetector(GestureTuning t, Handedness h) : base(t, h) { }

        /// <inheritdoc/>
        protected override float EvaluateHand(HandFrame f, in GestureContext ctx) =>
            Ramp.Below(f.PinchDistance, Tuning.PinchEnterDistanceMeters, Tuning.PinchExitDistanceMeters);
    }

    /// <summary>
    /// Flat palm inside the specimen (bounds + 5 cm): all four finger curls under 0.25.
    /// This is the cutting-plane gesture.
    /// </summary>
    public sealed class FlatPalmDetector : HandDetector
    {
        /// <summary>Creates a flat-palm detector.</summary>
        public FlatPalmDetector(GestureTuning t, Handedness h) : base(t, h) { }

        /// <inheritdoc/>
        protected override float EvaluateHand(HandFrame f, in GestureContext ctx)
        {
            float flat = FlatConfidence(f, Tuning);
            float margin = Tuning.FlatPalmBoundsMarginMeters;
            float inside = Ramp.Below(ctx.DistanceToSpecimen(f.PalmPosition), margin,
                margin + Tuning.DistanceRampWidthMeters);
            return Mathf.Min(flat, inside);
        }

        /// <summary>Flatness alone (shared with the peel detector): the most curled finger decides.</summary>
        public static float FlatConfidence(HandFrame f, GestureTuning t)
        {
            float maxCurl = Mathf.Max(Mathf.Max(f.FingerCurl(Finger.Index), f.FingerCurl(Finger.Middle)),
                Mathf.Max(f.FingerCurl(Finger.Ring), f.FingerCurl(Finger.Little)));
            return Ramp.Below(maxCurl, t.FlatPalmMaxCurl, t.FlatPalmExitCurl);
        }
    }

    /// <summary>
    /// Two flat palms facing each other (normal dot &lt; -0.7), both within 40 cm of the
    /// specimen. The palm distance itself is read by the peel tool, not here.
    /// </summary>
    public sealed class PeelDetector : IGestureDetector
    {
        private readonly GestureTuning _t;

        /// <summary>Creates a peel detector.</summary>
        public PeelDetector(GestureTuning tuning) { _t = tuning; }

        /// <inheritdoc/>
        public float Evaluate(in GestureContext ctx)
        {
            HandFrame l = ctx.Left, r = ctx.Right;
            if (l == null || r == null || !l.IsTracked || !r.IsTracked) return 0f;

            float flat = Mathf.Min(FlatPalmDetector.FlatConfidence(l, _t), FlatPalmDetector.FlatConfidence(r, _t));
            float facing = Ramp.Below(Vector3.Dot(l.PalmNormal, r.PalmNormal), _t.PeelMaxNormalDot, _t.PeelExitNormalDot);
            // Normals must also point *towards* each other, not away (back-to-back hands also give dot ≈ -1).
            Vector3 lToR = r.PalmPosition - l.PalmPosition;
            float towards = Vector3.Dot(l.PalmNormal, lToR) > 0f && Vector3.Dot(r.PalmNormal, -lToR) > 0f ? 1f : 0f;
            float maxD = _t.PeelMaxSpecimenDistanceMeters;
            float near = Mathf.Min(
                Ramp.Below(ctx.DistanceToSpecimen(l.PalmPosition), maxD, maxD + _t.DistanceRampWidthMeters),
                Ramp.Below(ctx.DistanceToSpecimen(r.PalmPosition), maxD, maxD + _t.DistanceRampWidthMeters));
            return Mathf.Min(Mathf.Min(flat, facing), Mathf.Min(towards, near));
        }

        /// <summary>Maps palm distance to peel amount 0..1 (0 = nothing peeled).</summary>
        public static float PeelAmount(HandFrame l, HandFrame r, GestureTuning t) =>
            Mathf.InverseLerp(t.PeelMinPalmDistanceMeters, t.PeelMaxPalmDistanceMeters,
                Vector3.Distance(l.PalmPosition, r.PalmPosition));
    }

    /// <summary>Index extended (curl &lt; 0.2), middle/ring/little curled (&gt; 0.6): the probe.</summary>
    public sealed class PointDetector : HandDetector
    {
        /// <summary>Creates a point detector.</summary>
        public PointDetector(GestureTuning t, Handedness h) : base(t, h) { }

        /// <inheritdoc/>
        protected override float EvaluateHand(HandFrame f, in GestureContext ctx)
        {
            float w = Tuning.CurlRampWidth;
            float index = Ramp.Below(f.FingerCurl(Finger.Index), Tuning.PointMaxIndexCurl, Tuning.PointMaxIndexCurl + w);
            float minOther = Mathf.Min(f.FingerCurl(Finger.Middle), Mathf.Min(f.FingerCurl(Finger.Ring), f.FingerCurl(Finger.Little)));
            float others = Ramp.Above(minOther, Tuning.PointMinOtherCurl, Tuning.PointMinOtherCurl - w);
            return Mathf.Min(index, others);
        }
    }

    /// <summary>
    /// Fist (mean curl &gt; 0.7) or all-finger pinch, with the palm within 8 cm of the specimen
    /// surface. Moves and rotates the specimen.
    /// </summary>
    public sealed class GrabDetector : HandDetector
    {
        /// <summary>Creates a grab detector.</summary>
        public GrabDetector(GestureTuning t, Handedness h) : base(t, h) { }

        /// <inheritdoc/>
        protected override float EvaluateHand(HandFrame f, in GestureContext ctx)
        {
            float fist = Ramp.Above(f.MeanFingerCurl, Tuning.GrabMinMeanCurl, Tuning.GrabMinMeanCurl - Tuning.CurlRampWidth);
            // "All-finger pinch": thumb tip close to index and middle tips (a cupped grab).
            float allPinch = Mathf.Min(
                Ramp.Below(f.PinchDistance, Tuning.PinchEnterDistanceMeters * 1.5f, Tuning.PinchExitDistanceMeters * 1.5f),
                Ramp.Below(Vector3.Distance(f.Joint(HandJoint.ThumbTip), f.Joint(HandJoint.MiddleTip)),
                    Tuning.PinchEnterDistanceMeters * 1.5f, Tuning.PinchExitDistanceMeters * 1.5f));
            float shape = Mathf.Max(fist, allPinch);
            float maxD = Tuning.GrabMaxSurfaceDistanceMeters;
            float near = Ramp.Below(ctx.DistanceToSpecimen(f.PalmPosition), maxD, maxD + Tuning.DistanceRampWidthMeters);
            return Mathf.Min(shape, near);
        }
    }

    /// <summary>
    /// "O" ring held up: thumb-index tips touching, other fingers extended, ring facing the eye.
    /// </summary>
    public sealed class LensDetector : HandDetector
    {
        /// <summary>Creates a lens detector.</summary>
        public LensDetector(GestureTuning t, Handedness h) : base(t, h) { }

        /// <inheritdoc/>
        protected override float EvaluateHand(HandFrame f, in GestureContext ctx)
        {
            float tips = Ramp.Below(f.PinchDistance, Tuning.LensMaxTipDistanceMeters, Tuning.LensMaxTipDistanceMeters + 0.015f);
            float maxOther = Mathf.Max(f.FingerCurl(Finger.Middle), Mathf.Max(f.FingerCurl(Finger.Ring), f.FingerCurl(Finger.Little)));
            float extended = Ramp.Below(maxOther, Tuning.LensMaxOtherCurl, Tuning.LensMaxOtherCurl + Tuning.CurlRampWidth);

            RingGeometry(f, out Vector3 centre, out Vector3 normal, out _);
            Vector3 toEye = ctx.Head.position - centre;
            float angle = Vector3.Angle(normal, toEye);
            angle = Mathf.Min(angle, 180f - angle); // the ring has no front or back
            float facing = Ramp.Below(angle, Tuning.LensMaxFacingAngleDegrees, Tuning.LensMaxFacingAngleDegrees + 15f);
            return Mathf.Min(tips, Mathf.Min(extended, facing));
        }

        /// <summary>
        /// Centre, plane normal and radius of the circle formed by thumb and index.
        /// Uses the knuckle, the index mid joints and the thumb joints so the estimate is
        /// stable even though the fingertips themselves touch.
        /// </summary>
        public static void RingGeometry(HandFrame f, out Vector3 centre, out Vector3 normal, out float radius)
        {
            Vector3 a = f.Joint(HandJoint.IndexProximal);
            Vector3 b = f.Joint(HandJoint.IndexIntermediate);
            Vector3 c = f.Joint(HandJoint.IndexDistal);
            Vector3 d = f.PinchPoint;
            Vector3 e = f.Joint(HandJoint.ThumbDistal);
            Vector3 g = f.Joint(HandJoint.ThumbProximal);
            centre = (a + b + c + d + e + g) / 6f;
            Vector3 n = Vector3.Cross(b - g, d - a);
            normal = n.sqrMagnitude > 1e-12f ? n.normalized : f.PalmForward;
            radius = 0.25f * (Vector3.Distance(a, d) + Vector3.Distance(b, e));
        }
    }

    /// <summary>Palm up and fist: the reset gesture (held 0.6 s via its GestureState).</summary>
    public sealed class ResetDetector : HandDetector
    {
        /// <summary>Creates a reset detector.</summary>
        public ResetDetector(GestureTuning t, Handedness h) : base(t, h) { }

        /// <inheritdoc/>
        protected override float EvaluateHand(HandFrame f, in GestureContext ctx)
        {
            float up = Ramp.Above(Vector3.Dot(f.PalmNormal, Vector3.up), Tuning.ResetMinPalmUpDot, Tuning.ResetMinPalmUpDot - 0.2f);
            float fist = Ramp.Above(f.MeanFingerCurl, Tuning.GrabMinMeanCurl, Tuning.GrabMinMeanCurl - Tuning.CurlRampWidth);
            return Mathf.Min(up, fist);
        }
    }

    /// <summary>
    /// Wrist roll relative to a reference, for the contrast dial during a palm cut. Not a
    /// recognizer: it measures a continuous angle once the cut is active.
    /// </summary>
    public sealed class TwistTracker
    {
        private Vector3 _referenceAcross;

        /// <summary>True after <see cref="Begin"/>.</summary>
        public bool HasReference { get; private set; }

        /// <summary>Stores the current hand roll as zero.</summary>
        public void Begin(HandFrame f)
        {
            _referenceAcross = Across(f);
            HasReference = true;
        }

        /// <summary>Signed roll in degrees around the hand's forward axis since <see cref="Begin"/>.</summary>
        public float Degrees(HandFrame f)
        {
            if (!HasReference) return 0f;
            return Vector3.SignedAngle(_referenceAcross, Across(f), f.PalmForward);
        }

        /// <summary>Clears the reference.</summary>
        public void End() => HasReference = false;

        private static Vector3 Across(HandFrame f) =>
            Vector3.ProjectOnPlane(f.Joint(HandJoint.IndexProximal) - f.Joint(HandJoint.LittleProximal), f.PalmForward).normalized;
    }
}
