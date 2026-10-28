using UnityEngine;
using UnityEngine.InputSystem;

namespace XRayHands.Input
{
    /// <summary>
    /// Keyboard + mouse hand simulator, so every gesture can be developed and demonstrated in
    /// the editor or on a PC without a headset. It produces full 26-joint hands through
    /// <see cref="HandPoseBuilder"/>, i.e. exactly the data real tracking provides, so the
    /// gesture code under test is the real gesture code.
    /// </summary>
    /// <remarks>
    /// Bindings (also in readme.txt):
    /// <list type="table">
    /// <item><term>Mouse</term><description>moves the right hand along the camera ray; wheel = depth</description></item>
    /// <item><term>LMB</term><description>pinch (right hand)</description></item>
    /// <item><term>Shift + LMB</term><description>two-hand pinch; wheel = hand distance (scale)</description></item>
    /// <item><term>G + LMB drag</term><description>grab (fist) and move</description></item>
    /// <item><term>C</term><description>flat palm facing away = cutting plane</description></item>
    /// <item><term>Q / E (with C)</term><description>wrist twist = contrast dial</description></item>
    /// <item><term>P + wheel</term><description>two facing palms = peel; wheel = palm distance</description></item>
    /// <item><term>F</term><description>index point = probe</description></item>
    /// <item><term>O</term><description>"OK" ring held up = X-ray lens</description></item>
    /// <item><term>R (hold)</term><description>palm-up fist = reset</description></item>
    /// </list>
    /// Shapes blend over ~70 ms instead of snapping, so hysteresis and hold times behave as
    /// they would with a real hand.
    /// </remarks>
    public sealed class SimulatedHandSource : MonoBehaviour, IHandSource
    {
        [Tooltip("Camera that defines the head pose and the mouse ray. Defaults to Camera.main.")]
        [SerializeField] private Camera _camera;

        [Tooltip("Specimen root; the peel and two-hand poses are placed around it.")]
        [SerializeField] private Transform _specimen;

        [Tooltip("Initial distance of the right hand along the mouse ray, metres.")]
        [SerializeField] private float _depthMeters = 0.45f;

        [Tooltip("Metres of depth change per wheel notch.")]
        [SerializeField] private float _wheelDepthStepMeters = 0.02f;

        [Tooltip("Shape blend speed (1/s). 15 ≈ 70 ms to settle.")]
        [SerializeField] private float _blendSpeed = 15f;

        [Tooltip("Wrist roll speed for Q/E, degrees per second.")]
        [SerializeField] private float _twistDegreesPerSecond = 60f;

        private const float WheelNotch = 120f; // Input System reports wheel in 1/120 notches on Windows
        private const float MinDepthMeters = 0.15f;
        private const float MaxDepthMeters = 1.5f;
        private const float MinPairDistanceMeters = 0.05f;
        private const float MaxPairDistanceMeters = 0.6f;

        private readonly HandFrame _left = new HandFrame(Handedness.Left);
        private readonly HandFrame _right = new HandFrame(Handedness.Right);
        private HandShape _leftShape = HandShape.Open;
        private HandShape _rightShape = HandShape.Open;
        private float _pairDistanceMeters = 0.25f;
        private float _twistDegrees;

        /// <inheritdoc/>
        public HandFrame Left => _left;

        /// <inheritdoc/>
        public HandFrame Right => _right;

        /// <inheritdoc/>
        public Pose Head => Cam != null ? new Pose(Cam.transform.position, Cam.transform.rotation) : Pose.identity;

        /// <summary>The specimen the two-hand poses are built around (set by Bootstrap).</summary>
        public Transform Specimen { get => _specimen; set => _specimen = value; }

        private Camera Cam => _camera != null ? _camera : (_camera = Camera.main);

        /// <inheritdoc/>
        public void Tick(float deltaTime)
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null || Cam == null)
            {
                _left.IsTracked = _right.IsTracked = false;
                return;
            }

            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            bool lmb = mouse.leftButton.isPressed;
            float wheel = mouse.scroll.ReadValue().y / WheelNotch;

            bool twoHandMode = kb.pKey.isPressed || (shift && lmb);
            if (twoHandMode) _pairDistanceMeters = Mathf.Clamp(_pairDistanceMeters + wheel * _wheelDepthStepMeters, MinPairDistanceMeters, MaxPairDistanceMeters);
            else _depthMeters = Mathf.Clamp(_depthMeters + wheel * _wheelDepthStepMeters, MinDepthMeters, MaxDepthMeters);

