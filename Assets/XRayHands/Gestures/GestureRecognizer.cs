using UnityEngine;
using XRayHands.Input;

namespace XRayHands.Gestures
{
    /// <summary>Every recognisable gesture. Per-hand gestures exist once for each hand.</summary>
    public enum GestureId
    {
        PinchLeft, PinchRight,
        FlatPalmLeft, FlatPalmRight,
        PointLeft, PointRight,
        GrabLeft, GrabRight,
        LensLeft, LensRight,
        ResetLeft, ResetRight,
        Peel,
    }

    /// <summary>
    /// Runs all detectors each frame and keeps one <see cref="GestureState"/> per gesture.
    /// Pure C#: tests construct it directly and feed frames; the app wraps it in a MonoBehaviour.
    /// Deciding which gesture *wins* when several are active is the job of the interaction
    /// layer's mode controller, not of recognition.
    /// </summary>
    public sealed class GestureRecognizer
    {
        private static readonly int Count = System.Enum.GetValues(typeof(GestureId)).Length;

        private readonly IGestureDetector[] _detectors = new IGestureDetector[Count];
        private readonly GestureState[] _states = new GestureState[Count];

        /// <summary>The tuning in use.</summary>
        public GestureTuning Tuning { get; }

        /// <summary>Builds detectors and states from <paramref name="tuning"/>.</summary>
        public GestureRecognizer(GestureTuning tuning)
        {
            Tuning = tuning;
            Add(GestureId.PinchLeft, new PinchDetector(tuning, Handedness.Left), 0f);
            Add(GestureId.PinchRight, new PinchDetector(tuning, Handedness.Right), 0f);
            Add(GestureId.FlatPalmLeft, new FlatPalmDetector(tuning, Handedness.Left), tuning.FlatPalmHoldSeconds);
            Add(GestureId.FlatPalmRight, new FlatPalmDetector(tuning, Handedness.Right), tuning.FlatPalmHoldSeconds);
            Add(GestureId.PointLeft, new PointDetector(tuning, Handedness.Left), 0.1f);
            Add(GestureId.PointRight, new PointDetector(tuning, Handedness.Right), 0.1f);
            Add(GestureId.GrabLeft, new GrabDetector(tuning, Handedness.Left), 0f);
            Add(GestureId.GrabRight, new GrabDetector(tuning, Handedness.Right), 0f);
            Add(GestureId.LensLeft, new LensDetector(tuning, Handedness.Left), tuning.LensHoldSeconds);
            Add(GestureId.LensRight, new LensDetector(tuning, Handedness.Right), tuning.LensHoldSeconds);
            Add(GestureId.ResetLeft, new ResetDetector(tuning, Handedness.Left), tuning.ResetHoldSeconds);
            Add(GestureId.ResetRight, new ResetDetector(tuning, Handedness.Right), tuning.ResetHoldSeconds);
            Add(GestureId.Peel, new PeelDetector(tuning), tuning.FlatPalmHoldSeconds);
        }

        private void Add(GestureId id, IGestureDetector detector, float enterHoldSeconds)
        {
            _detectors[(int)id] = detector;
            _states[(int)id] = new GestureState(Tuning.EnterConfidence, Tuning.ExitConfidence, enterHoldSeconds, Tuning.MinActiveSeconds);
        }

        /// <summary>Evaluate every detector for this frame and update the state machines.</summary>
        public void Update(in GestureContext ctx, float deltaTime)
        {
            for (int i = 0; i < Count; i++)
            {
                _states[i].Update(_detectors[i].Evaluate(ctx), deltaTime);
            }
        }

        /// <summary>The state of one gesture.</summary>
        public GestureState State(GestureId id) => _states[(int)id];

        /// <summary>Shortcut for <c>State(id).IsActive</c>.</summary>
        public bool IsActive(GestureId id) => _states[(int)id].IsActive;

        /// <summary>The per-hand id for a gesture kind, e.g. (PinchLeft, Right) → PinchRight.</summary>
        public static GestureId ForHand(GestureId leftVariant, Handedness hand) =>
            hand == Handedness.Left ? leftVariant : leftVariant + 1;

        /// <summary>Puts every gesture back to inactive.</summary>
        public void ResetAll()
        {
            for (int i = 0; i < Count; i++) _states[i].Reset();
        }
    }
}
