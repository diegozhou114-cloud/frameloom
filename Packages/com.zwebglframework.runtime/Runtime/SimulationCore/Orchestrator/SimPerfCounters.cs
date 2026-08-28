namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真性能计数器 — 记录每 Tick 的关键耗时。
    /// 通过 ISimLogger 输出，用于 WebGL/服务器性能调优。
    /// </summary>
    public class SimPerfCounters
    {
        /// <summary>最近一次 Tick 的 Checksum 计算耗时（毫秒）</summary>
        public float LastChecksumMs { get; private set; }

        /// <summary>最近一次 Tick 的 DrainCommands 耗时（毫秒）</summary>
        public float LastDrainCommandsMs { get; private set; }

        /// <summary>最近一次 Tick 的 Systems Update 耗时（毫秒）</summary>
        public float LastSystemsUpdateMs { get; private set; }

        /// <summary>最近一次 Tick 总耗时（毫秒）</summary>
        public float LastTickTotalMs => LastDrainCommandsMs + LastSystemsUpdateMs + LastChecksumMs;

        /// <summary>当前活跃 Entity 数量</summary>
        public int EntityCount { get; private set; }

        /// <summary>当前 Tick 编号</summary>
        public int TickNumber { get; private set; }

        /// <summary>最近 60 Tick 的平均总耗时（用于性能劣化检测）</summary>
        private float[] _recentTickTimes = new float[60];
        private int _recentIndex;

        /// <summary>记录一次 Tick 的性能数据</summary>
        public void RecordTick(int tickNumber, int entityCount,
            float drainCommandsMs, float systemsUpdateMs, float checksumMs)
        {
            TickNumber = tickNumber;
            EntityCount = entityCount;
            LastDrainCommandsMs = drainCommandsMs;
            LastSystemsUpdateMs = systemsUpdateMs;
            LastChecksumMs = checksumMs;

            _recentTickTimes[_recentIndex % 60] = LastTickTotalMs;
            _recentIndex++;
        }

        /// <summary>获取最近 60 Tick 的平均总耗时</summary>
        public float AverageTickMs()
        {
            int count = _recentIndex < 60 ? _recentIndex : 60;
            float sum = 0f;
            for (int i = 0; i < count; i++)
                sum += _recentTickTimes[i];
            return count > 0 ? sum / count : 0f;
        }

        /// <summary>以单行日志输出最近性能数据</summary>
        public string GetSummary()
        {
            return $"[SimPerf] Tick#{TickNumber} Entities={EntityCount} "
                 + $"Total={LastTickTotalMs:F2}ms "
                 + $"(Drain={LastDrainCommandsMs:F2} Update={LastSystemsUpdateMs:F2} Hash={LastChecksumMs:F2}) "
                 + $"Avg60={AverageTickMs():F2}ms";
        }
    }
}
