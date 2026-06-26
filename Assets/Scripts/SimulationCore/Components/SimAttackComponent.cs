namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>攻击组件 — 使用 LogicInt 和 Tick 计数</summary>
    public struct SimAttackComponent : IHashableComponent
    {
        /// <summary>攻击力</summary>
        public int Damage;
        /// <summary>攻击范围（逻辑单位）</summary>
        public LogicInt Range;
        /// <summary>攻击冷却（Tick 数）</summary>
        public int CooldownTicks;
        /// <summary>上次攻击 Tick</summary>
        public int LastAttackTick;

        public bool IsOnCooldown(int currentTick) =>
            currentTick - LastAttackTick < CooldownTicks;

        public static SimAttackComponent Create(int damage, float range, float cooldownSeconds, int ticksPerSecond)
        {
            return new SimAttackComponent
            {
                Damage = damage,
                Range = LogicInt.FromFloat(range),
                CooldownTicks = (int)(cooldownSeconds * ticksPerSecond),
                LastAttackTick = -99999,
            };
        }

        public ulong HashInto(ulong hash)
        {
            const ulong FNVPrime = 1099511628211UL;
            hash ^= (ulong)Damage; hash *= FNVPrime;
            hash ^= (ulong)Range.RawValue; hash *= FNVPrime;
            hash ^= (ulong)CooldownTicks; hash *= FNVPrime;
            hash ^= (ulong)LastAttackTick; hash *= FNVPrime;
            return hash;
        }
    }
}
