using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace XRayHands.EditorTools
{
    /// <summary>
    /// Command-line builds, so the APK can be produced without clicking through the editor:
    /// <code>Unity.exe -batchmode -quit -projectPath . -executeMethod XRayHands.EditorTools.BuildScript.BuildQuest -logFile build.log</code>
    /// Exit code 0 = success, 1 = failure (Unity sets it from the exception).
    /// </summary>
    public static class BuildScript
    {
        /// <summary>Output path of the Quest APK, relative to the project.</summary>
        public const string QuestApkPath = "Builds/XRayHands.apk";

        /// <summary>Configures XR/Player settings, then builds the Quest 3 APK.</summary>
        [MenuItem("XRayHands/Build Quest APK")]
        public static void BuildQuest()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Debug.Log(ProjectSetup.ConfigureAll());

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = QuestApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            Debug.Log($"[XRayHands] Build {s.result}: {s.outputPath}, {s.totalSize / (1024f * 1024f):0.0} MB, " +
                      $"{s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalTime.TotalMinutes:0.0} min");
            if (s.result != BuildResult.Succeeded) throw new System.Exception($"Quest build failed: {s.result}");
        }
    }
}
