using UnityEngine;
using XRayHands.Analytics;
using XRayHands.Core;
using XRayHands.Gestures;
using XRayHands.Input;

namespace XRayHands.Interaction
{
    /// <summary>
    /// Pinch on a defect inside the specimen. Before extraction, pulling the pinch away by
    /// <see cref="PullDistanceMeters"/> launches the extraction animation into the chart.
    /// After extraction, pinching a defect focuses it (selection linked back to its dot).
    /// </summary>
    public sealed class ExtractTool : ITool
    {
        /// <summary>Pull distance that triggers extraction, metres.</summary>
        public const float PullDistanceMeters = 0.12f;
        /// <summary>Search radius around the pinch point for a defect, in voxels.</summary>
        public const int PickRadiusVoxels = 2;

        private readonly ISpecimenHost _host;
        private readonly IFeedbackSink _fx;
        private readonly DefectChart3D _chart;
        private readonly System.Action _placeChart;
        private Handedness _hand;
        private Vector3 _start;
        private int _pickedId;

        /// <summary>Creates the tool. <paramref name="placeChart"/> positions the chart next to the specimen before extraction.</summary>
        public ExtractTool(ISpecimenHost host, IFeedbackSink fx, DefectChart3D chart, System.Action placeChart)
        {
            _host = host; _fx = fx; _chart = chart; _placeChart = placeChart;
        }

        /// <inheritdoc/>
        public string Name => "Extract";

        /// <inheritdoc/>
        public HandUsage Usage => HandPoses.Of(_hand);

        /// <summary>Number of extractions triggered (tests).</summary>
        public int ExtractCount { get; private set; }

