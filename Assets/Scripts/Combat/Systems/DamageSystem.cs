using UnityEngine;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 伤害系统 — 从 World 事件队列消费 DamageEvent，扣血。
    /// 不依赖 IComponent，纯队列驱动。
    /// Order: 30
    /// </summary>
    public class DamageSystem : SystemBase
    {
        public DamageSystem() { Order = 30; }

        public override void Update()
        {
            World.ConsumeEvents<DamageEvent>(evt =>
            {
                if (!World.TryGetComponent<HealthComponent>(evt.Target, out var health))
                    return;

                health.CurrentHP -= evt.Amount;
                if (health.CurrentHP < 0) health.CurrentHP = 0;

                // SetComponent 存在则覆盖（不触发 AddComponent 的 "if Has return" 导致写入丢失）
                World.SetComponent(evt.Target, health);

                Debug.Log($"[DamageSystem] {evt.Target} 受到 {evt.Amount} 伤害, HP={health.CurrentHP}/{health.MaxHP}");
            });
        }
    }
}
