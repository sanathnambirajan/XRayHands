using System.Collections;
using UnityEngine;
using XRayHands.Analytics;
using XRayHands.Core;
using XRayHands.Feedback;
using XRayHands.Gestures;
using XRayHands.Input;
using XRayHands.Rendering;

namespace XRayHands.App
{
    /// <summary>
    /// Composition root: creates the specimen, the hand source, the recognizer and the views,
    /// and wires them together. Everything else receives its dependencies from here, so no
    /// class searches the scene (no FindObjectOfType in hot paths).
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        [Tooltip("Specimen folder names under StreamingAssets/Specimens, in shelf order.")]
        [SerializeField] private string[] _specimenIds = { "nist_set1sample2", "enigma_1993_522_rotors" };

        [Tooltip("Index into the specimen list to load first.")]
        [SerializeField] private int _initialSpecimen;

        [Tooltip("Volume shader (keeps it in builds).")]
        [SerializeField] private Shader _volumeShader;

        [Tooltip("Gesture thresholds. A default instance is created if empty.")]
        [SerializeField] private GestureTuning _tuning;

        [Tooltip("Where the specimen appears, relative to the head at start: metres forward / down.")]
        [SerializeField] private Vector2 _spawnForwardDown = new Vector2(0.55f, 0.15f);

        /// <summary>The specimen root (moved by grab, scaled by two-hand pinch).</summary>
        public Transform SpecimenRoot { get; private set; }

        /// <summary>The volume renderer on the specimen root.</summary>
        public VolumeRenderer Volume { get; private set; }

        /// <summary>Linked selection shared by all views.</summary>
        public SelectionModel Selection { get; } = new SelectionModel();

        /// <summary>The active hand source.</summary>
        public IHandSource Hands { get; private set; }

        /// <summary>The gesture recognizer.</summary>
        public GestureRecognizer Recognizer { get; private set; }

        /// <summary>Current display magnification relative to the physical object.</summary>
        public float DisplayScale { get; private set; }

        /// <summary>The loaded dataset (null while loading).</summary>
        public VolumeDataset Dataset => Volume != null ? Volume.Dataset : null;

        /// <summary>Raised when a specimen finished loading.</summary>
        public event System.Action<VolumeDataset> SpecimenLoaded;

        /// <summary>Last load error, for tests and the on-screen status (null if none).</summary>
        public string LoadError { get; private set; }

        private void Awake()
        {
            if (_tuning == null) _tuning = GestureTuning.CreateDefault();
            Recognizer = new GestureRecognizer(_tuning);

            var root = new GameObject("Specimen");
            SpecimenRoot = root.transform;
            Volume = root.AddComponent<VolumeRenderer>();
            Volume.Initialize(_volumeShader);

            var sim = gameObject.AddComponent<SimulatedHandSource>();
            sim.Specimen = SpecimenRoot;
            Hands = sim;

            CreateHandView(Hands.Left, "LeftHand");
            CreateHandView(Hands.Right, "RightHand");
        }

        private IEnumerator Start()
        {
            yield return LoadSpecimen(_initialSpecimen);
        }

        /// <summary>Loads specimen <paramref name="index"/> and places it in front of the user.</summary>
        public IEnumerator LoadSpecimen(int index)
        {
            LoadError = null;
            string id = _specimenIds[Mathf.Clamp(index, 0, _specimenIds.Length - 1)];
            VolumeDataset loaded = null;
            yield return VolumeLoader.Load(id, d => loaded = d, e => LoadError = e);
            if (loaded == null)
            {
                Debug.LogError($"[XRayHands] {LoadError}");
                yield break;
            }

            Volume.Dataset?.Dispose();
            Selection.Clear();
            Volume.SetDataset(loaded, Selection);
            ResetView();
            SpecimenLoaded?.Invoke(loaded);
        }

        /// <summary>Puts the specimen back in front of the head at its default magnification.</summary>
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
        }

        /// <summary>
        /// Sets the magnification. The proxy cube's longest axis is 1, so the root's uniform
        /// scale in metres is (physical longest extent) × magnification.
        /// </summary>
        public void SetDisplayScale(float magnification)
        {
            SpecimenMeta.Display d = Dataset.Meta.display;
            DisplayScale = Mathf.Clamp(magnification, d.min_scale, d.max_scale);
            SpecimenRoot.localScale = Vector3.one * (d.physical_extent_m * DisplayScale);
        }

        /// <summary>The gesture context for this frame.</summary>
        public GestureContext BuildContext() =>
            new GestureContext(Hands.Left, Hands.Right, Hands.Head, Volume.ProxyTransform.localToWorldMatrix);

        private void Update()
        {
            Hands.Tick(Time.deltaTime);
            if (Dataset != null) Recognizer.Update(BuildContext(), Time.deltaTime);
        }

        private void CreateHandView(HandFrame frame, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<HandVisualizer>().Init(frame);
        }
    }
}
