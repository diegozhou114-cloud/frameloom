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

        private static bool _isValidating;

        [MenuItem(ValidateMenu, false, 100)]
        public static void CompileAndValidate()
        {
            if (_isValidating)
            {
                Debug.LogWarning("[BuildValidator] 验证已在运行中，跳过重复调用");
                return;
            }
            _isValidating = true;

            Debug.Log("[BuildValidator] ===== 编译验证开始 =====");

            // 先注册 logMessageReceived 捕获编译错误
            StartCapturingErrors();

            if (Application.isBatchMode)
            {
                // CI 环境：编译是同步的，Refresh 后编译立即完成
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                // 延迟一帧确保所有错误已回调
                EditorApplication.delayCall += AnalyzeCompilationResult;
            }
            else
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                // Editor 环境：编译异步，通过 update 回调等待
                EditorApplication.update += WaitForCompilation;
            }
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
            EditorApplication.delayCall -= AnalyzeCompilationResult;
            EditorApplication.delayCall += () =>
            {
                var errors = StopCapturingAndGetErrors();
                _isValidating = false;
                if (errors.Count == 0)
                {
                    Debug.Log("<color=green>[BuildValidator] ✅ 编译通过，零错误！</color>");
                    ValidateBootPipeline();
                    if (!Application.isBatchMode)
                        EditorUtility.DisplayDialog("编译验证", "✅ 所有程序集编译通过，零错误。", "确定");
                    return;
                }
                ReportErrors(errors);
                AttemptAutoFix(errors);
            };
        }

        private static List<CompileError> _capturedErrors;
        private static Application.LogCallback _captureHandler;

        /// <summary>开始捕获编译错误 — 在触发编译之前调用</summary>
        private static void StartCapturingErrors()
        {
            // 防御：先取消旧订阅，防止重复调用导致 handler 泄漏
            if (_captureHandler != null)
            {
                Application.logMessageReceived -= _captureHandler;
                _captureHandler = null;
            }

            _capturedErrors = new List<CompileError>();
            _captureHandler = (condition, stackTrace, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception) return;
                var parsed = ParseCompileError(condition);
                if (parsed.HasValue)
                    _capturedErrors.Add(parsed.Value);
            };
            Application.logMessageReceived += _captureHandler;
        }

        /// <summary>停止捕获并返回收集到的编译错误</summary>
        private static List<CompileError> StopCapturingAndGetErrors()
        {
            if (_captureHandler != null)
            {
                Application.logMessageReceived -= _captureHandler;
                _captureHandler = null;
            }
            var result = _capturedErrors ?? new List<CompileError>();
            _capturedErrors = null;
            return result;
        }

        private static CompileError? ParseCompileError(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;

            var match = System.Text.RegularExpressions.Regex.Match(
                message,
                @"^(.+?)\((\d+),\d+\):\s*(error|warning)\s+(CS\d+):\s*(.+)$");

            if (!match.Success) return null;

            return new CompileError
            {
                File = match.Groups[1].Value,
                Line = int.TryParse(match.Groups[2].Value, out var line) ? line : 0,
                Severity = match.Groups[3].Value == "error" ? ErrorSeverity.Fatal : ErrorSeverity.Warning,
                ErrorCode = match.Groups[4].Value,
                Message = match.Groups[5].Value.Trim(),
            };
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

        [MenuItem(MenuRoot + "Quick Validate (WebGL)", false, 200)]
        public static void QuickValidateWebGL() => BuildTarget("WebGL");

        [MenuItem(MenuRoot + "Quick Validate (Windows)", false, 201)]
        public static void QuickValidateWindows() => BuildTarget("StandaloneWindows64");

        [MenuItem(MenuRoot + "Quick Validate (Android)", false, 202)]
        public static void QuickValidateAndroid() => BuildTarget("Android");

        [MenuItem(MenuRoot + "Quick Validate (iOS)", false, 203)]
        public static void QuickValidateiOS() => BuildTarget("iOS");

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

        // ==================== C.1 启动流程检查 ====================

        private static void ValidateBootPipeline()
        {
            Debug.Log("[BuildValidator] 检查启动管线配置...");
            int issues = 0;

            // ① 检查 Resources/Config/ 下的必需配置文件
            string[] requiredConfigs =
            {
                "Assets/Resources/Config/AppConfig.asset",
                "Assets/Resources/Config/Buildings.json",
                "Assets/Resources/Config/Units.json",
                "Assets/Resources/Config/Levels.json",
            };

            foreach (var path in requiredConfigs)
            {
                if (!System.IO.File.Exists(path))
                {
                    Debug.LogWarning($"[BuildValidator] ⚠ 缺少配置文件: {path}");
                    issues++;
                }
            }

            // ② 检查 Build Settings 中是否有 Launcher 场景
            var scenes = EditorBuildSettings.scenes;
            bool hasLauncher = false;
            foreach (var scene in scenes)
            {
                if (scene.path.Contains("Launcher"))
                {
                    hasLauncher = true;
                    break;
                }
            }
            if (!hasLauncher)
            {
                Debug.LogWarning("[BuildValidator] ⚠ Build Settings 中未找到 Launcher 场景");
                issues++;
            }

            if (issues == 0)
                Debug.Log("<color=green>[BuildValidator] ✅ 启动管线配置完整</color>");
            else
                Debug.LogWarning($"[BuildValidator] ⚠ 启动管线配置有 {issues} 个问题");
        }
    }

    public enum ErrorSeverity { Fatal, Warning }

    public struct CompileError
    {
        public string File;
        public int Line;
        public string Message;
        public ErrorSeverity Severity;
        public string ErrorCode;
    }
}
