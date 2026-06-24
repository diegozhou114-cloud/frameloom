using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>单个组件池的密集数组快照（值类型，支持 Array.Copy）</summary>
    public class PoolSnapshot
    {
        /// <summary>组件类型全名（用于恢复时匹配）</summary>
        public string ComponentTypeName;
        /// <summary>Sparse 数组（entityId → denseIndex）</summary>
        public int[] Sparse;
        /// <summary>Dense Entities 数组</summary>
        public SimEntity[] DenseEntities;
        /// <summary>Dense Components 原始字节数组</summary>
        public byte[] DenseComponentsBytes;
        /// <summary>活跃组件数</summary>
        public int Count;
        /// <summary>单个组件的字节大小</summary>
        public int ComponentByteSize;
    }
}
