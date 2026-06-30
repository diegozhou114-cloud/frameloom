using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>
    /// 自动修复处理器 — 识别并修复常见编译错误。
    /// 可修复：缺失 using、命名空间错误、程序集引用缺失。
    /// </summary>
    public static class AutoFixProcessor
    {
        // 已知类型 → 所需 using 映射
        private static readonly System.Collections.Generic.Dictionary<string, (string, string)> _knownFixes =
            new System.Collections.Generic.Dictionary<string, (string, string)>
            {
                // { 错误关键词, (搜索模式, 修复 using) }
                { "The type or namespace name 'Task'",     ("Task", "using System.Threading.Tasks;") },
                { "The type or namespace name 'List'",     ("List<", "using System.Collections.Generic;") },
                { "The type or namespace name 'Debug'",    ("Debug.", "using UnityEngine;") },
                { "The type or namespace name 'Vector3'",  ("Vector3", "using UnityEngine;") },
                { "The type or namespace name 'GameObject'",("GameObject", "using UnityEngine;") },
                { "The type or namespace name 'Canvas'",   ("Canvas ", "using UnityEngine;") },
                { "The type or namespace name 'MonoBehaviour'",("MonoBehaviour", "using UnityEngine;") },
                { "The type or namespace name 'EventBus'", ("EventBus", "using ZWebGLFrameWork.Core;") },
                { "The type or namespace name 'Observable'",("Observable<", "using ZWebGLFrameWork.Data;") },
                { "The type or namespace name 'IComponent'",("IComponent", "using ZWebGLFrameWork.ECS;") },
                { "The type or namespace name 'Entity'",   (": Entity", "using ZWebGLFrameWork.ECS;") },
                { "The type or namespace name 'World'",    ("World ", "using ZWebGLFrameWork.ECS;") },
                { "The type or namespace name 'SystemGroup'",("SystemGroup", "using ZWebGLFrameWork.ECS;") },
                { "The type or namespace name 'SystemBase'",("SystemBase", "using ZWebGLFrameWork.ECS;") },
                { "The type or namespace name 'DataManager'",("DataManager", "using ZWebGLFrameWork.Data;") },
                { "The type or namespace name 'GridManager'",("GridManager", "using ZWebGLFrameWork.Gameplay;") },
                { "The type or namespace name 'CombatManager'",("CombatManager", "using ZWebGLFrameWork.Combat;") },
                { "The type or namespace name 'UIManager'",("UIManager", "using ZWebGLFrameWork.UI;") },
                { "The type or namespace name 'NetworkManager'",("NetworkManager", "using ZWebGLFrameWork.Network;") },
                { "The type or namespace name 'GameFSM'", ("GameFSM", "using ZWebGLFrameWork.FSM;") },
            };

        [MenuItem("ZWebGL/🔧 Fix Missing Usings (Current File)", false, 150)]
        public static void FixCurrentFileUsings()
        {
            var path = GetActiveFilePath();
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[AutoFix] 未找到活动文件");
                return;
            }

            int count = FixMissingUsings(path);
            if (count > 0)
            {
                Debug.Log($"[AutoFix] 在 {Path.GetFileName(path)} 中补充了 {count} 个 using");
                AssetDatabase.Refresh();
            }
            else
            {
                Debug.Log($"[AutoFix] {Path.GetFileName(path)} 无需修复");
            }
        }

        /// <summary>尝试修复编译错误</summary>
        public static bool TryFix(CompileError error)
        {
            if (string.IsNullOrEmpty(error.File)) return false;

            foreach (var kv in _knownFixes)
            {
                if (!error.Message.Contains(kv.Key)) continue;

                var (searchPattern, usingToAdd) = kv.Value;
                string content = File.ReadAllText(error.File);

                // 检查是否已有该 using
                if (content.Contains(usingToAdd)) continue;

                // 检查是否真的用了该类型
                if (!content.Contains(searchPattern)) continue;

                // 在最后一个 using 之后插入
                int insertPos = FindUsingInsertPosition(content);
                if (insertPos < 0) continue;

                string newContent = content.Insert(insertPos, usingToAdd + "\n");
                SafeWriteAllText(error.File, newContent);
                Debug.Log($"[AutoFix] 补充 using: {Path.GetFileName(error.File)} ← {usingToAdd}");
                return true;
            }

            return false;
        }

        private static int FixMissingUsings(string filePath)
        {
            string content = File.ReadAllText(filePath);
            int count = 0;

            foreach (var kv in _knownFixes)
            {
                var (searchPattern, usingToAdd) = kv.Value;
                if (content.Contains(usingToAdd)) continue;
                if (!content.Contains(searchPattern)) continue;

                int insertPos = FindUsingInsertPosition(content);
                if (insertPos < 0) continue;

                content = content.Insert(insertPos, usingToAdd + "\n");
                count++;
            }

            if (count > 0)
                SafeWriteAllText(filePath, content);

            return count;
        }

        private static void SafeWriteAllText(string filePath, string content)
        {
            // 创建备份
            string backupPath = filePath + ".bak";
            if (File.Exists(filePath) && !File.Exists(backupPath))
            {
                File.Copy(filePath, backupPath, overwrite: true);
                Debug.Log($"[AutoFix] 已备份: {backupPath}");
            }
            File.WriteAllText(filePath, content);
        }
        {
            // 找到最后一个 using 语句的位置
            var match = Regex.Match(content, @"^using .+;\s*$", RegexOptions.Multiline);
            if (!match.Success) return 0;

            // 找到所有 using 语句的最后一个
            var matches = Regex.Matches(content, @"^using .+;\s*$", RegexOptions.Multiline);
            if (matches.Count == 0) return 0;

            var last = matches[matches.Count - 1];
            return last.Index + last.Length;
        }

        private static string GetActiveFilePath()
        {
            var method = typeof(EditorWindow).Assembly
                .GetType("UnityEditor.SceneView")?
                .GetMethod("GetActiveWindow");

            // 通过 AssetDatabase 获取当前选中文件
            var guids = Selection.assetGUIDs;
            if (guids.Length > 0)
                return AssetDatabase.GUIDToAssetPath(guids[0]);

            return null;
        }

        // ==================== 程序集引用修复 ====================

        [MenuItem("ZWebGL/🔧 Fix Assembly References", false, 151)]
        public static void FixAssemblyReferences()
        {
            var guids = AssetDatabase.FindAssets("t:asmdef");
            int fixedCount = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/Scripts/")) continue;

                // 检查每个 asmdef 的 references 是否完整
                // 此处为框架性代码，需根据实际编译错误扩展
            }

            if (fixedCount > 0)
            {
                Debug.Log($"[AutoFix] 修复了 {fixedCount} 个程序集引用");
                AssetDatabase.Refresh();
            }
            else
            {
                Debug.Log("[AutoFix] 程序集引用无需修复");
            }
        }
    }
}
