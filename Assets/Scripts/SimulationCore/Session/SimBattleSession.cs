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
        private readonly CommandBuffer _commandBuffer;
        private readonly SimSystemGroup _systems;
        private readonly SimDeathSystem _deathSystem;
        private readonly Dictionary<int, int> _lastSequenceByPlayer = new Dictionary<int, int>();
        private SimBattleState _state;
        private PresentationFrame _currentFrame;
        private BattleInit _init;
        private bool _disposed;

        internal SimBattleSession(SimWorld world, DeterministicContentManifest manifest, ISimLogger logger)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _logger = logger;
            _commandBuffer = new CommandBuffer();
            _systems = SimCombatBootstrap.Build(world);
            _deathSystem = _systems.Find<SimDeathSystem>();
            _currentFrame = EmptyFrame();
            _state = new SimBattleState(SessionLifecycle.Created, SimCombatPhase.Idle,
                -1, 0, 0, 0, _currentFrame, null);
        }

        public SimBattleState State => _state;
        public ulong LastChecksum => _state.Checksum;

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
            _lastSequenceByPlayer.Clear();
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
            if (command == null) return new CommandSubmitResult(CommandDisposition.InvalidPayload);
            if (!IsPlayer(command.PlayerId))
                return new CommandSubmitResult(CommandDisposition.UnknownPlayer);
            if (command.Tick < _state.NextTick)
                return new CommandSubmitResult(CommandDisposition.LateTick);
            if (command.Tick > _state.NextTick + 64)
                return new CommandSubmitResult(CommandDisposition.FutureWindowExceeded);
            if (_lastSequenceByPlayer.TryGetValue(command.PlayerId, out int previousSequence))
            {
                if (command.Sequence == previousSequence)
                    return new CommandSubmitResult(CommandDisposition.Duplicate);
                if (command.Sequence < previousSequence)
                    return new CommandSubmitResult(CommandDisposition.NonMonotonicSequence);
            }
            _lastSequenceByPlayer[command.PlayerId] = command.Sequence;
            _commandBuffer.AddCommand(command);
            return new CommandSubmitResult(CommandDisposition.Accepted);
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
            List<ISimCommand> executed = _commandBuffer.Drain(completedTick);
            var outcomes = new List<CommandExecutionResult>(executed.Count);
            foreach (ISimCommand command in executed)
                outcomes.Add(ExecuteCommand(command));
            _systems.UpdateAll();
            SimCombatPhase phase = _state.Phase;
            SessionLifecycle lifecycle = _state.Lifecycle;
            if (_deathSystem.Winner.HasValue)
            {
                phase = SimCombatPhase.Ended;
                lifecycle = SessionLifecycle.Ended;
            }
            ulong checksum = _world.ComputeChecksum();
            _currentFrame = BuildFrame(false, completedTick, nextTick, revision);
            _state = new SimBattleState(lifecycle, phase,
                completedTick, nextTick, revision, checksum,
                BuildFrame(true, completedTick, nextTick, revision), null);
            return new TickResult(lifecycle == SessionLifecycle.Ended ? TickCode.Ended : TickCode.Advanced,
                completedTick, nextTick, revision,
                executed.ToArray(), outcomes.ToArray(),
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

        private SimEntity CreateInitialEntity(InitialEntitySpec spec)
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
            return entity;
        }

        private CommandExecutionResult ExecuteCommand(ISimCommand command)
        {
            switch (command)
            {
                case DeployCmd deploy:
                    if (deploy.GridX < 0 || deploy.GridY < 0)
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload, -1, -1);
                    int team = deploy.PlayerId == 1 ? SimTeam.Defender : SimTeam.Attacker;
                    SimArchetypeDefinition definition = _manifest.GetArchetype(deploy.UnitTypeId);
                    if (definition == null)
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload, -1, -1);
                    SimEntity deployed = CreateInitialEntity(new InitialEntitySpec(
                        deploy.UnitTypeId, deploy.GridX, deploy.GridY, team, deploy.PlayerId));
                    return new CommandExecutionResult(command, CommandDisposition.Accepted,
                        deployed.Id, deployed.Generation);
                case MoveCmd move:
                    if (!TryFindEntity(move.EntityId, out SimEntity mover) ||
                        !_world.TryGetComponent(mover, out SimMovementComponent movement))
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload, -1, -1);
                    if (!_world.TryGetComponent(mover, out SimIdentityComponent moveIdentity) ||
                        moveIdentity.OwnerPlayerId != move.PlayerId)
                        return new CommandExecutionResult(command, CommandDisposition.UnauthorizedEntity,
                            mover.Id, mover.Generation);
                    if (move.TargetX < 0 || move.TargetY < 0)
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload,
                            mover.Id, mover.Generation);
                    movement.Path = BuildPath(mover, move.TargetX, move.TargetY);
                    movement.PathIndex = 0;
                    movement.TargetGridX = move.TargetX;
                    movement.TargetGridY = move.TargetY;
                    _world.SetComponent(mover, movement);
                    return new CommandExecutionResult(command, CommandDisposition.Accepted,
                        mover.Id, mover.Generation);
                case SkillCmd skill:
                    if (!TryFindEntity(skill.CasterId, out SimEntity caster) ||
                        !_world.TryGetComponent(caster, out SimIdentityComponent skillIdentity))
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload, -1, -1);
                    if (skillIdentity.OwnerPlayerId != skill.PlayerId)
                        return new CommandExecutionResult(command, CommandDisposition.UnauthorizedEntity,
                            caster.Id, caster.Generation);
                    if (skill.SkillId < 0 || skill.TargetX < -1 || skill.TargetY < -1)
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload,
                            caster.Id, caster.Generation);
                    return new CommandExecutionResult(command, CommandDisposition.Accepted,
                        caster.Id, caster.Generation);
                default:
                    return new CommandExecutionResult(command, CommandDisposition.InvalidPayload, -1, -1);
            }
        }

        private bool TryFindEntity(int id, out SimEntity found)
        {
            SimEntity candidate = SimEntity.Invalid;
            _world.ForEachComponent<SimIdentityComponent>((entity, identity) =>
            {
                if (entity.Id == id) candidate = entity;
            });
            found = candidate;
            return found.IsValid;
        }

        private bool IsPlayer(int playerId)
        {
            if (_init == null) return false;
            foreach (SimPlayerIdentity player in _init.Players)
                if (player.PlayerId == playerId) return true;
            return false;
        }

        private List<int> BuildPath(SimEntity entity, int targetX, int targetY)
        {
            var path = new List<int>();
            if (!_world.TryGetComponent(entity, out SimTransformComponent transform)) return path;
            int x = transform.GridX;
            int y = transform.GridY;
            int steps = Math.Max(Math.Abs(targetX - x), Math.Abs(targetY - y));
            for (int step = 0; step < steps; step++)
            {
                if (x != targetX) x += targetX > x ? 1 : -1;
                if (y != targetY) y += targetY > y ? 1 : -1;
                path.Add((x << 16) | (y & 0xFFFF));
            }
            return path;
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
