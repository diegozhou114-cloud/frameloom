using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>移动组件 — 使用 LogicInt，路径为网格坐标列表</summary>
    public struct SimMovementComponent : ISimComponent
    {
        /// <summary>移动速度（逻辑单位/Tick）</summary>
        public LogicInt Speed;
        /// <summary>网格单元格边长（逻辑单位）</summary>
        public LogicInt CellSize;
        /// <summary>当前路径（网格坐标列表，GridX&lt;&lt;16|GridY）</summary>
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
    }
}
