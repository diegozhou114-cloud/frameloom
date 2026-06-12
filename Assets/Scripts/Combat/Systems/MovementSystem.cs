using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 移动系统 — 沿 A* 路径逐帧移动单位。
    /// 依赖：Transform + Movement
    /// Order: 10（最先执行）
    /// </summary>
    public class MovementSystem : SystemBase
    {
        public MovementSystem() { Order = 10; }

        // 收集批量写回，避免遍历中修改 Pool
        private readonly List<(Entity, MovementComponent)> _writeBack = new List<(Entity, MovementComponent)>();

        public override void Update()
        {
            float dt = Time.deltaTime;
            _writeBack.Clear();

            World.ForEachComponent<TransformComponent>((entity, ref transform) =>
            {
                if (!World.TryGetComponent<MovementComponent>(entity, out var movement))
                    return;
                if (!movement.HasPath) return;

                var nextWaypoint = movement.NextWaypoint.Value;
                Vector3 targetWorld = GridToWorld(nextWaypoint, movement.CellSize);

                Vector3 direction = (targetWorld - transform.Position).normalized;
                float step = movement.Speed * movement.CellSize * dt;
                transform.Position += direction *
                    Mathf.Min(step, Vector3.Distance(transform.Position, targetWorld));

                if (Vector3.Distance(transform.Position, targetWorld) < 0.05f)
                {
                    transform.Position = targetWorld;
                    transform.GridPosition = nextWaypoint;
                    movement.PathIndex++;

                    if (!movement.HasPath)
                    {
                        movement.Path = null;
                        movement.PathIndex = 0;
                    }
                }

                _writeBack.Add((entity, movement));
            });

            // 批量写回
            foreach (var (entity, movement) in _writeBack)
                World.AddComponent(entity, movement);
        }

        /// <summary>网格坐标 → 世界坐标（纯函数，不依赖 GridManager）</summary>
        public static Vector3 GridToWorld(Vector2Int gridPos, float cellSize)
        {
            return new Vector3(
                gridPos.x * cellSize + cellSize * 0.5f,
                0f,
                gridPos.y * cellSize + cellSize * 0.5f);
        }
    }
}
