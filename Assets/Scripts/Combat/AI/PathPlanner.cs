using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 路径规划器 — 调用 A* 寻路，将路径写入 MovementComponent。
    /// </summary>
    public class PathPlanner
    {
        private readonly World _world;
        private readonly AStarPathfinding _pathfinding;

        public PathPlanner(World world, AStarPathfinding pathfinding)
        {
            _world = world;
            _pathfinding = pathfinding;
        }

        /// <summary>寻路到目标并设置移动路径</summary>
        public bool PlanPath(Entity self, Vector2Int from, Vector2Int to, float speed, float cellSize)
        {
            var path = _pathfinding.FindPath(from, to);
            if (path.Count == 0) return false;

            if (!_world.HasComponent<MovementComponent>(self))
                _world.AddComponent(self, MovementComponent.Create(speed, cellSize));

            ref var mov = ref _world.GetComponent<MovementComponent>(self);
            mov.Path = path;
            mov.PathIndex = 0;

            return true;
        }

        /// <summary>清除移动路径（目标已在射程内）</summary>
        public void ClearPath(Entity self)
        {
            if (!_world.HasComponent<MovementComponent>(self)) return;

            ref var mov = ref _world.GetComponent<MovementComponent>(self);
            mov.Path = null;
            mov.PathIndex = 0;
        }
    }
}
