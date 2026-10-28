using System;
using UnityEngine;

namespace XRayHands.Core
{
    /// <summary>
    /// Mirror of <c>meta.json</c> written by <c>tools/pipeline.py</c>. Field names are
    /// snake_case on purpose: <see cref="JsonUtility"/> maps JSON keys to field names
    /// one-to-one, and matching the Python output exactly avoids a translation layer.
    /// </summary>
    [Serializable]
    public sealed class SpecimenMeta
    {
        public int schema_version;
        public string id;
        public string name;
        public int[] dims;          // x, y, z voxel counts
        public float[] spacing_mm;  // x, y, z voxel size in mm
        public string volume_file;
        public string label_file;
        public string features_file;
        public ValueRange value_range;
        public Window window;
        public Material[] materials;
        public int defect_count;
        public ChartAxis[] chart_axes;
        public GroundTruth ground_truth;
        public Display display;
        public string license;
        public string attribution;

        /// <summary>Raw grey-value range mapped to 0..255 in the 8-bit volume.</summary>
        [Serializable] public sealed class ValueRange { public float raw_low; public float raw_high; public string unit; }

        /// <summary>Default display window in normalised [0,1] intensity.</summary>
        [Serializable] public sealed class Window { public float level; public float width; }

        /// <summary>One intensity bin with a human-readable name (shown by the probe).</summary>
        [Serializable] public sealed class Material { public string name; public int u8_min; public int u8_max; public string color; }

        /// <summary>Which CSV column a chart axis shows, and its label.</summary>
        [Serializable] public sealed class ChartAxis { public string column; public string label; }

        /// <summary>Agreement with the reference segmentation (specimen A only).</summary>
        [Serializable]
        public sealed class GroundTruth
        {
            public float precision; public float recall; public float iou;
            public int reference_count; public int predicted_count; public int missed_count;
        }

        /// <summary>Physical size and the allowed display magnification range.</summary>
        [Serializable]
        public sealed class Display
        {
            public float physical_extent_m; public float default_scale; public float min_scale; public float max_scale;
        }

        /// <summary>Voxel counts as a vector.</summary>
        public Vector3Int Dims => new Vector3Int(dims[0], dims[1], dims[2]);

        /// <summary>Voxel size in mm as a vector.</summary>
        public Vector3 SpacingMm => new Vector3(spacing_mm[0], spacing_mm[1], spacing_mm[2]);

        /// <summary>True when the JSON had reference-segmentation metrics.</summary>
        public bool HasGroundTruth => ground_truth != null && ground_truth.reference_count > 0;

        /// <summary>Parses and checks the fields the app relies on.</summary>
        /// <exception cref="FormatException">If required fields are missing or inconsistent.</exception>
        public static SpecimenMeta Parse(string json)
        {
            SpecimenMeta m = JsonUtility.FromJson<SpecimenMeta>(json);
            if (m == null) throw new FormatException("meta.json is empty");
            if (m.dims == null || m.dims.Length != 3) throw new FormatException("meta.json: dims must have 3 values");
            if (m.spacing_mm == null || m.spacing_mm.Length != 3) throw new FormatException("meta.json: spacing_mm must have 3 values");
            if (m.materials == null || m.materials.Length == 0) throw new FormatException("meta.json: materials missing");
            if (m.chart_axes == null || m.chart_axes.Length != 3) throw new FormatException("meta.json: need exactly 3 chart axes");
            if (m.display == null || m.display.default_scale <= 0f) throw new FormatException("meta.json: display block missing");
            return m;
        }

        /// <summary>Name of the material bin containing 8-bit value <paramref name="u8"/>.</summary>
        public string MaterialAt(int u8)
        {
            foreach (Material mat in materials)
            {
                if (u8 >= mat.u8_min && u8 <= mat.u8_max) return mat.name;
            }
            return "unknown";
        }

        /// <summary>Converts an 8-bit value back to the raw scanner grey value it came from.</summary>
        public float RawValue(int u8) =>
            value_range == null ? u8 : Mathf.Lerp(value_range.raw_low, value_range.raw_high, u8 / 255f);
    }
}
