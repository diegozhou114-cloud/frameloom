using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>独立小游戏构建，不修改框架入口或编辑器场景列表。</summary>
    public static class MiniSiegeBuild
    {
        private const string ScenePath = "Assets/MiniSiegeGenerated.unity";

        [MenuItem("ZWebGL/Mini Siege/Build macOS")]
        public static void BuildMacOS() => Build(BuildTarget.StandaloneOSX);

        [MenuItem("ZWebGL/Mini Siege/Build WebGL")]
        public static void BuildWebGL() => Build(BuildTarget.WebGL);

        private static void Build(BuildTarget target)
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            string title = PlayerSettings.productName;
            int width = PlayerSettings.defaultScreenWidth;
            int height = PlayerSettings.defaultScreenHeight;
            var fullScreen = PlayerSettings.fullScreenMode;
            bool background = PlayerSettings.runInBackground;
            var compression = PlayerSettings.WebGL.compressionFormat;
            try
            {
                Type game = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("ZWebGLFrameWork.MiniSiege.MiniSiegeGame"))
                    .FirstOrDefault(t => t != null);
                if (game == null) throw new BuildFailedException("MiniSiegeGame is not compiled.");
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Mini Siege").AddComponent(game);
                EditorSceneManager.SaveScene(scene, ScenePath);
                PlayerSettings.productName = "Mini Siege";
                PlayerSettings.defaultScreenWidth = 1100;
                PlayerSettings.defaultScreenHeight = 760;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.runInBackground = true;
                if (target == BuildTarget.WebGL)
                    PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                string root = Environment.GetEnvironmentVariable("MINISIEGE_OUTPUT");
                if (string.IsNullOrWhiteSpace(root))
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MiniSiegeDemo");
                Directory.CreateDirectory(root);
                string output = Path.Combine(root, target == BuildTarget.StandaloneOSX ? "MiniSiege.app" : "WebGL");
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = output, target = target,
                    options = BuildOptions.Development
                });
                Debug.Log($"[MiniSiegeBuild] {report.summary.result}: {output}; errors={report.summary.totalErrors}");
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("Mini Siege build failed.");
            }
            finally
            {
                PlayerSettings.productName = title;
                PlayerSettings.defaultScreenWidth = width;
                PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.fullScreenMode = fullScreen;
                PlayerSettings.runInBackground = background;
                PlayerSettings.WebGL.compressionFormat = compression;
                if (setup.Any(s => s.isLoaded && s.isActive))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(ScenePath);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
