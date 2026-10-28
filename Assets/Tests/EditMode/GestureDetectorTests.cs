using NUnit.Framework;
using UnityEngine;
using XRayHands.Gestures;
using XRayHands.Input;

namespace XRayHands.Tests
{
    /// <summary>Positive, negative and near-miss cases for every detector.</summary>
    public class GestureDetectorTests
    {
        private GestureTuning _t;

        [SetUp]
        public void SetUp() => _t = GestureTuning.CreateDefault();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_t);

        // Hand positions: inside the 20 cm specimen, just outside, and far away.
        private static readonly Vector3 Inside = new Vector3(0f, 0f, -0.08f);
        private static readonly Vector3 Far = new Vector3(0f, 0.6f, -0.6f);

        // ------------------------------------------------------------------ pinch

        [Test]
        public void Pinch_DetectedWhenTipsTouch()
        {
            var r = HandFixtures.Hand(Handedness.Right, Far, Quaternion.identity, HandShape.Pinching);
            Assert.That(new PinchDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(1f));
        }

        [Test]
        public void Pinch_NotDetectedForOpenHand()
        {
            var r = HandFixtures.Hand(Handedness.Right, Far, Quaternion.identity, HandShape.Open);
            Assert.That(new PinchDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(0f));
        }

        [Test]
        public void Pinch_IsZeroForUntrackedHand()
        {
            Assert.That(new PinchDetector(_t, Handedness.Left).Evaluate(HandFixtures.Context(null, null)), Is.EqualTo(0f));
        }

        // ------------------------------------------------------------------ flat palm

        [Test]
        public void FlatPalm_DetectedInsideSpecimen()
        {
            var r = HandFixtures.Hand(Handedness.Right, Inside, Quaternion.identity, HandShape.Open);
            Assert.That(new FlatPalmDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(1f));
        }

        [Test]
        public void FlatPalm_NearMiss_SlightlyCurledFingersRejected()
        {
            var r = HandFixtures.Hand(Handedness.Right, Inside, Quaternion.identity, new HandShape(0f, 0.5f, 0.5f, 0.5f, 0.5f, 0f));
            Assert.That(new FlatPalmDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(0f));
        }

        [Test]
        public void FlatPalm_RejectedFarFromSpecimen()
        {
            var r = HandFixtures.Hand(Handedness.Right, Far, Quaternion.identity, HandShape.Open);
            Assert.That(new FlatPalmDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(0f));
        }

        // ------------------------------------------------------------------ peel

        [Test]
        public void Peel_DetectedForFacingPalms()
        {
            var l = HandFixtures.Hand(Handedness.Left, new Vector3(-0.15f, 0f, -0.05f), HandFixtures.PalmFacingRight, HandShape.Open);
            var r = HandFixtures.Hand(Handedness.Right, new Vector3(0.15f, 0f, -0.05f), HandFixtures.PalmFacingLeft, HandShape.Open);
            Assert.That(new PeelDetector(_t).Evaluate(HandFixtures.Context(l, r)), Is.EqualTo(1f));
        }

        [Test]
        public void Peel_RejectedForBackToBackPalms()
        {
            // Normals are anti-parallel (dot = -1) but point away from each other.
            var l = HandFixtures.Hand(Handedness.Left, new Vector3(-0.15f, 0f, -0.05f), HandFixtures.PalmFacingLeft, HandShape.Open);
            var r = HandFixtures.Hand(Handedness.Right, new Vector3(0.15f, 0f, -0.05f), HandFixtures.PalmFacingRight, HandShape.Open);
            Assert.That(new PeelDetector(_t).Evaluate(HandFixtures.Context(l, r)), Is.EqualTo(0f));
        }

        [Test]
        public void Peel_RejectedForParallelPalms()
        {
            var l = HandFixtures.Hand(Handedness.Left, new Vector3(-0.15f, 0f, -0.05f), Quaternion.identity, HandShape.Open);
            var r = HandFixtures.Hand(Handedness.Right, new Vector3(0.15f, 0f, -0.05f), Quaternion.identity, HandShape.Open);
            Assert.That(new PeelDetector(_t).Evaluate(HandFixtures.Context(l, r)), Is.EqualTo(0f));
        }

        [Test]
        public void PeelAmount_MapsPalmDistanceLinearly()
        {
            var l = HandFixtures.Hand(Handedness.Left, new Vector3(-0.1f, 0f, 0f), HandFixtures.PalmFacingRight, HandShape.Open);
            var near = HandFixtures.Hand(Handedness.Right, new Vector3(-0.1f + _t.PeelMinPalmDistanceMeters, 0f, 0f), HandFixtures.PalmFacingLeft, HandShape.Open);
            Assert.That(PeelDetector.PeelAmount(l, near, _t), Is.EqualTo(0f).Within(0.05f));
            var far = HandFixtures.Hand(Handedness.Right, new Vector3(-0.1f + _t.PeelMaxPalmDistanceMeters, 0f, 0f), HandFixtures.PalmFacingLeft, HandShape.Open);
            Assert.That(PeelDetector.PeelAmount(l, far, _t), Is.EqualTo(1f).Within(0.05f));
        }

