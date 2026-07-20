using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    public static class ReplayFormat
    {
        public const ushort CurrentVersion = 1;
        public const int TickRate = 20;
    }

    /// <summary>可序列化的命令包装</summary>
    [Serializable]
    public class SerializableCommand
    {
        public string Type { get; set; }
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int Sequence { get; set; }
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
    public class ReplayTickRecord
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
        public ushort FormatVersion { get; set; }
        public string SimulationSchemaHash { get; set; }
        public string ContentConfigHash { get; set; }
        public int TickRate { get; set; }
        public string MatchId { get; set; }
        public string InitialStateHash { get; set; }
        public string SubmissionNonce { get; set; }
        public int FinalTick { get; set; } = -1;
        public SimBattleResult FinalResult { get; set; }
        public ulong FinalChecksum { get; set; }

        // Retained until authoritative match migration removes client-owned initial state.
        public int Version { get; set; } = 1;
        public int Seed { get; set; }
        public SimEntitySpawnInfo[] InitialLayout { get; set; }
        public List<ReplayTickRecord> Records { get; set; } =
            new List<ReplayTickRecord>();

        public int TotalTicks => Records.Count;

        public ulong GetChecksum(int tick)
        {
            // TODO: Use Dictionary<int, ReplayTickRecord> for O(1) lookup in large replays
            foreach (ReplayTickRecord r in Records)
                if (r.Tick == tick) return r.Checksum;
            return 0;
        }
    }
}
