using ZWebGLFrameWork.Combat.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>攻击属性</summary>
    public struct AttackComponent : IComponent
    {
        /// <summary>攻击力</summary>
        public int Damage;

        /// <summary>攻击范围（格）</summary>
        public float Range;

        /// <summary>攻击冷却时间（秒）</summary>
        public float Cooldown;

        /// <summary>上次攻击时间</summary>
        public float LastAttackTime;

        /// <summary>当前是否在冷却中</summary>
        public bool IsOnCooldown(float currentTime) =>
            currentTime - LastAttackTime < Cooldown;

        public static AttackComponent Create(int damage, float range, float cooldown)
        {
            return new AttackComponent
            {
                Damage = damage,
                Range = range,
                Cooldown = cooldown,
                LastAttackTime = -999f,
            };
        }
    }
}
