using System.Text;
using UnityEngine;
using XRayHands.Gestures;
using XRayHands.Input;
using XRayHands.Interaction;

namespace XRayHands.App
{
    /// <summary>
    /// Writes what the gesture code measures from the hands to the console, twice a second:
    /// per hand the finger curls, pinch distance and distance to the specimen, then every
    /// gesture's confidence and the running tool. Used to tune the detectors against real
    /// tracked hands, which differ from the simulator's ideal poses.
    /// </summary>
    public static class GestureDiagnostics
    {
        private const float IntervalSeconds = 0.5f;
        private static float _nextLogTime;
        private static readonly StringBuilder Sb = new StringBuilder(512);

        /// <summary>Logs one line if the interval has passed.</summary>
        public static void Tick(in GestureContext ctx, GestureRecognizer recognizer, ITool activeTool)
        {
            if (Time.unscaledTime < _nextLogTime) return;
            _nextLogTime = Time.unscaledTime + IntervalSeconds;

            Sb.Clear().Append("[XRayHands][diag] ");
            AppendHand(ctx, ctx.Left, "L");
            AppendHand(ctx, ctx.Right, "R");
            Vector3 specimen = ctx.SpecimenLocalToWorld.GetColumn(3);
            Sb.Append($"| specimen {Vector3.Distance(ctx.Head.position, specimen) * 100f:F0}cm from head | conf");
            foreach (GestureId id in System.Enum.GetValues(typeof(GestureId)))
            {
                GestureState s = recognizer.State(id);
                Sb.Append(' ').Append(id).Append('=').Append(s.Confidence.ToString("F2"));
                if (s.IsActive) Sb.Append('*');
            }
            Sb.Append(" | tool ").Append(activeTool != null ? activeTool.GetType().Name : "none");
            Debug.Log(Sb.ToString());
        }

        private static void AppendHand(in GestureContext ctx, HandFrame h, string name)
        {
            if (!h.IsTracked)
            {
                Sb.Append(name).Append(": not tracked ");
                return;
            }
            Sb.Append(name).Append(": curl T").Append(h.Curl[0].ToString("F2"))
              .Append(" I").Append(h.Curl[1].ToString("F2"))
              .Append(" M").Append(h.Curl[2].ToString("F2"))
              .Append(" R").Append(h.Curl[3].ToString("F2"))
              .Append(" L").Append(h.Curl[4].ToString("F2"))
              .Append($" pinch {h.PinchDistance * 100f:F1}cm")
              .Append($" toSpecimen {ctx.DistanceToSpecimen(h.PalmPosition) * 100f:F0}cm")
              .Append($" palmUp {Vector3.Dot(h.PalmNormal, Vector3.up):F2} ");
        }
    }
}
