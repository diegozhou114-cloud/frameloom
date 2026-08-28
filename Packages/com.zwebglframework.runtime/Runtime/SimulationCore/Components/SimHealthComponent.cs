namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>生命值组件 — 纯整数</summary>
    public struct SimHealthComponent : IHashableComponent
    {
        public int MaxHP;
        public int CurrentHP;

        public bool IsAlive => CurrentHP > 0;

        public static SimHealthComponent Create(int maxHP)
        {
            return new SimHealthComponent { MaxHP = maxHP, CurrentHP = maxHP };
        }

        public ulong HashInto(ulong hash)
        {
            const ulong FNVPrime = 1099511628211UL;
            hash ^= (ulong)CurrentHP; hash *= FNVPrime;
            hash ^= (ulong)MaxHP; hash *= FNVPrime;
            return hash;
        }
    }
}
