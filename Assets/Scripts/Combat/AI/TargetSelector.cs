using UnityEngine;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 索敌器 — 唯一的索敌策略入口。
    /// 被 UnitAI 和 AttackSystem 共用，避免重复实现。
    /// 策略：优先距离近 + 血量低的敌人。
    /// </summary>
    public class TargetSelector
    {
        private readonly World _world;

        /// <summary>血量权重（距离 vs 血量，值越大越优先攻击残血）</summary>
        public float HealthWeight { get; set; } = 0.1f;

        public TargetSelector(World world)
        {
            _world = world;
        }

        /// <summary>寻找最佳攻击目标</summary>
        public Entity? FindTarget(TransformComponent selfTransform, Team selfTeam)
        {
            Entity? bestTarget = null;
            float bestScore = float.MaxValue;

            _world.ForEachComponent<TransformComponent>((entity, otherTransform) =>
            {
                if (!_world.TryGetComponent<TeamComponent>(entity, out var otherTeam)) return;
                if (!_world.TryGetComponent<HealthComponent>(entity, out var hp) || !hp.IsAlive) return;
                if (!TeamComponent.IsEnemy(selfTeam, otherTeam.TeamId)) return;

                float dist = Vector3.Distance(selfTransform.Position, otherTransform.Position);
                float score = dist - hp.CurrentHP * HealthWeight;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = entity;
                }
            });

            return bestTarget;
        }
    }
}
