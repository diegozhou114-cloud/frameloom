namespace ZWebGLFrameWork.SimulationCore
{
    public class SimArchetypeAttackSystem : SimArchetypeSystemBase
    {
        private int _currentTick;

        public SimArchetypeAttackSystem() { Order = 20; }

        public override void Update()
        {
            _currentTick++;

            World.ForEachEntities<SimAttackComponent, SimTransformComponent>(
                (entity, ref attack, ref transform) =>
            {
                if (attack.IsOnCooldown(_currentTick)) return;

                if (!World.TryGetComponent<SimTargetComponent>(entity, out var target) || !target.HasTarget)
                    return;

                if (!World.TryGetComponent<SimTransformComponent>(target.TargetEntity, out var targetTransform))
                    return;

                LogicInt dx = LogicInt.Abs(transform.PosX - targetTransform.PosX);
                LogicInt dy = LogicInt.Abs(transform.PosY - targetTransform.PosY);
                if (dx > attack.Range || dy > attack.Range) return;

                attack.LastAttackTick = _currentTick;
                World.SetComponent(entity, attack);

                World.EnqueueEvent(new SimDamageEvent
                {
                    Amount = attack.Damage,
                    Attacker = entity,
                    Target = target.TargetEntity,
                });
            });
        }

        public void Reset() { _currentTick = 0; }
    }
}
