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

        public void RestoreTick(int tick)
        {
            if (tick < 0) throw new System.ArgumentOutOfRangeException(nameof(tick));
            _currentTick = tick;
        }

        public ulong HashInto(ulong hash) => HashStateInto(hash, _currentTick);

        public static ulong HashStateInto(ulong hash, int currentTick)
        {
            var state = new DeterministicHash64(hash);
            state.AddInt32(currentTick);
            return state.Value;
        }
    }
}
