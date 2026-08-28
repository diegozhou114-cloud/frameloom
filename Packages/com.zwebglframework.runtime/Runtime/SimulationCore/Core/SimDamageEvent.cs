namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>确定性伤害事件</summary>
    public struct SimDamageEvent
    {
        public int Amount;
        public SimEntity Attacker;
        public SimEntity Target;
    }
}
