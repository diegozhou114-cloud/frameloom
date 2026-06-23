namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 确定性攻击系统 — 检查目标在射程内 → 冷却完毕 → 扣血。
    /// Order: 20
    /// </summary>
    public class SimAttackSystem : SimSystemBase
    {
        private int _currentTick;

        public SimAttackSystem() { Order = 20; }

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

                // 简单距离检查（曼哈顿距离，确定性）
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

        /// <summary>重置 Tick 计数（战斗开始时调用）</summary>
        public void Reset() { _currentTick = 0; }
    }
}