        // ------------------------------------------------------------------ point

        [Test]
        public void Point_Detected()
        {
            var r = HandFixtures.Hand(Handedness.Right, Far, Quaternion.identity, HandShape.Point);
            Assert.That(new PointDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(1f));
        }

        [Test]
        public void Point_RejectedForOpenHand_AndFist()
        {
            var d = new PointDetector(_t, Handedness.Right);
            var open = HandFixtures.Hand(Handedness.Right, Far, Quaternion.identity, HandShape.Open);
            var fist = HandFixtures.Hand(Handedness.Right, Far, Quaternion.identity, HandShape.Fist);
            Assert.That(d.Evaluate(HandFixtures.Context(null, open)), Is.EqualTo(0f));
            Assert.That(d.Evaluate(HandFixtures.Context(null, fist)), Is.EqualTo(0f));
        }

        // ------------------------------------------------------------------ grab

        [Test]
        public void Grab_DetectedForFistNearSpecimen()
        {
            var r = HandFixtures.Hand(Handedness.Right, Inside, Quaternion.identity, HandShape.Fist);
            Assert.That(new GrabDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(1f));
        }

        [Test]
        public void Grab_RejectedForFistFarAway()
        {
            var r = HandFixtures.Hand(Handedness.Right, Far, Quaternion.identity, HandShape.Fist);
            Assert.That(new GrabDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(0f));
        }

        [Test]
        public void Grab_RejectedForOpenHandNearSpecimen()
        {
            var r = HandFixtures.Hand(Handedness.Right, Inside, Quaternion.identity, HandShape.Open);
            Assert.That(new GrabDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(0f));
        }

        // ------------------------------------------------------------------ lens

        [Test]
        public void Lens_DetectedWhenRingFacesEye()
        {
            // The index curls in the hand's local Y-Z plane, so the ring's axis is the hand's
            // local X. Fingers up (local Z = world up) with local Y = world left puts local X
            // = world back, i.e. the ring axis points at the eye, as when you look through an "OK".
            Quaternion ringFacingEye = Quaternion.LookRotation(Vector3.up, Vector3.left);
            var r = HandFixtures.Hand(Handedness.Right, new Vector3(0f, -0.08f, -0.3f), ringFacingEye, HandShape.OkRing);
            float c = new LensDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r));
            Assert.That(c, Is.GreaterThan(0.5f), $"lens confidence {c}");
        }

        [Test]
        public void Lens_RejectedForPlainPinchWithCurledFingers()
        {
            var r = HandFixtures.Hand(Handedness.Right, new Vector3(0f, -0.08f, -0.3f),
                Quaternion.LookRotation(Vector3.up, Vector3.left), new HandShape(0.3f, 0.5f, 0.8f, 0.8f, 0.8f, 1f));
            Assert.That(new LensDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(0f));
        }

        // ------------------------------------------------------------------ reset

        [Test]
        public void Reset_DetectedForPalmUpFist()
        {
            var r = HandFixtures.Hand(Handedness.Right, Far, HandFixtures.PalmUp, HandShape.Fist);
            Assert.That(new ResetDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(1f));
        }

        [Test]
        public void Reset_RejectedForPalmDownFist()
        {
            var r = HandFixtures.Hand(Handedness.Right, Far, Quaternion.identity, HandShape.Fist);
            Assert.That(new ResetDetector(_t, Handedness.Right).Evaluate(HandFixtures.Context(null, r)), Is.EqualTo(0f));
        }

        // ------------------------------------------------------------------ twist

        [Test]
        public void Twist_MeasuresWristRoll()
        {
            var tracker = new TwistTracker();
            var a = HandFixtures.Hand(Handedness.Right, Inside, Quaternion.identity, HandShape.Open);
            tracker.Begin(a);
            var b = HandFixtures.Hand(Handedness.Right, Inside, Quaternion.AngleAxis(30f, Vector3.forward), HandShape.Open);
            Assert.That(Mathf.Abs(tracker.Degrees(b)), Is.EqualTo(30f).Within(1f));
        }

        // ------------------------------------------------------------------ geometry

        [Test]
        public void DistanceToSpecimen_IsZeroInside_AndEuclideanOutside()
        {
            var ctx = HandFixtures.Context(null, null);
            Assert.That(ctx.DistanceToSpecimen(Vector3.zero), Is.EqualTo(0f));
            Assert.That(ctx.DistanceToSpecimen(new Vector3(0.3f, 0f, 0f)), Is.EqualTo(0.2f).Within(1e-5f));
        }
    }
}
