using UnityEngine;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 目标评估器 — 验证当前目标是否仍然有效。
    /// 检查：目标存活、在射程内、可攻击。
    /// </summary>
    public class TargetEvaluator
    {
        private readonly World _world;

        public TargetEvaluator(World world)
        {
            _world = world;
        }

        /// <summary>当前目标是否仍然有效</summary>
        public TargetStatus Evaluate(Entity self, TargetComponent target, AttackComponent attack, TransformComponent selfTransform)
        {
            // 无目标
            if (!target.HasTarget)
                return TargetStatus.NoTarget;

            // 目标实体还存在吗
            if (!_world.TryGetComponent<TransformComponent>(target.TargetEntity, out var targetTransform))
                return TargetStatus.TargetDead;

            // 目标还活着吗
            if (!_world.TryGetComponent<HealthComponent>(target.TargetEntity, out var hp) || !hp.IsAlive)
                return TargetStatus.TargetDead;

            // 距离检测
            // NOTE: Vector3.Distance (Euclidean) is fine for small counts, but on grid-aligned
            // positions Manhattan or Chebyshev distance would be cheaper and equally correct.
            float dist = Vector3.Distance(selfTransform.Position, targetTransform.Position);
            if (dist <= attack.Range)
                return TargetStatus.InRange;

            return TargetStatus.OutOfRange;
        }
    }

    /// <summary>目标状态</summary>
    public enum TargetStatus
    {
        NoTarget,    // 没有目标
        TargetDead,  // 目标已死亡
        InRange,     // 目标在射程内
        OutOfRange,  // 目标在射程外（需要移动）
    }
}
