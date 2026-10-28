using UnityEngine;
using XRayHands.Gestures;
using XRayHands.Input;

namespace XRayHands.Tests
{
    /// <summary>
    /// Synthetic hand frames for detector tests. The specimen is a 20 cm cube at the origin;
    /// the head is 50 cm behind it, looking along +Z.
    /// </summary>
    public static class HandFixtures
    {
        public const float SpecimenSize = 0.2f;
        public static readonly Pose Head = new Pose(new Vector3(0f, 0f, -0.5f), Quaternion.identity);

        public static Matrix4x4 SpecimenMatrix => Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * SpecimenSize);

        /// <summary>A hand with its wrist at <paramref name="wrist"/>, rotated by <paramref name="rotation"/>.</summary>
        public static HandFrame Hand(Handedness h, Vector3 wrist, Quaternion rotation, HandShape shape)
        {
            var f = new HandFrame(h);
            HandPoseBuilder.Build(f, new Pose(wrist, rotation), shape);
            return f;
        }

        /// <summary>An untracked hand.</summary>
        public static HandFrame Untracked(Handedness h) => new HandFrame(h) { IsTracked = false };

        /// <summary>Context with the given hands and the default specimen/head.</summary>
        public static GestureContext Context(HandFrame left, HandFrame right) =>
            new GestureContext(left ?? Untracked(Handedness.Left), right ?? Untracked(Handedness.Right), Head, SpecimenMatrix);

        /// <summary>Palm-down, fingers forward.</summary>
        public static Quaternion PalmDown => Quaternion.identity;

        /// <summary>
        /// Palm vertical facing +X / -X for the peel gesture: a right hand rolled so its palm
        /// faces left (-X), a left hand rolled so its palm faces right (+X).
        /// </summary>
        public static Quaternion PalmFacingLeft => Quaternion.AngleAxis(-90f, Vector3.forward);
        public static Quaternion PalmFacingRight => Quaternion.AngleAxis(90f, Vector3.forward);

        /// <summary>Palm up (rolled 180° about the forearm).</summary>
        public static Quaternion PalmUp => Quaternion.AngleAxis(180f, Vector3.forward);
    }
}
