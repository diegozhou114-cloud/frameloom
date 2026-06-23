using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 攻击系统 — 检查目标在射程内 → 冷却完毕 → 开火 → 入队伤害事件。
    /// 索敌由 UnitAI/TargetSelector 负责，本系统不索敌。
    /// 依赖：Transform + Attack + Team + Target
    /// Order: 20
    /// </summary>
    public class AttackSystem : SystemBase
    {
        private float _currentTime;
        private TargetEvaluator _targetEvaluator;

        public AttackSystem() { Order = 20; }

        public override async UniTask InitAsync()
        {
            _targetEvaluator = new TargetEvaluator(World);
        }

        public override void Update()
        {
            _currentTime = Time.time;

            World.ForEachComponent<AttackComponent>((entity, attack) =>
            {
                if (!World.TryGetComponent<TransformComponent>(entity, out var transform)) return;
                if (attack.IsOnCooldown(_currentTime)) return;

                // 目标检查（不索敌，只验证）
                if (!World.TryGetComponent<TargetComponent>(entity, out var target) || !target.HasTarget)
                    return;

                var status = _targetEvaluator.Evaluate(entity, target, attack, transform);
                if (status != TargetStatus.InRange) return;

                attack.LastAttackTime = _currentTime;
                World.SetComponent(entity, attack);

                World.EnqueueEvent(new DamageEvent
                {
                    Amount = attack.Damage,
                    Attacker = entity,
                    Target = target.TargetEntity,
                });
            });
        }
    }
}
