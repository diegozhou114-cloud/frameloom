namespace ZWebGLFrameWork.SimulationCore
{
    public class SimAttackSystem : SimSystemBase
    {
        private int _currentTick;

        public int CurrentTick => _currentTick;

        public SimAttackSystem() { Order = 20; }

        public override void Update()
        {
            _currentTick++;

            World.ForEachEntities<SimAttackComponent, SimTransformComponent>(
                (SimEntity entity, ref SimAttackComponent attack, ref SimTransformComponent transform) =>
            {
                if (attack.IsOnCooldown(_currentTick)) return;

                if (!World.TryGetComponent<SimTargetComponent>(entity, out var target) || !target.HasTarget)
                    return;

                // Use cached target position (populated by TargetCacheSystem)
                // No remote entity lookup needed
                LogicInt dx = LogicInt.Abs(transform.PosX - target.TargetPosX);
                LogicInt dy = LogicInt.Abs(transform.PosY - target.TargetPosY);
                if (dx > attack.Range || dy > attack.Range) return;

                attack.LastAttackTick = _currentTick;

                World.EnqueueEvent(new SimDamageEvent
                {
                    Amount = attack.Damage,
                    Attacker = entity,
                    Target = target.TargetEntity,
                });
            });
        }

        public void Reset() { _currentTick = 0; }

        public ulong HashInto(ulong hash)
        {
            var state = new DeterministicHash64(hash);
            state.AddInt32(_currentTick);
            return state.Value;
        }
    }
}
