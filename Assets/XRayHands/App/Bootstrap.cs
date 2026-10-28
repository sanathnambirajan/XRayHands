using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using XRayHands.Analytics;
using XRayHands.Audio;
using XRayHands.Core;
using XRayHands.Feedback;
using XRayHands.Gestures;
using XRayHands.Input;
using XRayHands.Interaction;
using XRayHands.Rendering;

namespace XRayHands.App
{
    /// <summary>
    /// Composition root: creates the specimen, hand source, recognizer, tools and views, and
    /// wires them together. Everything else receives its dependencies from here, so no class
    /// searches the scene (no FindObjectOfType in hot paths) and every piece can be replaced
    /// by a test double.
    /// </summary>
    [DefaultExecutionOrder(-1000)] // runs before the XR/AR components so desktop mode can disable them first
    public sealed class Bootstrap : MonoBehaviour, ISpecimenHost
    {
        /// <summary>Eye height used when no headset tracks the camera (desktop / editor), metres.</summary>
        public const float DesktopEyeHeightMeters = 1.6f;
        private static readonly Color DesktopBackground = new Color(0.07f, 0.08f, 0.1f, 1f);

        [Tooltip("Specimen folder names under StreamingAssets/Specimens, in shelf order.")]
        [SerializeField] private string[] _specimenIds = { "nist_set1sample2", "enigma_1993_522_rotors" };

        [Tooltip("Index into the specimen list to load first.")]
        [SerializeField] private int _initialSpecimen;

        [Tooltip("Volume shader (keeps it in builds).")]
        [SerializeField] private Shader _volumeShader;

        [Tooltip("Chart dot shader (keeps it in builds).")]
        [SerializeField] private Shader _chartShader;

        [Tooltip("Monospace font for labels (IBM Plex Mono SDF). Falls back to the TMP default.")]
        [SerializeField] private TMP_FontAsset _labelFont;

        [Tooltip("Gesture thresholds. A default instance is created if empty.")]
        [SerializeField] private GestureTuning _tuning;

        [Tooltip("Where the specimen appears relative to the head at start: metres forward / down.")]
        [SerializeField] private Vector2 _spawnForwardDown = new Vector2(0.55f, 0.15f);

        private FeedbackHub _feedback;
        private DefectChart3D _chart;
        private Material _chartMaterial;
        private InteractionModeController _controller;
        private ProbeTool _probe;
        private ProbeSonifier _sonifier;
        private DefectColourMode _colourMode = DefectColourMode.Class;
        private bool _hideUnselected;
        private int _currentSpecimen;

        /// <inheritdoc/>
        public Transform SpecimenRoot { get; private set; }

        /// <inheritdoc/>
        public VolumeRenderer Volume { get; private set; }

        /// <inheritdoc/>
        public SelectionModel Selection { get; } = new SelectionModel();

        /// <summary>The active hand source.</summary>
        public IHandSource Hands { get; private set; }

        /// <summary>The gesture recognizer.</summary>
        public GestureRecognizer Recognizer { get; private set; }

        /// <summary>The interaction controller (tests inspect the active tool).</summary>
        public InteractionModeController Controller => _controller;

        /// <summary>The defect chart.</summary>
        public DefectChart3D Chart => _chart;

        /// <summary>The feedback hub (tests read labels).</summary>
        public FeedbackHub Feedback => _feedback;

        /// <summary>The probe tool (tests read the measurement).</summary>
        public ProbeTool Probe => _probe;

        /// <inheritdoc/>
        public float DisplayScale { get; private set; }

        /// <inheritdoc/>
        public VolumeDataset Dataset => Volume != null ? Volume.Dataset : null;

        /// <summary>Current defect colour mode.</summary>
        public DefectColourMode ColourMode => _colourMode;

        /// <summary>Raised when a specimen finished loading.</summary>
        public event System.Action<VolumeDataset> SpecimenLoaded;

        /// <summary>Last load error, for tests and diagnostics (null if none).</summary>
        public string LoadError { get; private set; }

