using System.IO;
using UnityEditor;
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

            // 2. 收集场景
            var scenes = new string[0];
            var sceneGuids = AssetDatabase.FindAssets("t:Scene");
            if (sceneGuids.Length == 0)
            {
                Debug.LogWarning("[BuildScript] 未找到场景文件，创建默认构建场景...");
                scenes = new[] { CreateDefaultScene() };
            }
            else
            {
                scenes = new string[sceneGuids.Length];
                for (int i = 0; i < sceneGuids.Length; i++)
                    scenes[i] = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
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

        private static string CreateDefaultScene()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // 创建必要的 GameObject
            var mainCamera = new GameObject("Main Camera");
            mainCamera.AddComponent<Camera>();
            mainCamera.tag = "MainCamera";

            var uiRoot = new GameObject("UIRoot");
            uiRoot.AddComponent<Canvas>();

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            string path = "Assets/Scenes";
            Directory.CreateDirectory(path);
            string scenePath = $"{path}/Default.unity";

            if (File.Exists(scenePath))
            {
                Debug.LogWarning($"[BuildScript] Default.unity 已存在，跳过覆盖");
                return scenePath;
            }

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
            return scenePath;
        }
    }
}
