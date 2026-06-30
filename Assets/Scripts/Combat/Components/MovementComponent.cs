using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>移动属性</summary>
    public struct MovementComponent : IComponent
    {
        /// <summary>移动速度（格/秒）</summary>
        public float Speed;

        /// <summary>网格单元格边长（世界单位），由 Spawner 注入</summary>
        public float CellSize;

        /// <summary>当前路径（网格坐标列表）</summary>
        /// <remarks>注意：Path 是引用类型。复制此结构体时，新旧副本共享同一个 List 实例，
        /// 对路径的修改会同时影响两者。如需独立副本请手动 new List&lt;Vector2Int&gt;(original.Path)。</remarks>
        public List<Vector2Int> Path;

        /// <summary>当前路径索引</summary>
        public int PathIndex;

        /// <summary>路径目标</summary>
        public Vector2Int TargetGridPos;

        /// <summary>是否有待走路径</summary>
        public bool HasPath => Path != null && PathIndex < Path.Count;

        /// <summary>下一个路径点</summary>
        public Vector2Int? NextWaypoint
        {
            get
            {
                if (!HasPath) return null;
                return Path[PathIndex];
            }
        }

        public static MovementComponent Create(float speed, float cellSize = 1f)
        {
            return new MovementComponent
            {
                Speed = speed,
                CellSize = cellSize,
                Path = null,
                PathIndex = 0,
            };
        }
    }
}
