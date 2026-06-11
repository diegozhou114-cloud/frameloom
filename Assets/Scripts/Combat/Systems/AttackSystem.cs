using UnityEngine;
using ZWebGLFrameWork.Combat.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 攻击系统 — 索敌 + 进入射程后攻击 + 冷却管理。
    /// 依赖：Transform + Attack + Team + (Target)
    /// Order: 20（在移动之后）
    /// </summary>
    public class AttackSystem : SystemBase
    {
        public AttackSystem() { Order = 20; }

        private float _currentTime;

        public override void Update()
        {
            _currentTime = Time.time;

            foreach (var entity in World.GetEntitiesWith<AttackComponent>())
            {
                if (!World.TryGetComponent<TransformComponent>(entity, out var transform)) continue;
                if (!World.TryGetComponent<TeamComponent>(entity, out var team)) continue;

                ref var attack = ref World.GetComponent<AttackComponent>(entity);

                // 冷却中
                if (attack.IsOnCooldown(_currentTime)) continue;

                // 检查目标
                if (!World.TryGetComponent<TargetComponent>(entity, out var target) || !target.HasTarget)
                {
                    // 索敌：找最近的敌人
                    var enemy = FindNearestEnemy(transform, team.TeamId);
                    if (enemy.HasValue)
                    {
                        World.AddComponent(entity, TargetComponent.Create());
                        ref var t = ref World.GetComponent<TargetComponent>(entity);
                        t.TargetEntity = enemy.Value;
                        target = t;
                    }
                    else continue;
                }

                // 检查目标是否有效
                if (!World.TryGetComponent<TransformComponent>(target.TargetEntity, out var targetTransform))
                {
                    World.RemoveComponent<TargetComponent>(entity);
                    continue;
                }

                // 距离检测
                float dist = Vector3.Distance(transform.Position, targetTransform.Position);
                if (dist > attack.Range)
                {
                    // 超出射程，需要移动过去（由 UnitAI 处理）
                    continue;
                }

                // 攻击！
                attack.LastAttackTime = _currentTime;
                World.AddComponent(entity, attack); // 写回

                // 标记伤害事件（由 DamageSystem 处理）
                World.AddComponent(target.TargetEntity, new DamageEvent
                {
                    Amount = attack.Damage,
                    Attacker = entity,
                });
            }
        }

        private Entity? FindNearestEnemy(TransformComponent selfTransform, Team selfTeam)
        {
            Entity? nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var other in World.GetEntitiesWith<TransformComponent>())
            {
                if (!World.TryGetComponent<TeamComponent>(other, out var otherTeam)) continue;
                if (!World.TryGetComponent<HealthComponent>(other, out var hp) || !hp.IsAlive) continue;
                if (!TeamComponent.IsEnemy(selfTeam, otherTeam.TeamId)) continue;

                var otherTransform = World.GetComponent<TransformComponent>(other);
                float dist = Vector3.Distance(selfTransform.Position, otherTransform.Position);

                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = other;
                }
            }

            return nearest;
        }
    }

    /// <summary>伤害事件（临时组件，DamageSystem 处理后移除）</summary>
    public struct DamageEvent : IComponent
    {
        public int Amount;
        public Entity Attacker;
    }
}
