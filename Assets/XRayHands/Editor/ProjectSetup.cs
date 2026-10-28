using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace XRayHands.EditorTools
{
    /// <summary>
    /// One-click, repeatable project configuration for Quest 3 (Android) and PCVR over Link
    /// (Windows). Doing this in code instead of by hand means the lab PCs and both students'
    /// machines end up with identical settings, and the settings are reviewable in Git.
    /// Menu: XRayHands → Configure XR + Player (Quest).
    /// </summary>
    public static class ProjectSetup
    {
        private const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        private const string SettingsAssetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

        // OpenXR feature ids, read from the installed packages' source (OpenXR 1.18, XR Hands 1.10,
        // Unity OpenXR Meta 2.7) rather than remembered.
        private static readonly string[] AndroidFeatures =
        {
            "com.unity.openxr.feature.metaquest",                       // Meta Quest Support
            "com.unity.openxr.feature.input.handtrackingsubsystem",     // XR Hands: Hand Tracking Subsystem
            "com.unity.openxr.feature.input.metahandtrackingaim",       // XR Hands: Meta Hand Tracking Aim
            "com.unity.openxr.feature.arfoundation-meta-session",       // Meta OpenXR: AR session
            "com.unity.openxr.feature.arfoundation-meta-camera",        // Meta OpenXR: camera (passthrough)
            // Meta Quest validation requires a controller profile to pair with, even for a
            // hands-only app; enabling it does not add controller input to X-Ray Hands.
            "com.unity.openxr.feature.input.oculustouch",
        };

        private static readonly string[] StandaloneFeatures =
        {
            "com.unity.openxr.feature.input.handtrackingsubsystem",
            "com.unity.openxr.feature.input.metahandtrackingaim",
        };

        /// <summary>Applies every XR and Player setting; returns a report of what changed.</summary>
        [MenuItem("XRayHands/Configure XR + Player (Quest)")]
        public static string ConfigureAll()
        {
            var log = new StringBuilder();
            XRGeneralSettingsPerBuildTarget perTarget = GetOrCreatePerTargetSettings(log);
            ConfigureTarget(perTarget, BuildTargetGroup.Android, AndroidFeatures, log);
            ConfigureTarget(perTarget, BuildTargetGroup.Standalone, StandaloneFeatures, log);
            ConfigurePlayer(log);
            ConfigureUrp(log);
            ApplyAutomaticQuestFixes(log);
            AssetDatabase.SaveAssets();
            Debug.Log("[XRayHands] Project setup:\n" + log);
            return log.ToString();
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreatePerTargetSettings(StringBuilder log)
        {
            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget existing) && existing != null)
            {
                return existing;
            }
            var created = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            System.IO.Directory.CreateDirectory("Assets/XR");
            AssetDatabase.CreateAsset(created, SettingsAssetPath);
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, created, true);
            log.AppendLine($"created {SettingsAssetPath}");
            return created;
        }

        private static void ConfigureTarget(XRGeneralSettingsPerBuildTarget perTarget, BuildTargetGroup group, string[] featureIds, StringBuilder log)
        {
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            XRGeneralSettings general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            bool assigned = XRPackageMetadataStore.AssignLoader(general.Manager, OpenXRLoader, group);
            log.AppendLine($"{group}: OpenXR loader {(assigned ? "assigned" : "already assigned or failed")}");

            FeatureHelpers.RefreshFeatures(group);
            OpenXRSettings openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openxr == null)
            {
                log.AppendLine($"{group}: no OpenXR settings (is the platform module installed?)");
                return;
            }
            // Single pass instanced: both eyes in one draw call, the brief's requirement and
            // the shaders are written for it (UNITY_VERTEX_OUTPUT_STEREO).
            openxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            foreach (OpenXRFeature f in openxr.GetFeatures())
            {
                foreach (string id in featureIds)
                {
                    if (FeatureIdOf(f) != id) continue;
                    f.enabled = true;
                    log.AppendLine($"{group}: enabled {f.GetType().Name}");
                }
            }
            EditorUtility.SetDirty(openxr);
        }

        private static string FeatureIdOf(OpenXRFeature f)
        {
            object[] attrs = f.GetType().GetCustomAttributes(typeof(UnityEditor.XR.OpenXR.Features.OpenXRFeatureAttribute), false);
            return attrs.Length > 0 ? ((UnityEditor.XR.OpenXR.Features.OpenXRFeatureAttribute)attrs[0]).FeatureId : null;
        }

        private static void ConfigurePlayer(StringBuilder log)
        {
            PlayerSettings.companyName = "Group07 UniPassau";
            PlayerSettings.productName = "X-Ray Hands";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "de.unipassau.ia.group07.xrayhands");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "de.unipassau.ia.group07.xrayhands");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            // Meta Quest requires Android API level 32 or higher.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.runInBackground = true;
            log.AppendLine("player: linear, IL2CPP, ARM64, Vulkan, min API 32, bundle de.unipassau.ia.group07.xrayhands");
        }

        /// <summary>
        /// Runs the OpenXR package's own automatic fixes for Android (e.g. latency optimisation
        /// = prioritise input polling). Using the package's fixes keeps us aligned with what
        /// Unity/Meta recommend for the installed version instead of hard-coding settings.
        /// </summary>
        private static void ApplyAutomaticQuestFixes(StringBuilder log)
        {
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android; // validation rules read the selected group
            var issues = new System.Collections.Generic.List<OpenXRFeature.ValidationRule>();
            UnityEditor.XR.OpenXR.OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
            foreach (OpenXRFeature.ValidationRule rule in issues)
            {
                if (!rule.fixItAutomatic || rule.fixIt == null) continue;
                rule.fixIt();
                log.AppendLine($"auto-fix: {StripTags(rule.message)}");
            }
        }

        private static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");

        private static void ConfigureUrp(StringBuilder log)
        {
            // Screen-space AO costs a full-screen pass per eye (on Quest and over Link) and adds
            // nothing to a volume rendered by our own shader: turn it off on every renderer.
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                foreach (ScriptableRendererFeature f in data.rendererFeatures)
                {
                    if (f == null || !f.GetType().Name.Contains("ScreenSpaceAmbientOcclusion") || !f.isActive) continue;
                    f.SetActive(false);
                    EditorUtility.SetDirty(data);
                    log.AppendLine($"urp: disabled SSAO on {path}");
                }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                asset.supportsCameraDepthTexture = true; // the volume stops rays at scene depth
                if (path.Contains("Mobile")) asset.msaaSampleCount = 4;
                EditorUtility.SetDirty(asset);
                log.AppendLine($"urp: {path} depth texture on{(path.Contains("Mobile") ? ", MSAA 4x" : "")}");
            }
        }
    }
}
