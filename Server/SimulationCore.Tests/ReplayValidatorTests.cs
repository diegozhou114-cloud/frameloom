using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.Server;
using ZWebGLFrameWork.SimulationCore.Tests;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class ReplayValidatorTests
    {
        [Test]
        public void ReplayValidator_EmptyReplay_IsRejected()
        {
            var validator = new ReplayValidator(
                new SimBattleSessionFactory(), SessionTestDriver.ManifestBytes());

            ReplayResult result = validator.Validate(new ReplayFile
            {
                Version = 1,
                Seed = 42,
                InitialLayout = new[] { new SimEntitySpawnInfo(10, 2, 2, SimTeam.Attacker) },
                Records = new List<TickRecord>(),
            });

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void ReplayValidator_GapDuplicateOrUnknownCommand_IsRejected()
        {
            var validator = new ReplayValidator(
                new SimBattleSessionFactory(), SessionTestDriver.ManifestBytes());
            ReplayFile replay = ValidReplay();

            replay.Records[0].Tick = 1;
            Assert.That(validator.Validate(replay).IsValid, Is.False);

            replay = ValidReplay();
            replay.Records.Add(new TickRecord { Tick = 0, Checksum = 0 });
            Assert.That(validator.Validate(replay).IsValid, Is.False);

            replay = ValidReplay();
            replay.Records[0].Commands.Add(new SerializableCommand
            {
                Type = "Unknown",
                Tick = 0,
            });
            Assert.That(validator.Validate(replay).IsValid, Is.False);
        }

        private static ReplayFile ValidReplay() => new ReplayFile
        {
            Version = 1,
            Seed = 42,
            InitialLayout = new[] { new SimEntitySpawnInfo(10, 2, 2, SimTeam.Attacker) },
            Records = new List<TickRecord>
            {
                new TickRecord { Tick = 0, Checksum = 0, Commands = new List<SerializableCommand>() },
            },
        };
    }
}
