using UnityEngine;

namespace XRayHands.Core
{
    /// <summary>
    /// Conversions between the three coordinate spaces used in the app.
    /// <list type="bullet">
    /// <item><b>Texture space</b>: [0,1]^3 over the volume, what the shader samples.</item>
    /// <item><b>Volume space (mm)</b>: physical millimetres from the volume corner, what
    /// <c>features.csv</c> uses for centroids and bounding boxes.</item>
    /// <item><b>Object space</b>: the unit cube [-0.5, 0.5]^3 of the volume proxy mesh; the
    /// specimen transform maps it to the world.</item>
    /// </list>
    /// Keeping all conversions here means every tool agrees on what "position" means.
    /// </summary>
    public static class PhysicalUnits
    {
        /// <summary>Millimetres per metre.</summary>
        public const float MillimetresPerMetre = 1000f;

        /// <summary>Physical size of the whole volume in mm.</summary>
        public static Vector3 ExtentMm(Vector3Int dims, Vector3 spacingMm) =>
            new Vector3(dims.x * spacingMm.x, dims.y * spacingMm.y, dims.z * spacingMm.z);

        /// <summary>Volume-space millimetres to texture coordinates [0,1]^3.</summary>
        public static Vector3 MmToTexture(Vector3 mm, Vector3 extentMm) =>
            new Vector3(mm.x / extentMm.x, mm.y / extentMm.y, mm.z / extentMm.z);

        /// <summary>Texture coordinates to volume-space millimetres.</summary>
        public static Vector3 TextureToMm(Vector3 uvw, Vector3 extentMm) => Vector3.Scale(uvw, extentMm);

        /// <summary>Texture coordinates to proxy-cube object space (centre at origin).</summary>
        public static Vector3 TextureToObject(Vector3 uvw) => uvw - new Vector3(0.5f, 0.5f, 0.5f);

        /// <summary>Proxy-cube object space to texture coordinates.</summary>
        public static Vector3 ObjectToTexture(Vector3 obj) => obj + new Vector3(0.5f, 0.5f, 0.5f);

        /// <summary>Volume-space millimetres to proxy-cube object space.</summary>
        public static Vector3 MmToObject(Vector3 mm, Vector3 extentMm) => TextureToObject(MmToTexture(mm, extentMm));

        /// <summary>
        /// Object-space scale that gives the proxy cube the volume's true aspect ratio, normalised
        /// so the longest axis is 1. The specimen root then applies the display scale.
        /// </summary>
        public static Vector3 AspectScale(Vector3 extentMm)
        {
            float longest = Mathf.Max(extentMm.x, Mathf.Max(extentMm.y, extentMm.z));
            return extentMm / longest;
        }

        /// <summary>Format a magnification factor the way the scale label shows it ("×8.0").</summary>
        public static string FormatScale(float factor) =>
            factor >= 100f ? $"×{factor:0}" : $"×{factor:0.0}";
    }
}
