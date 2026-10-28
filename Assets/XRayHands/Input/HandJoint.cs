namespace XRayHands.Input
{
    /// <summary>
    /// The 26 hand joints, in the same order as OpenXR / Unity XR Hands
    /// (<c>XRHandJointID</c> minus one, because XR Hands reserves 0 for "Invalid").
    /// Using the same order lets <see cref="XRHandsSource"/> copy joints with a plain offset.
    /// </summary>
    public enum HandJoint
    {
        Wrist = 0,
        Palm,
        ThumbMetacarpal, ThumbProximal, ThumbDistal, ThumbTip,
        IndexMetacarpal, IndexProximal, IndexIntermediate, IndexDistal, IndexTip,
        MiddleMetacarpal, MiddleProximal, MiddleIntermediate, MiddleDistal, MiddleTip,
        RingMetacarpal, RingProximal, RingIntermediate, RingDistal, RingTip,
        LittleMetacarpal, LittleProximal, LittleIntermediate, LittleDistal, LittleTip,
    }

    /// <summary>Fingers in thumb-to-little order; used to index curl values.</summary>
    public enum Finger
    {
        Thumb = 0, Index, Middle, Ring, Little,
    }

    /// <summary>Which hand a <see cref="HandFrame"/> describes.</summary>
    public enum Handedness
    {
        Left = 0, Right = 1,
    }
}
