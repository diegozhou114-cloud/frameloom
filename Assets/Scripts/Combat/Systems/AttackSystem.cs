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

        /// <summary>在 InitAsync 中创建，避免 Update 热路径中 new</summary>
        public override async System.Threading.Tasks.Task InitAsync()
        {
            _targetEvaluator = new TargetEvaluator(World);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public override void Update()
        {
            _currentTime = Time.time;

            World.ForEachComponent<AttackComponent>((entity, ref attack) =>
            {
                if (!World.TryGetComponent<TransformComponent>(entity, out var transform)) return;
                if (attack.IsOnCooldown(_currentTime)) return;

                // 目标检查（不索敌，只验证）
                if (!World.TryGetComponent<TargetComponent>(entity, out var target) || !target.HasTarget)
                    return;

                var status = _targetEvaluator.Evaluate(entity, target, attack, transform);
                if (status != TargetStatus.InRange) return;

                // 开火！attack 已通过 ref 原位修改，无需写回
                attack.LastAttackTime = _currentTime;

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
