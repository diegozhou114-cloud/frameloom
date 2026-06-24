using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 完整 Tick 快照 — 包含世界实体元数据 + 所有组件池的密集数据。
    /// 用于 FrameSync 回滚：RestoreFromFull(TickSnapshot) 后继续模拟。
    /// </summary>
    public class TickSnapshot
    {
        /// <summary>下一个 Entity ID</summary>
        public int NextId;
        /// <summary>Generation 数组</summary>
        public int[] Generations;
        /// <summary>所有活跃实体</summary>
        public SimEntity[] Entities;
        /// <summary>所有组件池的快照</summary>
        public List<PoolSnapshot> Pools;
        /// <summary>该 Tick 的 Checksum</summary>
        public ulong Checksum;
        /// <summary>快照对应的 Tick 号（用于回滚验证）</summary>
        public int Tick;
    }
}
