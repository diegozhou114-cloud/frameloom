using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>生命值</summary>
    public struct HealthComponent : IComponent
    {
        public int MaxHP;
        public int CurrentHP;

        /// <summary>是否存活</summary>
        public bool IsAlive => CurrentHP > 0;

        /// <summary>血量百分比</summary>
        public float Percent => MaxHP > 0 ? (float)CurrentHP / MaxHP : 0f;

        public static HealthComponent Create(int maxHP)
        {
            return new HealthComponent { MaxHP = maxHP, CurrentHP = maxHP };
        }
    }
}
