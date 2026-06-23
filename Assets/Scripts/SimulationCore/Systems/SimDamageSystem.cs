namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 确定性伤害系统 — 消费 SimDamageEvent 队列，扣减生命值。
    /// Order: 30
    /// </summary>
    public class SimDamageSystem : SimSystemBase
    {
        public SimDamageSystem() { Order = 30; }

        public override void Update()
        {
            World.ConsumeEvents<SimDamageEvent>(evt =>
            {
                if (!World.TryGetComponent<SimHealthComponent>(evt.Target, out var health))
                    return;

                health.CurrentHP -= evt.Amount;
                if (health.CurrentHP < 0) health.CurrentHP = 0;

                World.SetComponent(evt.Target, health);
            });
        }
    }
}
