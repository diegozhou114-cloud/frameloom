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
        /// <summary>当前路径 — 引用类型，哈希长度和每个 waypoint 以保留完整状态</summary>
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
            var state = new DeterministicHash64(hash);
            state.AddInt64(Speed.RawValue);
            state.AddInt64(CellSize.RawValue);
            state.AddInt32(PathIndex);
            state.AddInt32(TargetGridX);
            state.AddInt32(TargetGridY);
            state.AddInt32(Path?.Count ?? 0);
            if (Path != null)
                foreach (int waypoint in Path) state.AddInt32(waypoint);
            return state.Value;
        }
    }
}
