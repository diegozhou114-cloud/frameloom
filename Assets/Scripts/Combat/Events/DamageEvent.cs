namespace ZWebGLFrameWork.Combat
{
    /// <summary>战斗伤害事件 — 由 AttackSystem 入队，DamageSystem 消费</summary>
    public struct DamageEvent
    {
        public int Amount;
        public ECS.Entity Attacker;
        public ECS.Entity Target;
    }
}
