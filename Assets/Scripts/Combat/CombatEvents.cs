namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 战斗事件定义。
    /// 注意：CombatOrchestrator 当前使用字符串常量（如 "PhaseChanged"）发布事件，
    /// 缺乏编译期类型安全。未来建议迁移到 EventKey&lt;T&gt; 泛型事件键以在编译时捕获拼写错误。
    /// </summary>
    public enum CombatPhase { Idle, Deploying, Battling, Ended }

    public struct CombatEndEvent
    {
        public Team Winner;
        public int Stars;
        public int LootGold;
        public int LootElixir;
    }
}
