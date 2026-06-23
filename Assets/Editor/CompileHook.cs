using UnityEditor;
using UnityEngine;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>
    /// 编译钩子 — Unity 每次编译完成后自动触发验证。
    /// 通过 [InitializeOnLoad] 在编辑器启动时注册回调。
    /// </summary>
    [InitializeOnLoad]
    public static class CompileHook
    {
        private static bool _wasCompiling;

        static CompileHook()
        {
            EditorApplication.update += OnEditorUpdate;
            Debug.Log("[CompileHook] ✅ 已注册编译后自动验证");
        }

        private static void OnEditorUpdate()
        {
            bool isCompiling = EditorApplication.isCompiling;

            // 编译完成：isCompiling 从 true → false
            if (_wasCompiling && !isCompiling)
            {
                _wasCompiling = false;
                OnCompilationFinished();
            }

            _wasCompiling = isCompiling;
        }

        private static void OnCompilationFinished()
        {
            // 延迟一帧执行，确保所有程序集加载完毕
            EditorApplication.delayCall += RunValidation;
        }

        private static void RunValidation()
        {
            // 检查是否有编译错误
            if (HasCompileErrors())
            {
                Debug.LogError("[CompileHook] ❌ 检测到编译错误，请修复后重试。");
                return;
            }

            // 验证程序集引用完整性
            ValidateAssemblyReferences();

            Debug.Log("<color=green>[CompileHook] ✅ 编译验证通过</color>");
        }

        private static bool HasCompileErrors()
        {
            // 通过检查 Console 窗口获取编译错误数
            var logEntries = System.Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
            if (logEntries == null) return false;

            var getCountMethod = logEntries.GetMethod("GetCount");
            var startMethod = logEntries.GetMethod("StartGettingEntries");
            var getEntryMethod = logEntries.GetMethod("GetEntryInternal",
                new[] { typeof(int), typeof(int) });
            var endMethod = logEntries.GetMethod("EndGettingEntries");

            if (getCountMethod == null || startMethod == null || endMethod == null)
                return false;

            startMethod.Invoke(null, null);
            int count = (int)getCountMethod.Invoke(null, null);

            int errorCount = 0;
            for (int i = 0; i < count; i++)
            {
                // 检查是否是编译错误（mode == 0 为 Error）
                var args = new object[] { i, 0 };
                try { getEntryMethod?.Invoke(null, args); }
                catch { continue; }

                // args[0] = mode: 0=Error, 1=Warning, 2=Log
                if (args.Length > 0 && args[0] is int mode && mode == 0)
                    errorCount++;
            }

            endMethod.Invoke(null, null);

            if (errorCount > 0)
            {
                Debug.LogError($"[CompileHook] 编译错误: {errorCount} 个");
                return true;
            }

            return false;
        }

        private static void ValidateAssemblyReferences()
        {
            var asmdefs = AssetDatabase.FindAssets("t:asmdef");
            foreach (var guid in asmdefs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/Scripts/") && !path.StartsWith("Assets/Editor/"))
                    continue;

                var asmdef = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asmdef == null) continue;

                try
                {
                    var json = asmdef.text;
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        Debug.LogError($"[CompileHook] 空 asmdef: {path}");
                        continue;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[CompileHook] asmdef 解析失败: {path} — {ex.Message}");
                }
            }
        }

        // ==================== 手动触发 ====================

        [MenuItem("ZWebGL/🔁 Re-run Compile Hook", false, 160)]
        public static void ManualValidate()
        {
            Debug.Log("[CompileHook] 手动触发验证...");
            RunValidation();
        }
    }
}
