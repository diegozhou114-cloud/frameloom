using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>可序列化的命令包装</summary>
    [Serializable]
    public class SerializableCommand
    {
        [JsonInclude] public string Type { get; set; }
        [JsonInclude] public int Tick { get; set; }
        [JsonInclude] public int PlayerId { get; set; }
        [JsonInclude] public int UnitTypeId { get; set; }
        [JsonInclude] public int EntityId { get; set; }
        [JsonInclude] public int CasterId { get; set; }
        [JsonInclude] public int SkillId { get; set; }
        [JsonInclude] public int GridX { get; set; }
        [JsonInclude] public int TargetX { get; set; }
        [JsonInclude] public int GridY { get; set; }
        [JsonInclude] public int TargetY { get; set; }
    }

    /// <summary>单 Tick 的录制数据</summary>
    [Serializable]
    public class TickRecord
    {
        [JsonInclude] public int Tick { get; set; }
        [JsonInclude] public List<SerializableCommand> Commands { get; set; } = new List<SerializableCommand>();
        [JsonInclude] public ulong Checksum { get; set; }
    }

    /// <summary>
    /// 战斗回放文件 — 包含完整的命令序列和校验数据。
    /// 用于异步攻击验证：客户端上传，服务器重放比对。
    /// </summary>
    [Serializable]
    public class ReplayFile
    {
        [JsonInclude] public int Version { get; set; } = 1;
        [JsonInclude] public int Seed { get; set; }
        [JsonInclude] public SimEntitySpawnInfo[] InitialLayout { get; set; }
        [JsonInclude] public List<TickRecord> Records { get; set; } = new List<TickRecord>();

        public int TotalTicks => Records.Count;

        public ulong GetChecksum(int tick)
        {
            foreach (var r in Records)
                if (r.Tick == tick) return r.Checksum;
            return 0;
        }

        /// <summary>序列化为 JSON 字符串</summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>从 JSON 字符串反序列化</summary>
        public static ReplayFile FromJson(string json)
        {
            return JsonSerializer.Deserialize<ReplayFile>(json);
        }
    }
}
