using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    public struct ReplayResult
    {
        public bool IsValid;
        public int FailedTick;
        public ulong ExpectedChecksum;
        public ulong ActualChecksum;
    }

    public class ReplayValidator
    {
        private readonly ISimBattleSessionFactory _sessionFactory;
        private readonly byte[] _canonicalManifestBytes;
        private readonly ReplayValidationLimits _limits;
        private readonly ReplayStructuralValidator _structural;
        private readonly IReplayValidationBudget _budget;
        private readonly ISimLogger _logger;

        public ReplayValidator(ISimBattleSessionFactory sessionFactory,
            byte[] canonicalManifestBytes, ISimLogger logger = null)
        {
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            _canonicalManifestBytes = (byte[])(canonicalManifestBytes ??
                throw new ArgumentNullException(nameof(canonicalManifestBytes))).Clone();
            _limits = ReplayValidationLimits.Approved;
            _structural = new ReplayStructuralValidator(
                _limits, KnownCommandSchema.Version1);
            _budget = new UnlimitedReplayValidationBudget();
            _logger = logger;
        }

        public ReplayValidator(
            ISimBattleSessionFactory sessionFactory,
            ReplayValidationLimits limits,
            IReplayValidationBudget budget,
            ISimLogger logger = null)
        {
            _sessionFactory = sessionFactory ??
                throw new ArgumentNullException(nameof(sessionFactory));
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _structural = new ReplayStructuralValidator(
                _limits, KnownCommandSchema.Version1);
            _budget = budget ?? throw new ArgumentNullException(nameof(budget));
            _canonicalManifestBytes = Array.Empty<byte>();
            _logger = logger;
        }

        public Task<ReplayValidationResult> ValidateAsync(
            ReplayFile replay,
            AuthoritativeMatchRecord match,
            string submittingPlayerId,
            int serializedBytes,
            CancellationToken cancellationToken)
        {
            ReplayValidationResult structural =
                _structural.Validate(replay, serializedBytes);
            if (!structural.IsValid) return Task.FromResult(structural);
            if (match == null || !string.Equals(
                    replay.MatchId, match.MatchId, StringComparison.Ordinal))
                return Rejected(ReplayValidationCode.MatchMismatch);
            if (string.IsNullOrWhiteSpace(submittingPlayerId) ||
                match.AllowedSubmitterPlayerIds == null ||
                !match.AllowedSubmitterPlayerIds.Contains(
                    submittingPlayerId, StringComparer.Ordinal))
                return Rejected(ReplayValidationCode.UnauthorizedSubmitter);
            if (replay.SubmissionNonce != match.SubmissionNonce)
                return Rejected(ReplayValidationCode.MatchMismatch);
            if (replay.SimulationSchemaHash != match.SimulationSchemaHash)
                return Rejected(ReplayValidationCode.SchemaMismatch);
            if (replay.ContentConfigHash != match.ContentConfigHash)
                return Rejected(ReplayValidationCode.ConfigMismatch);
            if (match.BattleInit == null ||
                replay.TickRate != match.BattleInit.TickRate)
                return Rejected(ReplayValidationCode.TickRateMismatch);
            if (match.BattleInit.InitialEntities == null ||
                match.InitialSnapshot == null)
                return Rejected(ReplayValidationCode.InitialStateMismatch);
            if (!TryValidateAuthorityGraph(match, out ReplayValidationCode graphCode))
                return Rejected(graphCode);
            if (match.BattleInit.InitialEntities.Length >
                _limits.MaxInitialEntities)
                return Rejected(ReplayValidationCode.TooManyInitialEntities);
            if (!HasValidExpectedSnapshot(match.InitialSnapshot))
                return Rejected(ReplayValidationCode.InitialStateMismatch);
            if (match.InitialSnapshot.UncompressedLength >
                    _limits.MaxDecompressedSnapshotBytes ||
                match.InitialSnapshot.Payload.Length >
                    _limits.MaxDecompressedSnapshotBytes)
                return Rejected(ReplayValidationCode.SnapshotTooLarge);

            cancellationToken.ThrowIfCancellationRequested();
            bool playbackStarted = false;
            try
            {
                using (ISimBattleSession session = _sessionFactory.Create(
                    match.CanonicalManifestBytes, _logger))
                {
                    if (session == null)
                        return Rejected(ReplayValidationCode.InitialStateMismatch);
                    StartResult started = session.Start(match.BattleInit);
                    if (!started.Accepted)
                        return Rejected(ReplayValidationCode.InitialStateMismatch);
                    SimSnapshot actualInitial = session.CaptureSnapshot();
                    if (!SameInitialSnapshot(actualInitial, match.InitialSnapshot))
                        return Rejected(ReplayValidationCode.InitialStateMismatch);
                    string authoritativeInitialStateHash =
                        HashText.ToLowerHex(actualInitial.PayloadSha256);
                    if (match.InitialStateHash != authoritativeInitialStateHash ||
                        replay.InitialStateHash != authoritativeInitialStateHash)
                        return Rejected(ReplayValidationCode.InitialStateMismatch);

                    playbackStarted = true;
                    SimBattleResult actualFinalResult = null;
                    for (int index = 0; index < replay.Records.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ReplayTickRecord record = replay.Records[index];
                        if (!_budget.TryConsumeTick(record.Commands.Count))
                            return Rejected(
                                ReplayValidationCode.ValidationBudgetExceeded, index);
                        cancellationToken.ThrowIfCancellationRequested();

                        foreach (SerializableCommand serialized in record.Commands)
                        {
                            ISimCommand command;
                            try
                            {
                                command = CommandRecorder.DeserializeCommand(serialized);
                            }
                            catch (Exception exception) when (
                                exception is ArgumentException ||
                                exception is InvalidOperationException)
                            {
                                return Rejected(ReplayValidationCode.CommandRejected, index);
                            }

                            if (command == null || !session.Submit(command).Accepted)
                                return Rejected(ReplayValidationCode.CommandRejected, index);
                        }

                        TickResult tick = session.Tick();
                        if (tick?.CommandOutcomes == null ||
                            tick.CommandOutcomes.Any(outcome =>
                                outcome.Disposition != CommandDisposition.Accepted))
                            return Rejected(ReplayValidationCode.CommandRejected, index);
                        if (tick == null || tick.CompletedTick != record.Tick ||
                            tick.Checksum != record.Checksum)
                            return Rejected(ReplayValidationCode.ChecksumMismatch,
                                index, record.Checksum, tick?.Checksum ?? 0);

                        if (tick.Code == TickCode.Ended)
                        {
                            actualFinalResult = tick.Result;
                            if (index != replay.Records.Count - 1 ||
                                record.Tick != replay.FinalTick)
                                return Rejected(
                                    ReplayValidationCode.TrailingRecord, index + 1);
                        }
                    }

                    if (actualFinalResult == null || session.State == null ||
                        session.State.Lifecycle != SessionLifecycle.Ended)
                        return Rejected(ReplayValidationCode.Truncated);
                    if (replay.FinalChecksum !=
                        replay.Records[replay.FinalTick].Checksum)
                        return Rejected(ReplayValidationCode.FinalChecksumMismatch);
                    if (!SimBattleResultValue.Equals(
                            actualFinalResult, replay.FinalResult) ||
                        !SimBattleResultValue.Equals(
                            actualFinalResult, match.ExpectedResult))
                        return Rejected(ReplayValidationCode.ResultMismatch);

                    return Task.FromResult(ReplayValidationResult.Valid(
                        replay.FinalTick, replay.FinalChecksum, replay.FinalResult));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (!playbackStarted)
            {
                return Rejected(ReplayValidationCode.InitialStateMismatch);
            }
        }

        private static bool TryValidateAuthorityGraph(
            AuthoritativeMatchRecord match, out ReplayValidationCode code)
        {
            try
            {
                SimSnapshot snapshot = match.InitialSnapshot;
                BattleInit init = match.BattleInit;
                if (!string.Equals(match.MatchId, init.MatchId,
                        StringComparison.Ordinal) ||
                    !string.Equals(match.MatchId, snapshot.MatchId,
                        StringComparison.Ordinal))
                {
                    code = ReplayValidationCode.MatchMismatch;
                    return false;
                }
                if (!HasValidAllowedSubmitters(
                        init.Players, match.AllowedSubmitterPlayerIds))
                {
                    code = ReplayValidationCode.UnauthorizedSubmitter;
                    return false;
                }

                DeterministicContentManifest manifest =
                    DeterministicContentManifest.Parse(match.CanonicalManifestBytes);
                byte[] manifestHash = manifest.ContentHash;
                if (!string.Equals(match.ContentConfigHash,
                        HashText.ToLowerHex(manifestHash),
                        StringComparison.Ordinal) ||
                    !HashText.FixedTimeEquals(manifestHash, init.ContentHash) ||
                    !HashText.FixedTimeEquals(manifestHash, snapshot.ContentHash))
                {
                    code = ReplayValidationCode.ConfigMismatch;
                    return false;
                }
            }
            catch (Exception)
            {
                code = ReplayValidationCode.ConfigMismatch;
                return false;
            }

            code = ReplayValidationCode.Valid;
            return true;
        }

        private static bool HasValidAllowedSubmitters(
            SimPlayerIdentity[] players, string[] allowedSubmitters)
        {
            if (players == null || allowedSubmitters == null ||
                allowedSubmitters.Length == 0)
                return false;
            var participantAccounts = new HashSet<string>(StringComparer.Ordinal);
            foreach (SimPlayerIdentity player in players)
                if (string.IsNullOrWhiteSpace(player.AccountId) ||
                    !participantAccounts.Add(player.AccountId))
                    return false;

            var authorizedAccounts = new HashSet<string>(StringComparer.Ordinal);
            foreach (string submitter in allowedSubmitters)
                if (string.IsNullOrWhiteSpace(submitter) ||
                    !participantAccounts.Contains(submitter) ||
                    !authorizedAccounts.Add(submitter))
                    return false;
            return true;
        }

        private static bool HasValidExpectedSnapshot(SimSnapshot expected)
        {
            try
            {
                byte[] payload = expected.Payload;
                byte[] payloadSha256 = expected.PayloadSha256;
                if (payload == null || payloadSha256 == null) return false;
                using (SHA256 sha256 = SHA256.Create())
                    return HashText.FixedTimeEquals(
                        sha256.ComputeHash(payload), payloadSha256);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SameInitialSnapshot(
            SimSnapshot actual, SimSnapshot expected)
        {
            if (actual == null || expected == null) return false;
            try
            {
                return actual.CompletedTick == -1 &&
                    actual.MatchId == expected.MatchId &&
                    actual.SessionId == expected.SessionId &&
                    actual.SchemaVersion == expected.SchemaVersion &&
                    actual.CompletedTick == expected.CompletedTick &&
                    actual.NextTick == expected.NextTick &&
                    actual.TimelineRevision == expected.TimelineRevision &&
                    actual.UncompressedLength == expected.UncompressedLength &&
                    actual.StateChecksum == expected.StateChecksum &&
                    HashText.FixedTimeEquals(actual.ContentHash, expected.ContentHash) &&
                    HashText.FixedTimeEquals(actual.PayloadSha256, expected.PayloadSha256) &&
                    HashText.FixedTimeEquals(actual.Payload, expected.Payload);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Task<ReplayValidationResult> Rejected(
            ReplayValidationCode code, int failedTick = -1,
            ulong expectedChecksum = 0, ulong actualChecksum = 0) =>
            Task.FromResult(new ReplayValidationResult(
                code, failedTick, expectedChecksum, actualChecksum));

        public ReplayResult Validate(ReplayFile replay)
        {
            if (!IsStructurallyValid(replay))
                return Invalid(-1, 0, 0);
            if (replay.BattleInit != null)
            {
                using ISimBattleSession playbackSession =
                    _sessionFactory.Create(_canonicalManifestBytes, _logger);
                if (playbackSession == null) return Invalid(-1, 0, 0);
                ReplayPlaybackResult playback = ReplayPlayback.Run(
                    playbackSession, replay);
                return new ReplayResult
                {
                    IsValid = playback.Valid,
                    FailedTick = playback.FailedTick,
                    ExpectedChecksum = playback.ExpectedChecksum,
                    ActualChecksum = playback.ActualChecksum,
                };
            }
            DeterministicContentManifest manifest =
                DeterministicContentManifest.Parse(_canonicalManifestBytes);
            InitialEntitySpec[] entities = ConvertInitialEntities(replay.InitialLayout);
            var init = new BattleInit(1, SimComponentSchema.Version,
                "replay-match", "replay-session", replay.Seed, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[]
                {
                    new SimPlayerIdentity(0, "attacker"),
                    new SimPlayerIdentity(1, "defender"),
                },
                entities,
                6);

            using ISimBattleSession session =
                _sessionFactory.Create(_canonicalManifestBytes, _logger);
            if (session == null) return Invalid(-1, 0, 0);
            StartResult started = session.Start(init);
            if (!started.Accepted)
                return Invalid(-1, 0, session.State.Checksum);

            foreach (ReplayTickRecord tickRecord in replay.Records)
            {
                foreach (SerializableCommand serialized in tickRecord.Commands)
                {
                    ISimCommand command;
                    try
                    {
                        command = CommandRecorder.DeserializeCommand(serialized);
                    }
                    catch (ArgumentException)
                    {
                        return Invalid(tickRecord.Tick, tickRecord.Checksum, session.State.Checksum);
                    }
                    if (command == null || !session.Submit(command).Accepted)
                        return Invalid(tickRecord.Tick, tickRecord.Checksum, session.State.Checksum);
                }

                TickResult tick = session.Tick();
                if (tick.Checksum != tickRecord.Checksum)
                {
                    _logger?.Error($"Tick {tickRecord.Tick}: checksum mismatch! " +
                        $"Expected={tickRecord.Checksum:X16} Actual={tick.Checksum:X16}");
                    return Invalid(tickRecord.Tick, tickRecord.Checksum, tick.Checksum);
                }
            }

            return new ReplayResult { IsValid = true, FailedTick = -1 };
        }

        private static bool IsStructurallyValid(ReplayFile replay)
        {
            const int maxTicks = 12000;
            const int maxCommandsPerTick = 64;
            const int maxTotalCommands = 100000;
            const int maxEntities = 4096;
            if (replay == null || replay.Version != 1 ||
                replay.Records == null || replay.Records.Count == 0 ||
                replay.Records.Count > maxTicks)
                return false;
            if (replay.BattleInit == null &&
                (replay.InitialLayout == null || replay.InitialLayout.Length == 0 ||
                replay.InitialLayout.Length > maxEntities))
                return false;

            int totalCommands = 0;
            var lastSequenceByPlayer = new Dictionary<int, int>();
            for (int expectedTick = 0; expectedTick < replay.Records.Count; expectedTick++)
            {
                ReplayTickRecord record = replay.Records[expectedTick];
                if (record == null || record.Tick != expectedTick || record.Commands == null ||
                    record.Commands.Count > maxCommandsPerTick)
                    return false;
                totalCommands += record.Commands.Count;
                if (totalCommands > maxTotalCommands) return false;
                foreach (SerializableCommand command in record.Commands)
                {
                    if (!IsValidCommand(command, expectedTick)) return false;
                    if (lastSequenceByPlayer.TryGetValue(command.PlayerId, out int previous) &&
                        command.Sequence <= previous)
                        return false;
                    lastSequenceByPlayer[command.PlayerId] = command.Sequence;
                }
            }
            return true;
        }

        private static bool IsValidCommand(SerializableCommand command, int tick)
        {
            if (command == null || command.Tick != tick ||
                (command.PlayerId != 0 && command.PlayerId != 1) || command.Sequence < 0 ||
                command.Sequence == int.MaxValue ||
                !Enum.TryParse(command.Type, false, out SimCommandType type) ||
                type == SimCommandType.None)
                return false;
            switch (type)
            {
                case SimCommandType.Deploy:
                    return command.UnitTypeId > 0 &&
                        HasValidWaypoint(command.GridX, command.GridY);
                case SimCommandType.Move:
                    return command.EntityId >= 0 &&
                        HasValidWaypoint(command.TargetX, command.TargetY);
                case SimCommandType.Skill:
                    return command.CasterId >= 0 && command.SkillId > 0 &&
                        HasValidWaypoint(command.TargetX, command.TargetY);
                case SimCommandType.EndDeployment:
                    return command.UnitTypeId == 0 && command.EntityId == 0 &&
                        command.CasterId == 0 && command.SkillId == 0 &&
                        command.GridX == 0 && command.GridY == 0 &&
                        command.TargetX == 0 && command.TargetY == 0;
                default:
                    return false;
            }
        }

        private static bool HasValidWaypoint(int x, int y) =>
            x >= 0 && x <= ushort.MaxValue && y >= 0 && y <= ushort.MaxValue;

        private static InitialEntitySpec[] ConvertInitialEntities(SimEntitySpawnInfo[] layout)
        {
            if (layout == null) return Array.Empty<InitialEntitySpec>();
            var entities = new InitialEntitySpec[layout.Length];
            for (int index = 0; index < layout.Length; index++)
            {
                SimEntitySpawnInfo source = layout[index];
                int owner = source.TeamId == SimTeam.Defender ? 1 : 0;
                entities[index] = new InitialEntitySpec(source.ArchetypeId,
                    source.GridX, source.GridY, source.TeamId, owner);
            }
            return entities;
        }

        private static ReplayResult Invalid(int tick, ulong expected, ulong actual)
        {
            return new ReplayResult
            {
                IsValid = false,
                FailedTick = tick,
                ExpectedChecksum = expected,
                ActualChecksum = actual,
            };
        }
    }
}
