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
        private const string BuildDir = "Builds";

        // ==================== 各平台构建 ====================

        public static void BuildWebGL()
        {
            var path = Path.Combine(BuildDir, "WebGL");
            Build(path, BuildTarget.WebGL);
        }

        public static void BuildWindows()
        {
            var path = Path.Combine(BuildDir, "Windows", "ZWebGLFrameWork.exe");
            Build(path, BuildTarget.StandaloneWindows64);
        }

        public static void BuildAndroid()
        {
            var path = Path.Combine(BuildDir, "Android", "ZWebGLFrameWork.apk");
            Build(path, BuildTarget.Android);
        }

        public static void BuildIOS()
        {
            var path = Path.Combine(BuildDir, "iOS");
            Build(path, BuildTarget.iOS);
        }

        // ==================== 核心构建 ====================

        private static void Build(string outputPath, BuildTarget target)
        {
            // 1. 编译验证
            Debug.Log($"[BuildScript] 开始构建: {target}");
            AssetDatabase.Refresh();
            if (!BuildValidator.ValidateBootPipeline())
                throw new BuildFailedException("Required runtime asset validation failed.");

            // 2. 按 Build Settings 中的启用顺序收集场景
            var scenes = GetEnabledScenePaths();
            if (scenes.Length == 0)
            {
                throw new BuildFailedException(
                    "[BuildScript] Build Settings has no enabled scenes; Launcher must be enabled scene zero.");
            }

            // 3. 构建
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"<color=green>[BuildScript] ✅ 构建成功: {summary.totalSize / 1024 / 1024}MB, " +
                          $"耗时 {summary.totalTime.TotalSeconds:F1}s</color>");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[BuildScript] ❌ 构建失败: {summary.totalErrors} errors");
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            Debug.LogError($"  [{step.name}] {msg.content}");
                    }
                }
                EditorApplication.Exit(1);
            }
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
