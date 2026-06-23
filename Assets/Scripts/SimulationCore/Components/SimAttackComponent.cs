namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>攻击组件 — 使用 LogicInt 和 Tick 计数</summary>
    public struct SimAttackComponent : ISimComponent
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
    }
}
