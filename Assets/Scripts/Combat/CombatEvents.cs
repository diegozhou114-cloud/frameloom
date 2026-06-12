namespace ZWebGLFrameWork.Combat
{
    public enum CombatPhase { Idle, Deploying, Battling, Ended }

    public struct CombatEndEvent
    {
        public Team Winner;
        public int Stars;
        public int LootGold;
        public int LootElixir;
    }
}
