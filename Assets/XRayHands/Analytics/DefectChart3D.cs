using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using XRayHands.Core;

namespace XRayHands.Analytics
{
    /// <summary>
    /// 3D scatter plot of all defects (axes from meta.json, e.g. log10 volume × sphericity ×
    /// build height). Point size encodes volume, colour encodes class (or TP/FP/FN).
    /// <para>The extraction animation is the SciVis → InfoVis moment: every defect flies out of
    /// the specimen along an arc into its chart position, staggered by distance so the eye can
    /// follow the flow instead of seeing everything teleport.</para>
    /// </summary>
    /// <remarks>
    /// All dots are one mesh (an octahedron per dot, vertex-coloured): thousands of dots cost
    /// one draw call, which matters on Quest. Chart local space is the unit cube [0,1]^3,
    /// scaled by <see cref="SizeMeters"/>.
    /// </remarks>
    public sealed class DefectChart3D : MonoBehaviour
    {
        /// <summary>Edge length of the chart cube in metres.</summary>
        public const float SizeMeters = 0.3f;
        /// <summary>Smallest / largest dot radius in metres.</summary>
        public const float MinDotRadius = 0.0025f, MaxDotRadius = 0.009f;
        /// <summary>Flight time of one dot, seconds.</summary>
        public const float FlightSeconds = 0.9f;
        /// <summary>Spread of start times across all dots, seconds.</summary>
        public const float StaggerSeconds = 0.9f;
        /// <summary>Arc apex height above the straight path, metres.</summary>
        public const float ArcHeightMeters = 0.12f;

        private const int VertsPerDot = 6;
        private const int IndicesPerDot = 24;
        private static readonly Vector3[] Octa =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };
        private static readonly int[] OctaTris =
        {
            0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 4, 3, 0, 1, 3, 4, 5, 3, 1, 0, 3, 5,
        };

        private Mesh _mesh;
        private Vector3[] _verts;
        private Color32[] _colours;
        private DefectRecord[] _records;
        private Vector3[] _chartPos;     // chart-local [0,1]^3
        private float[] _radius;
        private Vector3[] _startWorld;   // extraction start (defect position in the specimen)
        private float[] _delay;
        private float _extractTime = -1f;
        private Vector2[] _range = new Vector2[3];
        private string[] _columns = new string[3];
        private readonly Dictionary<int, int> _indexById = new Dictionary<int, int>();
        private readonly List<GameObject> _axisObjects = new List<GameObject>();

        /// <summary>True once extraction has started (dots are in the world).</summary>
        public bool IsShown => _extractTime >= 0f;

        /// <summary>True when every dot has landed.</summary>
        public bool ExtractionComplete { get; private set; }

        /// <summary>Raised when every dot has landed.</summary>
        public event Action Extracted;