        /// <summary>True while a specimen is loading.</summary>
        public bool IsLoading { get; private set; }

        private void Awake()
        {
            if (_tuning == null) _tuning = GestureTuning.CreateDefault();
            if (_labelFont == null) _labelFont = TMP_Settings.defaultFontAsset;
            Recognizer = new GestureRecognizer(_tuning);

            var root = new GameObject("Specimen");
            SpecimenRoot = root.transform;
            Volume = root.AddComponent<VolumeRenderer>();
            Volume.Initialize(_volumeShader);

            // Real hands when an XR hand subsystem is running (headset, Link, XR Simulator);
            // keyboard/mouse simulation otherwise. Everything downstream is identical.
            if (XRHandsSource.IsAvailable())
            {
                SetXrComponentsEnabled(true);
                var xr = gameObject.AddComponent<XRHandsSource>();
                Camera head = Camera.main;
                xr.TrackingSpace = head != null ? head.transform.parent : null;
                Hands = xr;
            }
            else
            {
                var sim = gameObject.AddComponent<SimulatedHandSource>();
                sim.Specimen = SpecimenRoot;
                Hands = sim;
                EnterDesktopMode();
            }
            CreateHandView(Hands.Left, "LeftHand");
            CreateHandView(Hands.Right, "RightHand");

            _feedback = new GameObject("Feedback").AddComponent<FeedbackHub>();
            _feedback.Font = _labelFont;

            var chartGo = new GameObject("DefectChart", typeof(MeshFilter), typeof(MeshRenderer));
            _chart = chartGo.AddComponent<DefectChart3D>();
            _chartMaterial = new Material(_chartShader != null ? _chartShader : Shader.Find("XRayHands/ChartDots"));

            var sonifierGo = new GameObject("ProbeSonifier", typeof(AudioSource));
            _sonifier = sonifierGo.AddComponent<ProbeSonifier>();
            _sonifier.Clicked += p => _feedback.Pulse(p, 0.015f);

            _probe = new ProbeTool(this, _feedback);
            _controller = new InteractionModeController(
                new ITool[]
                {
                    new PeelTool(this, _feedback),
                    new BrushTool(this, _feedback, _chart),
                    new ScaleTool(this, _feedback),
                    new GrabTool(this, _feedback),
                    new SliceTool(this, _feedback),
                    new ChartSelectTool(this, _feedback, _chart),
                    new ExtractTool(this, _feedback, _chart, PlaceChart),
                    _probe,
                },
                new LensTool(this),
                new ResetTool(this, _feedback));

            Selection.Changed += OnSelectionChanged;
        }

        private IEnumerator Start()
        {
            yield return LoadSpecimen(_initialSpecimen);
        }

        /// <summary>Loads specimen <paramref name="index"/> and places it in front of the user.</summary>
        public IEnumerator LoadSpecimen(int index)
        {
            IsLoading = true;
            LoadError = null;
            _currentSpecimen = Mathf.Clamp(index, 0, _specimenIds.Length - 1);
            string id = _specimenIds[_currentSpecimen];
            VolumeDataset loaded = null;
            yield return VolumeLoader.Load(id, d => loaded = d, e => LoadError = e);
            IsLoading = false;
            if (loaded == null)
            {
                Debug.LogError($"[XRayHands] {LoadError}");
                yield break;
            }

            _controller.Cancel();
            Volume.Dataset?.Dispose();
            Selection.Clear();
            Volume.SetDataset(loaded, Selection);
            _chart.Build(loaded, _chartMaterial, _labelFont);
            ResetView();
            RefreshColours();
            SpecimenLoaded?.Invoke(loaded);
        }

        /// <inheritdoc/>
        public void ResetView()
        {
            if (Dataset == null) return;
            Pose head = Hands.Head;
            Vector3 forward = Vector3.ProjectOnPlane(head.rotation * Vector3.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            SpecimenRoot.position = head.position + forward * _spawnForwardDown.x + Vector3.down * _spawnForwardDown.y;
            SpecimenRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);
            SetDisplayScale(Dataset.Meta.display.default_scale);
            Volume.SetPeel(0f);
            Volume.DisableClip();
            Volume.DisableLens();
            Vector2 window = Dataset.AutoWindow();
            Volume.SetWindow(window.x, window.y);
            _chart.Collapse();
            _feedback.HideDashedLine("link");
            _feedback.HideLabel("tooltip");
        }

