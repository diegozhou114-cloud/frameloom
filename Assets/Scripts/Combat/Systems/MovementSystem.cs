using UnityEngine;
using ZWebGLFrameWork.Combat.ECS;
using ZWebGLFrameWork.Gameplay;

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

        private GridManager _grid;

        public override void Update()
        {
            if (_grid == null) _grid = GridManager.Instance;
            if (_grid == null) return;

            float dt = Time.deltaTime;

            foreach (var entity in World.GetEntitiesWith<TransformComponent>())
            {
                if (!World.TryGetComponent<MovementComponent>(entity, out var movement))
                    continue;
                if (!movement.HasPath) continue;

                ref var transform = ref World.GetComponent<TransformComponent>(entity);

                // 获取下一个路径点
                var nextWaypoint = movement.NextWaypoint.Value;
                Vector3 targetWorld = _grid.GridToWorld(nextWaypoint);

                // 移动
                Vector3 direction = (targetWorld - transform.Position).normalized;
                float step = movement.Speed * _grid.CellSize * dt;
                transform.Position += direction * Mathf.Min(step, Vector3.Distance(transform.Position, targetWorld));

                // 到达当前路径点
                if (Vector3.Distance(transform.Position, targetWorld) < 0.05f)
                {
                    transform.Position = targetWorld;
                    transform.GridPosition = nextWaypoint;
                    movement.PathIndex++;

                    // 到达终点
                    if (!movement.HasPath)
                    {
                        movement.Path = null;
                        movement.PathIndex = 0;

                        // 更新 component
                        World.AddComponent(entity, movement);
                    }
                }

                World.AddComponent(entity, movement);
            }
        }
    }
}
