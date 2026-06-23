using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>单 Tick 的录制数据</summary>
    [Serializable]
    public struct TickRecord
    {
        public int Tick;
        public byte[][] Commands;  // 序列化的命令数组
        public ulong Checksum;
    }

    /// <summary>
    /// 战斗回放文件 — 包含完整的命令序列和校验数据。
    /// 用于异步攻击验证：客户端上传，服务器重放比对。
    /// </summary>
    [Serializable]
    public class ReplayFile
    {
        /// <summary>格式版本号（递增兼容）</summary>
        public int Version = 1;
        /// <summary>随机种子</summary>
        public int Seed;
        /// <summary>初始实体布局（ISimInitializer 序列化）</summary>
        public SimEntitySpawnInfo[] InitialLayout;
        /// <summary>每 Tick 的录制数据</summary>
        public List<TickRecord> Records = new List<TickRecord>();

        /// <summary>战斗总 Tick 数</summary>
        public int TotalTicks => Records.Count;

        /// <summary>获取指定 Tick 的校验和</summary>
        public ulong GetChecksum(int tick)
        {
            foreach (var r in Records)
                if (r.Tick == tick) return r.Checksum;
            return 0;
        }
    }
}
