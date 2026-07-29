using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class SimBattleSession : ISimBattleSession
    {
        private const int MaxWaypointCoordinate = ushort.MaxValue;
        private readonly SimWorld _world;
        private readonly DeterministicContentManifest _manifest;
        private readonly ISimLogger _logger;
        private readonly CommandBuffer _commandBuffer;
        private readonly SimSystemGroup _systems;
        private readonly SimDeathSystem _deathSystem;
        private readonly SimDamageSystem _damageSystem;
        private readonly SimAttackSystem _attackSystem;
        private readonly Dictionary<int, int> _lastSequenceByPlayer = new Dictionary<int, int>();
        private const int BasicSkillId = 1;
        private const int BasicSkillDamage = 30;
        private const int MaxSnapshotBytes = 8 * 1024 * 1024;
        private SimBattleState _state;
        private PresentationFrame _currentFrame;
        private BattleInit _init;
        private SimModeProfile _modeProfile;
        private int _deployedCount;
        private SimTerminalOutcome _terminalOutcome;
        private int _terminalFinalTick = -1;
        private int _sealedTick = -1;
        private bool _disposed;

        internal SimBattleSession(SimWorld world, DeterministicContentManifest manifest, ISimLogger logger)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _logger = logger;
            _commandBuffer = new CommandBuffer();
            _systems = SimCombatBootstrap.Build(world);
            _deathSystem = _systems.Find<SimDeathSystem>();
            _damageSystem = _systems.Find<SimDamageSystem>();
            _attackSystem = _systems.Find<SimAttackSystem>();
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
            _modeProfile = _manifest.GetMode(init.Mode);
            _deployedCount = 0;
            _terminalOutcome = null;
            _terminalFinalTick = -1;
            _lastSequenceByPlayer.Clear();
            _currentFrame = BuildFullFrame(-1, 0, 0);
            SimCombatPhase initialPhase = _modeProfile.ManualDeployment
                ? SimCombatPhase.Deploying : SimCombatPhase.Battling;
            _state = new SimBattleState(SessionLifecycle.Running, initialPhase,
                -1, 0, 0, 0, _currentFrame, null);
            RefreshChecksum();
            _logger?.Log($"[SimBattleSession] Started match {init.MatchId}.");
            return new StartResult(StartCode.Accepted, _currentFrame);
        }

        public CommandSubmitResult Submit(ISimCommand command)
        {
            if (_disposed || _state.Lifecycle != SessionLifecycle.Running)
                return new CommandSubmitResult(CommandDisposition.WrongPhase);
            if (command == null) return new CommandSubmitResult(CommandDisposition.InvalidPayload);
            if (command.Sequence < 0 || command.Sequence == int.MaxValue)
                return new CommandSubmitResult(CommandDisposition.InvalidPayload);
            if (!IsPlayer(command.PlayerId))
                return new CommandSubmitResult(CommandDisposition.UnknownPlayer);
            if (command.Tick < _state.NextTick)
                return new CommandSubmitResult(CommandDisposition.LateTick);
            if (command.Tick == _sealedTick)
                return new CommandSubmitResult(CommandDisposition.LateTick);
            if (command.Tick > _state.NextTick + 64)
                return new CommandSubmitResult(CommandDisposition.FutureWindowExceeded);
            CommandDisposition validation = ValidateCommand(command);
            if (validation != CommandDisposition.Accepted)
                return new CommandSubmitResult(validation);
            if (_lastSequenceByPlayer.TryGetValue(command.PlayerId, out int previousSequence))
            {
                if (command.Sequence == previousSequence)
                    return new CommandSubmitResult(CommandDisposition.Duplicate);
                if (command.Sequence < previousSequence)
                    return new CommandSubmitResult(CommandDisposition.NonMonotonicSequence);
            }
            _lastSequenceByPlayer[command.PlayerId] = command.Sequence;
            _commandBuffer.AddCommand(command);
            RefreshChecksum();
            return new CommandSubmitResult(CommandDisposition.Accepted);
        }

        internal void SealTick(int tick)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SimBattleSession));
            if (_state.Lifecycle != SessionLifecycle.Running)
                throw new InvalidOperationException("Only running sessions can seal ticks.");
            if (tick != _state.NextTick)
                throw new ArgumentOutOfRangeException(nameof(tick),
                    "Only the next simulation tick can be sealed.");
            _sealedTick = tick;
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
            var acceptedCommands = new List<ISimCommand>(executed.Count);
            var outcomes = new List<CommandExecutionResult>(executed.Count);
            foreach (ISimCommand command in executed)
            {
                CommandExecutionResult outcome = ExecuteCommand(command);
                outcomes.Add(outcome);
                if (outcome.Disposition == CommandDisposition.Accepted)
                    acceptedCommands.Add(command);
            }
            SimCombatPhase phase = _state.Phase;
            SessionLifecycle lifecycle = _state.Lifecycle;
            if (phase == SimCombatPhase.Deploying && _modeProfile.ManualDeployment &&
                nextTick >= _modeProfile.DeployDurationTicks)
                phase = SimCombatPhase.Battling;

            SimBattleEvent[] tickEvents = Array.Empty<SimBattleEvent>();
            bool systemsUpdated = false;
            if (phase == SimCombatPhase.Battling)
            {
                _systems.UpdateAll();
                systemsUpdated = true;
                int futureWaves = _modeProfile.WaveTicks.Count(tick => tick > completedTick);
                bool timedOut = nextTick >= _modeProfile.TimeoutTicks;
                SimTerminalOutcome terminalOutcome = new SimModeRules(_modeProfile).Evaluate(
                    new SimModeEvaluationInput(completedTick, _deathSystem.AttackerAlive,
                        _deathSystem.DefenderAlive, _deathSystem.DestructionBasisPoints,
                        futureWaves, timedOut, true));
                if (terminalOutcome != null)
                {
                    phase = SimCombatPhase.Ended;
                    lifecycle = SessionLifecycle.Ended;
                    _terminalOutcome = terminalOutcome;
                    _terminalFinalTick = completedTick;
                }
            }
            PresentationFrame fullFrame = BuildFullFrame(completedTick, nextTick, revision);
            _currentFrame = BuildDeltaFrame(_state.Presentation, fullFrame);
            if (systemsUpdated)
                tickEvents = BuildTickEvents(_currentFrame.Destroyed);
            _state = new SimBattleState(lifecycle, phase,
                completedTick, nextTick, revision, 0,
                fullFrame, null);
            ulong checksum = RefreshChecksum();
            SimBattleResult result = null;
            if (_terminalOutcome != null)
            {
                result = SimBattleResult.FromOutcome(
                    _init.MatchId, _terminalOutcome, _terminalFinalTick, checksum);
                _state = new SimBattleState(lifecycle, phase,
                    completedTick, nextTick, revision, checksum,
                    _state.Presentation, result);
            }
            _sealedTick = -1;
            return new TickResult(lifecycle == SessionLifecycle.Ended ? TickCode.Ended : TickCode.Advanced,
                completedTick, nextTick, revision,
                acceptedCommands.ToArray(), outcomes.ToArray(),
                tickEvents, _currentFrame, checksum, result);
        }

        public SimSnapshot CaptureSnapshot()
        {
            if (_init == null) throw new InvalidOperationException("Session has not started.");
            SessionSnapshotData data = CreateSnapshotData();
            byte[] payload = SessionSnapshotCodec.Encode(data);
            if (payload.Length > MaxSnapshotBytes)
                throw new InvalidOperationException("Snapshot exceeds the maximum payload size.");
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
            if (_init == null || snapshot == null)
                return new SnapshotRestoreResult(SnapshotRestoreCode.SnapshotUnavailable, _currentFrame);
            if (!string.Equals(snapshot.MatchId, _init.MatchId, StringComparison.Ordinal))
                return new SnapshotRestoreResult(SnapshotRestoreCode.ForeignMatch, _currentFrame);
            if (!string.Equals(snapshot.SessionId, _init.SessionId, StringComparison.Ordinal))
                return new SnapshotRestoreResult(SnapshotRestoreCode.ForeignSession, _currentFrame);
            if (snapshot.SchemaVersion != _init.SchemaVersion)
                return new SnapshotRestoreResult(SnapshotRestoreCode.UnsupportedSchema, _currentFrame);
            if (!snapshot.ContentHash.SequenceEqual(_manifest.ContentHash))
                return new SnapshotRestoreResult(SnapshotRestoreCode.ContentMismatch, _currentFrame);
            if (snapshot.TimelineRevision < _state.TimelineRevision)
                return new SnapshotRestoreResult(SnapshotRestoreCode.StaleRevision, _currentFrame);
            if (snapshot.NextTick != snapshot.CompletedTick + 1)
                return new SnapshotRestoreResult(SnapshotRestoreCode.InvalidTick, _currentFrame);
            byte[] payload = snapshot.Payload;
            if (snapshot.UncompressedLength < 0 || snapshot.UncompressedLength > MaxSnapshotBytes ||
                payload.Length > MaxSnapshotBytes)
                return new SnapshotRestoreResult(SnapshotRestoreCode.Oversized, _currentFrame);
            if (snapshot.UncompressedLength != payload.Length)
                return new SnapshotRestoreResult(SnapshotRestoreCode.CorruptPayload, _currentFrame);
            byte[] expectedHash;
            using (SHA256 sha256 = SHA256.Create()) expectedHash = sha256.ComputeHash(payload);
            if (snapshot.PayloadSha256.Length != expectedHash.Length ||
                !snapshot.PayloadSha256.SequenceEqual(expectedHash))
                return new SnapshotRestoreResult(SnapshotRestoreCode.CorruptPayload, _currentFrame);

            SessionSnapshotData data;
            SimWorld detachedWorld;
            try
            {
                data = SessionSnapshotCodec.Decode(payload);
                if (data.CompletedTick != snapshot.CompletedTick || data.NextTick != snapshot.NextTick ||
                    data.Lifecycle == SessionLifecycle.Created || data.Lifecycle == SessionLifecycle.Disposed)
                    return new SnapshotRestoreResult(SnapshotRestoreCode.InvalidTick, _currentFrame);
                bool terminalState = data.Lifecycle == SessionLifecycle.Ended &&
                    data.Phase == SimCombatPhase.Ended && data.TerminalOutcome != null &&
                    data.TerminalFinalTick == data.CompletedTick;
                if (terminalState != (data.TerminalOutcome != null) ||
                    (data.Lifecycle == SessionLifecycle.Ended) != terminalState ||
                    (data.Lifecycle == SessionLifecycle.Running &&
                        data.Phase != SimCombatPhase.Deploying &&
                        data.Phase != SimCombatPhase.Battling) ||
                    (data.Phase == SimCombatPhase.Deploying && !_modeProfile.ManualDeployment) ||
                    (data.Phase == SimCombatPhase.Deploying &&
                        data.NextTick >= _modeProfile.DeployDurationTicks) ||
                    (data.Lifecycle == SessionLifecycle.Running &&
                        ShouldHaveTerminalOutcome(data)) ||
                    data.AttackSystemTick > data.NextTick ||
                    data.DeployedCount > _modeProfile.MaxDeployCount ||
                    !HasValidRestoredCommandState(data))
                    return new SnapshotRestoreResult(SnapshotRestoreCode.CorruptPayload, _currentFrame);
                int capacity = Math.Max(8, data.WorldSnapshot.Generations?.Length ?? 0);
                detachedWorld = new SimWorld(SimComponentSchema.CreateRegistry(), capacity);
                detachedWorld.RestoreFromFull(data.WorldSnapshot);
                if ((data.WorldSnapshot.PendingDamageEvents?.Count ?? 0) != 0 ||
                    !HasValidWorldCoordinates(detachedWorld) ||
                    !HasValidRestoredEntityIdentities(data, detachedWorld) ||
                    !HasValidRestoredDeathState(data, detachedWorld) ||
                    !HasValidRestoredTerminalOutcome(data) ||
                    !HasValidRestoredLiveCommandOwnership(data, detachedWorld) ||
                    detachedWorld.ComputeChecksum() != data.WorldSnapshot.Checksum ||
                    ComputeChecksum(data, detachedWorld) != snapshot.StateChecksum)
                    return new SnapshotRestoreResult(SnapshotRestoreCode.CorruptPayload, _currentFrame);
            }
            catch (Exception exception) when (exception is FormatException ||
                exception is ArgumentException || exception is InvalidOperationException)
            {
                return new SnapshotRestoreResult(SnapshotRestoreCode.CorruptPayload, _currentFrame);
            }

            _world.RestoreFromFull(data.WorldSnapshot);
            _commandBuffer.RestorePendingCommands(data.PendingCommands);
            _lastSequenceByPlayer.Clear();
            foreach (var pair in data.LastSequenceByPlayer)
                _lastSequenceByPlayer.Add(pair.Key, pair.Value);
            _attackSystem.RestoreTick(data.AttackSystemTick);
            _deathSystem.RestoreState(data.AttackerAlive, data.DefenderAlive,
                data.AttackerDead, data.DefenderDead, data.Winner);
            _deployedCount = data.DeployedCount;
            _terminalOutcome = data.TerminalOutcome;
            _terminalFinalTick = data.TerminalFinalTick;
            long revision = _state.TimelineRevision + 1;
            _currentFrame = BuildFullFrame(data.CompletedTick, data.NextTick, revision);
            _state = new SimBattleState(data.Lifecycle, data.Phase,
                data.CompletedTick, data.NextTick, revision, 0, _currentFrame, null);
            ulong restoredChecksum = RefreshChecksum();
            if (restoredChecksum != snapshot.StateChecksum)
                throw new InvalidOperationException("Validated snapshot checksum changed during atomic apply.");
            if (_terminalOutcome != null)
            {
                SimBattleResult result = SimBattleResult.FromOutcome(
                    _init.MatchId, _terminalOutcome, _terminalFinalTick, restoredChecksum);
                _state = new SimBattleState(data.Lifecycle, data.Phase,
                    data.CompletedTick, data.NextTick, revision, restoredChecksum,
                    _currentFrame, result);
            }
            _sealedTick = -1;
            return new SnapshotRestoreResult(SnapshotRestoreCode.Restored, _currentFrame);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _state = new SimBattleState(SessionLifecycle.Disposed, _state.Phase,
                _state.CompletedTick, _state.NextTick, _state.TimelineRevision,
                0, _currentFrame, _state.Result);
            RefreshChecksum();
        }

        private StartCode Validate(BattleInit init)
        {
            if (init == null) return StartCode.InvalidMatch;
            if (init.ProtocolVersion != 1) return StartCode.UnsupportedProtocol;
            if (init.SchemaVersion != SimComponentSchema.Version) return StartCode.UnsupportedSchema;
            if (string.IsNullOrEmpty(init.MatchId) || string.IsNullOrEmpty(init.SessionId) ||
                init.TickRate != 20 || init.InputDeadlineTicks < 0)
                return StartCode.InvalidMatch;
            if (!init.ContentHash.SequenceEqual(_manifest.ContentHash)) return StartCode.InvalidContent;
            if (!Enum.IsDefined(typeof(SimCombatMode), init.Mode)) return StartCode.InvalidMode;
            if (_manifest.GetMode(init.Mode) == null) return StartCode.InvalidMode;

            var players = new HashSet<int>();
            var accounts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var player in init.Players)
                if (!players.Add(player.PlayerId) || string.IsNullOrEmpty(player.AccountId) ||
                    !accounts.Add(player.AccountId))
                    return StartCode.InvalidPlayer;
            if (players.Count == 0) return StartCode.InvalidPlayer;

            var owners = new HashSet<int>(players);
            var occupiedCells = new HashSet<(int X, int Y)>();
            int attackerPlayerId = init.Players[0].PlayerId;
            foreach (var entity in init.InitialEntities)
            {
                if (_manifest.GetArchetype(entity.ArchetypeId) == null ||
                    !owners.Contains(entity.OwnerPlayerId) ||
                    !IsWaypointCoordinate(entity.GridX, entity.GridY) ||
                    (entity.TeamId != SimTeam.Attacker && entity.TeamId != SimTeam.Defender) ||
                    (entity.TeamId == SimTeam.Attacker) !=
                        (entity.OwnerPlayerId == attackerPlayerId) ||
                    !occupiedCells.Add((entity.GridX, entity.GridY)))
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
                    if (_state.Phase != SimCombatPhase.Deploying ||
                        !_modeProfile.ManualDeployment)
                        return new CommandExecutionResult(command,
                            CommandDisposition.WrongPhase, -1, -1);
                    if (_deployedCount >= _modeProfile.MaxDeployCount)
                        return new CommandExecutionResult(command,
                            CommandDisposition.InvalidPayload, -1, -1);
                    if (!IsAttacker(deploy.PlayerId))
                        return new CommandExecutionResult(command,
                            CommandDisposition.UnauthorizedEntity, -1, -1);
                    SimArchetypeDefinition definition = _manifest.GetArchetype(deploy.UnitTypeId);
                    if (definition == null || !IsWaypointCoordinate(deploy.GridX, deploy.GridY) ||
                        IsCellOccupied(deploy.GridX, deploy.GridY))
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload, -1, -1);
                    SimEntity deployed = CreateInitialEntity(new InitialEntitySpec(
                        deploy.UnitTypeId, deploy.GridX, deploy.GridY,
                        SimTeam.Attacker, deploy.PlayerId));
                    _deployedCount++;
                    return new CommandExecutionResult(command, CommandDisposition.Accepted,
                        deployed.Id, deployed.Generation);
                case EndDeploymentCmd:
                    if (_state.Phase != SimCombatPhase.Deploying || !_modeProfile.ManualDeployment)
                        return new CommandExecutionResult(command, CommandDisposition.WrongPhase, -1, -1);
                    if (!IsAttacker(command.PlayerId))
                        return new CommandExecutionResult(command,
                            CommandDisposition.UnauthorizedEntity, -1, -1);
                    _state = new SimBattleState(_state.Lifecycle, SimCombatPhase.Battling,
                        _state.CompletedTick, _state.NextTick, _state.TimelineRevision,
                        _state.Checksum, _state.Presentation, _state.Result);
                    return new CommandExecutionResult(command, CommandDisposition.Accepted, -1, -1);
                case MoveCmd move:
                    if (_state.Phase != SimCombatPhase.Battling)
                        return new CommandExecutionResult(command, CommandDisposition.WrongPhase, -1, -1);
                    if (!TryFindEntity(move.EntityId, out SimEntity mover) ||
                        !_world.TryGetComponent(mover, out SimMovementComponent movement))
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload, -1, -1);
                    if (!_world.TryGetComponent(mover, out SimIdentityComponent moveIdentity) ||
                        moveIdentity.OwnerPlayerId != move.PlayerId)
                        return new CommandExecutionResult(command, CommandDisposition.UnauthorizedEntity,
                            mover.Id, mover.Generation);
                    if (!IsWaypointCoordinate(move.TargetX, move.TargetY))
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
                    if (_state.Phase != SimCombatPhase.Battling)
                        return new CommandExecutionResult(command, CommandDisposition.WrongPhase, -1, -1);
                    if (!TryFindEntity(skill.CasterId, out SimEntity caster) ||
                        !_world.TryGetComponent(caster, out SimIdentityComponent skillIdentity))
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload, -1, -1);
                    if (skillIdentity.OwnerPlayerId != skill.PlayerId)
                        return new CommandExecutionResult(command, CommandDisposition.UnauthorizedEntity,
                            caster.Id, caster.Generation);
                    if (!_world.TryGetComponent(caster, out SimTeamComponent casterTeam))
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload,
                            caster.Id, caster.Generation);
                    if (skill.SkillId != BasicSkillId ||
                        !IsWaypointCoordinate(skill.TargetX, skill.TargetY))
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload,
                            caster.Id, caster.Generation);
                    SimEntity target = FindEnemyAt(skill.TargetX, skill.TargetY, casterTeam.TeamId);
                    if (!target.IsValid)
                        return new CommandExecutionResult(command, CommandDisposition.InvalidPayload,
                            caster.Id, caster.Generation);
                    _world.EnqueueEvent(new SimDamageEvent
                    {
                        Amount = BasicSkillDamage,
                        Attacker = caster,
                        Target = target,
                    });
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

        private bool IsAttacker(int playerId)
        {
            SimPlayerIdentity[] players = _init.Players;
            return players.Length > 0 && players[0].PlayerId == playerId;
        }

        private bool HasValidRestoredCommandState(SessionSnapshotData data)
        {
            foreach (KeyValuePair<int, int> sequence in data.LastSequenceByPlayer)
                if (!IsPlayer(sequence.Key) || sequence.Value < 0 ||
                    sequence.Value == int.MaxValue)
                    return false;

            var sequences = new HashSet<(int PlayerId, int Sequence)>();
            foreach (ISimCommand command in data.PendingCommands)
            {
                if (command == null || command.Sequence < 0 ||
                    command.Sequence == int.MaxValue || !IsPlayer(command.PlayerId) ||
                    command.Tick < data.NextTick ||
                    (long)command.Tick > (long)data.NextTick + 64 ||
                    !sequences.Add((command.PlayerId, command.Sequence)) ||
                    !data.LastSequenceByPlayer.TryGetValue(command.PlayerId,
                        out int acceptedSequence) || command.Sequence > acceptedSequence ||
                    !HasWaypointCoordinates(command) ||
                    !HasValidRestoredCommandPayload(command) ||
                    !HasAllocatedEntityReference(command, data.WorldSnapshot.NextId) ||
                    !HasValidRestoredCommandAuthorization(data, command))
                    return false;
            }
            return true;
        }

        private bool HasValidRestoredCommandPayload(ISimCommand command)
        {
            return command switch
            {
                DeployCmd deploy => _manifest.GetArchetype(deploy.UnitTypeId) != null,
                SkillCmd skill => skill.SkillId == BasicSkillId,
                _ => true,
            };
        }

        private static bool HasAllocatedEntityReference(ISimCommand command, int nextEntityId)
        {
            return command switch
            {
                MoveCmd move => move.EntityId >= 0 && move.EntityId < nextEntityId,
                SkillCmd skill => skill.CasterId >= 0 && skill.CasterId < nextEntityId,
                _ => true,
            };
        }

        private static bool HasValidRestoredLiveCommandOwnership(
            SessionSnapshotData data, SimWorld world)
        {
            foreach (ISimCommand command in data.PendingCommands)
            {
                int entityId = command switch
                {
                    MoveCmd move => move.EntityId,
                    SkillCmd skill => skill.CasterId,
                    _ => -1,
                };
                if (entityId < 0 || !TryGetLiveEntityOwner(world, entityId,
                        out int ownerPlayerId, out SimEntity entity))
                    continue;
                if (ownerPlayerId != command.PlayerId)
                    return false;
                if (command is MoveCmd &&
                    !world.TryGetComponent(entity, out SimMovementComponent _))
                    return false;
            }
            return true;
        }

        private static bool TryGetLiveEntityOwner(
            SimWorld world, int entityId, out int ownerPlayerId, out SimEntity found)
        {
            int? owner = null;
            SimEntity candidate = SimEntity.Invalid;
            world.ForEachComponent<SimIdentityComponent>((entity, identity) =>
            {
                if (entity.Id == entityId)
                {
                    owner = identity.OwnerPlayerId;
                    candidate = entity;
                }
            });
            found = candidate;
            ownerPlayerId = owner.GetValueOrDefault();
            return owner.HasValue;
        }

        private bool HasValidRestoredCommandAuthorization(
            SessionSnapshotData data, ISimCommand command)
        {
            if (data.Phase == SimCombatPhase.Deploying &&
                (command is MoveCmd || command is SkillCmd))
                return false;
            return command switch
            {
                DeployCmd deploy => _modeProfile.ManualDeployment && IsAttacker(deploy.PlayerId),
                EndDeploymentCmd endDeployment => _modeProfile.ManualDeployment &&
                    IsAttacker(endDeployment.PlayerId),
                _ => true,
            };
        }

        private CommandDisposition ValidateCommand(ISimCommand command)
        {
            switch (command)
            {
                case DeployCmd deploy:
                    if (_state.Phase != SimCombatPhase.Deploying ||
                        !_modeProfile.ManualDeployment)
                        return CommandDisposition.WrongPhase;
                    if (_deployedCount >= _modeProfile.MaxDeployCount)
                        return CommandDisposition.InvalidPayload;
                    if (!IsAttacker(deploy.PlayerId))
                        return CommandDisposition.UnauthorizedEntity;
                    return deploy.UnitTypeId > 0 &&
                        IsWaypointCoordinate(deploy.GridX, deploy.GridY) &&
                        _manifest.GetArchetype(deploy.UnitTypeId) != null &&
                        !IsCellOccupied(deploy.GridX, deploy.GridY)
                        ? CommandDisposition.Accepted : CommandDisposition.InvalidPayload;
                case EndDeploymentCmd:
                    if (_state.Phase != SimCombatPhase.Deploying || !_modeProfile.ManualDeployment)
                        return CommandDisposition.WrongPhase;
                    return IsAttacker(command.PlayerId)
                        ? CommandDisposition.Accepted : CommandDisposition.UnauthorizedEntity;
                case MoveCmd move:
                    if (_state.Phase != SimCombatPhase.Battling)
                        return CommandDisposition.WrongPhase;
                    if (!IsWaypointCoordinate(move.TargetX, move.TargetY) ||
                        !TryFindEntity(move.EntityId, out SimEntity mover) ||
                        !_world.TryGetComponent(mover, out SimMovementComponent _))
                        return CommandDisposition.InvalidPayload;
                    if (!_world.TryGetComponent(mover, out SimIdentityComponent moveIdentity) ||
                        moveIdentity.OwnerPlayerId != move.PlayerId)
                        return CommandDisposition.UnauthorizedEntity;
                    return CommandDisposition.Accepted;
                case SkillCmd skill:
                    if (_state.Phase != SimCombatPhase.Battling)
                        return CommandDisposition.WrongPhase;
                    if (skill.SkillId != BasicSkillId ||
                        !IsWaypointCoordinate(skill.TargetX, skill.TargetY) ||
                        !TryFindEntity(skill.CasterId, out SimEntity caster))
                        return CommandDisposition.InvalidPayload;
                    if (!_world.TryGetComponent(caster, out SimIdentityComponent skillIdentity) ||
                        skillIdentity.OwnerPlayerId != skill.PlayerId)
                        return CommandDisposition.UnauthorizedEntity;
                    if (!_world.TryGetComponent(caster, out SimTeamComponent casterTeam))
                        return CommandDisposition.InvalidPayload;
                    return FindEnemyAt(skill.TargetX, skill.TargetY, casterTeam.TeamId).IsValid
                        ? CommandDisposition.Accepted : CommandDisposition.InvalidPayload;
                default:
                    return CommandDisposition.InvalidPayload;
            }
        }

        private static bool IsWaypointCoordinate(int x, int y) =>
            x >= 0 && x <= MaxWaypointCoordinate && y >= 0 && y <= MaxWaypointCoordinate;

        private static bool HasWaypointCoordinates(ISimCommand command)
        {
            return command switch
            {
                DeployCmd deploy => IsWaypointCoordinate(deploy.GridX, deploy.GridY),
                MoveCmd move => IsWaypointCoordinate(move.TargetX, move.TargetY),
                SkillCmd skill => IsWaypointCoordinate(skill.TargetX, skill.TargetY),
                _ => true,
            };
        }

        private static bool HasValidWorldCoordinates(SimWorld world)
        {
            bool valid = true;
            world.ForEachComponent<SimTransformComponent>((entity, transform) =>
            {
                if (!IsWaypointCoordinate(transform.GridX, transform.GridY) ||
                    (world.TryGetComponent(entity, out SimMovementComponent movement)
                        ? !HasValidMovementPosition(transform, movement)
                        : !IsAtCellCenter(transform, LogicInt.FromInt(1))))
                    valid = false;
            });
            return valid;
        }

        private static bool HasValidMovementPosition(
            SimTransformComponent transform, SimMovementComponent movement)
        {
            LogicInt cellSize = movement.CellSize;
            if (!IsWaypointCoordinate(movement.TargetGridX, movement.TargetGridY))
                return false;
            LogicInt halfCell = cellSize / LogicInt.FromInt(2);
            LogicInt originX = LogicInt.FromInt(transform.GridX) * cellSize + halfCell;
            LogicInt originY = LogicInt.FromInt(transform.GridY) * cellSize + halfCell;
            if (!movement.HasPath)
                return IsAtCellCenter(transform, cellSize);

            (int x, int y)? next = movement.NextWaypoint;
            if (!next.HasValue ||
                Math.Abs(next.Value.x - transform.GridX) > 1 ||
                Math.Abs(next.Value.y - transform.GridY) > 1 ||
                (next.Value.x == transform.GridX && next.Value.y == transform.GridY))
                return false;

            LogicInt destinationX = LogicInt.FromInt(next.Value.x) * cellSize + halfCell;
            LogicInt destinationY = LogicInt.FromInt(next.Value.y) * cellSize + halfCell;
            if (!IsBetween(transform.PosX, originX, destinationX) ||
                !IsBetween(transform.PosY, originY, destinationY))
                return false;
            if (originX == destinationX) return transform.PosX == originX;
            if (originY == destinationY) return transform.PosY == originY;
            return LogicInt.Abs(transform.PosX - originX) ==
                LogicInt.Abs(transform.PosY - originY);
        }

        private static bool IsAtCellCenter(SimTransformComponent transform, LogicInt cellSize)
        {
            LogicInt halfCell = cellSize / LogicInt.FromInt(2);
            return transform.PosX == LogicInt.FromInt(transform.GridX) * cellSize + halfCell &&
                transform.PosY == LogicInt.FromInt(transform.GridY) * cellSize + halfCell;
        }

        private static bool IsBetween(LogicInt value, LogicInt left, LogicInt right)
        {
            LogicInt minimum = LogicInt.Min(left, right);
            LogicInt maximum = LogicInt.Max(left, right);
            return value >= minimum && value <= maximum;
        }

        private bool HasValidRestoredDeathState(SessionSnapshotData data, SimWorld world)
        {
            if (data.CompletedTick < 0 || data.Phase == SimCombatPhase.Deploying)
            {
                return data.AttackerAlive == 0 && data.DefenderAlive == 0 &&
                    data.AttackerDead == 0 && data.DefenderDead == 0 &&
                    !data.Winner.HasValue;
            }

            int attackerAlive = 0;
            int defenderAlive = 0;
            bool valid = true;
            world.ForEachComponent<SimHealthComponent>((entity, health) =>
            {
                if (!health.IsAlive ||
                    !world.TryGetComponent(entity, out SimTeamComponent team) ||
                    (team.TeamId != SimTeam.Attacker && team.TeamId != SimTeam.Defender))
                {
                    valid = false;
                    return;
                }

                if (team.TeamId == SimTeam.Attacker) attackerAlive++;
                else defenderAlive++;
            });
            if (!valid || attackerAlive + defenderAlive != world.EntityCount)
                return false;

            int initialAttackers = 0;
            int initialDefenders = 0;
            foreach (InitialEntitySpec entity in _init.InitialEntities)
            {
                if (entity.TeamId == SimTeam.Attacker) initialAttackers++;
                else if (entity.TeamId == SimTeam.Defender) initialDefenders++;
            }

            int attackerTotal = initialAttackers + data.DeployedCount;
            if (attackerAlive > attackerTotal || defenderAlive > initialDefenders)
                return false;
            int attackerDead = attackerTotal - attackerAlive;
            int defenderDead = initialDefenders - defenderAlive;
            int? winner = attackerAlive == 0 && defenderAlive > 0
                ? SimTeam.Defender
                : defenderAlive == 0 && attackerAlive > 0
                    ? SimTeam.Attacker
                    : (int?)null;
            return data.AttackerAlive == attackerAlive &&
                data.DefenderAlive == defenderAlive &&
                data.AttackerDead == attackerDead &&
                data.DefenderDead == defenderDead &&
                data.Winner == winner;
        }

        private bool HasValidRestoredEntityIdentities(SessionSnapshotData data, SimWorld world)
        {
            InitialEntitySpec[] initialEntities = _init.InitialEntities;
            long expectedNextId = (long)initialEntities.Length + data.DeployedCount;
            if (expectedNextId != data.WorldSnapshot.NextId)
                return false;

            int identityCount = 0;
            bool valid = true;
            world.ForEachComponent<SimIdentityComponent>((entity, identity) =>
            {
                identityCount++;
                if (!world.TryGetComponent(entity, out SimTeamComponent team))
                {
                    valid = false;
                    return;
                }

                if (!world.TryGetComponent(entity, out SimHealthComponent health))
                {
                    valid = false;
                    return;
                }

                SimArchetypeDefinition archetype = _manifest.GetArchetype(identity.ArchetypeId);
                if (archetype == null || health.MaxHP != archetype.MaxHealth ||
                    health.CurrentHP <= 0 || health.CurrentHP > health.MaxHP)
                {
                    valid = false;
                    return;
                }

                bool hasAttack = world.TryGetComponent(entity, out SimAttackComponent attack);
                bool hasTarget = world.TryGetComponent(entity, out SimTargetComponent _);
                if (archetype.Damage == 0)
                {
                    if (hasAttack || hasTarget)
                    {
                        valid = false;
                        return;
                    }
                }
                else if (!hasAttack || !hasTarget || attack.Damage != archetype.Damage ||
                    attack.Range.RawValue != archetype.RangeRaw ||
                    attack.CooldownTicks != archetype.CooldownTicks ||
                    (attack.LastAttackTick != -99999 &&
                        (attack.LastAttackTick < 1 ||
                            attack.LastAttackTick > data.AttackSystemTick)))
                {
                    valid = false;
                    return;
                }

                bool hasMovement = world.TryGetComponent(entity, out SimMovementComponent movement);
                if (archetype.SpeedRaw == 0)
                {
                    if (hasMovement)
                    {
                        valid = false;
                        return;
                    }
                }
                else if (!hasMovement || movement.Speed.RawValue != archetype.SpeedRaw ||
                    movement.CellSize.RawValue != LogicInt.FromInt(1).RawValue)
                {
                    valid = false;
                    return;
                }

                if (entity.Id < initialEntities.Length)
                {
                    InitialEntitySpec initial = initialEntities[entity.Id];
                    if (identity.ArchetypeId != initial.ArchetypeId ||
                        identity.OwnerPlayerId != initial.OwnerPlayerId ||
                        team.TeamId != initial.TeamId)
                        valid = false;
                    return;
                }

                if (identity.OwnerPlayerId != _init.Players[0].PlayerId ||
                    team.TeamId != SimTeam.Attacker)
                    valid = false;
            });
            return valid && identityCount == world.EntityCount;
        }

        private bool HasValidRestoredTerminalOutcome(SessionSnapshotData data)
        {
            if (data.TerminalOutcome == null) return true;

            SimTerminalOutcome expected = GetExpectedTerminalOutcome(data);
            return expected != null &&
                expected.WinnerTeamId == data.TerminalOutcome.WinnerTeamId &&
                expected.Stars == data.TerminalOutcome.Stars &&
                expected.LootGold == data.TerminalOutcome.LootGold &&
                expected.LootElixir == data.TerminalOutcome.LootElixir;
        }

        private bool ShouldHaveTerminalOutcome(SessionSnapshotData data) =>
            data.Phase == SimCombatPhase.Battling &&
            GetExpectedTerminalOutcome(data) != null;

        private SimTerminalOutcome GetExpectedTerminalOutcome(SessionSnapshotData data)
        {
            int futureWaves = _modeProfile.WaveTicks.Count(tick => tick > data.CompletedTick);
            bool timedOut = data.NextTick >= _modeProfile.TimeoutTicks;
            int defenderTotal = data.DefenderAlive + data.DefenderDead;
            int destructionBasisPoints = defenderTotal == 0
                ? 0
                : (int)((long)data.DefenderDead * 10000 / defenderTotal);
            return new SimModeRules(_modeProfile).Evaluate(
                new SimModeEvaluationInput(data.CompletedTick, data.AttackerAlive,
                    data.DefenderAlive, destructionBasisPoints, futureWaves, timedOut, true));
        }

        private bool IsCellOccupied(int gridX, int gridY)
        {
            bool occupied = false;
            _world.ForEachComponent<SimTransformComponent>((entity, transform) =>
            {
                if (transform.GridX == gridX && transform.GridY == gridY)
                    occupied = true;
            });
            return occupied;
        }

        private SimEntity FindEnemyAt(int gridX, int gridY, int casterTeamId)
        {
            SimEntity found = SimEntity.Invalid;
            _world.ForEachEntities<SimTransformComponent, SimTeamComponent>(
                (entity, transform, team) =>
                {
                    if (!found.IsValid && transform.GridX == gridX && transform.GridY == gridY &&
                        team.TeamId != casterTeamId &&
                        _world.TryGetComponent(entity, out SimHealthComponent health) && health.IsAlive)
                        found = entity;
                });
            return found;
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

        private SimBattleEvent[] BuildTickEvents(
            IReadOnlyList<EntityPresentationKey> destroyedEntities)
        {
            int damageCount = _damageSystem?.AppliedEvents.Count ?? 0;
            int destroyedCount = destroyedEntities?.Count ?? 0;
            if (damageCount == 0 && destroyedCount == 0)
                return Array.Empty<SimBattleEvent>();
            var events = new SimBattleEvent[damageCount + destroyedCount];
            for (int index = 0; index < damageCount; index++)
            {
                SimDamageEvent damage = _damageSystem.AppliedEvents[index];
                events[index] = new SimBattleEvent(
                    SimBattleEventType.DamageApplied,
                    new EntityPresentationKey(damage.Attacker.Id, damage.Attacker.Generation),
                    new EntityPresentationKey(damage.Target.Id, damage.Target.Generation),
                    damage.Amount);
            }
            for (int index = 0; index < destroyedCount; index++)
            {
                EntityPresentationKey target = destroyedEntities[index];
                EntityPresentationKey source = default;
                for (int damageIndex = damageCount - 1; damageIndex >= 0; damageIndex--)
                {
                    SimDamageEvent damage = _damageSystem.AppliedEvents[damageIndex];
                    if (damage.Target.Id != target.EntityId ||
                        damage.Target.Generation != target.Generation)
                        continue;
                    source = new EntityPresentationKey(
                        damage.Attacker.Id, damage.Attacker.Generation);
                    break;
                }
                events[damageCount + index] = new SimBattleEvent(
                    SimBattleEventType.EntityDestroyed, source, target, 0);
            }
            return events;
        }

        private ulong RefreshChecksum()
        {
            ulong checksum = ComputeChecksum();
            _state = new SimBattleState(_state.Lifecycle, _state.Phase,
                _state.CompletedTick, _state.NextTick, _state.TimelineRevision,
                checksum, _state.Presentation, _state.Result);
            return checksum;
        }

        private ulong ComputeChecksum()
        {
            ulong commandHash = _commandBuffer.HashInto(DeterministicHash64.OffsetBasis);
            var sessionState = new SessionChecksumState(
                _init?.ProtocolVersion ?? 0,
                _init?.SchemaVersion ?? SimComponentSchema.Version,
                _manifest.ContentHash,
                _init?.Seed ?? 0,
                _state.Lifecycle,
                _state.Phase,
                _state.CompletedTick,
                _state.NextTick,
                _deployedCount,
                _terminalOutcome,
                _terminalFinalTick,
                commandHash,
                HashAcceptedSequences(_lastSequenceByPlayer));
            ulong hash = sessionState.HashInto(DeterministicHash64.OffsetBasis);
            if (_attackSystem != null) hash = _attackSystem.HashInto(hash);
            if (_deathSystem != null) hash = _deathSystem.HashInto(hash);
            return _world.HashInto(hash);
        }

        private SessionSnapshotData CreateSnapshotData()
        {
            return new SessionSnapshotData
            {
                Lifecycle = _state.Lifecycle,
                Phase = _state.Phase,
                CompletedTick = _state.CompletedTick,
                NextTick = _state.NextTick,
                AttackSystemTick = _attackSystem.CurrentTick,
                AttackerAlive = _deathSystem.AttackerAlive,
                DefenderAlive = _deathSystem.DefenderAlive,
                AttackerDead = _deathSystem.AttackerDead,
                DefenderDead = _deathSystem.DefenderDead,
                Winner = _deathSystem.Winner,
                DeployedCount = _deployedCount,
                TerminalOutcome = _terminalOutcome,
                TerminalFinalTick = _terminalFinalTick,
                LastSequenceByPlayer = new Dictionary<int, int>(_lastSequenceByPlayer),
                PendingCommands = _commandBuffer.ExportPendingCommands(),
                WorldSnapshot = _world.ExportFullSnapshot(),
            };
        }

        private ulong ComputeChecksum(SessionSnapshotData data, SimWorld world)
        {
            var commands = new CommandBuffer();
            commands.RestorePendingCommands(data.PendingCommands);
            ulong commandHash = commands.HashInto(DeterministicHash64.OffsetBasis);
            var sessionState = new SessionChecksumState(
                _init.ProtocolVersion, _init.SchemaVersion, _manifest.ContentHash, _init.Seed,
                data.Lifecycle, data.Phase, data.CompletedTick, data.NextTick,
                data.DeployedCount, data.TerminalOutcome, data.TerminalFinalTick,
                commandHash, HashAcceptedSequences(data.LastSequenceByPlayer));
            ulong hash = sessionState.HashInto(DeterministicHash64.OffsetBasis);
            hash = SimAttackSystem.HashStateInto(hash, data.AttackSystemTick);
            hash = SimDeathSystem.HashStateInto(hash, data.AttackerAlive, data.DefenderAlive,
                data.AttackerDead, data.DefenderDead, data.Winner);
            return world.HashInto(hash);
        }

        private static ulong HashAcceptedSequences(
            IReadOnlyDictionary<int, int> sequences)
        {
            var ordered = new List<KeyValuePair<int, int>>(
                sequences ?? new Dictionary<int, int>());
            ordered.Sort((left, right) => left.Key.CompareTo(right.Key));
            var hash = new DeterministicHash64(DeterministicHash64.OffsetBasis);
            hash.AddInt32(ordered.Count);
            foreach (KeyValuePair<int, int> sequence in ordered)
            {
                hash.AddInt32(sequence.Key);
                hash.AddInt32(sequence.Value);
            }
            return hash.Value;
        }

        private TickResult CreateTickResult(TickCode code)
        {
            return new TickResult(code, _state.CompletedTick, _state.NextTick,
                _state.TimelineRevision, Array.Empty<ISimCommand>(),
                Array.Empty<CommandExecutionResult>(), Array.Empty<SimBattleEvent>(),
                _currentFrame, _state.Checksum, _state.Result);
        }

        private PresentationFrame BuildFullFrame(int completedTick, int nextTick, long revision)
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
            return new PresentationFrame(true, completedTick, nextTick, revision,
                states.ToArray(), Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationKey>());
        }

        private static PresentationFrame BuildDeltaFrame(
            PresentationFrame previous, PresentationFrame current)
        {
            var previousByKey = new Dictionary<EntityPresentationKey,
                EntityPresentationState>();
            foreach (EntityPresentationState state in previous.Entities)
                previousByKey.Add(Key(state), state);

            var created = new List<EntityPresentationState>();
            var updated = new List<EntityPresentationState>();
            var remaining = new HashSet<EntityPresentationKey>(previousByKey.Keys);
            foreach (EntityPresentationState state in current.Entities)
            {
                EntityPresentationKey key = Key(state);
                if (!previousByKey.TryGetValue(key, out EntityPresentationState old))
                    created.Add(state);
                else if (!SamePresentationState(old, state))
                    updated.Add(state);
                remaining.Remove(key);
            }

            var destroyed = new List<EntityPresentationKey>(remaining);
            created.Sort(CompareStates);
            updated.Sort(CompareStates);
            destroyed.Sort(CompareKeys);
            return new PresentationFrame(false, current.CompletedTick,
                current.NextTick, current.TimelineRevision,
                Array.Empty<EntityPresentationState>(), created.ToArray(),
                updated.ToArray(), destroyed.ToArray());
        }

        private static EntityPresentationKey Key(EntityPresentationState state) =>
            new EntityPresentationKey(state.EntityId, state.Generation);

        private static bool SamePresentationState(
            EntityPresentationState left, EntityPresentationState right) =>
            left.ArchetypeId == right.ArchetypeId &&
            left.OwnerPlayerId == right.OwnerPlayerId &&
            left.TeamId == right.TeamId &&
            left.GridX == right.GridX &&
            left.GridY == right.GridY &&
            left.PosXRaw == right.PosXRaw &&
            left.PosYRaw == right.PosYRaw &&
            left.OrientationMilliDegrees == right.OrientationMilliDegrees &&
            left.Health == right.Health;

        private static int CompareStates(
            EntityPresentationState left, EntityPresentationState right)
        {
            int byId = left.EntityId.CompareTo(right.EntityId);
            return byId != 0 ? byId : left.Generation.CompareTo(right.Generation);
        }

        private static int CompareKeys(
            EntityPresentationKey left, EntityPresentationKey right)
        {
            int byId = left.EntityId.CompareTo(right.EntityId);
            return byId != 0 ? byId : left.Generation.CompareTo(right.Generation);
        }

        private static PresentationFrame EmptyFrame() =>
            new PresentationFrame(true, -1, 0, 0,
                Array.Empty<EntityPresentationState>(), Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationState>(), Array.Empty<EntityPresentationKey>());
    }
}
