using System.IO;
using UnityEngine;

namespace XRayHands.Verification
{
    /// <summary>
    /// Renders a camera offscreen at an arbitrary resolution and saves a PNG. Used by the
    /// screenshot harness and for print-quality figures: the result does not depend on the
    /// Game view size and contains no editor UI.
    /// </summary>
    public static class CaptureUtility
    {
        /// <summary>MSAA samples for captures (smooth hand skeleton lines and chart dots).</summary>
        public const int CaptureMsaa = 8;

        /// <summary>Renders <paramref name="camera"/> into a <paramref name="width"/>×<paramref name="height"/> PNG at <paramref name="path"/>.</summary>
        public static string Capture(Camera camera, string path, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = CaptureMsaa,
            };
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.Destroy(tex);
                return Path.GetFullPath(path);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                Object.Destroy(rt);
            }
        }
    }
}