            if (kb.cKey.isPressed)
            {
                if (kb.qKey.isPressed) _twistDegrees -= _twistDegreesPerSecond * deltaTime;
                if (kb.eKey.isPressed) _twistDegrees += _twistDegreesPerSecond * deltaTime;
            }
            else
            {
                _twistDegrees = 0f;
            }

            Transform cam = Cam.transform;
            Ray ray = Cam.ScreenPointToRay(mouse.position.ReadValue());
            Vector3 handPoint = ray.origin + ray.direction * _depthMeters;
            float blend = 1f - Mathf.Exp(-_blendSpeed * deltaTime);

            // Default orientation: fingers along the view ray, palm down.
            Quaternion along = Quaternion.LookRotation(ray.direction, cam.up);
            Pose rightWrist;
            HandShape rightTarget;
            bool leftTracked = false;
            Pose leftWrist = default;
            HandShape leftTarget = HandShape.Open;

            if (kb.pKey.isPressed)
            {
                // Peel: palms vertical and facing each other across the specimen.
                Vector3 centre = _specimen != null ? _specimen.position : handPoint;
                Vector3 side = cam.right * (0.5f * _pairDistanceMeters);
                Quaternion fingersForward = Quaternion.LookRotation(cam.forward, cam.up);
                rightWrist = new Pose(centre + side - cam.forward * 0.06f, fingersForward * Quaternion.AngleAxis(-90f, Vector3.forward));
                leftWrist = new Pose(centre - side - cam.forward * 0.06f, fingersForward * Quaternion.AngleAxis(90f, Vector3.forward));
                rightTarget = HandShape.Open;
                leftTarget = HandShape.Open;
                leftTracked = true;
            }
            else if (shift && lmb)
            {
                // Two-hand pinch: hands mirrored around the mouse point; wheel spreads them.
                Vector3 side = cam.right * (0.5f * _pairDistanceMeters);
                rightWrist = new Pose(handPoint + side, along);
                leftWrist = new Pose(handPoint - side, along);
                rightTarget = HandShape.Pinching;
                leftTarget = HandShape.Pinching;
                leftTracked = true;
            }
            else if (kb.cKey.isPressed)
            {
                // Cut: flat hand, fingers up, palm facing away from the user; the palm plane is
                // the cutting plane. Q/E roll the wrist for the contrast dial.
                Quaternion fingersUpPalmAway = Quaternion.LookRotation(cam.up, -cam.forward);
                rightWrist = new Pose(handPoint - cam.up * 0.05f,
                    Quaternion.AngleAxis(_twistDegrees, cam.forward) * fingersUpPalmAway);
                rightTarget = HandShape.Open;
            }
            else if (kb.oKey.isPressed)
            {
                // Lens: ring axis pointing at the eye (see LensDetector for the geometry).
                Quaternion ringFacingEye = Quaternion.LookRotation(cam.up, -cam.right);
                rightWrist = new Pose(handPoint - cam.up * 0.09f, ringFacingEye);
                rightTarget = HandShape.OkRing;
            }
            else if (kb.rKey.isPressed)
            {
                rightWrist = new Pose(handPoint, along * Quaternion.AngleAxis(180f, Vector3.forward));
                rightTarget = HandShape.Fist;
            }
            else if (kb.fKey.isPressed)
            {
                rightWrist = new Pose(handPoint - ray.direction * 0.17f, along); // index tip at the mouse point
                rightTarget = HandShape.Point;
            }
            else if (kb.gKey.isPressed && lmb)
            {
                rightWrist = new Pose(handPoint - ray.direction * 0.05f, along);
                rightTarget = HandShape.Fist;
            }
            else
            {
                rightWrist = new Pose(handPoint - ray.direction * 0.1f, along);
                rightTarget = lmb ? HandShape.Pinching : HandShape.Open;
            }

            _rightShape = HandShape.Lerp(_rightShape, rightTarget, blend);
            HandPoseBuilder.Build(_right, rightWrist, _rightShape);

            if (leftTracked)
            {
                _leftShape = HandShape.Lerp(_leftShape, leftTarget, blend);
                HandPoseBuilder.Build(_left, leftWrist, _leftShape);
            }
            else
            {
                _left.IsTracked = false;
            }
        }
    }
}
