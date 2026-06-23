namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>生命值组件 — 纯整数</summary>
    public struct SimHealthComponent : ISimComponent
    {
        public int MaxHP;
        public int CurrentHP;

        public bool IsAlive => CurrentHP > 0;

        public static SimHealthComponent Create(int maxHP)
        {
            return new SimHealthComponent { MaxHP = maxHP, CurrentHP = maxHP };
        }
    }
}
