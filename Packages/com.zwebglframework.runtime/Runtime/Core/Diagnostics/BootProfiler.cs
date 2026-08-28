using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 启动耗时性能埋点 — 记录每个 Phase 的开始/结束时间。
    /// 输出 Delta 到日志，可通过 GetReport() 获取完整报告。
    /// </summary>
    public static class BootProfiler
    {
        private static readonly List<PhaseTiming> _phases = new();
        private static float _phaseStartTime;

        /// <summary>阶段计时列表（按调用顺序）</summary>
        public static IReadOnlyList<PhaseTiming> Phases => _phases;

        /// <summary>开始新阶段</summary>
        public static void BeginPhase(string phaseName)
        {
            _phaseStartTime = Time.realtimeSinceStartup;
            Debug.Log($"[BootProfiler] ▶ {phaseName}");
        }

        /// <summary>结束当前阶段并记录耗时</summary>
        public static void EndPhase(string phaseName)
        {
            float elapsed = Time.realtimeSinceStartup - _phaseStartTime;
            _phases.Add(new PhaseTiming
            {
                Name = phaseName,
                ElapsedMs = elapsed * 1000f,
                Timestamp = Time.realtimeSinceStartup,
            });
            Debug.Log($"[BootProfiler] ✓ {phaseName}: {elapsed * 1000f:F0}ms");
        }

        /// <summary>获取所有阶段耗时报告</summary>
        public static string GetReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 启动耗时报告 ===");
            float total = 0;
            foreach (var p in _phases)
            {
                sb.AppendLine($"  {p.Name}: {p.ElapsedMs:F0}ms");
                total += p.ElapsedMs;
            }
            sb.AppendLine($"  总计: {total:F0}ms");
            return sb.ToString();
        }

        /// <summary>清空历史记录</summary>
        public static void Clear()
        {
            _phases.Clear();
        }

        [System.Serializable]
        public struct PhaseTiming
        {
            public string Name;
            public float ElapsedMs;
            public float Timestamp;
        }
    }
}
