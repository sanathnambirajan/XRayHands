using NUnit.Framework;
using UnityEngine;
using XRayHands.Input;

namespace XRayHands.Tests
{
    public class HandFrameTests
    {
        [TestCase(0f)]
        [TestCase(0.3f)]
        [TestCase(0.7f)]
        [TestCase(1f)]
        public void Curl_RoundTripsThroughBuilder(float curl)
        {
            var f = HandFixtures.Hand(Handedness.Right, Vector3.zero, Quaternion.identity,
                new HandShape(0f, curl, curl, curl, curl, 0f));
            Assert.That(f.FingerCurl(Finger.Index), Is.EqualTo(curl).Within(0.01f));
            Assert.That(f.FingerCurl(Finger.Little), Is.EqualTo(curl).Within(0.01f));
            Assert.That(f.MeanFingerCurl, Is.EqualTo(curl).Within(0.01f));
        }

        [Test]
        public void PalmNormal_PointsDown_ForPalmDownHands([Values(Handedness.Left, Handedness.Right)] Handedness h)
        {
            var f = HandFixtures.Hand(h, Vector3.zero, Quaternion.identity, HandShape.Open);
            Assert.That(Vector3.Dot(f.PalmNormal, Vector3.down), Is.GreaterThan(0.95f));
            Assert.That(Vector3.Dot(f.PalmForward, Vector3.forward), Is.GreaterThan(0.95f));
        }

        [Test]
        public void PalmNormal_FollowsWristRotation()
        {
            var f = HandFixtures.Hand(Handedness.Right, Vector3.zero, HandFixtures.PalmUp, HandShape.Open);
            Assert.That(Vector3.Dot(f.PalmNormal, Vector3.up), Is.GreaterThan(0.95f));
        }

        [Test]
        public void Pinch_BringsTipsWithinOneCentimetre()
        {
            var open = HandFixtures.Hand(Handedness.Right, Vector3.zero, Quaternion.identity, HandShape.Open);
            var pinch = HandFixtures.Hand(Handedness.Right, Vector3.zero, Quaternion.identity, HandShape.Pinching);
            Assert.That(open.PinchDistance, Is.GreaterThan(0.04f));
            Assert.That(pinch.PinchDistance, Is.LessThan(0.01f));
        }

        [Test]
        public void LeftHand_IsMirrorOfRight()
        {
            var l = HandFixtures.Hand(Handedness.Left, Vector3.zero, Quaternion.identity, HandShape.Open);
            var r = HandFixtures.Hand(Handedness.Right, Vector3.zero, Quaternion.identity, HandShape.Open);
            Vector3 li = l.Joint(HandJoint.IndexTip), ri = r.Joint(HandJoint.IndexTip);
            Assert.That(li.x, Is.EqualTo(-ri.x).Within(1e-5f));
            Assert.That(li.z, Is.EqualTo(ri.z).Within(1e-5f));
        }

        [Test]
        public void CopyFrom_CopiesJointsAndDerivedValues()
        {
            var a = HandFixtures.Hand(Handedness.Right, new Vector3(1, 2, 3), Quaternion.identity, HandShape.Fist);
            var b = new HandFrame(Handedness.Right);
            b.CopyFrom(a);
            Assert.That(b.IsTracked, Is.True);
            Assert.That(b.MeanFingerCurl, Is.EqualTo(a.MeanFingerCurl).Within(1e-6f));
            Assert.That(b.Joint(HandJoint.IndexTip), Is.EqualTo(a.Joint(HandJoint.IndexTip)));
        }
    }
}
