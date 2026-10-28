using UnityEngine;
using UnityEngine.Rendering;
using XRayHands.Analytics;
using XRayHands.Core;

namespace XRayHands.Rendering
{
    /// <summary>Ray-marching quality presets (steps along the cube diagonal).</summary>
    public enum QualityTier
    {
        Low = 96, Medium = 160, High = 256,
    }

    /// <summary>
    /// Owns the volume proxy cube and its material, and is the only class that writes shader
    /// uniforms. Tools call its small API (peel, clip plane, lens, window, colours) instead of
    /// touching the material, so every visual state has one entry point.
    /// </summary>
    /// <remarks>
    /// Hierarchy: this component sits on the <b>specimen root</b> (moved/rotated/scaled by the
    /// grab tool). A child "Proxy" holds the unit cube, scaled to the volume's aspect ratio, so
    /// <see cref="ProxyTransform"/>'s local space is exactly the shader's object space.
    /// </remarks>
    public sealed class VolumeRenderer : MonoBehaviour
    {
        [Tooltip("The XRayHands/XRayVolume shader (referenced so it is included in builds).")]
        [SerializeField] private Shader _shader;

        [Tooltip("Starting quality tier.")]
        [SerializeField] private QualityTier _quality = QualityTier.Medium;

        private static readonly int DensityTexId = Shader.PropertyToID("_DensityTex");
        private static readonly int LabelTexId = Shader.PropertyToID("_LabelTex");
        private static readonly int BrickTexId = Shader.PropertyToID("_BrickTex");
        private static readonly int TransferTexId = Shader.PropertyToID("_TransferTex");
        private static readonly int IdLutId = Shader.PropertyToID("_IdLut");
        private static readonly int IdLutSizeId = Shader.PropertyToID("_IdLutSize");
        private static readonly int StepCountId = Shader.PropertyToID("_StepCount");
        private static readonly int PeelId = Shader.PropertyToID("_Peel");
        private static readonly int WindowLevelId = Shader.PropertyToID("_WindowLevel");
        private static readonly int WindowWidthId = Shader.PropertyToID("_WindowWidth");
        private static readonly int ClipPlaneId = Shader.PropertyToID("_ClipPlane");
        private static readonly int ClipEnabledId = Shader.PropertyToID("_ClipEnabled");
        private static readonly int LensCentreId = Shader.PropertyToID("_LensCentreWS");
        private static readonly int LensNormalId = Shader.PropertyToID("_LensNormalWS");
        private static readonly int LensRadiusId = Shader.PropertyToID("_LensRadius");
        private static readonly int DefectGlowId = Shader.PropertyToID("_DefectGlow");
        private static readonly int BrickGridId = Shader.PropertyToID("_BrickGrid");
        private static readonly int VolumeDimsId = Shader.PropertyToID("_VolumeDims");

        private Material _material;
        private Transform _proxy;
        private Texture2D _transfer;
        private IdLookupTexture _idLut;

        /// <summary>The loaded dataset (null until <see cref="SetDataset"/>).</summary>
        public VolumeDataset Dataset { get; private set; }

        /// <summary>Unit cube whose local space is the shader's object space.</summary>
        public Transform ProxyTransform => _proxy;

        /// <summary>Current peel threshold 0..1.</summary>
        public float Peel { get; private set; }

        /// <summary>Current window level / width (normalised density).</summary>
        public Vector2 Window { get; private set; } = new Vector2(0.5f, 1f);

        /// <summary>True while a clip plane is applied.</summary>
        public bool ClipEnabled { get; private set; }

        /// <summary>Current quality tier.</summary>
        public QualityTier Quality => _quality;

        /// <summary>True once <see cref="Initialize"/> has run.</summary>
        public bool IsInitialized => _material != null;

        /// <summary>
        /// Creates the material and the proxy cube. Called explicitly by the composition root
        /// (instead of Awake) so the shader can be injected before anything is built.
        /// </summary>
        public void Initialize(Shader shader)
        {
            if (IsInitialized) return;
            if (shader != null) _shader = shader;
            if (_shader == null) _shader = Shader.Find("XRayHands/XRayVolume");
            _material = new Material(_shader) { name = "XRayVolume (instance)" };

            var go = new GameObject("Proxy");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = CreateUnitCube();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _proxy = go.transform;
            SetQuality(_quality);
        }

        private void OnDestroy()
        {
            _idLut?.Dispose();
            if (_transfer != null) Destroy(_transfer);
            if (_material != null) Destroy(_material);
        }

        /// <summary>Binds a loaded dataset: textures, aspect ratio, transfer function, colours.</summary>
        public void SetDataset(VolumeDataset dataset, SelectionModel selection)
        {
            Dataset = dataset;
            _proxy.localScale = PhysicalUnits.AspectScale(dataset.ExtentMm);

            if (_transfer != null) Destroy(_transfer);
            _transfer = TransferFunction.CreateTexture(dataset.Meta);
            _idLut?.Dispose();
            _idLut = new IdLookupTexture(dataset.Meta, dataset.Catalog);

            _material.SetTexture(DensityTexId, dataset.DensityGradientTexture);
            _material.SetTexture(LabelTexId, dataset.LabelTexture);
            _material.SetTexture(BrickTexId, dataset.BrickTexture);
            _material.SetTexture(TransferTexId, _transfer);
            _material.SetTexture(IdLutId, _idLut.Texture);
            _material.SetVector(IdLutSizeId, new Vector4(_idLut.Texture.width, _idLut.Texture.height,
                1f / _idLut.Texture.width, 1f / _idLut.Texture.height));
            Vector3Int g = BrickGridBaker.GridDims(dataset.Dims);
            _material.SetVector(BrickGridId, new Vector4(g.x, g.y, g.z, BrickGridBaker.BrickSize));
            _material.SetVector(VolumeDimsId, new Vector4(dataset.Dims.x, dataset.Dims.y, dataset.Dims.z, 0f));

            SetWindow(dataset.Meta.window.level, dataset.Meta.window.width);
            SetPeel(0f);
            DisableClip();
            DisableLens();
            RefreshColours(DefectColourMode.Class, selection, false);
        }