        /// <summary>Builds dots and axes for a dataset. The chart stays hidden until <see cref="Extract"/>.</summary>
        public void Build(VolumeDataset data, Material dotMaterial, TMP_FontAsset font)
        {
            Clear();
            _records = new DefectRecord[data.Catalog.Count];
            for (int i = 0; i < _records.Length; i++) _records[i] = data.Catalog.Records[i];
            for (int a = 0; a < 3; a++) _columns[a] = data.Meta.chart_axes[a].column;

            _chartPos = new Vector3[_records.Length];
            _radius = new float[_records.Length];
            _startWorld = new Vector3[_records.Length];
            _delay = new float[_records.Length];
            ComputeRanges();
            float maxCbrt = 0f;
            foreach (DefectRecord r in _records) maxCbrt = Mathf.Max(maxCbrt, Mathf.Pow(r.VolumeMm3, 1f / 3f));
            for (int i = 0; i < _records.Length; i++)
            {
                _indexById[_records[i].Id] = i;
                _chartPos[i] = Normalised(_records[i]);
                // Radius ∝ cube root of volume, i.e. ∝ physical size, so area does not exaggerate big pores.
                _radius[i] = Mathf.Lerp(MinDotRadius, MaxDotRadius, Mathf.Pow(_records[i].VolumeMm3, 1f / 3f) / Mathf.Max(maxCbrt, 1e-9f));
            }

            _verts = new Vector3[_records.Length * VertsPerDot];
            _colours = new Color32[_verts.Length];
            var normals = new Vector3[_verts.Length];
            var tris = new int[_records.Length * IndicesPerDot];
            for (int i = 0; i < _records.Length; i++)
            {
                for (int v = 0; v < VertsPerDot; v++) normals[i * VertsPerDot + v] = Octa[v];
                for (int t = 0; t < IndicesPerDot; t++) tris[i * IndicesPerDot + t] = i * VertsPerDot + OctaTris[t];
            }
            _mesh = new Mesh { name = "ChartDots", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.vertices = _verts;
            _mesh.normals = normals;
            _mesh.colors32 = _colours;
            _mesh.triangles = tris;
            _mesh.bounds = new Bounds(Vector3.one * 0.5f, Vector3.one * 100f); // dots fly in from outside
            gameObject.GetComponent<MeshFilter>().sharedMesh = _mesh;
            gameObject.GetComponent<MeshRenderer>().sharedMaterial = dotMaterial;
            BuildAxes(data, font);
            SetVisible(false);
        }

        /// <summary>Recolours every dot (call when the selection or colour mode changes).</summary>
        public void Recolour(Func<DefectRecord, Color32> colourOf, Func<int, bool> isFocused)
        {
            if (_records == null) return;
            for (int i = 0; i < _records.Length; i++)
            {
                Color32 c = colourOf(_records[i]);
                c.a = c.a > 0 ? (byte)255 : (byte)0; // the dot shader clips on alpha
                for (int v = 0; v < VertsPerDot; v++) _colours[i * VertsPerDot + v] = c;
            }
            _mesh.colors32 = _colours;
            _focusedId = 0;
            foreach (DefectRecord r in _records) if (isFocused(r.Id)) { _focusedId = r.Id; break; }
            WriteVertices();
        }

        private int _focusedId;

        /// <summary>
        /// Starts the extraction animation. <paramref name="defectWorld"/> gives each defect's
        /// current world position inside the specimen.
        /// </summary>
        public void Extract(Func<DefectRecord, Vector3> defectWorld)
        {
            Vector3 chartCentre = transform.TransformPoint(Vector3.one * 0.5f);
            float maxD = 1e-6f;
            for (int i = 0; i < _records.Length; i++)
            {
                _startWorld[i] = defectWorld(_records[i]);
                _delay[i] = Vector3.Distance(_startWorld[i], chartCentre);
                maxD = Mathf.Max(maxD, _delay[i]);
            }
            // Nearest defects leave first: the flow reads as a stream from specimen to chart.
            for (int i = 0; i < _records.Length; i++) _delay[i] = StaggerSeconds * _delay[i] / maxD;
            _extractTime = 0f;
            ExtractionComplete = false;
            SetVisible(true);
            WriteVertices();
        }

        /// <summary>Hides the chart and returns to the pre-extraction state.</summary>
        public void Collapse()
        {
            _extractTime = -1f;
            ExtractionComplete = false;
            SetVisible(false);
        }

        private void Update()
        {
            if (_extractTime < 0f || ExtractionComplete) return;
            _extractTime += Time.deltaTime;
            WriteVertices();
            if (_extractTime >= StaggerSeconds + FlightSeconds)
            {
                ExtractionComplete = true;
                Extracted?.Invoke();
            }
        }

        /// <summary>Where the dot of <paramref name="id"/> is now, in world space.</summary>
        public bool TryGetDotWorld(int id, out Vector3 world)
        {
            if (_indexById.TryGetValue(id, out int i))
            {
                world = transform.TransformPoint(CurrentLocal(i));
                return true;
            }
            world = default;
            return false;
        }

        /// <summary>The visible dot nearest to <paramref name="world"/> within <paramref name="maxMeters"/> (0 = none).</summary>
        public int NearestDot(Vector3 world, float maxMeters)
        {
            if (!ExtractionComplete) return 0;
            int best = 0;
            float bestD = maxMeters;
            for (int i = 0; i < _records.Length; i++)
            {
                if (_colours[i * VertsPerDot].a == 0) continue;
                float d = Vector3.Distance(world, transform.TransformPoint(_chartPos[i]));
                if (d < bestD) { bestD = d; best = _records[i].Id; }
            }
            return best;
        }

        /// <summary>True if <paramref name="world"/> is inside the chart cube expanded by <paramref name="marginMeters"/>.</summary>
        public bool Contains(Vector3 world, float marginMeters)
        {
            if (!ExtractionComplete) return false;
            Vector3 l = transform.InverseTransformPoint(world);
            float m = marginMeters / SizeMeters;
            return l.x >= -m && l.y >= -m && l.z >= -m && l.x <= 1 + m && l.y <= 1 + m && l.z <= 1 + m;
        }

        /// <summary>Ids of visible dots inside the axis-aligned chart-space box spanned by two world points.</summary>
        public List<int> IdsInBox(Vector3 worldA, Vector3 worldB)
        {
            Vector3 a = transform.InverseTransformPoint(worldA), b = transform.InverseTransformPoint(worldB);
            Vector3 lo = Vector3.Min(a, b), hi = Vector3.Max(a, b);
            var ids = new List<int>();
            for (int i = 0; i < _records.Length; i++)
            {
                Vector3 p = _chartPos[i];
                if (_colours[i * VertsPerDot].a == 0) continue;
                if (p.x >= lo.x && p.y >= lo.y && p.z >= lo.z && p.x <= hi.x && p.y <= hi.y && p.z <= hi.z) ids.Add(_records[i].Id);
            }
            return ids;
        }

        /// <summary>Chart-local position of a record (for tests).</summary>
        public Vector3 Normalised(in DefectRecord r) => new Vector3(
            Mathf.InverseLerp(_range[0].x, _range[0].y, r.Column(_columns[0])),
            Mathf.InverseLerp(_range[1].x, _range[1].y, r.Column(_columns[1])),
            Mathf.InverseLerp(_range[2].x, _range[2].y, r.Column(_columns[2])));

        private void ComputeRanges()
        {
            for (int a = 0; a < 3; a++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (DefectRecord r in _records)
                {
                    float v = r.Column(_columns[a]);
                    if (float.IsNaN(v)) continue;
                    lo = Mathf.Min(lo, v);
                    hi = Mathf.Max(hi, v);
                }
                if (hi <= lo) hi = lo + 1f;
                _range[a] = new Vector2(lo, hi);
            }
        }

        private Vector3 CurrentLocal(int i)
        {
            if (_extractTime < 0f) return _chartPos[i];
            float t = Mathf.Clamp01((_extractTime - _delay[i]) / FlightSeconds);
            if (t >= 1f) return _chartPos[i];
            float e = t * t * (3f - 2f * t); // smoothstep easing
            Vector3 start = transform.InverseTransformPoint(_startWorld[i]);
            Vector3 end = _chartPos[i];
            Vector3 apex = 0.5f * (start + end) + transform.InverseTransformVector(Vector3.up * ArcHeightMeters);
            // Quadratic Bézier: a smooth arc that leaves the specimen upwards.
            return Vector3.Lerp(Vector3.Lerp(start, apex, e), Vector3.Lerp(apex, end, e), e);
        }

        private void WriteVertices()
        {
            if (_records == null) return;
            float toLocal = 1f / SizeMeters;
            for (int i = 0; i < _records.Length; i++)
            {
                Vector3 c = CurrentLocal(i);
                float r = _radius[i] * toLocal * (_records[i].Id == _focusedId ? 1.8f : 1f);
                for (int v = 0; v < VertsPerDot; v++) _verts[i * VertsPerDot + v] = c + Octa[v] * r;
            }
            _mesh.vertices = _verts;
        }

        private void BuildAxes(VolumeDataset data, TMP_FontAsset font)
        {
            var lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.75f, 0.8f, 0.85f) };
            Vector3[] dirs = { Vector3.right, Vector3.up, Vector3.forward };
            for (int a = 0; a < 3; a++)
            {
                var go = new GameObject($"Axis{a}");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = 2;
                lr.SetPosition(0, Vector3.zero);
                lr.SetPosition(1, dirs[a]);
                lr.widthMultiplier = 0.0015f;
                lr.sharedMaterial = lineMat;
                _axisObjects.Add(go);

                // Title at the axis midpoint, pushed outward; tick values at both ends.
                Vector3 outward = -(Vector3.one - dirs[a]) * 0.1f;
                AddText(data.Meta.chart_axes[a].label, dirs[a] * 0.5f + outward, TitleSize, font);
                AddText(FormatTick(_range[a].x), dirs[a] * 0.03f + outward * 0.5f, TickSize, font);
                AddText(FormatTick(_range[a].y), dirs[a] * 0.97f + outward * 0.5f, TickSize, font);
            }
        }

        // Text sizes in chart-local units (the chart is scaled by SizeMeters): ≈1.5 cm / 1.1 cm tall.
        private const float TitleSize = 0.05f;
        private const float TickSize = 0.037f;

        private static string FormatTick(float v) => Mathf.Abs(v) >= 1000f || (Mathf.Abs(v) < 0.01f && v != 0f) ? v.ToString("0.0E0") : v.ToString("0.##");

        private void AddText(string text, Vector3 localPos, float size, TMP_FontAsset font)
        {
            var go = new GameObject("AxisLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size * 10f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.85f, 0.9f, 0.95f);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(1f, 0.1f);
            go.AddComponent<FaceCamera>();
            _axisObjects.Add(go);
        }

        private void SetVisible(bool visible)
        {
            GetComponent<MeshRenderer>().enabled = visible;
            foreach (GameObject go in _axisObjects) go.SetActive(visible);
        }

        private void Clear()
        {
            foreach (GameObject go in _axisObjects) Destroy(go);
            _axisObjects.Clear();
            _indexById.Clear();
            if (_mesh != null) Destroy(_mesh);
            _extractTime = -1f;
            ExtractionComplete = false;
        }
    }

    /// <summary>Keeps world-space text readable by turning it towards the main camera.</summary>
    public sealed class FaceCamera : MonoBehaviour
    {
        private Transform _cam;

        private void LateUpdate()
        {
            if (_cam == null)
            {
                Camera c = Camera.main;
                if (c == null) return;
                _cam = c.transform;
            }
            transform.rotation = Quaternion.LookRotation(transform.position - _cam.position, _cam.up);
        }
    }
}
