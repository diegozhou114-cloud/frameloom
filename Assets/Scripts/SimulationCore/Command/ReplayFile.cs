using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>可序列化的命令包装</summary>
    [Serializable]
    public class SerializableCommand
    {
        public string Type { get; set; }
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int UnitTypeId { get; set; }
        public int EntityId { get; set; }
        public int CasterId { get; set; }
        public int SkillId { get; set; }
        public int GridX { get; set; }
        public int TargetX { get; set; }
        public int GridY { get; set; }
        public int TargetY { get; set; }
    }

    /// <summary>单 Tick 的录制数据</summary>
    [Serializable]
    public class TickRecord
    {
        public int Tick { get; set; }
        public List<SerializableCommand> Commands { get; set; } = new List<SerializableCommand>();
        public ulong Checksum { get; set; }
    }

    /// <summary>
    /// 战斗回放文件 — 包含完整的命令序列和校验数据。
    /// 用于异步攻击验证：客户端上传，服务器重放比对。
    /// </summary>
    [Serializable]
    public class ReplayFile
    {
        public int Version { get; set; } = 1;
        public int Seed { get; set; }
        public SimEntitySpawnInfo[] InitialLayout { get; set; }
        public List<TickRecord> Records { get; set; } = new List<TickRecord>();

        public int TotalTicks => Records.Count;

        public ulong GetChecksum(int tick)
        {
            // TODO: Use Dictionary<int, TickRecord> for O(1) lookup in large replays
            foreach (var r in Records)
                if (r.Tick == tick) return r.Checksum;
            return 0;
        }
    }
}
