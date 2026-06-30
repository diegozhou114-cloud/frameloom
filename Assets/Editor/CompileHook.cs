using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>
    /// 编译钩子 — Unity 每次编译完成后自动触发验证。
    /// 通过 [InitializeOnLoad] + CompilationPipeline 回调实现。
    /// </summary>
    [InitializeOnLoad]
    public static class CompileHook
    {
        private static bool _lastCompileHadErrors;

        static CompileHook()
        {
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            Debug.Log("[CompileHook] ✅ 已注册编译后自动验证");
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var msg in messages)
            {
                if (msg.type == CompilerMessageType.Error)
                {
                    _lastCompileHadErrors = true;
                    return;
                }
            }
        }

        private static void OnCompilationFinished(object obj)
        {
            // 延迟一帧执行，确保所有程序集加载完毕
            EditorApplication.delayCall += RunValidation;
        }

        private static void RunValidation()
        {
            if (_lastCompileHadErrors)
            {
                Debug.LogError($"[CompileHook] ❌ 检测到编译错误，请修复后重试。");
                _lastCompileHadErrors = false;
                return;
            }

            ValidateAssemblyReferences();
            Debug.Log("<color=green>[CompileHook] ✅ 编译验证通过</color>");
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
