using UnityEngine;
using XRayHands.Input;

namespace XRayHands.Feedback
{
    /// <summary>
    /// Draws a hand as a cyan (#68D2FF) joint skeleton with glowing fingertips, the brief's
    /// look. One LineRenderer per finger (wrist → tip) and small spheres at the tips. Created
    /// once; each frame only positions are written (no allocation).
    /// </summary>
    public sealed class HandVisualizer : MonoBehaviour
    {
        /// <summary>Skeleton colour from the brief.</summary>
        public static readonly Color HandCyan = new Color32(0x68, 0xD2, 0xFF, 0xFF);

        private const float BoneWidthMeters = 0.004f;
        private const float TipDiameterMeters = 0.012f;

        private static readonly HandJoint[][] Chains =
        {
            new[] { HandJoint.Wrist, HandJoint.ThumbMetacarpal, HandJoint.ThumbProximal, HandJoint.ThumbDistal, HandJoint.ThumbTip },
            new[] { HandJoint.Wrist, HandJoint.IndexMetacarpal, HandJoint.IndexProximal, HandJoint.IndexIntermediate, HandJoint.IndexDistal, HandJoint.IndexTip },
            new[] { HandJoint.Wrist, HandJoint.MiddleMetacarpal, HandJoint.MiddleProximal, HandJoint.MiddleIntermediate, HandJoint.MiddleDistal, HandJoint.MiddleTip },
            new[] { HandJoint.Wrist, HandJoint.RingMetacarpal, HandJoint.RingProximal, HandJoint.RingIntermediate, HandJoint.RingDistal, HandJoint.RingTip },
            new[] { HandJoint.Wrist, HandJoint.LittleMetacarpal, HandJoint.LittleProximal, HandJoint.LittleIntermediate, HandJoint.LittleDistal, HandJoint.LittleTip },
            new[] { HandJoint.IndexProximal, HandJoint.MiddleProximal, HandJoint.RingProximal, HandJoint.LittleProximal }, // knuckle bar
        };

        private HandFrame _frame;
        private LineRenderer[] _lines;
        private Transform[] _tips;
        private Material _lineMaterial;
        private Material _tipMaterial;

        /// <summary>Highlight intensity 0..1 (e.g. while a gesture is active); brightens the skeleton.</summary>
        public float Highlight { get; set; }

        /// <summary>Binds the frame to draw and builds the line renderers.</summary>
        public void Init(HandFrame frame)
        {
            _frame = frame;
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            _lineMaterial = new Material(unlit) { color = HandCyan };
            _tipMaterial = new Material(unlit) { color = Color.Lerp(HandCyan, Color.white, 0.5f) };

            _lines = new LineRenderer[Chains.Length];
            for (int i = 0; i < Chains.Length; i++)
            {
                var go = new GameObject($"Bone{i}");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = Chains[i].Length;
                lr.widthMultiplier = BoneWidthMeters;
                lr.sharedMaterial = _lineMaterial;
                lr.useWorldSpace = true;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _lines[i] = lr;
            }

            HandJoint[] tipJoints = { HandJoint.ThumbTip, HandJoint.IndexTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip };
            _tips = new Transform[tipJoints.Length];
            for (int i = 0; i < tipJoints.Length; i++)
            {
                GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(s.GetComponent<Collider>());
                s.name = $"Tip{i}";
                s.transform.SetParent(transform, false);
                s.transform.localScale = Vector3.one * TipDiameterMeters;
                s.GetComponent<MeshRenderer>().sharedMaterial = _tipMaterial;
                _tips[i] = s.transform;
            }
        }

        private void LateUpdate()
        {
            if (_frame == null) return;
            bool show = _frame.IsTracked;
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i].enabled = show;
                if (!show) continue;
                HandJoint[] chain = Chains[i];
                for (int j = 0; j < chain.Length; j++) _lines[i].SetPosition(j, _frame.Joint(chain[j]));
            }
            _tips[0].gameObject.SetActive(show);
            if (!show) { for (int i = 1; i < _tips.Length; i++) _tips[i].gameObject.SetActive(false); return; }
            _tips[0].position = _frame.Joint(HandJoint.ThumbTip);
            _tips[1].position = _frame.Joint(HandJoint.IndexTip);
            _tips[2].position = _frame.Joint(HandJoint.MiddleTip);
            _tips[3].position = _frame.Joint(HandJoint.RingTip);
            _tips[4].position = _frame.Joint(HandJoint.LittleTip);
            for (int i = 1; i < _tips.Length; i++) _tips[i].gameObject.SetActive(true);

            Color c = Color.Lerp(HandCyan, Color.white, 0.6f * Highlight);
            _lineMaterial.color = c;
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null) Destroy(_lineMaterial);
            if (_tipMaterial != null) Destroy(_tipMaterial);
        }
    }
}
