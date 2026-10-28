using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace XRayHands.Core
{
    /// <summary>Defect class as written by the pipeline; the integer is the shader class index.</summary>
    public enum DefectClass
    {
        GasPore = 0, LackOfFusion = 1, Crack = 2, Component = 3,
    }

    /// <summary>Agreement with the reference segmentation.</summary>
    public enum GroundTruthStatus
    {
        NotApplicable = 0, TruePositive = 1, FalsePositive = 2, FalseNegative = 3,
    }

    /// <summary>One row of <c>features.csv</c>: a defect (specimen A) or a part (specimen B).</summary>
    public readonly struct DefectRecord
    {
        public readonly int Id;
        public readonly DefectClass Class;
        public readonly int VoxelCount;
        public readonly float VolumeMm3;
        public readonly float Log10VolumeMm3;
        public readonly float EqDiameterMm;
        public readonly float Sphericity;
        public readonly float Elongation;
        public readonly Vector3 CentroidMm;
        public readonly float DepthMm;
        public readonly Vector3 BoundsMinMm;
        public readonly Vector3 BoundsMaxMm;
        public readonly float MeanDensityU8;
        public readonly int MaterialBin;
        public readonly GroundTruthStatus GroundTruth;

        public DefectRecord(int id, DefectClass cls, int voxelCount, float volumeMm3, float log10VolumeMm3,
            float eqDiameterMm, float sphericity, float elongation, Vector3 centroidMm, float depthMm,
            Vector3 boundsMinMm, Vector3 boundsMaxMm, float meanDensityU8, int materialBin, GroundTruthStatus gt)
        {
            Id = id; Class = cls; VoxelCount = voxelCount; VolumeMm3 = volumeMm3; Log10VolumeMm3 = log10VolumeMm3;
            EqDiameterMm = eqDiameterMm; Sphericity = sphericity; Elongation = elongation; CentroidMm = centroidMm;
            DepthMm = depthMm; BoundsMinMm = boundsMinMm; BoundsMaxMm = boundsMaxMm; MeanDensityU8 = meanDensityU8;
            MaterialBin = materialBin; GroundTruth = gt;
        }

        /// <summary>Value of a numeric CSV column by name (used for configurable chart axes).</summary>
        public float Column(string name)
        {
            switch (name)
            {
                case "voxel_count": return VoxelCount;
                case "volume_mm3": return VolumeMm3;
                case "log10_volume_mm3": return Log10VolumeMm3;
                case "eq_diameter_mm": return EqDiameterMm;
                case "sphericity": return Sphericity;
                case "elongation": return Elongation;
                case "centroid_x_mm": return CentroidMm.x;
                case "centroid_y_mm": return CentroidMm.y;
                case "centroid_z_mm": return CentroidMm.z;
                case "depth_mm": return DepthMm;
                case "mean_density_u8": return MeanDensityU8;
                case "material_bin": return MaterialBin;
                default: throw new ArgumentException($"unknown feature column '{name}'");
            }
        }
    }

    /// <summary>
    /// All defects of a specimen, parsed from <c>features.csv</c>, with O(1) lookup by label id.
    /// The label id is the value stored in the label volume, so the shader, the chart and the
    /// probe all refer to the same defect by the same number.
    /// </summary>
    public sealed class FeatureCatalog
    {
        private readonly DefectRecord[] _records;
        private readonly Dictionary<int, int> _indexById;

        /// <summary>All records in CSV order.</summary>
        public IReadOnlyList<DefectRecord> Records => _records;

        /// <summary>Number of records.</summary>
        public int Count => _records.Length;

        /// <summary>Largest label id (sizes the shader's per-id lookup texture).</summary>
        public int MaxId { get; }

        private FeatureCatalog(DefectRecord[] records)
        {
            _records = records;
            _indexById = new Dictionary<int, int>(records.Length);
            for (int i = 0; i < records.Length; i++)
            {
                _indexById[records[i].Id] = i;
                MaxId = Mathf.Max(MaxId, records[i].Id);
            }
        }

        /// <summary>Record for a label id, if it exists.</summary>
        public bool TryGet(int id, out DefectRecord record)
        {
            if (_indexById.TryGetValue(id, out int i))
            {
                record = _records[i];
                return true;
            }
            record = default;
            return false;
        }

        /// <summary>Parses CSV text with the header produced by <c>tools/common.py</c>.</summary>
        /// <exception cref="FormatException">On a missing column or malformed number.</exception>
        public static FeatureCatalog Parse(string csv)
        {
            string[] lines = csv.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) throw new FormatException("features.csv is empty");
            string[] header = lines[0].TrimEnd('\r').Split(',');
            var col = new Dictionary<string, int>(header.Length);
            for (int i = 0; i < header.Length; i++) col[header[i]] = i;

            int C(string name) => col.TryGetValue(name, out int i) ? i : throw new FormatException($"features.csv: missing column '{name}'");
            int cId = C("id"), cClass = C("class"), cVox = C("voxel_count"), cVol = C("volume_mm3"), cLog = C("log10_volume_mm3"),
                cEq = C("eq_diameter_mm"), cSph = C("sphericity"), cElo = C("elongation"),
                cCx = C("centroid_x_mm"), cCy = C("centroid_y_mm"), cCz = C("centroid_z_mm"), cDepth = C("depth_mm"),
                cMinX = C("bbox_min_x_mm"), cMinY = C("bbox_min_y_mm"), cMinZ = C("bbox_min_z_mm"),
                cMaxX = C("bbox_max_x_mm"), cMaxY = C("bbox_max_y_mm"), cMaxZ = C("bbox_max_z_mm"),
                cDen = C("mean_density_u8"), cBin = C("material_bin"), cGt = C("gt_status");

            var records = new DefectRecord[lines.Length - 1];
            for (int r = 1; r < lines.Length; r++)
            {
                string[] f = lines[r].TrimEnd('\r').Split(',');
                float F(int i) => ParseFloat(f[i], r);
                records[r - 1] = new DefectRecord(
                    int.Parse(f[cId], CultureInfo.InvariantCulture), ParseClass(f[cClass]),
                    int.Parse(f[cVox], CultureInfo.InvariantCulture), F(cVol), F(cLog), F(cEq), F(cSph), F(cElo),
                    new Vector3(F(cCx), F(cCy), F(cCz)), F(cDepth),
                    new Vector3(F(cMinX), F(cMinY), F(cMinZ)), new Vector3(F(cMaxX), F(cMaxY), F(cMaxZ)),
                    F(cDen), int.Parse(f[cBin], CultureInfo.InvariantCulture), ParseGt(f[cGt]));
            }
            return new FeatureCatalog(records);
        }

        private static float ParseFloat(string s, int row)
        {
            if (string.Equals(s, "nan", StringComparison.OrdinalIgnoreCase)) return float.NaN;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
            throw new FormatException($"features.csv row {row}: '{s}' is not a number");
        }

        /// <summary>Maps the pipeline's class names to <see cref="DefectClass"/>.</summary>
        public static DefectClass ParseClass(string s)
        {
            switch (s)
            {
                case "gas_pore": return DefectClass.GasPore;
                case "lack_of_fusion": return DefectClass.LackOfFusion;
                case "crack": return DefectClass.Crack;
                case "component": return DefectClass.Component;
                default: throw new FormatException($"unknown defect class '{s}'");
            }
        }

        /// <summary>Maps TP/FP/FN/NA to <see cref="GroundTruthStatus"/>.</summary>
        public static GroundTruthStatus ParseGt(string s)
        {
            switch (s)
            {
                case "TP": return GroundTruthStatus.TruePositive;
                case "FP": return GroundTruthStatus.FalsePositive;
                case "FN": return GroundTruthStatus.FalseNegative;
                case "NA": return GroundTruthStatus.NotApplicable;
                default: throw new FormatException($"unknown gt_status '{s}'");
            }
        }
    }
}
