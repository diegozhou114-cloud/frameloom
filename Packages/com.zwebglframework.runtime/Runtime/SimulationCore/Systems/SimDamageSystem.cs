using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    public class SimDamageSystem : SimSystemBase
    {
        private readonly List<SimDamageEvent> _appliedEvents = new List<SimDamageEvent>();
        private readonly Action<SimDamageEvent> _applyDamage;

        public IReadOnlyList<SimDamageEvent> AppliedEvents => _appliedEvents;

        public SimDamageSystem()
        {
            Order = 30;
            _applyDamage = ApplyDamage;
        }

        public override void Update()
        {
            _appliedEvents.Clear();
            World.ConsumeEvents(_applyDamage);
        }

        private void ApplyDamage(SimDamageEvent evt)
        {
            if (!World.TryGetComponent<SimHealthComponent>(evt.Target, out var health))
                return;
            health.CurrentHP -= evt.Amount;
            if (health.CurrentHP < 0) health.CurrentHP = 0;
            World.SetComponent(evt.Target, health);
            _appliedEvents.Add(evt);
        }
    }
}
