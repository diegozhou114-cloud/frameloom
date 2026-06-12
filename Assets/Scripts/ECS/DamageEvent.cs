using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.ECS
{
    /// <summary>ECS 伤害事件 — 由 AttackSystem 入队，DamageSystem 消费</summary>
    public struct DamageEvent
    {
        public int Amount;
        public Entity Attacker;
        public Entity Target;
    }
}
