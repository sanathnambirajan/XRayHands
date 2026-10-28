using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace XRayHands.Input
{
    /// <summary>
    /// Real hand tracking through Unity XR Hands (OpenXR on Quest, Quest Link, or the Meta XR
    /// Simulator). Copies the 26 joints into <see cref="HandFrame"/>s in world space. Gesture
    /// code cannot tell this source from the simulator, which is the point of IHandSource.
    /// </summary>
    /// <remarks>
    /// XR Hands reports joint poses in tracking (session) space; <see cref="TrackingSpace"/>
    /// (the XR Origin's camera-offset transform) maps them to world space. Joint order: our
    /// <see cref="HandJoint"/> index i corresponds to <c>XRHandJointIDUtility.FromIndex(i)</c>.
    /// </remarks>
    public sealed class XRHandsSource : MonoBehaviour, IHandSource
    {
        [Tooltip("Tracking-space transform (XR Origin camera offset). World = this transform applied to the joint poses.")]
        [SerializeField] private Transform _trackingSpace;

        [Tooltip("Head camera. Defaults to Camera.main.")]
        [SerializeField] private Camera _head;

        private static readonly List<XRHandSubsystem> Subsystems = new List<XRHandSubsystem>();
        private readonly HandFrame _left = new HandFrame(Handedness.Left);
        private readonly HandFrame _right = new HandFrame(Handedness.Right);
        private XRHandSubsystem _subsystem;

        /// <inheritdoc/>
        public HandFrame Left => _left;

        /// <inheritdoc/>
        public HandFrame Right => _right;

        /// <summary>Tracking space; set by the composition root.</summary>
        public Transform TrackingSpace { get => _trackingSpace; set => _trackingSpace = value; }

        /// <inheritdoc/>
        public Pose Head
        {
            get
            {
                Camera c = _head != null ? _head : (_head = Camera.main);
                return c != null ? new Pose(c.transform.position, c.transform.rotation) : Pose.identity;
            }
        }

        /// <summary>True if a running XR hand subsystem exists (decides real vs simulated hands).</summary>
        public static bool IsAvailable()
        {
            SubsystemManager.GetSubsystems(Subsystems);
            foreach (XRHandSubsystem s in Subsystems) if (s.running) return true;
            return false;
        }

        /// <inheritdoc/>
        public void Tick(float deltaTime)
        {
            if (_subsystem == null || !_subsystem.running)
            {
                _subsystem = null;
                SubsystemManager.GetSubsystems(Subsystems);
                foreach (XRHandSubsystem s in Subsystems) if (s.running) { _subsystem = s; break; }
            }
            if (_subsystem == null)
            {
                _left.IsTracked = _right.IsTracked = false;
                return;
            }
            Copy(_subsystem.leftHand, _left);
            Copy(_subsystem.rightHand, _right);
        }

        private void Copy(XRHand hand, HandFrame frame)
        {
            if (!hand.isTracked)
            {
                frame.IsTracked = false;
                return;
            }
            for (int i = 0; i < HandFrame.JointCount; i++)
            {
                XRHandJoint joint = hand.GetJoint(XRHandJointIDUtility.FromIndex(i));
                if (!joint.TryGetPose(out Pose p)) continue; // keep last known pose for this joint
                if (_trackingSpace != null)
                {
                    p.position = _trackingSpace.TransformPoint(p.position);
                    p.rotation = _trackingSpace.rotation * p.rotation;
                }
                frame.Positions[i] = p.position;
                frame.Rotations[i] = p.rotation;
            }
            frame.IsTracked = true;
            frame.ComputeDerived();
        }
    }
}
