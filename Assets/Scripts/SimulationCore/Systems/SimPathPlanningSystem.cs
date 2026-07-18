using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class SimPathPlanningSystem : SimSystemBase
    {
        public SimPathPlanningSystem() { Order = 8; }

        public override void Update()
        {
            World.ForEachEntities<SimMovementComponent, SimTransformComponent>(
                (entity, movement, transform) =>
                {
                    if (!World.TryGetComponent(entity, out SimTargetComponent target) ||
                        !target.HasTarget ||
                        !World.TryGetComponent(target.TargetEntity, out SimTransformComponent targetTransform))
                    {
                        movement.Path = null;
                        movement.PathIndex = 0;
                        World.SetComponent(entity, movement);
                        return;
                    }

                    int targetX = targetTransform.GridX;
                    int targetY = targetTransform.GridY;
                    bool sameTarget = movement.TargetGridX == targetX && movement.TargetGridY == targetY;
                    movement.TargetGridX = targetX;
                    movement.TargetGridY = targetY;
                    int range = 0;
                    if (World.TryGetComponent(entity, out SimAttackComponent attack))
                        range = attack.Range.FloorToInt();
                    int distance = Math.Abs(transform.GridX - targetX) +
                                   Math.Abs(transform.GridY - targetY);
                    if (distance <= range)
                    {
                        movement.Path = null;
                        movement.PathIndex = 0;
                        World.SetComponent(entity, movement);
                        return;
                    }

                    if (movement.HasPath && sameTarget)
                        return;

                    var path = new List<int>();
                    int currentX = transform.GridX;
                    int currentY = transform.GridY;
                    int steps = Math.Max(Math.Abs(targetX - currentX), Math.Abs(targetY - currentY));
                    for (int step = 0; step < steps; step++)
                    {
                        if (currentX != targetX) currentX += targetX > currentX ? 1 : -1;
                        if (currentY != targetY) currentY += targetY > currentY ? 1 : -1;
                        path.Add((currentX << 16) | (currentY & 0xFFFF));
                    }
                    movement.Path = path;
                    movement.PathIndex = 0;
                    World.SetComponent(entity, movement);
                });
        }
    }
}
