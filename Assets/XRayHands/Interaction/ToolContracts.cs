using UnityEngine;
using XRayHands.Analytics;
using XRayHands.Core;
using XRayHands.Gestures;
using XRayHands.Input;
using XRayHands.Rendering;

namespace XRayHands.Interaction
{
    /// <summary>What tools may do to the specimen. Implemented by the app's composition root.</summary>
    public interface ISpecimenHost
    {
        /// <summary>Root transform (grab moves and rotates it).</summary>
        Transform SpecimenRoot { get; }

        /// <summary>The volume renderer (peel, clip, lens, colours).</summary>
        VolumeRenderer Volume { get; }

        /// <summary>Loaded dataset, or null.</summary>
        VolumeDataset Dataset { get; }

        /// <summary>Linked selection.</summary>
        SelectionModel Selection { get; }

        /// <summary>Current magnification relative to the physical object.</summary>
        float DisplayScale { get; }

        /// <summary>Sets the magnification (clamped to the specimen's allowed range).</summary>
        void SetDisplayScale(float magnification);

        /// <summary>Resets position, scale, cut, peel and lens.</summary>
        void ResetView();
    }

    /// <summary>
    /// Feedback the tools request. The app routes these to labels, glyphs and audio; tests use
    /// a recording implementation. Tools never create GameObjects themselves.
    /// </summary>
    public interface IFeedbackSink
    {
        /// <summary>Shows (or moves) a small world-space label with a stable key.</summary>
        void ShowLabel(string key, Vector3 position, string text);

        /// <summary>Hides a label.</summary>
        void HideLabel(string key);

        /// <summary>Plays the short "gesture recognised" sound at a position.</summary>
        void PlayRecognised(Vector3 position);

        /// <summary>Draws the ring showing the cut plane's extent (radius in metres); null hides it.</summary>
        void ShowCutRing(Vector3? centre, Vector3 normal, float radius);

        /// <summary>Draws a dashed line between two points (peel palms, chart links); key identifies it.</summary>
        void ShowDashedLine(string key, Vector3 a, Vector3 b);

        /// <summary>Hides a dashed line.</summary>
        void HideDashedLine(string key);

        /// <summary>Visual ring pulse at a point (probe click, reset confirmation).</summary>
        void Pulse(Vector3 position, float radius);
    }

    /// <summary>Everything a tool receives each frame.</summary>
    public readonly struct ToolFrame
    {
        /// <summary>Hands, head and specimen for this frame.</summary>
        public readonly GestureContext Context;

        /// <summary>Gesture states.</summary>
        public readonly GestureRecognizer Gestures;

        /// <summary>Thresholds.</summary>
        public readonly GestureTuning Tuning;

        /// <summary>Frame time in seconds.</summary>
        public readonly float DeltaTime;

        /// <summary>Creates a frame.</summary>
        public ToolFrame(GestureContext context, GestureRecognizer gestures, float deltaTime)
        {
            Context = context;
            Gestures = gestures;
            Tuning = gestures.Tuning;
            DeltaTime = deltaTime;
        }

        /// <summary>Is a per-hand gesture active on <paramref name="hand"/>?</summary>
        public bool Active(GestureId leftVariant, Handedness hand) =>
            Gestures.IsActive(GestureRecognizer.ForHand(leftVariant, hand));
    }

    /// <summary>
    /// An interaction tool: a small state machine that starts on a gesture, runs while the
    /// gesture holds, and ends. Only one primary tool runs at a time (see
    /// <see cref="InteractionModeController"/>), which keeps gestures from fighting.
    /// </summary>
    public interface ITool
    {
        /// <summary>Name for logs and tests.</summary>
        string Name { get; }

        /// <summary>Hands this tool occupies while running (for concurrency decisions).</summary>
        HandUsage Usage { get; }

        /// <summary>True if the tool's start gesture is present this frame.</summary>
        bool CanStart(in ToolFrame f);

        /// <summary>Called once when the tool becomes active.</summary>
        void Begin(in ToolFrame f);

        /// <summary>Called every frame while active. Return false to end.</summary>
        bool Tick(in ToolFrame f);

        /// <summary>Called once when the tool stops (gesture released or pre-empted).</summary>
        void End();
    }

    /// <summary>Which hands a running tool uses.</summary>
    [System.Flags]
    public enum HandUsage
    {
        None = 0, Left = 1, Right = 2, Both = Left | Right,
    }

    /// <summary>Small helpers shared by tools.</summary>
    public static class HandPoses
    {
        /// <summary>Orientation of a hand: forward = fingers, up = back of the hand.</summary>
        public static Quaternion Rotation(HandFrame f) => Quaternion.LookRotation(f.PalmForward, -f.PalmNormal);

        /// <summary>The usage flag for one hand.</summary>
        public static HandUsage Of(Handedness h) => h == Handedness.Left ? HandUsage.Left : HandUsage.Right;
    }
}
