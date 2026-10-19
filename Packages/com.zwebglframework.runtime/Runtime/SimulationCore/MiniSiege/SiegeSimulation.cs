using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore.MiniSiege
{
    /// <summary>Two-lane deterministic battle using the framework's ECS and fixed-point math.</summary>
    public sealed partial class SiegeSimulation
    {
        public const int TickRate = 20;
        public const int DurationTicks = 180 * TickRate;
        public const int BaseHP = 300;
        public const int MaxEnergy = 12;
        public const int SkillCooldownTicks = 12 * TickRate;
        private readonly SimWorld _world = new SimWorld(SimComponentSchema.CreateRegistry());
        private readonly SimEntity[] _bases = new SimEntity[2];
        private readonly int[] _energy = { 8, 8 };
        private readonly int[] _skillReady = new int[2];
        private readonly int[] _lastSequence = { -1, -1 };
        private readonly List<SimEntity> _entities = new List<SimEntity>();
        private readonly int _seed;
        public int Tick { get; private set; }
        public int Winner { get; private set; } = -1;
        public ulong Checksum { get; private set; }
        public string LastAction { get; private set; } = "选择路线，保护你的弓手";

        /// <summary>Seed identifies an identical initial battle on all three replicas.</summary>
        public SiegeSimulation(int seed)
        {
            _seed = seed;
            for (int player = 0; player < 2; player++)
                _bases[player] = Spawn(player, 0, -1);
            RefreshChecksum();
        }

        public int Energy(int player) => _energy[player];
        public int SkillRemaining(int player) => Math.Max(0, _skillReady[player] - Tick);
        public int BaseHealth(int player) => _world.GetComponentRef<SimHealthComponent>(_bases[player]).CurrentHP;
        public static int Cost(int kind) => kind == 1 ? 3 : kind == 2 ? 4 : kind == 3 ? 6 : 0;

        /// <summary>Advance exactly once per authority frame, never from Unity deltaTime.</summary>
        public void Step(SiegeInput[] inputs)
        {
            if (Winner >= 0) return;
            if (inputs == null || inputs.Length != 2 || inputs[0] == null || inputs[1] == null ||
                inputs[0].Player != 0 || inputs[1].Player != 1)
                throw new ArgumentException("An ordered input for each player is required.", nameof(inputs));
            if (Tick > 0 && Tick % TickRate == 0)
                for (int player = 0; player < 2; player++)
                    _energy[player] = Math.Min(MaxEnergy, _energy[player] + (Tick >= 90 * TickRate ? 2 : 1));
            foreach (SiegeInput input in inputs) ApplyInput(input);
            CollectEntities();
            AdvanceCombat();
            int blue = BaseHealth(0), red = BaseHealth(1);
            Tick++;
            if (blue <= 0 || red <= 0)
                Winner = blue <= 0 && red <= 0 ? 2 : blue <= 0 ? 1 : 0;
            else if (Tick >= DurationTicks)
                Winner = blue == red ? 2 : blue > red ? 0 : 1;
            RefreshChecksum();
        }

        private void ApplyInput(SiegeInput input)
        {
            if (input.Unit == 0) return;
            if (input.Sequence <= _lastSequence[input.Player]) return;
            _lastSequence[input.Player] = input.Sequence;
            int player = input.Player, cost = Cost(input.Unit);
            if (input.Unit < 1 || input.Unit > 3 || input.Lane < 0 || input.Lane > 1 ||
                _energy[player] < cost || (input.Unit == 3 && Tick < _skillReady[player])) return;
            if (input.Unit != 3 && _world.EntityCount >= 130) return;
            _energy[player] -= cost;
            if (input.Unit == 3)
            {
                _skillReady[player] = Tick + SkillCooldownTicks;
                Pulse(player, input.Lane);
            }
            else Spawn(player, input.Unit, input.Lane);
            LastAction = (player == 0 ? "蓝方" : "红方") + " / " +
                (input.Lane == 0 ? "上路" : "下路") + " / " +
                (input.Unit == 1 ? "护卫" : input.Unit == 2 ? "弓手" : "冲击");
        }

        private SimEntity Spawn(int player, int kind, int lane)
        {
            SimEntity entity = _world.CreateEntity(typeof(SimTransformComponent),
                typeof(SimHealthComponent), typeof(SimTeamComponent),
                typeof(SimIdentityComponent), typeof(SimAttackComponent));
            _world.SetComponent(entity, new SimTransformComponent
            {
                PosX = LogicInt.FromInt(player == 0 ? (kind == 0 ? 1 : 3) : (kind == 0 ? 29 : 27)),
                PosY = LogicInt.FromInt(lane), GridY = lane
            });
            _world.SetComponent(entity, SimHealthComponent.Create(kind == 0 ? BaseHP : kind == 1 ? 90 : 45));
            _world.SetComponent(entity, SimTeamComponent.Create(player + 1));
            _world.SetComponent(entity, new SimIdentityComponent { ArchetypeId = kind, OwnerPlayerId = player });
            _world.SetComponent(entity, new SimAttackComponent
            {
                Damage = kind == 1 ? 14 : 10,
                Range = LogicInt.FromRaw(kind == 0 ? 40000 : kind == 1 ? 8500 : 32000),
                CooldownTicks = kind == 0 ? 15 : 20, LastAttackTick = -100
            });
            return entity;
        }

        /// <summary>Return sorted copies; Unity rendering never owns simulation state.</summary>
        public void CopyUnits(List<SiegeUnitView> destination)
        {
            destination.Clear();
            _world.ForEachEntities<SimIdentityComponent>(entity =>
            {
                var identity = _world.GetComponentRef<SimIdentityComponent>(entity);
                var health = _world.GetComponentRef<SimHealthComponent>(entity);
                var position = _world.GetComponentRef<SimTransformComponent>(entity);
                var attack = _world.GetComponentRef<SimAttackComponent>(entity);
                destination.Add(new SiegeUnitView(entity.Id, identity.OwnerPlayerId,
                    identity.ArchetypeId, position.GridY, health.CurrentHP,
                    health.MaxHP, attack.LastAttackTick, position.PosX));
            });
        }

        private void CollectEntities()
        {
            _entities.Clear();
            _world.ForEachEntities<SimIdentityComponent>(entity => _entities.Add(entity));
        }

        private void RefreshChecksum()
        {
            var hash = new DeterministicHash64(DeterministicHash64.OffsetBasis);
            hash.AddInt32(_seed); hash.AddInt32(Tick); hash.AddInt32(Winner);
            for (int player = 0; player < 2; player++)
            {
                hash.AddInt32(_energy[player]); hash.AddInt32(_skillReady[player]);
                hash.AddInt32(_lastSequence[player]);
            }
            hash.AddUInt64(_world.ComputeChecksum());
            Checksum = hash.Value;
        }
    }
}
