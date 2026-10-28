using UnityEngine;

namespace XRayHands.Gestures
{
    /// <summary>
    /// Soft thresholds. Every gesture criterion is a linear ramp from 0 (clearly not met) to 1
    /// (clearly met); a gesture's confidence is the minimum of its criteria (fuzzy AND), so the
    /// weakest criterion decides, exactly like a strict AND but continuous.
    /// </summary>
    public static class Ramp
    {
        /// <summary>1 when <paramref name="value"/> ≤ <paramref name="full"/>, 0 when ≥ <paramref name="zero"/> (full &lt; zero).</summary>
        public static float Below(float value, float full, float zero) => Mathf.Clamp01((zero - value) / (zero - full));

        /// <summary>1 when <paramref name="value"/> ≥ <paramref name="full"/>, 0 when ≤ <paramref name="zero"/> (zero &lt; full).</summary>
        public static float Above(float value, float full, float zero) => Mathf.Clamp01((value - zero) / (full - zero));
    }
}
