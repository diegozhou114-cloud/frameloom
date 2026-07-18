using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class SimBattleSession : ISimBattleSession
    {
        private readonly SimWorld _world;
        private readonly DeterministicContentManifest _manifest;
        private readonly ISimLogger _logger;
        private SimBattleState _state;
        private PresentationFrame _currentFrame;
        private BattleInit _init;
        private bool _disposed;

        internal SimBattleSession(SimWorld world, DeterministicContentManifest manifest, ISimLogger logger)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _logger = logger;
            _currentFrame = EmptyFrame();
            _state = new SimBattleState(SessionLifecycle.Created, SimCombatPhase.Idle,
                -1, 0, 0, 0, _currentFrame, null);
        }

        public SimBattleState State => _state;

        public StartResult Start(BattleInit init)
        {
            if (_disposed) return new StartResult(StartCode.AlreadyStarted, _currentFrame);
            if (_init != null) return new StartResult(StartCode.AlreadyStarted, _currentFrame);
            StartCode validation = Validate(init);
            if (validation != StartCode.Accepted)
                return new StartResult(validation, _currentFrame);

            foreach (var spec in init.InitialEntities)
                CreateInitialEntity(spec);

            _init = init;
            _currentFrame = BuildFrame(true, -1, 0, 0);
            _state = new SimBattleState(SessionLifecycle.Running, SimCombatPhase.Deploying,
                -1, 0, 0, _world.ComputeChecksum(), _currentFrame, null);
            _logger?.Log($"[SimBattleSession] Started match {init.MatchId}.");
            return new StartResult(StartCode.Accepted, _currentFrame);
        }

        public CommandSubmitResult Submit(ISimCommand command)
        {
            if (_disposed || _state.Lifecycle != SessionLifecycle.Running)
                return new CommandSubmitResult(CommandDisposition.WrongPhase);
            return new CommandSubmitResult(command == null
                ? CommandDisposition.InvalidPayload
                : CommandDisposition.Accepted);
        }

        public TickResult Tick()
        {
            if (_disposed)
                return CreateTickResult(TickCode.Disposed);
            if (_state.Lifecycle != SessionLifecycle.Running)
                return CreateTickResult(TickCode.NotRunning);

            int completedTick = _state.NextTick;
            int nextTick = completedTick + 1;
            long revision = _state.TimelineRevision;
            ulong checksum = _world.ComputeChecksum();
            _currentFrame = BuildFrame(false, completedTick, nextTick, revision);
            _state = new SimBattleState(SessionLifecycle.Running, _state.Phase,
                completedTick, nextTick, revision, checksum,
                BuildFrame(true, completedTick, nextTick, revision), null);
            return new TickResult(TickCode.Advanced, completedTick, nextTick, revision,
                Array.Empty<ISimCommand>(), Array.Empty<CommandExecutionResult>(),
                Array.Empty<SimBattleEvent>(), _currentFrame, checksum, null);
        }

        public SimSnapshot CaptureSnapshot()
        {
            byte[] payload = Array.Empty<byte>();
            byte[] payloadHash;
            using (SHA256 sha256 = SHA256.Create())
                payloadHash = sha256.ComputeHash(payload);
            return new SimSnapshot(
                _init?.MatchId ?? string.Empty,
                _init?.SessionId ?? string.Empty,
                _init?.SchemaVersion ?? SimComponentSchema.Version,
                _manifest.ContentHash,
                _state.CompletedTick,
                _state.NextTick,
                _state.TimelineRevision,
                payload.Length,
                _state.Checksum,
                payload,
                payloadHash);
        }

        public SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot)
        {
            if (_disposed) return new SnapshotRestoreResult(SnapshotRestoreCode.Disposed, _currentFrame);
            return new SnapshotRestoreResult(SnapshotRestoreCode.SnapshotUnavailable, _currentFrame);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _state = new SimBattleState(SessionLifecycle.Disposed, _state.Phase,
                _state.CompletedTick, _state.NextTick, _state.TimelineRevision,
                _state.Checksum, _currentFrame, _state.Result);
        }

        private StartCode Validate(BattleInit init)
        {
            if (init == null) return StartCode.InvalidMatch;
            if (init.ProtocolVersion != 1) return StartCode.UnsupportedProtocol;
            if (init.SchemaVersion != SimComponentSchema.Version) return StartCode.UnsupportedSchema;
            if (string.IsNullOrEmpty(init.MatchId) || string.IsNullOrEmpty(init.SessionId) || init.TickRate != 20)
                return StartCode.InvalidMatch;
            if (!init.ContentHash.SequenceEqual(_manifest.ContentHash)) return StartCode.InvalidContent;
            if (!Enum.IsDefined(typeof(SimCombatMode), init.Mode)) return StartCode.InvalidMode;

            var players = new HashSet<int>();
            foreach (var player in init.Players)
                if (!players.Add(player.PlayerId) || string.IsNullOrEmpty(player.AccountId))
                    return StartCode.InvalidPlayer;

            var owners = new HashSet<int>(players);
            foreach (var entity in init.InitialEntities)
            {
                if (_manifest.GetArchetype(entity.ArchetypeId) == null ||
                    !owners.Contains(entity.OwnerPlayerId) ||
                    entity.GridX < 0 || entity.GridY < 0 ||
                    (entity.TeamId != SimTeam.Attacker && entity.TeamId != SimTeam.Defender))
                    return StartCode.InvalidEntity;
            }
            return StartCode.Accepted;
        }

        private void CreateInitialEntity(InitialEntitySpec spec)
        {
            SimArchetypeDefinition definition = _manifest.GetArchetype(spec.ArchetypeId);
            var componentTypes = new List<Type>
            {
                typeof(SimTransformComponent),
                typeof(SimHealthComponent),
                typeof(SimTeamComponent),
                typeof(SimIdentityComponent),
            };
            if (definition.SpeedRaw != 0) componentTypes.Add(typeof(SimMovementComponent));
            if (definition.Damage > 0)
            {
                componentTypes.Add(typeof(SimAttackComponent));
                componentTypes.Add(typeof(SimTargetComponent));
            }

            SimEntity entity = _world.CreateEntity(componentTypes.ToArray());
            _world.SetComponent(entity, SimTransformComponent.Create(spec.GridX, spec.GridY, LogicInt.FromInt(1)));
            _world.SetComponent(entity, SimHealthComponent.Create(definition.MaxHealth));
            _world.SetComponent(entity, SimTeamComponent.Create(spec.TeamId));
            _world.SetComponent(entity, new SimIdentityComponent
            {
                ArchetypeId = spec.ArchetypeId,
                OwnerPlayerId = spec.OwnerPlayerId,
            });
            if (definition.SpeedRaw != 0)
                _world.SetComponent(entity, new SimMovementComponent
                {
                    Speed = LogicInt.FromRaw(definition.SpeedRaw),
                    CellSize = LogicInt.FromInt(1),
                });
            if (definition.Damage > 0)
            {
                _world.SetComponent(entity, new SimAttackComponent
                {
                    Damage = definition.Damage,
                    Range = LogicInt.FromRaw(definition.RangeRaw),
                    CooldownTicks = definition.CooldownTicks,
                    LastAttackTick = -99999,
                });
                _world.SetComponent(entity, SimTargetComponent.Create());
            }
        }

        private TickResult CreateTickResult(TickCode code)
        {
            return new TickResult(code, _state.CompletedTick, _state.NextTick,
                _state.TimelineRevision, Array.Empty<ISimCommand>(),
                Array.Empty<CommandExecutionResult>(), Array.Empty<SimBattleEvent>(),
                _currentFrame, _state.Checksum, _state.Result);
        }

        private PresentationFrame BuildFrame(bool isFull, int completedTick, int nextTick, long revision)
        {
            var states = new List<EntityPresentationState>();
            _world.ForEachComponent<SimIdentityComponent>((entity, identity) =>
            {
                _world.TryGetComponent(entity, out SimTransformComponent transform);
                _world.TryGetComponent(entity, out SimHealthComponent health);
                _world.TryGetComponent(entity, out SimTeamComponent team);
                states.Add(new EntityPresentationState(entity.Id, entity.Generation,
                    identity.ArchetypeId, identity.OwnerPlayerId, team.TeamId,
                    transform.GridX, transform.GridY, transform.PosX.RawValue,
                    transform.PosY.RawValue, 0, health.CurrentHP));
            });
            states.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
            return new PresentationFrame(isFull, completedTick, nextTick, revision,
                states.ToArray(), Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationKey>());
        }

        private static PresentationFrame EmptyFrame() =>
            new PresentationFrame(true, -1, 0, 0,
                Array.Empty<EntityPresentationState>(), Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationState>(), Array.Empty<EntityPresentationKey>());
    }
}
