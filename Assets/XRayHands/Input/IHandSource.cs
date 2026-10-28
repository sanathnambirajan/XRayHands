using UnityEngine;

namespace XRayHands.Input
{
    /// <summary>
    /// Anything that produces hand poses: real XR hand tracking, the keyboard/mouse simulator,
    /// or a scripted <c>GestureTimeline</c> in tests. Gesture logic only ever sees this
    /// interface, which is what makes it testable without a headset.
    /// </summary>
    public interface IHandSource
    {
        /// <summary>Left hand frame, refreshed in place each frame.</summary>
        HandFrame Left { get; }

        /// <summary>Right hand frame, refreshed in place each frame.</summary>
        HandFrame Right { get; }

        /// <summary>Head (eye centre) pose in world space; needed by the lens gesture.</summary>
        Pose Head { get; }

        /// <summary>Advance one frame. Called once per frame by the recognizer, before detection.</summary>
        void Tick(float deltaTime);
    }
}
