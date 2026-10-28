using UnityEngine;

namespace XRayHands.Input
{
    /// <summary>
    /// One hand's pose for one frame: 26 joint positions/rotations in world space, plus values
    /// derived from them that every gesture detector needs (finger curls, palm pose, pinch gap).
    /// </summary>
    /// <remarks>
    /// Why a reusable class instead of a struct: a struct holding 26-element arrays would still
    /// share those arrays when copied, which invites aliasing bugs, and allocating fresh arrays
    /// per frame would create garbage on Quest. Each <see cref="IHandSource"/> owns two
    /// instances and refills them in place every frame (zero allocations in Update).
    /// </remarks>
    public sealed class HandFrame
    {
        /// <summary>Number of joints (OpenXR hand model).</summary>
        public const int JointCount = 26;

        // A fully curled finger bends roughly 90° at each of its three joints; we normalise the
        // summed bend to 0..1 against slightly less than that so a firm fist reads as ~1.
        private const float FingerFullCurlDegrees = 230f;
        // The thumb has only two bending joints and a smaller range.
        private const float ThumbFullCurlDegrees = 110f;

        /// <summary>Joint positions in world space (metres), indexed by <see cref="HandJoint"/>.</summary>
        public readonly Vector3[] Positions = new Vector3[JointCount];

        /// <summary>Joint rotations in world space, indexed by <see cref="HandJoint"/>.</summary>
        public readonly Quaternion[] Rotations = new Quaternion[JointCount];

        /// <summary>Finger curl 0 (straight) .. 1 (fully curled), indexed by <see cref="Finger"/>.</summary>
        public readonly float[] Curl = new float[5];

        /// <summary>Which hand this frame belongs to.</summary>
        public Handedness Handedness { get; }

        /// <summary>True if the tracker currently sees this hand. All other values are stale when false.</summary>
        public bool IsTracked { get; set; }

        /// <summary>Palm centre in world space.</summary>
        public Vector3 PalmPosition { get; private set; }

        /// <summary>Unit vector pointing out of the palm (the side you would push with).</summary>
        public Vector3 PalmNormal { get; private set; }

        /// <summary>Unit vector from wrist towards the middle finger.</summary>
        public Vector3 PalmForward { get; private set; }

        /// <summary>Distance between thumb tip and index tip in metres.</summary>
        public float PinchDistance { get; private set; }

        /// <summary>Midpoint between thumb tip and index tip: where a pinch "holds" things.</summary>
        public Vector3 PinchPoint { get; private set; }

        /// <summary>Mean curl of index, middle, ring and little finger.</summary>
        public float MeanFingerCurl { get; private set; }

        /// <summary>Creates an empty, untracked frame for <paramref name="handedness"/>.</summary>
        public HandFrame(Handedness handedness)
        {
            Handedness = handedness;
            for (int i = 0; i < JointCount; i++) Rotations[i] = Quaternion.identity;
        }

        /// <summary>Position of one joint.</summary>
        public Vector3 Joint(HandJoint j) => Positions[(int)j];

        /// <summary>Curl of one finger.</summary>
        public float FingerCurl(Finger f) => Curl[(int)f];

        /// <summary>Copies all joint data from <paramref name="other"/> (no allocation).</summary>
        public void CopyFrom(HandFrame other)
        {
            System.Array.Copy(other.Positions, Positions, JointCount);
            System.Array.Copy(other.Rotations, Rotations, JointCount);
            IsTracked = other.IsTracked;
            ComputeDerived();
        }

        /// <summary>
        /// Recomputes curls, palm pose and pinch values from the joint arrays. Call after the
        /// joints have been written for this frame.
        /// </summary>
        public void ComputeDerived()
        {
            Vector3 wrist = Joint(HandJoint.Wrist);
            Vector3 indexBase = Joint(HandJoint.IndexProximal);
            Vector3 littleBase = Joint(HandJoint.LittleProximal);
            Vector3 middleBase = Joint(HandJoint.MiddleProximal);

            PalmPosition = Joint(HandJoint.Palm);
            PalmForward = SafeNormalize(middleBase - wrist, Vector3.forward);

            // The palm normal is the cross product of "across the knuckles" and "along the hand".
            // The order flips between hands so the normal always points out of the palm.
            // Check (right hand flat, palm down, fingers +Z, thumb side -X):
            // across = (-1,0,0), forward = (0,0,1), forward × across = (0,-1,0) = down. ✓
            Vector3 across = indexBase - littleBase;
            Vector3 n = Handedness == Handedness.Right
                ? Vector3.Cross(PalmForward, across)
                : Vector3.Cross(across, PalmForward);
            PalmNormal = SafeNormalize(n, Vector3.down);

            Curl[(int)Finger.Thumb] = ChainCurl(HandJoint.ThumbMetacarpal, 4, ThumbFullCurlDegrees);
            Curl[(int)Finger.Index] = ChainCurl(HandJoint.IndexMetacarpal, 5, FingerFullCurlDegrees);
            Curl[(int)Finger.Middle] = ChainCurl(HandJoint.MiddleMetacarpal, 5, FingerFullCurlDegrees);
            Curl[(int)Finger.Ring] = ChainCurl(HandJoint.RingMetacarpal, 5, FingerFullCurlDegrees);
            Curl[(int)Finger.Little] = ChainCurl(HandJoint.LittleMetacarpal, 5, FingerFullCurlDegrees);
            MeanFingerCurl = 0.25f * (Curl[1] + Curl[2] + Curl[3] + Curl[4]);

            Vector3 thumbTip = Joint(HandJoint.ThumbTip);
            Vector3 indexTip = Joint(HandJoint.IndexTip);
            PinchDistance = Vector3.Distance(thumbTip, indexTip);
            PinchPoint = 0.5f * (thumbTip + indexTip);
        }

        /// <summary>
        /// Sum of bend angles between consecutive bones of a finger chain, normalised to 0..1.
        /// Uses only positions, so it works identically for real tracking and simulated hands.
        /// </summary>
        private float ChainCurl(HandJoint first, int jointsInChain, float fullCurlDegrees)
        {
            int start = (int)first;
            float sum = 0f;
            for (int i = 0; i < jointsInChain - 2; i++)
            {
                Vector3 a = Positions[start + i + 1] - Positions[start + i];
                Vector3 b = Positions[start + i + 2] - Positions[start + i + 1];
                if (a.sqrMagnitude < 1e-10f || b.sqrMagnitude < 1e-10f) continue;
                sum += Vector3.Angle(a, b);
            }
            return Mathf.Clamp01(sum / fullCurlDegrees);
        }

        private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            float m = v.magnitude;
            return m > 1e-6f ? v / m : fallback;
        }
    }
}
