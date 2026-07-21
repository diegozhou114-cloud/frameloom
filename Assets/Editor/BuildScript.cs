using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>
    /// 命令行构建脚本 — 供 CI/CD 调用。
    /// 用法: Unity -batchmode -executeMethod ZWebGLFrameWork.Editor.BuildScript.BuildWebGL
    /// </summary>
    public static class BuildScript
    {
        public const string BuildReportPrefix = "[BuildReport] ";

        [Serializable]
        public sealed class MachineReadableBuildReport
        {
            public string target;
            public string outputPath;
            public string result;
            public ulong totalSizeBytes;
            public int totalErrors;
            public double totalTimeSeconds;
        }

        // ==================== 各平台构建 ====================

        public static void BuildWebGL()
        {
            var path = Path.Combine(GetEvidenceRoot(), "builds", "WebGL");
            Build(path, BuildTarget.WebGL);
        }

        public static void BuildWindows()
        {
            var path = Path.Combine(GetEvidenceRoot(), "builds", "Windows", "ZWebGLFrameWork.exe");
            Build(path, BuildTarget.StandaloneWindows64);
        }

        public static void BuildAndroid()
        {
            var path = Path.Combine(GetEvidenceRoot(), "builds", "Android", "ZWebGLFrameWork.apk");
            Build(path, BuildTarget.Android);
        }

        public static void BuildIOS()
        {
            var path = Path.Combine(GetEvidenceRoot(), "builds", "iOS");
            Build(path, BuildTarget.iOS);
        }

        // ==================== 核心构建 ====================

        private static void Build(string outputPath, BuildTarget target)
        {
            Debug.Log($"[BuildScript] Starting build: {target}");
            AssetDatabase.Refresh();
            if (!BuildValidator.ValidateBootPipeline())
                throw new BuildFailedException("Required runtime asset validation failed.");

            var scenes = GetEnabledScenePaths();
            if (scenes.Length == 0)
                throw new BuildFailedException(
                    "Build Settings has no enabled scenes; Launcher must be enabled scene zero.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            string summaryPath = BuildReportJsonWriter.Write(report, outputPath, scenes,
                GetEvidenceRoot());
            Debug.Log($"[BuildScript] Build report: {summaryPath}");
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(
                    $"Build failed: errors={report.summary.totalErrors}, " +
                    $"warnings={report.summary.totalWarnings}");
        }

        public static string GetEvidenceRoot()
        {
            string configured = Environment.GetEnvironmentVariable("ZWEB_EVIDENCE_ROOT");
            string root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Path.GetTempPath(), "zweb-repair-evidence")
                : configured;
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullRoot, projectRoot, StringComparison.OrdinalIgnoreCase) ||
                fullRoot.StartsWith(projectRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException(
                    $"ZWEB_EVIDENCE_ROOT must be outside the repository: {fullRoot}");
            return fullRoot;
        }

        public static void EmitBuildReport(BuildTarget target, string outputPath,
            BuildResult result, ulong totalSizeBytes, int totalErrors,
            TimeSpan totalTime)
        {
            var report = new MachineReadableBuildReport
            {
                target = target.ToString(),
                outputPath = outputPath ?? string.Empty,
                result = result.ToString(),
                totalSizeBytes = totalSizeBytes,
                totalErrors = totalErrors,
                totalTimeSeconds = totalTime.TotalSeconds,
            };
            Debug.Log(BuildReportPrefix + JsonUtility.ToJson(report));
        }

        public static string[] GetEnabledScenePaths()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }
    }
}
