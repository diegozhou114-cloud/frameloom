using System;
using System.Text;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class CommandRecorderBufferedOutputTests
    {
        [Test]
        public void BufferedRecord_SerializationFailureDoesNotPartiallyMutateReplay()
        {
            CommandRecorder recorder = SeededRecorder();
            string before = Snapshot(recorder.Export());
            using var output = new SimTickOutputBuffer(
                SimTickOutputFields.ExecutedCommands, initialCommandCapacity: 1);
            output.CopyCollections(new ISimCommand[] { new ThrowingCommand() },
                Array.Empty<CommandExecutionResult>(), Array.Empty<SimBattleEvent>());
            output.Publish(TickCode.Advanced, SessionLifecycle.Running,
                SimCombatPhase.Battling, 10, 4, 123UL, null);

            Assert.Throws<InvalidOperationException>(() => recorder.Record(output));

            Assert.That(Snapshot(recorder.Export()), Is.EqualTo(before));
        }

        private static CommandRecorder SeededRecorder()
        {
            var recorder = new CommandRecorder(
                42, Array.Empty<SimEntitySpawnInfo>(), new CommandBuffer());
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

        private sealed class ThrowingCommand : ISimCommand
        {
            public SimCommandType Type => throw new InvalidOperationException(
                "serialization failed");
            public int Tick => 10;
            public int PlayerId => 0;
            public int Sequence => 1;
        }
    }
}
