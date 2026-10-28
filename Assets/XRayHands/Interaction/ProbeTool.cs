using UnityEngine;
using XRayHands.Core;
using XRayHands.Gestures;
using XRayHands.Input;
using XRayHands.Rendering;

namespace XRayHands.Interaction
{
    /// <summary>What the probe measures at the fingertip this frame.</summary>
    public struct ProbeReading
    {
        /// <summary>True while the fingertip is inside the volume.</summary>
        public bool Inside;
        /// <summary>Fingertip world position (sound source).</summary>
        public Vector3 WorldPosition;
        /// <summary>Normalised density 0..1 (drives the tone pitch).</summary>
        public float Density01;
        /// <summary>Raw scanner grey value (uncalibrated attenuation, shown on the label).</summary>
        public float RawValue;
        /// <summary>Material bin name from meta.json.</summary>
        public string Material;
        /// <summary>Defect id under the fingertip (0 = none).</summary>
        public int DefectId;
        /// <summary>Physical distance (mm) from the fingertip to the nearest defect surface (0 inside one).</summary>
        public float NearestDefectMm;
        /// <summary>Id of the nearest defect.</summary>
        public int NearestDefectId;
    }

    /// <summary>
    /// Index point = probe. Reads the CT value under the fingertip, names the material and the
    /// defect, and finds the nearest defect for the Geiger clicks. Pure measurement: sound and
    /// visuals subscribe to <see cref="Reading"/> (see ProbeSonifier).
    /// </summary>
    public sealed class ProbeTool : ITool
    {
        private const string LabelKey = "probe";
        private readonly ISpecimenHost _host;
        private readonly IFeedbackSink _fx;
        private Handedness _hand;
        private ProbeReading _reading;

        /// <summary>Creates the tool.</summary>
        public ProbeTool(ISpecimenHost host, IFeedbackSink fx) { _host = host; _fx = fx; }

        /// <inheritdoc/>
        public string Name => "Probe";

        /// <inheritdoc/>
        public HandUsage Usage => HandPoses.Of(_hand);

        /// <summary>Latest reading (Inside = false when not probing).</summary>
        public ProbeReading Reading => _reading;

        /// <inheritdoc/>
        public bool CanStart(in ToolFrame f) => f.Active(GestureId.PointLeft, Handedness.Right) || f.Active(GestureId.PointLeft, Handedness.Left);

        /// <inheritdoc/>
        public void Begin(in ToolFrame f)
        {
            _hand = f.Active(GestureId.PointLeft, Handedness.Right) ? Handedness.Right : Handedness.Left;
        }

        /// <inheritdoc/>
        public bool Tick(in ToolFrame f)
        {
            HandFrame h = f.Context.Hand(_hand);
            if (!f.Active(GestureId.PointLeft, _hand) || !h.IsTracked || _host.Dataset == null)
            {
                _reading.Inside = false;
                return false;
            }
            Vector3 tip = h.Joint(HandJoint.IndexTip);
            _reading = Measure(_host.Volume, _host.Dataset, tip);
            if (_reading.Inside)
            {
                string defect = _reading.DefectId > 0 && _host.Dataset.Catalog.TryGet(_reading.DefectId, out DefectRecord r)
                    ? $"\n#{r.Id} {ClassName(r.Class)}  Ø{r.EqDiameterMm * 1000f:0} µm"
                    : $"\nnearest defect {_reading.NearestDefectMm * 1000f:0} µm";
                _fx.ShowLabel(LabelKey, tip + Vector3.up * 0.035f,
                    $"{_reading.Material}  {_reading.RawValue:0} gv{defect}");
            }
            else
            {
                _fx.HideLabel(LabelKey);
            }
            return true;
        }

        /// <inheritdoc/>
        public void End()
        {
            _reading.Inside = false;
            _fx.HideLabel(LabelKey);
        }

        /// <summary>
        /// Measures the volume at a world position. Static and side-effect free so tests can
        /// call it directly with a known dataset.
        /// </summary>
        public static ProbeReading Measure(VolumeRenderer volume, VolumeDataset data, Vector3 worldPos)
        {
            var reading = new ProbeReading { WorldPosition = worldPos };
            Vector3 uvw = volume.WorldToTexture(worldPos);
            if (!VolumeRenderer.InsideVolume(uvw)) return reading;

            reading.Inside = true;
            int u8 = data.DensityAt(uvw);
            reading.Density01 = u8 / 255f;
            reading.RawValue = data.Meta.RawValue(u8);
            reading.Material = data.Meta.MaterialAt(u8);
            reading.DefectId = data.LabelAt(uvw);

            // Nearest defect surface, approximated as centroid distance minus equivalent radius.
            // A linear scan over a few thousand defects is ~microseconds and allocation-free;
            // a spatial index would only pay off at 10^5+ defects.
            Vector3 mm = PhysicalUnits.TextureToMm(uvw, data.ExtentMm);
            float best = float.MaxValue;
            int bestId = 0;
            foreach (DefectRecord r in data.Catalog.Records)
            {
                if (r.GroundTruth == GroundTruthStatus.FalseNegative) continue; // not our detection
                float d = Vector3.Distance(mm, r.CentroidMm) - 0.5f * r.EqDiameterMm;
                if (d < best) { best = d; bestId = r.Id; }
            }
            reading.NearestDefectMm = reading.DefectId > 0 ? 0f : Mathf.Max(best, 0f);
            reading.NearestDefectId = reading.DefectId > 0 ? reading.DefectId : bestId;
            return reading;
        }

        /// <summary>Human-readable class name for labels.</summary>
        public static string ClassName(DefectClass c)
        {
            switch (c)
            {
                case DefectClass.Crack: return "crack";
                case DefectClass.LackOfFusion: return "lack of fusion";
                case DefectClass.GasPore: return "gas pore";
                default: return "part";
            }
        }
    }
}
