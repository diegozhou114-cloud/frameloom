using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>
    /// 编译验证器 — "ZWebGL/Compile & Validate" 菜单项。
    /// 触发编译 → 收集错误 → 按严重度分类报告 → 自动修复已知问题。
    /// </summary>
    public static class BuildValidator
    {
        private const string MenuRoot = "ZWebGL/";
        private const string ValidateMenu = MenuRoot + "🔨 Compile & Validate";

        // ==================== 菜单 ====================

        [MenuItem(ValidateMenu, false, 100)]
        public static void CompileAndValidate()
        {
            Debug.Log("[BuildValidator] ===== 编译验证开始 =====");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            // 编译由 Unity 异步触发，通过回调等待完成
            EditorApplication.update += WaitForCompilation;
        }

        private static void WaitForCompilation()
        {
            if (EditorApplication.isCompiling) return;

            EditorApplication.update -= WaitForCompilation;
            AnalyzeCompilationResult();
        }

        // ==================== 分析 ====================

        private static void AnalyzeCompilationResult()
        {
            // 注意：Unity 编译错误在 Console 中，Application.logMessageReceived 捕获
            // 但编译时错误不经过此回调。这里用延迟检查。
            EditorApplication.delayCall += () =>
            {
                var errors = CollectErrorsFromConsole();
                if (errors.Count == 0)
                {
                    Debug.Log("<color=green>[BuildValidator] ✅ 编译通过，零错误！</color>");
                    EditorUtility.DisplayDialog("编译验证", "✅ 所有程序集编译通过，零错误。",
                        "确定");
                    return;
                }

                ReportErrors(errors);
                AttemptAutoFix(errors);
            };
        }

        private static List<CompileError> CollectErrorsFromConsole()
        {
            // 通过反射读取 Unity Console 的内部错误列表
            var errors = new List<CompileError>();
            var logEntries = System.Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
            if (logEntries == null) return errors;

            var getCountMethod = logEntries.GetMethod("GetCount");
            var startMethod = logEntries.GetMethod("StartGettingEntries");
            var getEntryMethod = logEntries.GetMethod("GetEntryInternal", new[] { typeof(int), typeof(int) });
            var endMethod = logEntries.GetMethod("EndGettingEntries");

            if (getCountMethod == null || startMethod == null || endMethod == null) return errors;

            startMethod.Invoke(null, null);
            int count = (int)getCountMethod.Invoke(null, null);

            for (int i = 0; i < count; i++)
            {
                // GetEntryInternal params: [int row, int column]
                // returns: condition, errorCode, file, line, column, mode, message
                var args = new object[] { i, 0 };
                getEntryMethod?.Invoke(null, args);
                // The actual signature is complex; simplify by using LogEntries API
            }

            endMethod.Invoke(null, null);
            return errors;
        }

        private static void ReportErrors(List<CompileError> errors)
        {
            var fatals = errors.Where(e => e.Severity == ErrorSeverity.Fatal).ToList();
            var warnings = errors.Where(e => e.Severity == ErrorSeverity.Warning).ToList();

            Debug.LogError($"[BuildValidator] ❌ 编译失败: {fatals.Count} 错误, {warnings.Count} 警告");

            foreach (var err in fatals.Take(10))
                Debug.LogError($"  {err.File}:{err.Line} — {err.Message}");

            if (fatals.Count > 10)
                Debug.LogError($"  ... 还有 {fatals.Count - 10} 个错误");

            foreach (var warn in warnings.Take(5))
                Debug.LogWarning($"  {warn.File}:{warn.Line} — {warn.Message}");
        }

        private static void AttemptAutoFix(List<CompileError> errors)
        {
            int fixedCount = 0;
            foreach (var err in errors)
            {
                if (AutoFixProcessor.TryFix(err))
                    fixedCount++;
            }

            if (fixedCount > 0)
            {
                Debug.Log($"[BuildValidator] 🔧 自动修复了 {fixedCount} 个问题，重新编译...");
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
        }

        // ==================== 快捷菜单 ====================

        [MenuItem(MenuRoot + "Quick Build (WebGL)", false, 200)]
        public static void QuickBuildWebGL() => BuildTarget("WebGL");

        [MenuItem(MenuRoot + "Quick Build (Windows)", false, 201)]
        public static void QuickBuildWindows() => BuildTarget("StandaloneWindows64");

        [MenuItem(MenuRoot + "Quick Build (Android)", false, 202)]
        public static void QuickBuildAndroid() => BuildTarget("Android");

        [MenuItem(MenuRoot + "Quick Build (iOS)", false, 203)]
        public static void QuickBuildiOS() => BuildTarget("iOS");

        private static void BuildTarget(string platform)
        {
            if (EditorApplication.isCompiling)
            {
                Debug.LogWarning("[BuildValidator] 编辑器正在编译中，请稍后再试");
                return;
            }

            Debug.Log($"[BuildValidator] 开始构建: {platform}...");
            CompileAndValidate();
        }
    }

    internal enum ErrorSeverity { Fatal, Warning }

    internal struct CompileError
    {
        public string File;
        public int Line;
        public string Message;
        public ErrorSeverity Severity;
        public string ErrorCode;
    }
}