        /// <inheritdoc/>
        public bool CanStart(in ToolFrame f)
        {
            for (int i = 0; i < 2; i++)
            {
                var h = (Handedness)i;
                if (f.Gestures.State(GestureRecognizer.ForHand(GestureId.PinchLeft, h)).JustEntered &&
                    PickDefect(f.Context.Hand(h).PinchPoint) > 0) return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public void Begin(in ToolFrame f)
        {
            _hand = f.Gestures.State(GestureId.PinchRight).JustEntered ? Handedness.Right : Handedness.Left;
            _start = f.Context.Hand(_hand).PinchPoint;
            _pickedId = PickDefect(_start);
            _host.Selection.Focus(_pickedId);
            _fx.PlayRecognised(_start);
        }

        /// <inheritdoc/>
        public bool Tick(in ToolFrame f)
        {
            HandFrame h = f.Context.Hand(_hand);
            if (!f.Active(GestureId.PinchLeft, _hand) || !h.IsTracked) return false;
            float pulled = Vector3.Distance(h.PinchPoint, _start);
            if (!_chart.IsShown)
            {
                _fx.ShowDashedLine("extract", _start, h.PinchPoint);
                if (pulled >= PullDistanceMeters)
                {
                    _placeChart();
                    _chart.Extract(r => _host.Volume.MmToWorld(r.CentroidMm));
                    ExtractCount++;
                    _fx.PlayRecognised(h.PinchPoint);
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public void End() => _fx.HideDashedLine("extract");

        /// <summary>Label id at or near a world point (0 = none). Searches a small voxel neighbourhood.</summary>
        public int PickDefect(Vector3 world)
        {
            VolumeDataset d = _host.Dataset;
            if (d == null) return 0;
            Vector3 uvw = _host.Volume.WorldToTexture(world);
            if (!Rendering.VolumeRenderer.InsideVolume(uvw)) return 0;
            Vector3Int c = d.VoxelAt(uvw);
            int r = PickRadiusVoxels;
            for (int dz = -r; dz <= r; dz++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = c.x + dx, y = c.y + dy, z = c.z + dz;
                if (x < 0 || y < 0 || z < 0 || x >= d.Dims.x || y >= d.Dims.y || z >= d.Dims.z) continue;
                int id = d.Labels[d.Index(x, y, z)];
                if (id > 0 && d.Catalog.TryGet(id, out DefectRecord rec) && rec.GroundTruth != GroundTruthStatus.FalseNegative) return id;
            }
            return 0;
        }
    }

    /// <summary>Pinch a chart dot to focus it; the defect glows in the specimen and a link line connects them.</summary>
    public sealed class ChartSelectTool : ITool
    {
        /// <summary>Max distance from pinch to a dot, metres.</summary>
        public const float PickRadiusMeters = 0.025f;
        private readonly ISpecimenHost _host;
        private readonly IFeedbackSink _fx;
        private readonly DefectChart3D _chart;
        private Handedness _hand;

        /// <summary>Creates the tool.</summary>
        public ChartSelectTool(ISpecimenHost host, IFeedbackSink fx, DefectChart3D chart) { _host = host; _fx = fx; _chart = chart; }

        /// <inheritdoc/>
        public string Name => "ChartSelect";

        /// <inheritdoc/>
        public HandUsage Usage => HandPoses.Of(_hand);

        /// <inheritdoc/>
        public bool CanStart(in ToolFrame f)
        {
            for (int i = 0; i < 2; i++)
            {
                var h = (Handedness)i;
                if (f.Gestures.State(GestureRecognizer.ForHand(GestureId.PinchLeft, h)).JustEntered &&
                    _chart.NearestDot(f.Context.Hand(h).PinchPoint, PickRadiusMeters) > 0) return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public void Begin(in ToolFrame f)
        {
            _hand = f.Gestures.State(GestureId.PinchRight).JustEntered ? Handedness.Right : Handedness.Left;
            Vector3 p = f.Context.Hand(_hand).PinchPoint;
            _host.Selection.Focus(_chart.NearestDot(p, PickRadiusMeters));
            _fx.PlayRecognised(p);
        }

        /// <inheritdoc/>
        public bool Tick(in ToolFrame f) => f.Active(GestureId.PinchLeft, _hand);

        /// <inheritdoc/>
        public void End() { }
    }

    /// <summary>
    /// Two-hand box brushing in chart space: both pinches inside the chart span a box; the
    /// defects inside are selected and highlighted in the specimen. The brush stays after release.
    /// </summary>
    public sealed class BrushTool : ITool
    {
        private const float StartMarginMeters = 0.05f;
        private readonly ISpecimenHost _host;
        private readonly IFeedbackSink _fx;
        private readonly DefectChart3D _chart;

        /// <summary>Creates the tool.</summary>
        public BrushTool(ISpecimenHost host, IFeedbackSink fx, DefectChart3D chart) { _host = host; _fx = fx; _chart = chart; }

        /// <inheritdoc/>
        public string Name => "Brush";

        /// <inheritdoc/>
        public HandUsage Usage => HandUsage.Both;

        /// <inheritdoc/>
        public bool CanStart(in ToolFrame f) =>
            f.Gestures.IsActive(GestureId.PinchLeft) && f.Gestures.IsActive(GestureId.PinchRight) &&
            _chart.Contains(f.Context.Left.PinchPoint, StartMarginMeters) && _chart.Contains(f.Context.Right.PinchPoint, StartMarginMeters);

        /// <inheritdoc/>
        public void Begin(in ToolFrame f) => _fx.PlayRecognised(f.Context.Right.PinchPoint);

        /// <inheritdoc/>
        public bool Tick(in ToolFrame f)
        {
            if (!f.Gestures.IsActive(GestureId.PinchLeft) || !f.Gestures.IsActive(GestureId.PinchRight)) return false;
            Vector3 a = f.Context.Left.PinchPoint, b = f.Context.Right.PinchPoint;
            _host.Selection.SetBrush(_chart.IdsInBox(a, b));
            _fx.ShowDashedLine("brush", a, b);
            _fx.ShowLabel("brush", 0.5f * (a + b) + Vector3.up * 0.03f, $"{_host.Selection.Brushed.Count} selected");
            return true;
        }

        /// <inheritdoc/>
        public void End()
        {
            _fx.HideDashedLine("brush");
            _fx.HideLabel("brush");
        }
    }

    /// <summary>
    /// Decides which tool owns the hands. Rules:
    /// <list type="number">
    /// <item>A running primary tool keeps control until its own gesture ends (no stealing mid-gesture).</item>
    /// <item>Otherwise the first tool in priority order whose start gesture is present starts.
    /// Order: Peel, Brush, Scale, Grab, Slice, ChartSelect, Extract, Probe. Two-hand tools come
    /// first because their gestures contain one-hand gestures (two pinches are also one pinch).</item>
    /// <item>Lens and Reset run alongside on whichever hand is free.</item>
    /// </list>
    /// </summary>
    public sealed class InteractionModeController
    {
        private readonly ITool[] _tools;
        private readonly LensTool _lens;
        private readonly ResetTool _reset;

        /// <summary>The running primary tool, or null.</summary>
        public ITool Active { get; private set; }

        /// <summary>Raised when the active tool changes (old, new).</summary>
        public event System.Action<ITool, ITool> ToolChanged;

        /// <summary>Creates a controller with tools in priority order.</summary>
        public InteractionModeController(ITool[] toolsByPriority, LensTool lens, ResetTool reset)
        {
            _tools = toolsByPriority;
            _lens = lens;
            _reset = reset;
        }

        /// <summary>Runs one frame of interaction.</summary>
        public void Tick(in ToolFrame f)
        {
            if (Active != null && !Active.Tick(f))
            {
                ITool old = Active;
                Active.End();
                Active = null;
                ToolChanged?.Invoke(old, null);
            }
            if (Active == null)
            {
                foreach (ITool t in _tools)
                {
                    if (!t.CanStart(f)) continue;
                    Active = t;
                    t.Begin(f);
                    ToolChanged?.Invoke(null, t);
                    if (!t.Tick(f)) { t.End(); Active = null; ToolChanged?.Invoke(t, null); }
                    break;
                }
            }
            _lens?.Tick(f, Active != null ? Active.Usage : HandUsage.None);
            _reset?.Tick(f);
        }

        /// <summary>Stops the active tool (e.g. on specimen change).</summary>
        public void Cancel()
        {
            if (Active == null) return;
            ITool old = Active;
            Active.End();
            Active = null;
            ToolChanged?.Invoke(old, null);
        }
    }
}