        /// <summary>Recomputes defect colours (call when mode or selection changes).</summary>
        public void RefreshColours(DefectColourMode mode, SelectionModel selection, bool hideUnselected) =>
            _idLut?.Rebuild(mode, selection, hideUnselected);

        /// <summary>Colour the volume currently uses for a defect id.</summary>
        public Color32 DefectColour(int id) => _idLut != null ? _idLut.ColourOf(id) : default;

        /// <summary>Density threshold below which material turns to glass (0 = off).</summary>
        public void SetPeel(float threshold)
        {
            Peel = Mathf.Clamp01(threshold);
            _material.SetFloat(PeelId, Peel);
        }

        /// <summary>Window for the cut-face slice, in normalised density.</summary>
        public void SetWindow(float level, float width)
        {
            Window = new Vector2(Mathf.Clamp01(level), Mathf.Clamp(width, 0.01f, 1f));
            _material.SetFloat(WindowLevelId, Window.x);
            _material.SetFloat(WindowWidthId, Window.y);
        }

        /// <summary>
        /// Clips the volume with a world-space plane. The half-space the normal points into is
        /// kept; the tool orients the normal away from the user, so the near part is removed.
        /// </summary>
        public void SetClipPlaneWorld(Vector3 pointWS, Vector3 normalWS)
        {
            // Planes transform with the transpose of the point transform: for p_ws = M p_os,
            // (n, d)·(M p_os) = (Mᵀ (n, d))·p_os.
            var planeWS = new Vector4(normalWS.x, normalWS.y, normalWS.z, -Vector3.Dot(normalWS, pointWS));
            Vector4 planeOS = _proxy.localToWorldMatrix.transpose * planeWS;
            float len = new Vector3(planeOS.x, planeOS.y, planeOS.z).magnitude;
            _material.SetVector(ClipPlaneId, planeOS / Mathf.Max(len, 1e-8f));
            _material.SetFloat(ClipEnabledId, 1f);
            ClipEnabled = true;
        }

        /// <summary>Removes the clip plane.</summary>
        public void DisableClip()
        {
            _material.SetFloat(ClipEnabledId, 0f);
            ClipEnabled = false;
        }

        /// <summary>Activates the magic lens: a disc in world space through which the volume is peeled.</summary>
        public void SetLens(Vector3 centreWS, Vector3 normalWS, float radiusMeters)
        {
            _material.SetVector(LensCentreId, centreWS);
            _material.SetVector(LensNormalId, normalWS.normalized);
            _material.SetFloat(LensRadiusId, radiusMeters);
        }

        /// <summary>Turns the lens off.</summary>
        public void DisableLens() => _material.SetFloat(LensRadiusId, 0f);

        /// <summary>Defect emission strength.</summary>
        public void SetDefectGlow(float glow) => _material.SetFloat(DefectGlowId, glow);

        /// <summary>Selects the ray-marching step count.</summary>
        public void SetQuality(QualityTier tier)
        {
            _quality = tier;
            if (_material != null) _material.SetFloat(StepCountId, (int)tier);
        }

        /// <summary>World position → texture coordinates [0,1]^3 (outside values mean outside the volume).</summary>
        public Vector3 WorldToTexture(Vector3 worldPos) =>
            PhysicalUnits.ObjectToTexture(_proxy.InverseTransformPoint(worldPos));

        /// <summary>Volume-space millimetres → world position.</summary>
        public Vector3 MmToWorld(Vector3 mm) =>
            _proxy.TransformPoint(PhysicalUnits.MmToObject(mm, Dataset.ExtentMm));

        /// <summary>True if <paramref name="uvw"/> lies inside the volume.</summary>
        public static bool InsideVolume(Vector3 uvw) =>
            uvw.x >= 0f && uvw.y >= 0f && uvw.z >= 0f && uvw.x <= 1f && uvw.y <= 1f && uvw.z <= 1f;

        private static Mesh CreateUnitCube()
        {
            // Build our own cube: the built-in one has 24 vertices with UVs/normals we do not need.
            var mesh = new Mesh { name = "VolumeProxyCube" };
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f);
            mesh.vertices = v;
            mesh.triangles = new[]
            {
                0, 2, 1, 1, 2, 3,  // -z
                4, 5, 6, 5, 7, 6,  // +z
                0, 1, 4, 1, 5, 4,  // -y
                2, 6, 3, 3, 6, 7,  // +y
                0, 4, 2, 2, 4, 6,  // -x
                1, 3, 5, 3, 7, 5,  // +x
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
