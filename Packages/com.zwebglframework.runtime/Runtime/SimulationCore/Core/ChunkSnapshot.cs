using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>单个 Chunk 的列数据快照（Array.Copy 级别）</summary>
    public class ChunkSnapshot
    {
        /// <summary>该 Chunk 的组件类型 ID 列表（已排序）</summary>
        public int[] ComponentTypeIds;
        /// <summary>Entity ID 列副本</summary>
        public int[] EntityIds;
        /// <summary>Generation 列副本</summary>
        public int[] Generations;
        /// <summary>活跃实体数</summary>
        public int Count;
        /// <summary>每列组件的 Array 副本（长度为 Count，即仅含活跃实体数据）</summary>
        public Array[] Columns;
    }
}
