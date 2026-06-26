using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>移动组件 — 使用 LogicInt，路径为网格坐标列表</summary>
    public struct SimMovementComponent : IHashableComponent
    {
        /// <summary>移动速度（逻辑单位/Tick）</summary>
        public LogicInt Speed;
        /// <summary>网格单元格边长（逻辑单位）</summary>
        public LogicInt CellSize;
        /// <summary>当前路径 — 引用类型，HashInto 只哈希存在性不哈希内容（PathIndex/GridPos 已反映移动进度）</summary>
        public List<int> Path;
        /// <summary>当前路径索引</summary>
        public int PathIndex;
        /// <summary>路径目标网格坐标</summary>
        public int TargetGridX, TargetGridY;

        public bool HasPath => Path != null && PathIndex < Path.Count;

        public (int x, int y)? NextWaypoint
        {
            get
            {
                if (!HasPath) return null;
                int packed = Path[PathIndex];
                return ((packed >> 16) & 0xFFFF, packed & 0xFFFF);
            }
        }

        public static SimMovementComponent Create(float speed, float cellSize = 1f)
        {
            return new SimMovementComponent
            {
                Speed = LogicInt.FromFloat(speed),
                CellSize = LogicInt.FromFloat(cellSize),
                Path = null,
                PathIndex = 0,
            };
        }

        public ulong HashInto(ulong hash)
        {
            const ulong FNVPrime = 1099511628211UL;
            hash ^= (ulong)Speed.RawValue; hash *= FNVPrime;
            hash ^= (ulong)CellSize.RawValue; hash *= FNVPrime;
            hash ^= (ulong)PathIndex; hash *= FNVPrime;
            hash ^= (ulong)TargetGridX; hash *= FNVPrime;
            hash ^= (ulong)TargetGridY; hash *= FNVPrime;
            // Path reference type — hash by existence, not content (content changes are reflected in PathIndex/GridPos)
            if (Path != null) { hash ^= 1UL; hash *= FNVPrime; }
            return hash;
        }
    }
}
