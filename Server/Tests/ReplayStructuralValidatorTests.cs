using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ZWebGLFrameWork.Server;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class ReplayStructuralValidatorTests
    {
        private static readonly ReplayValidationLimits ApprovedLimits =
            new ReplayValidationLimits(
                maxFileBytes: 16777216,
                maxTicks: 12000,
                maxCommandsPerTick: 64,
                maxTotalCommands: 100000,
                maxInitialEntities: 4096,
                maxDecompressedSnapshotBytes: 8388608);

        [Test]
        public void ReplayValidator_EmptyReplay_IsRejectedWithStableReason()
        {
            var validator = new ReplayStructuralValidator(
                ApprovedLimits, KnownCommandSchema.Version1);

            Assert.That(validator.Validate(null, 0).Code,
                Is.EqualTo(ReplayValidationCode.NullReplay));
            Assert.That(validator.Validate(ReplayFixture.Empty(), 2).Code,
                Is.EqualTo(ReplayValidationCode.Empty));
            Assert.That(validator.Validate(
                    ReplayFixture.MissingInitialStateIdentity(), 128).Code,
                Is.EqualTo(ReplayValidationCode.InitialStateMismatch));
        }

        [Test]
        public void ReplayValidator_UnknownVersionOrConfigHash_IsRejected()
        {
            var validator = new ReplayStructuralValidator(
                ApprovedLimits, KnownCommandSchema.Version1);

            Assert.That(validator.Validate(
                    ReplayFixture.WithFormatVersion(ushort.MaxValue), 128).Code,
                Is.EqualTo(ReplayValidationCode.UnsupportedVersion));
            Assert.That(validator.Validate(
                    ReplayFixture.WithContentConfigHash("not-a-sha256"), 128).Code,
                Is.EqualTo(ReplayValidationCode.ConfigMismatch));
            Assert.That(validator.Validate(
                    ReplayFixture.WithTickRate(30), 128).Code,
                Is.EqualTo(ReplayValidationCode.TickRateMismatch));
        }

        [Test]
        public void ReplayValidator_MissingVersionAndTickRate_IsRejected()
        {
            var validator = new ReplayStructuralValidator(
                ApprovedLimits, KnownCommandSchema.Version1);
            ReplayFile replay = ReplayFixture.Valid();
            replay.FormatVersion = 0;
            Assert.That(validator.Validate(replay, 128).Code,
                Is.EqualTo(ReplayValidationCode.UnsupportedVersion));

            replay = ReplayFixture.Valid();
            replay.TickRate = 0;
            Assert.That(validator.Validate(replay, 128).Code,
                Is.EqualTo(ReplayValidationCode.TickRateMismatch));
        }

        [Test]
        public void ReplayValidator_GapDuplicateReorderOrTruncation_IsRejected()
        {
            var validator = new ReplayStructuralValidator(
                ApprovedLimits, KnownCommandSchema.Version1);

            Assert.That(validator.Validate(ReplayFixture.WithTicks(0, 2), 128).Code,
                Is.EqualTo(ReplayValidationCode.TickGap));
            Assert.That(validator.Validate(ReplayFixture.WithTicks(0, 1, 1), 128).Code,
                Is.EqualTo(ReplayValidationCode.DuplicateOrUnorderedTick));
            Assert.That(validator.Validate(ReplayFixture.WithTicks(0, 2, 1), 128).Code,
                Is.EqualTo(ReplayValidationCode.DuplicateOrUnorderedTick));
            Assert.That(validator.Validate(
                    ReplayFixture.TruncatedBeforeFinalTick(), 128).Code,
                Is.EqualTo(ReplayValidationCode.Truncated));
        }

        [Test]
        public void ReplayValidator_UnknownOrInvalidCommand_IsRejected()
        {
            var validator = new ReplayStructuralValidator(
                ApprovedLimits, KnownCommandSchema.Version1);

            Assert.That(validator.Validate(
                    ReplayFixture.WithUnknownCommand(), 128).Code,
                Is.EqualTo(ReplayValidationCode.UnknownCommand));
            Assert.That(validator.Validate(
                    ReplayFixture.WithOutOfRangeCommand(), 128).Code,
                Is.EqualTo(ReplayValidationCode.InvalidCommand));
            Assert.That(validator.Validate(
                    ReplayFixture.WithDuplicateSequence(), 128).Code,
                Is.EqualTo(ReplayValidationCode.InvalidCommand));
        }

        [Test]
        public void ReplayValidator_EndDeploymentRequiresZeroPayload()
        {
            var validator = new ReplayStructuralValidator(
                ApprovedLimits, KnownCommandSchema.Version1);
            ReplayFile replay = ReplayFixture.Valid();
            replay.Records[0].Commands.Add(new SerializableCommand
            {
                Type = nameof(SimCommandType.EndDeployment),
                Tick = 0,
                PlayerId = 0,
                Sequence = 1,
            });

            Assert.That(validator.Validate(replay, 128).Code,
                Is.EqualTo(ReplayValidationCode.Valid));
            replay.Records[0].Commands[0].UnitTypeId = 1;
            Assert.That(validator.Validate(replay, 128).Code,
                Is.EqualTo(ReplayValidationCode.InvalidCommand));
        }

        [Test]
        public void ReplayValidator_NullRecordsAndCommands_ReturnStableReasons()
        {
            var validator = new ReplayStructuralValidator(
                ApprovedLimits, KnownCommandSchema.Version1);
            ReplayFile replay = ReplayFixture.Valid();
            replay.Records = null;
            Assert.That(validator.Validate(replay, 128).Code,
                Is.EqualTo(ReplayValidationCode.NullReplay));

            replay = ReplayFixture.Valid();
            replay.Records[0] = null;
            Assert.That(validator.Validate(replay, 128).Code,
                Is.EqualTo(ReplayValidationCode.MalformedJson));

            replay = ReplayFixture.Valid();
            replay.Records[0].Commands = null;
            Assert.That(validator.Validate(replay, 128).Code,
                Is.EqualTo(ReplayValidationCode.TooManyCommands));

            replay = ReplayFixture.Valid();
            replay.Records[0].Commands.Add(null);
            Assert.That(validator.Validate(replay, 128).Code,
                Is.EqualTo(ReplayValidationCode.InvalidCommand));
        }

        [Test]
        public void ReplayCli_OversizedOrOverLimitReplay_IsRejected()
        {
            var limits = new ReplayValidationLimits(
                maxFileBytes: 128,
                maxTicks: 2,
                maxCommandsPerTick: 1,
                maxTotalCommands: 1,
                maxInitialEntities: 1,
                maxDecompressedSnapshotBytes: 64);
            var validator = new ReplayStructuralValidator(
                limits, KnownCommandSchema.Version1);

            Assert.That(validator.Validate(ReplayFixture.Valid(), 129).Code,
                Is.EqualTo(ReplayValidationCode.FileTooLarge));
            Assert.That(validator.Validate(ReplayFixture.WithTicks(0, 1, 2), 128).Code,
                Is.EqualTo(ReplayValidationCode.TooManyTicks));
            Assert.That(validator.Validate(
                    ReplayFixture.WithTwoCommandsInOneTick(), 128).Code,
                Is.EqualTo(ReplayValidationCode.TooManyCommands));
            Assert.That(validator.Validate(
                    ReplayFixture.WithOneCommandInEachOfTwoTicks(), 128).Code,
                Is.EqualTo(ReplayValidationCode.TooManyCommands));
        }

        [Test]
        public void ReplayValidationLimits_RejectInvalidConfiguration()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new ReplayValidationLimits(0, 1, 1, 1, 1, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new ReplayValidationLimits(1, 0, 1, 1, 1, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new ReplayValidationLimits(1, 1, 0, 1, 1, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new ReplayValidationLimits(1, 1, 2, 1, 1, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new ReplayValidationLimits(1, 1, 1, 1, 0, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new ReplayValidationLimits(1, 1, 1, 1, 1, 0));
        }

        [Test]
        public void ReplayInput_EnforcesByteLimitAndStrictUtf8()
        {
            Assert.That(ReplayInput.ReadUtf8(
                    new MemoryStream(new byte[] { 1, 2, 3 }), 3).ByteCount,
                Is.EqualTo(3));
            Assert.Throws<ReplayInputLimitException>(() => ReplayInput.ReadUtf8(
                new MemoryStream(new byte[] { 1, 2, 3, 4 }), 3));
            Assert.Throws<System.Text.DecoderFallbackException>(() => ReplayInput.ReadUtf8(
                new MemoryStream(new byte[] { 0xc3, 0x28 }), 3));
            ReplayInputText bom = ReplayInput.ReadUtf8(
                new MemoryStream(new byte[] { 0xef, 0xbb, 0xbf, 0x7b, 0x7d }), 5);
            Assert.That(bom.Text, Is.EqualTo("{}"));
            Assert.That(bom.ByteCount, Is.EqualTo(5));
        }

        [TestCase(ReplayValidationCode.Empty, 2)]
        [TestCase(ReplayValidationCode.UnknownCommand, 2)]
        [TestCase(ReplayValidationCode.InvalidCommand, 2)]
        [TestCase(ReplayValidationCode.UnsupportedVersion, 3)]
        [TestCase(ReplayValidationCode.SchemaMismatch, 3)]
        [TestCase(ReplayValidationCode.ConfigMismatch, 4)]
        [TestCase(ReplayValidationCode.InitialStateMismatch, 4)]
        [TestCase(ReplayValidationCode.TickRateMismatch, 4)]
        [TestCase(ReplayValidationCode.FileTooLarge, 5)]
        [TestCase(ReplayValidationCode.TooManyTicks, 5)]
        [TestCase(ReplayValidationCode.TooManyCommands, 5)]
        public void ReplayCli_MapsStableExitCodes(
            ReplayValidationCode code, int expectedExitCode)
        {
            Assert.That(ReplayCliExitCodes.For(code), Is.EqualTo(expectedExitCode));
        }

        private static class ReplayFixture
        {
            private const string SchemaHash =
                "1111111111111111111111111111111111111111111111111111111111111111";
            private const string ConfigHash =
                "2222222222222222222222222222222222222222222222222222222222222222";
            private const string InitialHash =
                "3333333333333333333333333333333333333333333333333333333333333333";

            public static ReplayFile Valid() => WithTicks(0);

            public static ReplayFile Empty()
            {
                ReplayFile replay = Valid();
                replay.Records.Clear();
                replay.FinalTick = -1;
                return replay;
            }

            public static ReplayFile MissingInitialStateIdentity()
            {
                ReplayFile replay = Valid();
                replay.InitialStateHash = null;
                return replay;
            }

            public static ReplayFile WithFormatVersion(ushort version)
            {
                ReplayFile replay = Valid();
                replay.FormatVersion = version;
                return replay;
            }

            public static ReplayFile WithContentConfigHash(string hash)
            {
                ReplayFile replay = Valid();
                replay.ContentConfigHash = hash;
                return replay;
            }

            public static ReplayFile WithTickRate(int tickRate)
            {
                ReplayFile replay = Valid();
                replay.TickRate = tickRate;
                return replay;
            }

            public static ReplayFile WithTicks(params int[] ticks)
            {
                var replay = new ReplayFile
                {
                    FormatVersion = ReplayFormat.CurrentVersion,
                    SimulationSchemaHash = SchemaHash,
                    ContentConfigHash = ConfigHash,
                    TickRate = ReplayFormat.TickRate,
                    MatchId = "match-1",
                    InitialStateHash = InitialHash,
                    SubmissionNonce = "nonce-1",
                    FinalTick = ticks.Length - 1,
                    FinalChecksum = 123,
                    Records = new List<ReplayTickRecord>()
                };
                foreach (int tick in ticks)
                {
                    replay.Records.Add(new ReplayTickRecord
                    {
                        Tick = tick,
                        Checksum = (ulong)(tick + 1),
                        Commands = new List<SerializableCommand>()
                    });
                }
                return replay;
            }

            public static ReplayFile TruncatedBeforeFinalTick()
            {
                ReplayFile replay = WithTicks(0);
                replay.FinalTick = 1;
                return replay;
            }

            public static ReplayFile WithUnknownCommand()
            {
                ReplayFile replay = Valid();
                replay.Records[0].Commands.Add(Command("Unknown", 0, 0, 1));
                return replay;
            }

            public static ReplayFile WithOutOfRangeCommand()
            {
                ReplayFile replay = Valid();
                SerializableCommand command = Command("Deploy", 0, 0, 1);
                command.UnitTypeId = 0;
                command.GridX = -1;
                replay.Records[0].Commands.Add(command);
                return replay;
            }

            public static ReplayFile WithDuplicateSequence()
            {
                ReplayFile replay = WithTicks(0, 1);
                replay.Records[0].Commands.Add(ValidDeploy(0, 0, 1));
                replay.Records[1].Commands.Add(ValidDeploy(1, 0, 1));
                return replay;
            }

            public static ReplayFile WithTwoCommandsInOneTick()
            {
                ReplayFile replay = Valid();
                replay.Records[0].Commands.Add(ValidDeploy(0, 0, 1));
                replay.Records[0].Commands.Add(ValidDeploy(0, 0, 2));
                return replay;
            }

            public static ReplayFile WithOneCommandInEachOfTwoTicks()
            {
                ReplayFile replay = WithTicks(0, 1);
                replay.Records[0].Commands.Add(ValidDeploy(0, 0, 1));
                replay.Records[1].Commands.Add(ValidDeploy(1, 0, 2));
                return replay;
            }

            private static SerializableCommand ValidDeploy(
                int tick, int playerId, int sequence)
            {
                SerializableCommand command = Command(
                    "Deploy", tick, playerId, sequence);
                command.UnitTypeId = 10;
                command.GridX = 2;
                command.GridY = 3;
                return command;
            }

            private static SerializableCommand Command(
                string type, int tick, int playerId, int sequence) =>
                new SerializableCommand
                {
                    Type = type,
                    Tick = tick,
                    PlayerId = playerId,
                    Sequence = sequence
                };
        }
    }
}
