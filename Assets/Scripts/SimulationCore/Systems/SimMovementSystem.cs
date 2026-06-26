namespace ZWebGLFrameWork.SimulationCore
{
    public class SimMovementSystem : SimSystemBase
    {
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

                LogicInt targetX = (LogicInt.FromInt(wx) * movement.CellSize) + (movement.CellSize / new LogicInt(2));
                LogicInt targetY = (LogicInt.FromInt(wy) * movement.CellSize) + (movement.CellSize / new LogicInt(2));

                LogicInt dx = targetX - transform.PosX;
                LogicInt dy = targetY - transform.PosY;
                LogicInt dist = LogicInt.Abs(dx) + LogicInt.Abs(dy);

                LogicInt step = movement.Speed * LogicDeltaTime;
                if (step >= dist)
                {
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
                    if (dist.RawValue > 0)
                    {
                        LogicInt fracX = (dx * step.RawValue) / dist;
                        LogicInt fracY = (dy * step.RawValue) / dist;
                        transform.PosX += fracX;
                        transform.PosY += fracY;
                    }
                }
            });
        }
    }
}