        /// <inheritdoc/>
        public void SetDisplayScale(float magnification)
        {
            SpecimenMeta.Display d = Dataset.Meta.display;
            DisplayScale = Mathf.Clamp(magnification, d.min_scale, d.max_scale);
            SpecimenRoot.localScale = Vector3.one * (d.physical_extent_m * DisplayScale);
        }

        /// <summary>The gesture context for this frame.</summary>
        public GestureContext BuildContext() =>
            new GestureContext(Hands.Left, Hands.Right, Hands.Head, Volume.ProxyTransform.localToWorldMatrix);

        /// <summary>Switches between class colours and TP/FP/FN (specimen A only).</summary>
        public void SetColourMode(DefectColourMode mode)
        {
            if (mode == DefectColourMode.GroundTruth && (Dataset == null || !Dataset.Meta.HasGroundTruth)) return;
            _colourMode = mode;
            RefreshColours();
            if (mode == DefectColourMode.GroundTruth)
            {
                SpecimenMeta.GroundTruth gt = Dataset.Meta.ground_truth;
                _feedback.ShowLabel("gt", ChartAnchor() + Vector3.up * 0.36f,
                    $"precision {gt.precision:0.00}  recall {gt.recall:0.00}  IoU {gt.iou:0.00}");
            }
            else
            {
                _feedback.HideLabel("gt");
            }
        }

        /// <summary>"Hide unselected" mode: after extraction, ghost everything not selected.</summary>
        public void SetHideUnselected(bool hide)
        {
            _hideUnselected = hide;
            RefreshColours();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Hands.Tick(dt);
            if (Dataset == null || IsLoading) return;

            GestureContext ctx = BuildContext();
            Recognizer.Update(ctx, dt);
            _controller.Tick(new ToolFrame(ctx, Recognizer, dt));
            _sonifier.UpdateReading(_probe.Reading, Mathf.Max(Dataset.ExtentMm.x, Mathf.Max(Dataset.ExtentMm.y, Dataset.ExtentMm.z)));
            UpdateLinkLine();
            HandleKeyboardShortcuts();
        }

