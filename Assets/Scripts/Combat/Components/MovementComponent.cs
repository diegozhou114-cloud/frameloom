using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Combat.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>移动属性</summary>
    public struct MovementComponent : IComponent
    {
        /// <summary>移动速度（格/秒）</summary>
        public float Speed;

        /// <summary>当前路径（网格坐标列表）</summary>
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

        public static MovementComponent Create(float speed)
        {
            return new MovementComponent
            {
                Speed = speed,
                Path = null,
                PathIndex = 0,
            };
        }
    }
}
