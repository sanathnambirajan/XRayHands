using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace XRayHands.Core
{
    /// <summary>
    /// Loads a processed specimen folder from <c>StreamingAssets/Specimens/&lt;id&gt;/</c>.
    /// </summary>
    /// <remarks>
    /// Why UnityWebRequest instead of File.ReadAllBytes: on Android (Quest) StreamingAssets live
    /// inside the compressed APK and are not files on disk; UnityWebRequest is the supported
    /// way to read them, and it also works on PC and in the editor, so there is one code path.
    /// </remarks>
    public static class VolumeLoader
    {
        /// <summary>Folder inside StreamingAssets that holds all specimens.</summary>
        public const string SpecimensFolder = "Specimens";

        /// <summary>URL-or-path of a file inside a specimen folder.</summary>
        public static string SpecimenFilePath(string specimenId, string fileName) =>
            Path.Combine(Application.streamingAssetsPath, SpecimensFolder, specimenId, fileName).Replace('\\', '/');

        /// <summary>
        /// Coroutine: loads meta, features, volume and labels, then creates the GPU textures.
        /// Calls exactly one of <paramref name="onLoaded"/> or <paramref name="onError"/>.
        /// </summary>
        public static IEnumerator Load(string specimenId, Action<VolumeDataset> onLoaded, Action<string> onError)
        {
            string metaText = null, csvText = null;
            byte[] volume = null, labelBytes = null;
            string error = null;

            yield return Fetch(SpecimenFilePath(specimenId, "meta.json"), r => metaText = r.downloadHandler.text, e => error = e);
            if (error != null) { onError(error); yield break; }

            SpecimenMeta meta;
            try { meta = SpecimenMeta.Parse(metaText); }
            catch (FormatException ex) { onError($"{specimenId}: {ex.Message}"); yield break; }

            yield return Fetch(SpecimenFilePath(specimenId, meta.features_file), r => csvText = r.downloadHandler.text, e => error = e);
            if (error == null) yield return Fetch(SpecimenFilePath(specimenId, meta.volume_file), r => volume = r.downloadHandler.data, e => error = e);
            if (error == null) yield return Fetch(SpecimenFilePath(specimenId, meta.label_file), r => labelBytes = r.downloadHandler.data, e => error = e);
            if (error != null) { onError(error); yield break; }

            VolumeDataset dataset;
            try
            {
                FeatureCatalog catalog = FeatureCatalog.Parse(csvText);
                dataset = new VolumeDataset(meta, catalog, volume, DecodeLabels(labelBytes));
                dataset.CreateTextures();
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
            {
                onError($"{specimenId}: {ex.Message}");
                yield break;
            }
            onLoaded(dataset);
        }

        /// <summary>Little-endian uint16 bytes → ushort array.</summary>
        public static ushort[] DecodeLabels(byte[] bytes)
        {
            if (bytes.Length % 2 != 0) throw new FormatException("label file has an odd byte count");
            var labels = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, labels, 0, bytes.Length); // all Unity targets are little-endian
            return labels;
        }

        private static IEnumerator Fetch(string path, Action<UnityWebRequest> onOk, Action<string> onError)
        {
            // In the editor and on PC the path is a plain file path; UnityWebRequest needs a URI.
            string uri = path.Contains("://") ? path : new Uri(path).AbsoluteUri;
            using (UnityWebRequest req = UnityWebRequest.Get(uri))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) onError($"could not load {path}: {req.error}");
                else onOk(req);
            }
        }
    }
}
