using UnityEngine;
using ZWebGLFrameWork.Combat.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 伤害系统 — 处理 DamageEvent，扣血。
    /// Order: 30
    /// </summary>
    public class DamageSystem : SystemBase
    {
        public DamageSystem() { Order = 30; }

        public override void Update()
        {
            foreach (var entity in World.GetEntitiesWith<DamageEvent>())
            {
                if (!World.TryGetComponent<HealthComponent>(entity, out var health))
                {
                    World.RemoveComponent<DamageEvent>(entity);
                    continue;
                }

                var damageEvent = World.GetComponent<DamageEvent>(entity);

                // 扣血
                health.CurrentHP -= damageEvent.Amount;
                if (health.CurrentHP < 0) health.CurrentHP = 0;

                World.AddComponent(entity, health);
                World.RemoveComponent<DamageEvent>(entity);

                Debug.Log($"[DamageSystem] {entity} 受到 {damageEvent.Amount} 伤害, HP={health.CurrentHP}/{health.MaxHP}");
            }
        }
    }
}
