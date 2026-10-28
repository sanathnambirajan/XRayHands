using UnityEngine;
using XRayHands.Analytics;
using XRayHands.Core;

namespace XRayHands.Rendering
{
    /// <summary>
    /// Builds the 1D transfer function (density → colour, opacity) from a specimen's material
    /// table. The look follows the brief: air invisible, material cool steel-grey, the densest
    /// material warm amber. Opacity is authored for 160 samples across the cube diagonal; the
    /// shader corrects it for other step counts.
    /// </summary>
    public static class TransferFunction
    {
        /// <summary>Entries in the 1D texture (one per 8-bit density value).</summary>
        public const int Resolution = 256;

        /// <summary>Per-sample opacity of solid material (before gradient weighting).</summary>
        public const float MaterialAlpha = 0.09f;

        /// <summary>Densities above this fraction of the range are tinted amber ("dense metal").</summary>
        public const float DenseStart = 0.82f;

        // Width of the air→material opacity ramp in 8-bit units: a hard step would alias.
        private const int EdgeRampU8 = 10;

        private static readonly Color SteelGrey = new Color32(0xA9, 0xB6, 0xC2, 0xFF);
        private static readonly Color Amber = new Color32(0xE8, 0xA8, 0x4A, 0xFF);

        /// <summary>Computes the 256 transfer-function entries for <paramref name="meta"/>.</summary>
        public static Color[] Build(SpecimenMeta meta)
        {
            var tf = new Color[Resolution];
            // The first material bin is air; everything above its upper edge is material.
            int airMax = meta.materials[0].u8_max;
            for (int i = 0; i < Resolution; i++)
            {
                float d = i / (float)(Resolution - 1);
                float alpha = Mathf.Clamp01((i - airMax) / (float)EdgeRampU8) * MaterialAlpha;
                Color binColour = DefectPalette.ParseHex(BinColour(meta, i));
                // Single-material specimens use steel grey; multi-material ones keep their bin colours.
                Color baseColour = meta.materials.Length <= 2 ? SteelGrey : Color.Lerp(SteelGrey, binColour, 0.6f);
                Color c = Color.Lerp(baseColour, Amber, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DenseStart, 1f, d)));
                c.a = alpha;
                tf[i] = c;
            }
            return tf;
        }

        /// <summary>Creates the 256×1 texture the shader samples.</summary>
        public static Texture2D CreateTexture(SpecimenMeta meta)
        {
            var tex = new Texture2D(Resolution, 1, TextureFormat.RGBA32, false, true)
            {
                name = $"{meta.id}_transfer",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels(Build(meta));
            tex.Apply(false, false);
            return tex;
        }

        private static string BinColour(SpecimenMeta meta, int u8)
        {
            foreach (SpecimenMeta.Material m in meta.materials)
            {
                if (u8 >= m.u8_min && u8 <= m.u8_max) return m.color;
            }
            return "#A9B6C2";
        }
    }

    /// <summary>
    /// The per-id colour lookup texture (256 × ceil(maxId/256)). Rebuilt on the CPU whenever
    /// the colour mode or selection changes, which costs microseconds for a few thousand ids
    /// and keeps every display rule in testable C# instead of shader branches.
    /// </summary>
    public sealed class IdLookupTexture : System.IDisposable
    {
        /// <summary>LUT width; ids map to (id % Width, id / Width).</summary>
        public const int Width = 256;

        private readonly FeatureCatalog _catalog;
        private readonly Color32[] _pixels;
        private readonly Color32[] _materialColours;

        /// <summary>The GPU texture.</summary>
        public Texture2D Texture { get; }

        /// <summary>Creates a LUT large enough for every id in <paramref name="catalog"/>.</summary>
        public IdLookupTexture(SpecimenMeta meta, FeatureCatalog catalog)
        {
            _catalog = catalog;
            int height = Mathf.Max(1, (catalog.MaxId + Width) / Width);
            _pixels = new Color32[Width * height];
            _materialColours = new Color32[meta.materials.Length];
            for (int i = 0; i < meta.materials.Length; i++) _materialColours[i] = DefectPalette.ParseHex(meta.materials[i].color);
            Texture = new Texture2D(Width, height, TextureFormat.RGBA32, false, true)
            {
                name = $"{meta.id}_idlut",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
        }

        /// <summary>Recomputes every id's colour and uploads the texture.</summary>
        public void Rebuild(DefectColourMode mode, SelectionModel selection, bool hideUnselected)
        {
            System.Array.Clear(_pixels, 0, _pixels.Length);
            foreach (DefectRecord r in _catalog.Records)
            {
                _pixels[r.Id] = DefectPalette.ColourOf(r, mode, selection, hideUnselected, _materialColours);
            }
            Texture.SetPixels32(_pixels);
            Texture.Apply(false, false);
        }

        /// <summary>Colour currently assigned to <paramref name="id"/> (for tests and the chart).</summary>
        public Color32 ColourOf(int id) => id >= 0 && id < _pixels.Length ? _pixels[id] : default;

        /// <summary>Destroys the texture.</summary>
        public void Dispose() => Object.Destroy(Texture);
    }
}
