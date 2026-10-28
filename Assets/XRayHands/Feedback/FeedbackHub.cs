using System.Collections.Generic;
using TMPro;
using UnityEngine;
using XRayHands.Interaction;

namespace XRayHands.Feedback
{
    /// <summary>
    /// Implements every visual/audio affordance the tools ask for. Objects are pooled by key,
    /// so a label shown every frame is one object that moves, not a new one per frame.
    /// </summary>
    public sealed class FeedbackHub : MonoBehaviour, IFeedbackSink
    {
        private const int RingSegments = 64;
        private const float LineWidthMeters = 0.0018f;
        private const float PulseSeconds = 0.35f;
        private const int SampleRate = 44100;

        private static readonly Color PlateColour = new Color(0.05f, 0.06f, 0.08f, 0.78f);
        private static readonly Color TextColour = new Color(0.9f, 0.94f, 1f);
        private static readonly Color LineColour = new Color(0.62f, 0.85f, 1f, 0.9f);

        private readonly Dictionary<string, TextMeshPro> _labels = new Dictionary<string, TextMeshPro>();
        private readonly Dictionary<string, LineRenderer> _lines = new Dictionary<string, LineRenderer>();
        private readonly List<(LineRenderer ring, float age, float radius)> _pulses = new List<(LineRenderer, float, float)>();
        private readonly Stack<LineRenderer> _pulsePool = new Stack<LineRenderer>();
        private LineRenderer _cutRing;
        private Material _lineMaterial;
        private Material _dashMaterial;
        private Material _plateMaterial;
        private TMP_FontAsset _font;
        private AudioSource _audio;
        private AudioClip _blip;

        /// <summary>Font for all labels (set before first use; null = TMP default).</summary>
        public TMP_FontAsset Font { get => _font; set => _font = value; }

        /// <summary>All label texts currently visible, for tests and verification.</summary>
        public IReadOnlyDictionary<string, TextMeshPro> Labels => _labels;

        private void Awake()
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            _lineMaterial = new Material(unlit) { color = LineColour };
            SetTransparent(_lineMaterial);
            _dashMaterial = new Material(unlit) { mainTexture = CreateDashTexture(), color = LineColour };
            SetTransparent(_dashMaterial);
            _plateMaterial = new Material(unlit) { color = PlateColour };
            SetTransparent(_plateMaterial);
            _cutRing = CreateLine("CutRing", _lineMaterial, RingSegments, true);
            _cutRing.enabled = false;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f;
            _audio.playOnAwake = false;
            _blip = CreateBlip();
        }

        /// <inheritdoc/>
        public void ShowLabel(string key, Vector3 position, string text)
        {
            if (!_labels.TryGetValue(key, out TextMeshPro tmp))
            {
                tmp = CreateLabel(key);
                _labels[key] = tmp;
            }
            tmp.gameObject.SetActive(true);
            tmp.transform.position = position;
            if (tmp.text != text) tmp.text = text; // avoid re-meshing when unchanged
        }

        /// <inheritdoc/>
        public void HideLabel(string key)
        {
            if (_labels.TryGetValue(key, out TextMeshPro tmp)) tmp.gameObject.SetActive(false);
        }

        /// <summary>True if a label with this key is visible (tests).</summary>
        public bool IsLabelVisible(string key, out string text)
        {
            text = null;
            if (!_labels.TryGetValue(key, out TextMeshPro tmp) || !tmp.gameObject.activeSelf) return false;
            text = tmp.text;
            return true;
        }

        /// <inheritdoc/>
        public void PlayRecognised(Vector3 position)
        {
            _audio.transform.position = position;
            _audio.PlayOneShot(_blip, 0.5f);
        }

        /// <inheritdoc/>
        public void ShowCutRing(Vector3? centre, Vector3 normal, float radius)
        {
            if (centre == null) { _cutRing.enabled = false; return; }
            _cutRing.enabled = true;
            WriteCircle(_cutRing, centre.Value, normal, radius);
        }

