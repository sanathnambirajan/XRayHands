using UnityEngine;

namespace XRayHands.Input
{
    /// <summary>High-level description of a hand shape: finger curls and thumb-index pinch.</summary>
    [System.Serializable]
    public struct HandShape
    {
        /// <summary>Curl 0..1 per finger.</summary>
        public float Thumb, Index, Middle, Ring, Little;

        /// <summary>0 = thumb at rest, 1 = thumb tip touching index tip.</summary>
        public float Pinch;

        /// <summary>Creates a shape from explicit values.</summary>
        public HandShape(float thumb, float index, float middle, float ring, float little, float pinch)
        {
            Thumb = thumb; Index = index; Middle = middle; Ring = ring; Little = little; Pinch = pinch;
        }

        /// <summary>Flat open hand.</summary>
        public static HandShape Open => new HandShape(0f, 0f, 0f, 0f, 0f, 0f);

        /// <summary>Closed fist.</summary>
        public static HandShape Fist => new HandShape(0.8f, 0.95f, 0.95f, 0.95f, 0.95f, 0f);

        /// <summary>Index finger pointing, others curled.</summary>
        public static HandShape Point => new HandShape(0.6f, 0.05f, 0.9f, 0.9f, 0.9f, 0f);

        /// <summary>Thumb-index pinch with the other fingers relaxed.</summary>
        public static HandShape Pinching => new HandShape(0.3f, 0.35f, 0.3f, 0.3f, 0.3f, 1f);

        /// <summary>"OK" ring: thumb and index form a circle, other fingers extended.</summary>
        public static HandShape OkRing => new HandShape(0.3f, 0.55f, 0.05f, 0.05f, 0.05f, 1f);

        /// <summary>Component-wise interpolation, used for smooth simulated transitions.</summary>
        public static HandShape Lerp(HandShape a, HandShape b, float t) => new HandShape(
            Mathf.Lerp(a.Thumb, b.Thumb, t), Mathf.Lerp(a.Index, b.Index, t), Mathf.Lerp(a.Middle, b.Middle, t),
            Mathf.Lerp(a.Ring, b.Ring, t), Mathf.Lerp(a.Little, b.Little, t), Mathf.Lerp(a.Pinch, b.Pinch, t));
    }

    /// <summary>
    /// Builds a plausible 26-joint hand from a wrist pose and a <see cref="HandShape"/>.
    /// Used by <see cref="SimulatedHandSource"/> and by unit tests, so gestures can be
    /// exercised without a headset.
    /// </summary>
    /// <remarks>
    /// The skeleton is authored for a right hand in wrist-local space (fingers along +Z, palm
    /// facing -Y, thumb on the -X side) and mirrored in X for the left hand. Each finger bends
    /// by the same angle at its three joints, chosen so that <see cref="HandFrame"/>'s curl
    /// measure reads back exactly the requested curl. Joint rotations are set to the wrist
    /// rotation: gesture detectors use positions only, so this simplification is safe.
    /// </remarks>
    public static class HandPoseBuilder
    {
        // Approximate adult hand dimensions in metres (wrist-local, right hand).
        private static readonly Vector3[] MetacarpalBase =
        {
            new Vector3(-0.020f, -0.010f, 0.025f), // thumb
            new Vector3(-0.018f, 0f, 0.020f),      // index
            new Vector3(0.000f, 0f, 0.020f),       // middle
            new Vector3(0.016f, 0f, 0.020f),       // ring
            new Vector3(0.030f, 0f, 0.018f),       // little
        };

        private static readonly float[] MetacarpalLength = { 0.035f, 0.068f, 0.070f, 0.066f, 0.060f };
        private static readonly float[] ProximalLength = { 0.030f, 0.040f, 0.045f, 0.042f, 0.034f };
        private static readonly float[] IntermediateLength = { 0.025f, 0.025f, 0.028f, 0.027f, 0.020f };
        private static readonly float[] DistalLength = { 0f, 0.020f, 0.021f, 0.021f, 0.019f };

        private const float FingerFullCurlDegrees = 230f; // must match HandFrame
        private const float ThumbFullCurlDegrees = 110f;  // must match HandFrame
        private static readonly Vector3 ThumbRestDirection = new Vector3(-0.6f, -0.25f, 0.75f).normalized;

