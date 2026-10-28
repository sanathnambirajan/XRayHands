using UnityEngine;
using XRayHands.Input;

namespace XRayHands.Gestures
{
    /// <summary>
    /// Everything a detector may look at in one frame: both hands, the head, and where the
    /// specimen is. Passed by reference (<c>in</c>) so building it costs no allocation.
    /// </summary>
    public readonly struct GestureContext
    {
        /// <summary>Left hand (check <see cref="HandFrame.IsTracked"/>).</summary>
        public readonly HandFrame Left;

        /// <summary>Right hand (check <see cref="HandFrame.IsTracked"/>).</summary>
        public readonly HandFrame Right;

        /// <summary>Eye-centre pose.</summary>
        public readonly Pose Head;

        /// <summary>Maps the specimen's unit proxy cube [-0.5, 0.5]^3 to world space.</summary>
        public readonly Matrix4x4 SpecimenLocalToWorld;

        /// <summary>Inverse of <see cref="SpecimenLocalToWorld"/>.</summary>
        public readonly Matrix4x4 SpecimenWorldToLocal;

        /// <summary>Creates a context; the inverse matrix is computed once here.</summary>
        public GestureContext(HandFrame left, HandFrame right, Pose head, Matrix4x4 specimenLocalToWorld)
        {
            Left = left;
            Right = right;
            Head = head;
            SpecimenLocalToWorld = specimenLocalToWorld;
            SpecimenWorldToLocal = specimenLocalToWorld.inverse;
        }

        /// <summary>The frame for <paramref name="h"/>.</summary>
        public HandFrame Hand(Handedness h) => h == Handedness.Left ? Left : Right;

        /// <summary>
        /// World-space distance from <paramref name="worldPoint"/> to the specimen box
        /// (0 when inside). Works for any rotation and non-uniform scale of the specimen.
        /// </summary>
        public float DistanceToSpecimen(Vector3 worldPoint)
        {
            Vector3 local = SpecimenWorldToLocal.MultiplyPoint3x4(worldPoint);
            Vector3 clamped = new Vector3(
                Mathf.Clamp(local.x, -0.5f, 0.5f),
                Mathf.Clamp(local.y, -0.5f, 0.5f),
                Mathf.Clamp(local.z, -0.5f, 0.5f));
            Vector3 closestWorld = SpecimenLocalToWorld.MultiplyPoint3x4(clamped);
            return Vector3.Distance(worldPoint, closestWorld);
        }
    }
}
