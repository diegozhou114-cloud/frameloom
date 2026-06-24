using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 确定性移动系统 — 沿路径逐 Tick 移动单位。
    /// 依赖：SimTransform + SimMovement
    /// Order: 10（最先执行）
    /// </summary>
    public class SimMovementSystem : SimSystemBase
    {
        /// <summary>每 Tick 的固定时间步长（逻辑单位量级，= 1）</summary>
        public const int LogicDeltaTime = 1;

        public SimMovementSystem() { Order = 10; }

        public override void Update()
        {
            World.ForEachEntities<SimTransformComponent, SimMovementComponent>(
                (entity, ref transform, ref movement) =>
            {
                if (!movement.HasPath) return;

                var wp = movement.NextWaypoint;
                if (!wp.HasValue) return;
                (int wx, int wy) = wp.Value;

                LogicInt targetX = (LogicInt.FromInt(wx) * movement.CellSize) + (movement.CellSize / LogicInt.FromInt(2));
                LogicInt targetY = (LogicInt.FromInt(wy) * movement.CellSize) + (movement.CellSize / LogicInt.FromInt(2));

                LogicInt dx = targetX - transform.PosX;
                LogicInt dy = targetY - transform.PosY;
                LogicInt dist = LogicInt.Abs(dx) + LogicInt.Abs(dy); // 曼哈顿近似

                LogicInt step = movement.Speed * LogicDeltaTime;
                if (step >= dist)
                {
                    // 到达路径点
                    transform.PosX = targetX;
                    transform.PosY = targetY;
                    transform.GridX = wx;
                    transform.GridY = wy;
                    movement.PathIndex++;

                    if (!movement.HasPath)
                    {
                        movement.Path = null;
                        movement.PathIndex = 0;
                    }
                }
                else
                {
                    // 朝目标移动一步（方向单位化用整数除法近似）
                    if (dist.RawValue > 0)
                    {
                        LogicInt fracX = (dx * step.RawValue) / dist;
                        LogicInt fracY = (dy * step.RawValue) / dist;
                        transform.PosX += fracX;
                        transform.PosY += fracY;
                    }
                }

                World.SetComponent(entity, transform);
                World.SetComponent(entity, movement);
            });
        }
    }
}