        private void HandleKeyboardShortcuts()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.tKey.wasPressedThisFrame)
                SetColourMode(_colourMode == DefectColourMode.Class ? DefectColourMode.GroundTruth : DefectColourMode.Class);
            if (kb.hKey.wasPressedThisFrame) SetHideUnselected(!_hideUnselected);
            if (kb.xKey.wasPressedThisFrame) ExtractChart();
            if (kb.backspaceKey.wasPressedThisFrame) { ResetView(); Selection.Clear(); }
            if (kb.digit1Key.wasPressedThisFrame && _currentSpecimen != 0) StartCoroutine(LoadSpecimen(0));
            if (kb.digit2Key.wasPressedThisFrame && _currentSpecimen != 1) StartCoroutine(LoadSpecimen(1));
            if (kb.tabKey.wasPressedThisFrame)
            {
                QualityTier next = Volume.Quality == QualityTier.Low ? QualityTier.Medium
                    : Volume.Quality == QualityTier.Medium ? QualityTier.High : QualityTier.Low;
                Volume.SetQuality(next);
            }
        }

        private void OnSelectionChanged()
        {
            RefreshColours();
            if (Selection.FocusedId != 0 && Dataset.Catalog.TryGet(Selection.FocusedId, out DefectRecord r))
            {
                string unit = r.Class == DefectClass.Component ? "part" : ProbeTool.ClassName(r.Class);
                _feedback.ShowLabel("tooltip", Volume.MmToWorld(r.CentroidMm) + Vector3.up * 0.05f,
                    $"#{r.Id} {unit}\nV {r.VolumeMm3:0.###E0} mm³  ψ {r.Sphericity:0.00}");
            }
            else
            {
                _feedback.HideLabel("tooltip");
            }
        }

        private void RefreshColours()
        {
            if (Dataset == null) return;
            Volume.RefreshColours(_colourMode, Selection, _hideUnselected);
            _chart.Recolour(rec => Volume.DefectColour(rec.Id), id => id == Selection.FocusedId);
        }

        private void UpdateLinkLine()
        {
            // Dashed link from the focused dot to its defect: the visible proof that the two views are linked.
            int id = Selection.FocusedId;
            if (id != 0 && _chart.ExtractionComplete && _chart.TryGetDotWorld(id, out Vector3 dot)
                && Dataset.Catalog.TryGet(id, out DefectRecord r))
            {
                _feedback.ShowDashedLine("link", dot, Volume.MmToWorld(r.CentroidMm));
            }
            else
            {
                _feedback.HideDashedLine("link");
            }
        }

        private Vector3 ChartAnchor()
        {
            Pose head = Hands.Head;
            Vector3 right = Vector3.ProjectOnPlane(head.rotation * Vector3.right, Vector3.up).normalized;
            float specimenHalf = 0.5f * SpecimenRoot.lossyScale.x;
            return SpecimenRoot.position + right * (specimenHalf + 0.08f + 0.5f * DefectChart3D.SizeMeters);
        }

        /// <summary>Extracts all defects into the chart (keyboard fallback for the pinch-pull gesture).</summary>
        public void ExtractChart()
        {
            if (Dataset == null || _chart.IsShown) return;
            PlaceChart();
            _chart.Extract(r => Volume.MmToWorld(r.CentroidMm));
        }

        /// <summary>Places the chart cube to the right of the specimen, facing the user.</summary>
        private void PlaceChart()
        {
            Pose head = Hands.Head;
            Vector3 forward = Vector3.ProjectOnPlane(head.rotation * Vector3.forward, Vector3.up).normalized;
            Transform t = _chart.transform;
            t.rotation = Quaternion.LookRotation(forward, Vector3.up);
            t.localScale = Vector3.one * DefectChart3D.SizeMeters;
            // The chart's local origin is its corner; centre the cube on the anchor.
            t.position = ChartAnchor() - t.rotation * (Vector3.one * (0.5f * DefectChart3D.SizeMeters));
        }

        /// <summary>
        /// The XR rig components (XR Origin, AR Session, AR camera manager/background, tracked
        /// pose driver) are saved <b>disabled</b> in the scene and switched on here only when an
        /// XR runtime is running. Without a headset they would only log "no subsystem"
        /// warnings, and AR Foundation runs them before any script of ours could stop them.
        /// Matched by namespace so the app assembly needs no AR Foundation reference.
        /// </summary>
        public static void SetXrComponentsEnabled(bool enabled)
        {
            foreach (Behaviour b in FindObjectsByType<Behaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (IsXrRigComponent(b)) b.enabled = enabled;
            }
        }

        /// <summary>True for the XR rig behaviours managed by <see cref="SetXrComponentsEnabled"/>.</summary>
        public static bool IsXrRigComponent(Behaviour b)
        {
            string ns = b.GetType().Namespace ?? string.Empty;
            return ns.StartsWith("UnityEngine.XR.ARFoundation") || ns.StartsWith("Unity.XR.CoreUtils") ||
                   ns.StartsWith("UnityEngine.InputSystem.XR");
        }

        /// <summary>
        /// No XR runtime: camera at standing eye height and an opaque background, since there
        /// is no passthrough to show through a transparent one.
        /// </summary>
        private void EnterDesktopMode()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            cam.transform.SetPositionAndRotation(new Vector3(0f, DesktopEyeHeightMeters, 0f), Quaternion.Euler(12f, 0f, 0f));
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = DesktopBackground;
        }

        private void CreateHandView(HandFrame frame, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<HandVisualizer>().Init(frame);
        }
    }
}