        /// <summary>
        /// Fills <paramref name="frame"/> with a hand of the given shape whose wrist is at
        /// <paramref name="wrist"/>, then calls <see cref="HandFrame.ComputeDerived"/>.
        /// </summary>
        public static void Build(HandFrame frame, Pose wrist, HandShape shape)
        {
            Vector3[] p = frame.Positions;
            float mirror = frame.Handedness == Handedness.Left ? -1f : 1f;

            p[(int)HandJoint.Wrist] = Vector3.zero;
            BuildFinger(p, Finger.Index, HandJoint.IndexMetacarpal, shape.Index);
            BuildFinger(p, Finger.Middle, HandJoint.MiddleMetacarpal, shape.Middle);
            BuildFinger(p, Finger.Ring, HandJoint.RingMetacarpal, shape.Ring);
            BuildFinger(p, Finger.Little, HandJoint.LittleMetacarpal, shape.Little);
            BuildThumb(p, shape.Thumb, shape.Pinch);
            p[(int)HandJoint.Palm] = 0.5f * (p[(int)HandJoint.Wrist] + p[(int)HandJoint.MiddleProximal]);

            for (int i = 0; i < HandFrame.JointCount; i++)
            {
                Vector3 local = p[i];
                local.x *= mirror;
                p[i] = wrist.position + wrist.rotation * local;
                frame.Rotations[i] = wrist.rotation;
            }
            frame.IsTracked = true;
            frame.ComputeDerived();
        }

        private static void BuildFinger(Vector3[] p, Finger finger, HandJoint metacarpal, float curl)
        {
            int f = (int)finger;
            int j = (int)metacarpal;
            // The three measured bends sit at proximal, intermediate and distal joints.
            float bend = Mathf.Clamp01(curl) * FingerFullCurlDegrees / 3f;
            Quaternion step = Quaternion.AngleAxis(bend, Vector3.right); // +X bends +Z towards -Y (palm side)

            Vector3 dir = Vector3.forward;
            p[j] = MetacarpalBase[f];
            p[j + 1] = p[j] + dir * MetacarpalLength[f];      // proximal joint (knuckle)
            dir = step * dir;
            p[j + 2] = p[j + 1] + dir * ProximalLength[f];    // intermediate
            dir = step * dir;
            p[j + 3] = p[j + 2] + dir * IntermediateLength[f]; // distal
            dir = step * dir;
            p[j + 4] = p[j + 3] + dir * DistalLength[f];      // tip
        }

        private static void BuildThumb(Vector3[] p, float curl, float pinch)
        {
            int j = (int)HandJoint.ThumbMetacarpal;
            float bend = Mathf.Clamp01(curl) * ThumbFullCurlDegrees / 2f;
            // The thumb flexes across the palm, i.e. around an axis perpendicular to its own
            // direction and to the palm normal (-Y).
            Vector3 axis = Vector3.Cross(Vector3.down, ThumbRestDirection).normalized;
            Quaternion step = Quaternion.AngleAxis(bend, axis);

            Vector3 dir = ThumbRestDirection;
            p[j] = MetacarpalBase[0];
            p[j + 1] = p[j] + dir * MetacarpalLength[0];
            dir = step * dir;
            p[j + 2] = p[j + 1] + dir * ProximalLength[0];
            dir = step * dir;
            p[j + 3] = p[j + 2] + dir * IntermediateLength[0];

            if (pinch > 0f)
            {
                // Pull the distal thumb segment so its tip meets the index tip (minus a 5 mm
                // skin gap at full pinch). Only the tip region moves, like a real pinch.
                Vector3 target = p[(int)HandJoint.IndexTip] + new Vector3(-0.005f, -0.004f, 0f);
                float t = Mathf.Clamp01(pinch);
                p[j + 3] = Vector3.Lerp(p[j + 3], target, t);
                p[j + 2] = Vector3.Lerp(p[j + 2], 0.5f * (p[j + 1] + p[j + 3]) + new Vector3(-0.01f, -0.01f, 0f), t);
            }
        }
    }
}
