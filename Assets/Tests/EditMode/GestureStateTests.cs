using NUnit.Framework;
using UnityEngine;
using XRayHands.Gestures;
using XRayHands.Input;

namespace XRayHands.Tests
{
    /// <summary>Hysteresis, hold times and jitter rejection of the shared state machine.</summary>
    public class GestureStateTests
    {
        private const float Dt = 1f / 72f; // Quest 3 frame time at 72 Hz

        [Test]
        public void Enters_OnlyAfterHoldTime()
        {
            var s = new GestureState(0.9f, 0.1f, enterHoldSeconds: 0.15f, minActiveSeconds: 0f);
            for (int i = 0; i < 10; i++) s.Update(1f, Dt); // 0.139 s
            Assert.That(s.IsActive, Is.False);
            s.Update(1f, Dt);                               // 0.153 s
            Assert.That(s.IsActive, Is.True);
            Assert.That(s.JustEntered, Is.True);
            s.Update(1f, Dt);
            Assert.That(s.JustEntered, Is.False);
        }

        [Test]
        public void ZeroHold_EntersOnFirstFrame()
        {
            var s = new GestureState(0.9f, 0.1f, 0f, 0f);
            s.Update(0.95f, Dt);
            Assert.That(s.IsActive, Is.True);
        }

        [Test]
        public void Hysteresis_MidBandConfidenceKeepsCurrentState()
        {
            var s = new GestureState(0.9f, 0.1f, 0f, 0f);
            s.Update(0.5f, Dt);
            Assert.That(s.IsActive, Is.False, "mid-band must not enter");
            s.Update(1f, Dt);
            s.Update(0.5f, Dt);
            Assert.That(s.IsActive, Is.True, "mid-band must not exit");
            s.Update(0.05f, Dt);
            Assert.That(s.IsActive, Is.False);
            Assert.That(s.JustExited, Is.True);
        }

        [Test]
        public void Jitter_AroundEnterThreshold_DoesNotFlicker()
        {
            var s = new GestureState(0.9f, 0.1f, 0.1f, 0.1f);
            int transitions = 0;
            var rng = new System.Random(1);
            for (int i = 0; i < 300; i++)
            {
                float c = 0.85f + 0.1f * (float)rng.NextDouble(); // 0.85..0.95 noise
                s.Update(c, Dt);
                if (s.JustEntered || s.JustExited) transitions++;
            }
            Assert.That(transitions, Is.LessThanOrEqualTo(1), "noise near the threshold may enter once but never toggle");
        }

        [Test]
        public void MinActiveTime_PreventsImmediateExit()
        {
            var s = new GestureState(0.9f, 0.1f, 0f, 0.2f);
            s.Update(1f, Dt);
            s.Update(0f, Dt);
            Assert.That(s.IsActive, Is.True);
            for (int i = 0; i < 15; i++) s.Update(0f, Dt);
            Assert.That(s.IsActive, Is.False);
        }

        [Test]
        public void Recognizer_FlatPalmNeedsHoldThenReleases()
        {
            var t = GestureTuning.CreateDefault();
            try
            {
                var rec = new GestureRecognizer(t);
                var open = HandFixtures.Hand(Handedness.Right, new Vector3(0f, 0f, -0.05f), Quaternion.identity, HandShape.Open);
                var fist = HandFixtures.Hand(Handedness.Right, new Vector3(0f, 0f, -0.05f), Quaternion.identity, HandShape.Fist);
                var ctxOpen = HandFixtures.Context(null, open);
                var ctxFist = HandFixtures.Context(null, fist);

                rec.Update(ctxOpen, 0.05f);
                Assert.That(rec.IsActive(GestureId.FlatPalmRight), Is.False);
                for (int i = 0; i < 3; i++) rec.Update(ctxOpen, 0.05f);
                Assert.That(rec.IsActive(GestureId.FlatPalmRight), Is.True);
                Assert.That(rec.IsActive(GestureId.GrabRight), Is.False);

                for (int i = 0; i < 3; i++) rec.Update(ctxFist, 0.05f);
                Assert.That(rec.IsActive(GestureId.FlatPalmRight), Is.False);
                Assert.That(rec.IsActive(GestureId.GrabRight), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(t);
            }
        }

        [Test]
        public void ForHand_MapsLeftVariantToRight()
        {
            Assert.That(GestureRecognizer.ForHand(GestureId.PinchLeft, Handedness.Right), Is.EqualTo(GestureId.PinchRight));
            Assert.That(GestureRecognizer.ForHand(GestureId.LensLeft, Handedness.Left), Is.EqualTo(GestureId.LensLeft));
        }
    }
}
