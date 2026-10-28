using UnityEngine;
using XRayHands.Core;

namespace XRayHands.Analytics
{
    /// <summary>How defects are coloured in the volume and the chart.</summary>
    public enum DefectColourMode
    {
        /// <summary>By class: crack, lack-of-fusion, gas pore (or material bin for parts).</summary>
        Class,
        /// <summary>By agreement with the reference segmentation: TP / FP / FN.</summary>
        GroundTruth,
    }

    /// <summary>
    /// Pure function from (defect, mode, selection) to display colour. Used for the shader's
    /// per-id lookup texture and for the chart dots, so both views always agree.
    /// </summary>
    public static class DefectPalette
    {
        /// <summary>Crack: coral red #FF5546.</summary>
        public static readonly Color32 Crack = new Color32(0xFF, 0x55, 0x46, 0xFF);
        /// <summary>Shrinkage / lack-of-fusion: orange #FF9A3D.</summary>
        public static readonly Color32 LackOfFusion = new Color32(0xFF, 0x9A, 0x3D, 0xFF);
        /// <summary>Gas pore: pale yellow #FFDF8A.</summary>
        public static readonly Color32 GasPore = new Color32(0xFF, 0xDF, 0x8A, 0xFF);
        /// <summary>Selection highlight: warm white.</summary>
        public static readonly Color32 Selected = new Color32(0xFF, 0xF6, 0xE6, 0xFF);

        // Ground-truth colours chosen from a colour-blind-safe set (blue / orange / magenta),
        // deliberately different from the class colours so the two modes cannot be confused.
        /// <summary>True positive: blue #4DA6FF.</summary>
        public static readonly Color32 TruePositive = new Color32(0x4D, 0xA6, 0xFF, 0xFF);
        /// <summary>False positive: orange #FF9A3D.</summary>
        public static readonly Color32 FalsePositive = new Color32(0xFF, 0x9A, 0x3D, 0xFF);
        /// <summary>False negative (missed reference pore): magenta #E040FB.</summary>
        public static readonly Color32 FalseNegative = new Color32(0xE0, 0x40, 0xFB, 0xFF);

        /// <summary>Opacity of a normal defect sample in the volume.</summary>
        public const byte DefectAlpha = 230;
        /// <summary>Opacity of unselected defects in "hide unselected" mode (a faint ghost).</summary>
        public const byte GhostAlpha = 18;

        /// <summary>
        /// Display colour of one defect. Alpha 0 means "not drawn".
        /// </summary>
        /// <param name="record">The defect.</param>
        /// <param name="mode">Colour mode.</param>
        /// <param name="selection">Current selection (may be null).</param>
        /// <param name="hideUnselected">When true and something is selected, everything else is a ghost.</param>
        /// <param name="materialColours">Specimen B: colour per material bin (from meta.json).</param>
        public static Color32 ColourOf(in DefectRecord record, DefectColourMode mode, SelectionModel selection,
            bool hideUnselected, Color32[] materialColours)
        {
            bool isFn = record.GroundTruth == GroundTruthStatus.FalseNegative;
            // Missed reference pores are not our detections: only the ground-truth view shows them.
            if (isFn && mode != DefectColourMode.GroundTruth) return new Color32(0, 0, 0, 0);

            Color32 c = mode == DefectColourMode.GroundTruth ? GroundTruthColour(record.GroundTruth) : ClassColour(record, materialColours);
            c.a = DefectAlpha;

            if (selection != null)
            {
                if (selection.IsSelected(record.Id)) return new Color32(Selected.r, Selected.g, Selected.b, 255);
                if (hideUnselected && selection.HasSelection) c.a = GhostAlpha;
            }
            return c;
        }

        /// <summary>Class colour; parts of specimen B use their material bin colour.</summary>
        public static Color32 ClassColour(in DefectRecord r, Color32[] materialColours)
        {
            switch (r.Class)
            {
                case DefectClass.Crack: return Crack;
                case DefectClass.LackOfFusion: return LackOfFusion;
                case DefectClass.GasPore: return GasPore;
                default:
                    return materialColours != null && r.MaterialBin >= 0 && r.MaterialBin < materialColours.Length
                        ? materialColours[r.MaterialBin]
                        : new Color32(0x9F, 0xB4, 0xC8, 0xFF);
            }
        }

        /// <summary>Colour for a ground-truth status.</summary>
        public static Color32 GroundTruthColour(GroundTruthStatus s)
        {
            switch (s)
            {
                case GroundTruthStatus.TruePositive: return TruePositive;
                case GroundTruthStatus.FalsePositive: return FalsePositive;
                case GroundTruthStatus.FalseNegative: return FalseNegative;
                default: return new Color32(0x9F, 0xB4, 0xC8, 0xFF);
            }
        }

        /// <summary>Parses "#RRGGBB" (meta.json material colours).</summary>
        public static Color32 ParseHex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? (Color32)c : new Color32(0x9F, 0xB4, 0xC8, 0xFF);
        }
    }
}
