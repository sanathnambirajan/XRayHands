using UnityEngine;

namespace XRayHands.Gestures
{
    /// <summary>
    /// Hysteresis state machine shared by all gestures.
    /// <para>Inactive → Active when confidence ≥ enter threshold continuously for
    /// <see cref="EnterHoldSeconds"/>. Active → Inactive when confidence ≤ exit threshold, but
    /// never before <see cref="MinActiveSeconds"/> have passed.</para>
    /// Two thresholds plus two timers is what removes flicker: a hand hovering at a boundary
    /// produces noisy confidence, but it must cross the whole band to change state.
    /// </summary>
    public sealed class GestureState
    {
        /// <summary>Confidence needed to start.</summary>
        public float EnterThreshold;

        /// <summary>Confidence at which an active gesture ends.</summary>
        public float ExitThreshold;

        /// <summary>Confidence must stay above enter for this long before activating (seconds).</summary>
        public float EnterHoldSeconds;

        /// <summary>Minimum active duration (seconds).</summary>
        public float MinActiveSeconds;

        /// <summary>True while the gesture is recognised.</summary>
        public bool IsActive { get; private set; }

        /// <summary>True only on the frame the gesture became active.</summary>
        public bool JustEntered { get; private set; }

        /// <summary>True only on the frame the gesture ended.</summary>
        public bool JustExited { get; private set; }

        /// <summary>Most recent confidence 0..1.</summary>
        public float Confidence { get; private set; }

        /// <summary>Seconds since the gesture became active (0 when inactive).</summary>
        public float ActiveSeconds { get; private set; }

        /// <summary>How far the enter hold has progressed 0..1 (drives "charging" affordances).</summary>
        public float EnterProgress => EnterHoldSeconds <= 0f ? (IsActive ? 1f : 0f) : Mathf.Clamp01(_aboveSeconds / EnterHoldSeconds);

        private float _aboveSeconds;

        /// <summary>Creates a state with explicit thresholds and timers.</summary>
        public GestureState(float enter, float exit, float enterHoldSeconds, float minActiveSeconds)
        {
            EnterThreshold = enter;
            ExitThreshold = exit;
            EnterHoldSeconds = enterHoldSeconds;
            MinActiveSeconds = minActiveSeconds;
        }

        /// <summary>Feed this frame's confidence. Call exactly once per frame.</summary>
        public void Update(float confidence, float deltaTime)
        {
            Confidence = confidence;
            JustEntered = false;
            JustExited = false;

            if (!IsActive)
            {
                _aboveSeconds = confidence >= EnterThreshold ? _aboveSeconds + deltaTime : 0f;
                // ">=" with a tiny epsilon so a zero hold enters on the first qualifying frame.
                if (confidence >= EnterThreshold && _aboveSeconds >= EnterHoldSeconds - 1e-6f)
                {
                    IsActive = true;
                    JustEntered = true;
                    ActiveSeconds = 0f;
                }
            }
            else
            {
                ActiveSeconds += deltaTime;
                if (confidence <= ExitThreshold && ActiveSeconds >= MinActiveSeconds)
                {
                    IsActive = false;
                    JustExited = true;
                    ActiveSeconds = 0f;
                    _aboveSeconds = 0f;
                }
            }
        }

        /// <summary>Forces the state inactive (e.g. when tracking is lost or the app resets).</summary>
        public void Reset()
        {
            JustExited = IsActive;
            JustEntered = false;
            IsActive = false;
            ActiveSeconds = 0f;
            _aboveSeconds = 0f;
            Confidence = 0f;
        }
    }
}