        /// <inheritdoc/>
        public void ShowDashedLine(string key, Vector3 a, Vector3 b)
        {
            if (!_lines.TryGetValue(key, out LineRenderer lr))
            {
                lr = CreateLine($"Dash_{key}", _dashMaterial, 2, false);
                lr.textureMode = LineTextureMode.Tile;
                _lines[key] = lr;
            }
            lr.enabled = true;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            // Tile so each dash is ~1 cm regardless of length.
            lr.material.mainTextureScale = new Vector2(Vector3.Distance(a, b) / 0.01f, 1f);
        }

        /// <inheritdoc/>
        public void HideDashedLine(string key)
        {
            if (_lines.TryGetValue(key, out LineRenderer lr)) lr.enabled = false;
        }

        /// <inheritdoc/>
        public void Pulse(Vector3 position, float radius)
        {
            LineRenderer ring = _pulsePool.Count > 0 ? _pulsePool.Pop() : CreateLine("Pulse", new Material(_lineMaterial), RingSegments, true);
            ring.enabled = true;
            ring.transform.position = position;
            _pulses.Add((ring, 0f, radius));
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            Vector3 toCam = cam != null ? cam.transform.position : Vector3.zero;
            for (int i = _pulses.Count - 1; i >= 0; i--)
            {
                (LineRenderer ring, float age, float radius) = _pulses[i];
                age += Time.deltaTime;
                float t = age / PulseSeconds;
                if (t >= 1f)
                {
                    ring.enabled = false;
                    _pulsePool.Push(ring);
                    _pulses.RemoveAt(i);
                    continue;
                }
                Vector3 p = ring.transform.position;
                WriteCircle(ring, p, (toCam - p).normalized, Mathf.Lerp(0.2f, 1f, t) * radius);
                Color c = LineColour;
                c.a = 1f - t;
                ring.material.color = c;
                _pulses[i] = (ring, age, radius);
            }
        }

        private TextMeshPro CreateLabel(string key)
        {
            var go = new GameObject($"Label_{key}");
            go.transform.SetParent(transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = 0.18f;
            tmp.color = TextColour;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(0.16f, 0.04f);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            go.AddComponent<Analytics.FaceCamera>();

            // Dark translucent plate behind the text for contrast against passthrough.
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(plate.GetComponent<Collider>());
            plate.name = "Plate";
            plate.transform.SetParent(go.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.001f);
            plate.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            plate.transform.localScale = new Vector3(0.17f, 0.045f, 1f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = _plateMaterial;
            return tmp;
        }

        private LineRenderer CreateLine(string name, Material mat, int points, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = points;
            lr.loop = loop;
            lr.useWorldSpace = true;
            lr.widthMultiplier = LineWidthMeters;
            lr.sharedMaterial = mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        private static void WriteCircle(LineRenderer lr, Vector3 centre, Vector3 normal, float radius)
        {
            Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(normal, u);
            for (int i = 0; i < lr.positionCount; i++)
            {
                float a = i * Mathf.PI * 2f / lr.positionCount;
                lr.SetPosition(i, centre + radius * (Mathf.Cos(a) * u + Mathf.Sin(a) * v));
            }
        }

        private static Texture2D CreateDashTexture()
        {
            var tex = new Texture2D(8, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            for (int i = 0; i < 8; i++) tex.SetPixel(i, 0, i < 5 ? Color.white : new Color(1, 1, 1, 0));
            tex.Apply();
            return tex;
        }

        private static void SetTransparent(Material m)
        {
            // URP Unlit: switch to transparent surface so alpha in the colour is respected.
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;
        }

        private static AudioClip CreateBlip()
        {
            // 70 ms rising two-tone chirp with a soft envelope: short, clear, not annoying.
            int n = SampleRate * 7 / 100;
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float f = Mathf.Lerp(880f, 1320f, i / (float)n);
                float env = Mathf.Sin(Mathf.PI * i / n);
                data[i] = 0.4f * env * Mathf.Sin(2f * Mathf.PI * f * t);
            }
            AudioClip clip = AudioClip.Create("Recognised", n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
