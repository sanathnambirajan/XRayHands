using UnityEngine;

namespace XRayHands.Input
{
    /// <summary>
    /// Every gesture threshold in one asset, so tuning on the headset never means hunting
    /// through code. Values are the starting points from the project brief; units are in the
    /// field names. Each detector turns its raw measurement into a 0..1 confidence with a
    /// linear ramp between an "enter" value and an "exit" value; the gap between the two is
    /// the hysteresis that stops gestures from flickering at the boundary.
    /// </summary>
    [CreateAssetMenu(menuName = "X-Ray Hands/Gesture Tuning", fileName = "GestureTuning")]
    public sealed class GestureTuning : ScriptableObject
    {
        [Header("State machine")]
        [Tooltip("Confidence at or above which a gesture may become active.")]
        [Range(0f, 1f)] public float EnterConfidence = 0.95f;

        [Tooltip("Confidence at or below which an active gesture ends.")]
        [Range(0f, 1f)] public float ExitConfidence = 0.05f;

        [Tooltip("Minimum time a gesture stays active once entered (anti-flicker), seconds.")]
        public float MinActiveSeconds = 0.1f;

        [Header("Pinch")]
        [Tooltip("Thumb-index tip distance that starts a pinch, metres.")]
        public float PinchEnterDistanceMeters = 0.020f;

        [Tooltip("Thumb-index tip distance that releases a pinch, metres.")]
        public float PinchExitDistanceMeters = 0.035f;

        [Header("Flat palm (cutting plane)")]
        [Tooltip("All four finger curls must be below this to count as flat.")]
        public float FlatPalmMaxCurl = 0.25f;

        [Tooltip("Curl at which a flat palm is considered fully lost (hysteresis band).")]
        public float FlatPalmExitCurl = 0.40f;

        [Tooltip("Palm must be inside the specimen bounds expanded by this margin, metres.")]
        public float FlatPalmBoundsMarginMeters = 0.05f;

        [Tooltip("How long the palm must be flat and inside before the cut starts, seconds.")]
        public float FlatPalmHoldSeconds = 0.15f;

        [Header("Peel (two facing palms)")]
        [Tooltip("Dot product of the two palm normals must be below this (they face each other).")]
        public float PeelMaxNormalDot = -0.7f;

        [Tooltip("Dot product at which the peel is lost.")]
        public float PeelExitNormalDot = -0.4f;

        [Tooltip("Both palms must be within this distance of the specimen surface, metres.")]
        public float PeelMaxSpecimenDistanceMeters = 0.40f;

        [Tooltip("Palm distance mapped to 0 % peel, metres.")]
        public float PeelMinPalmDistanceMeters = 0.08f;

        [Tooltip("Palm distance mapped to 100 % peel, metres.")]
        public float PeelMaxPalmDistanceMeters = 0.45f;

        [Header("Point (probe)")]
        [Tooltip("Index curl must be below this.")]
        public float PointMaxIndexCurl = 0.20f;

        [Tooltip("Middle, ring and little curl must be above this.")]
        public float PointMinOtherCurl = 0.60f;

        [Header("O-lens")]
        [Tooltip("Thumb-index tip distance for the ring, metres.")]
        public float LensMaxTipDistanceMeters = 0.025f;

        [Tooltip("Middle, ring and little must be extended: curl below this.")]
        public float LensMaxOtherCurl = 0.35f;

        [Tooltip("Ring plane must face the eye within this angle, degrees.")]
        public float LensMaxFacingAngleDegrees = 35f;

        [Tooltip("Hold time before the lens appears, seconds.")]
        public float LensHoldSeconds = 0.25f;

        [Header("Grab")]
        [Tooltip("Mean curl of the four fingers above this counts as a fist.")]
        public float GrabMinMeanCurl = 0.70f;

        [Tooltip("Palm must be within this distance of the specimen surface, metres.")]
        public float GrabMaxSurfaceDistanceMeters = 0.08f;

        [Header("Reset")]
        [Tooltip("Palm normal must point up within this dot product of world up.")]
        public float ResetMinPalmUpDot = 0.7f;

        [Tooltip("Fist with palm up must be held this long, seconds.")]
        public float ResetHoldSeconds = 0.6f;

        [Header("Shared ramps")]
        [Tooltip("Width of the soft ramp used for curl and distance criteria (hysteresis band).")]
        public float CurlRampWidth = 0.15f;

        [Tooltip("Width of the soft ramp for 'inside bounds' criteria, metres.")]
        public float DistanceRampWidthMeters = 0.02f;

        /// <summary>A tuning instance with the default values, for tests and as a fallback.</summary>
        public static GestureTuning CreateDefault() => CreateInstance<GestureTuning>();
    }
}
