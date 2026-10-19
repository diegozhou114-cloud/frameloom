using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore.MiniSiege
{
    public sealed partial class SiegeSimulation
    {
        private readonly List<(SimEntity Target, int Damage)> _hits =
            new List<(SimEntity, int)>();
        private readonly List<(SimEntity Entity, LogicInt X)> _moves =
            new List<(SimEntity, LogicInt)>();

        /// <summary>Plan from one state, then apply damage simultaneously to avoid first-player bias.</summary>
        private void AdvanceCombat()
        {
            _hits.Clear();
            _moves.Clear();
            foreach (SimEntity entity in _entities)
            {
                if (!_world.GetComponentRef<SimHealthComponent>(entity).IsAlive) continue;
                var identity = _world.GetComponentRef<SimIdentityComponent>(entity);
                var position = _world.GetComponentRef<SimTransformComponent>(entity);
                ref var attack = ref _world.GetComponentRef<SimAttackComponent>(entity);
                SimEntity target = FindTarget(entity, identity, position, attack.Range);
                if (target.IsValid)
                {
                    if (!attack.IsOnCooldown(Tick))
                    {
                        attack.LastAttackTick = Tick;
                        _hits.Add((target, attack.Damage));
                    }
                }
                else if (identity.ArchetypeId != 0)
                {
                    LogicInt step = LogicInt.FromRaw(identity.ArchetypeId == 1 ? 1000 : 750);
                    LogicInt x = position.PosX + step * (identity.OwnerPlayerId == 0 ? 1 : -1);
                    _moves.Add((entity, LogicInt.Max(LogicInt.FromInt(1), LogicInt.Min(LogicInt.FromInt(29), x))));
                }
            }
            foreach (var move in _moves)
                _world.GetComponentRef<SimTransformComponent>(move.Entity).PosX = move.X;
            foreach (var hit in _hits)
            {
                ref var health = ref _world.GetComponentRef<SimHealthComponent>(hit.Target);
                health.CurrentHP = System.Math.Max(0, health.CurrentHP - hit.Damage);
            }
            foreach (SimEntity entity in _entities)
                if (!_world.GetComponentRef<SimHealthComponent>(entity).IsAlive &&
                    _world.GetComponentRef<SimIdentityComponent>(entity).ArchetypeId != 0)
                    _world.DestroyEntity(entity);
        }

        private SimEntity FindTarget(SimEntity self, SimIdentityComponent identity,
            SimTransformComponent position, LogicInt range)
        {
            SimEntity best = SimEntity.Invalid;
            LogicInt nearest = LogicInt.FromInt(100);
            foreach (SimEntity candidate in _entities)
            {
                if (candidate.Id == self.Id || !_world.GetComponentRef<SimHealthComponent>(candidate).IsAlive)
                    continue;
                var other = _world.GetComponentRef<SimIdentityComponent>(candidate);
                if (other.OwnerPlayerId == identity.OwnerPlayerId) continue;
                var location = _world.GetComponentRef<SimTransformComponent>(candidate);
                if (identity.ArchetypeId != 0 && other.ArchetypeId != 0 && location.GridY != position.GridY)
                    continue;
                LogicInt distance = LogicInt.Abs(location.PosX - position.PosX);
                if (distance <= range && distance < nearest)
                {
                    nearest = distance;
                    best = candidate;
                }
            }
            return best;
        }

        private void Pulse(int player, int lane)
        {
            CollectEntities();
            foreach (SimEntity entity in _entities)
            {
                var identity = _world.GetComponentRef<SimIdentityComponent>(entity);
                var position = _world.GetComponentRef<SimTransformComponent>(entity);
                if (identity.ArchetypeId == 0 || identity.OwnerPlayerId == player || position.GridY != lane ||
                    position.PosX < LogicInt.FromInt(10) || position.PosX > LogicInt.FromInt(20)) continue;
                ref var health = ref _world.GetComponentRef<SimHealthComponent>(entity);
                health.CurrentHP = System.Math.Max(0, health.CurrentHP - 35);
            }
        }
    }
}
