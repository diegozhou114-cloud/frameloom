using System;
using System.Collections.Generic;
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
        private readonly ISimLogger _logger;

        public ReplayValidator(ISimBattleSessionFactory sessionFactory,
            byte[] canonicalManifestBytes, ISimLogger logger = null)
        {
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            _canonicalManifestBytes = (byte[])(canonicalManifestBytes ??
                throw new ArgumentNullException(nameof(canonicalManifestBytes))).Clone();
            _logger = logger;
        }

        public ReplayResult Validate(ReplayFile replay)
        {
            if (!IsStructurallyValid(replay))
                return Invalid(-1, 0, 0);
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
                replay.InitialLayout == null || replay.InitialLayout.Length == 0 ||
                replay.InitialLayout.Length > maxEntities ||
                replay.Records == null || replay.Records.Count == 0 ||
                replay.Records.Count > maxTicks)
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
                !Enum.TryParse(command.Type, false, out SimCommandType type) ||
                type == SimCommandType.None)
                return false;
            switch (type)
            {
                case SimCommandType.Deploy:
                    return command.UnitTypeId > 0 && command.GridX >= 0 && command.GridY >= 0;
                case SimCommandType.Move:
                    return command.EntityId >= 0 && command.TargetX >= 0 && command.TargetY >= 0;
                case SimCommandType.Skill:
                    return command.CasterId >= 0 && command.SkillId > 0 &&
                        command.TargetX >= 0 && command.TargetY >= 0;
                default:
                    return false;
            }
        }

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
