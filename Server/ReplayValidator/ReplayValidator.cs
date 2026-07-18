using System;
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
            if (replay == null) throw new ArgumentNullException(nameof(replay));
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

            foreach (TickRecord tickRecord in replay.Records)
            {
                foreach (SerializableCommand serialized in tickRecord.Commands)
                {
                    ISimCommand command = CommandRecorder.DeserializeCommand(serialized);
                    if (command != null) session.Submit(command);
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
