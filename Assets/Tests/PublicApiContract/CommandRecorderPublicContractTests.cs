using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.PublicApiContract.Tests
{
    public sealed class CommandRecorderPublicContractTests
    {
        [Test]
        public void BufferedRecord_OwnsCommandsAcrossParentClearReuseAndDispose()
        {
            using ISimBattleSession session = PublicApiContractTestSupport.CreateStartedSession();
            using var output = new SimTickOutputBuffer(
                SimTickOutputFields.ExecutedCommands, initialCommandCapacity: 2);
            Assert.That(session.Submit(Deploy(0, 1, 2)).Accepted, Is.True);
            PublicApiContractTestSupport.Publish(session, output);
            Assert.That(output.ExecutedCommands, Has.Count.EqualTo(1));
            var recorder = NewRecorder();

            recorder.Record(output);
            Assert.That(recorder.Export().Records, Has.Count.EqualTo(1));
            Assert.That(recorder.Export().Records[0].Commands, Has.Count.EqualTo(1));
            string recorded = Snapshot(recorder.Export());
            int nextTick = output.Tick + 1;

            output.Clear();
            Assert.That(session.Submit(Deploy(nextTick, 2, 3)).Accepted, Is.True);
            PublicApiContractTestSupport.Publish(session, output);
            Assert.That(output.ExecutedCommands, Has.Count.EqualTo(1));
            output.Dispose();

            Assert.That(Snapshot(recorder.Export()), Is.EqualTo(recorded));
        }

        [Test]
        public void BufferedRecord_MatchesTickResultCommandsAndTerminalFields()
        {
            using ISimBattleSession session = PublicApiContractTestSupport.CreateStartedSession();
            using var output = new SimTickOutputBuffer(
                SimTickOutputFields.ExecutedCommands, initialCommandCapacity: 2);
            Assert.That(session.Submit(Deploy(0, 1, 2)).Accepted, Is.True);
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 2,
            }).Accepted, Is.True);
            PublicApiContractTestSupport.Publish(session, output);
            var buffered = NewRecorder();
            var owning = NewRecorder();

            buffered.Record(output);
            owning.Record(ToTickResult(output));
            int firstTick = output.Tick;
            for (int count = 0; count < 4000 && output.Result == null; count++)
                PublicApiContractTestSupport.Publish(session, output);

            Assert.That(output.Result, Is.Not.Null);
            if (output.Tick != firstTick)
            {
                buffered.Record(output);
                owning.Record(ToTickResult(output));
            }
            Assert.That(Snapshot(buffered.Export()), Is.EqualTo(Snapshot(owning.Export())));
        }

        [Test]
        public void BufferedRecord_RejectsInvalidOutputsWithoutReplayMutation()
        {
            var recorder = SeededRecorder();
            AssertRejected<ArgumentNullException>(recorder, null);
            using var fresh = new SimTickOutputBuffer(SimTickOutputFields.ExecutedCommands);
            AssertRejected<ArgumentException>(recorder, fresh);
            using ISimBattleSession session = PublicApiContractTestSupport.CreateStartedSession();
            using var missing = new SimTickOutputBuffer(SimTickOutputFields.None);
            PublicApiContractTestSupport.Publish(session, missing);
            AssertRejected<ArgumentException>(recorder, missing);
            missing.Dispose();

            using var lifecycle = new SimTickOutputBuffer(SimTickOutputFields.ExecutedCommands);
            PublicApiContractTestSupport.Publish(session, lifecycle);
            lifecycle.Clear();
            AssertRejected<ArgumentException>(recorder, lifecycle);
            lifecycle.Dispose();
            AssertRejected<ArgumentException>(recorder, lifecycle);
        }

        private static CommandRecorder NewRecorder() => new CommandRecorder(
            42, Array.Empty<SimEntitySpawnInfo>(), new CommandBuffer());

        private static DeployCmd Deploy(int tick, int sequence, int gridX) => new DeployCmd
        {
            Tick = tick, PlayerId = 0, Sequence = sequence,
            UnitTypeId = 10, GridX = gridX, GridY = 2,
        };

        private static TickResult ToTickResult(SimTickOutputBuffer output) => new TickResult(
            output.Code, output.Tick, output.Tick + 1, output.Revision,
            new List<ISimCommand>(output.ExecutedCommands).ToArray(),
            Array.Empty<CommandExecutionResult>(), Array.Empty<SimBattleEvent>(),
            null, output.Checksum, output.Result);

        private static CommandRecorder SeededRecorder()
        {
            var recorder = NewRecorder();
            recorder.Record(new TickResult(TickCode.Ended, 9, 10, 3,
                new ISimCommand[] { new SkillCmd
                {
                    Tick = 9, PlayerId = 1, Sequence = 7,
                    CasterId = 4, SkillId = 2, TargetX = 8, TargetY = 6,
                } }, Array.Empty<CommandExecutionResult>(), Array.Empty<SimBattleEvent>(),
                null, 99UL, new SimBattleResult(
                    "sentinel-match", "sentinel-result", 1, 2, 3, 4, 9, 99UL)));
            return recorder;
        }

        private static void AssertRejected<T>(CommandRecorder recorder,
            SimTickOutputBuffer output) where T : ArgumentException
        {
            string before = Snapshot(recorder.Export());
            T exception = Assert.Throws<T>(() => recorder.Record(output));
            Assert.That(exception.ParamName, Is.EqualTo("output"));
            Assert.That(Snapshot(recorder.Export()), Is.EqualTo(before));
        }

        private static string Snapshot(ReplayFile replay)
        {
            var text = new StringBuilder().Append(replay.Records.Count);
            foreach (ReplayTickRecord record in replay.Records)
            {
                text.Append('|').Append(record.Tick).Append(':').Append(record.Checksum)
                    .Append(':').Append(record.Commands.Count);
                foreach (SerializableCommand command in record.Commands)
                    text.Append(':').Append(command.Type ?? "<null>")
                        .Append(',').Append(command.Tick).Append(',').Append(command.PlayerId)
                        .Append(',').Append(command.Sequence).Append(',').Append(command.UnitTypeId)
                        .Append(',').Append(command.EntityId).Append(',').Append(command.CasterId)
                        .Append(',').Append(command.SkillId).Append(',').Append(command.GridX)
                        .Append(',').Append(command.TargetX).Append(',').Append(command.GridY)
                        .Append(',').Append(command.TargetY);
            }
            text.Append("|final:").Append(replay.FinalTick).Append(':')
                .Append(replay.FinalChecksum);
            SimBattleResult result = replay.FinalResult;
            if (result == null) return text.Append(":<null>").ToString();
            return text.Append(':').Append(result.MatchId).Append(':').Append(result.ResultId)
                .Append(':').Append(result.WinnerTeamId).Append(':').Append(result.Stars)
                .Append(':').Append(result.LootGold).Append(':').Append(result.LootElixir)
                .Append(':').Append(result.FinalTick).Append(':').Append(result.FinalChecksum)
                .ToString();
        }
    }
}
