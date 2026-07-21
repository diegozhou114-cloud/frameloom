using System.Linq;
using NUnit.Framework;
using ZWebGLFrameWork.Combat;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class ReplaySessionRoundTripTests
    {
        [Test]
        public void Recorder_RecordsExactlyTheCommandsExecutedThisTick()
        {
            byte[] manifest = SessionTestDriver.ManifestBytes();
            BattleInit init = SessionTestDriver.DeployingBattleInit();
            using ISimBattleSession session =
                new SimBattleSessionFactory().Create(manifest);
            Assert.That(session.Start(init).Accepted, Is.True);
            var recorder = new CommandRecorder(init);
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
                UnitTypeId = 10, GridX = 2, GridY = 2,
            }).Accepted, Is.True);

            TickResult tick = session.Tick();
            recorder.Record(tick);
            ReplayFile replay = recorder.Export();

            Assert.That(replay.Records[0].Commands.Count, Is.EqualTo(1));
            Assert.That(replay.Records[0].Commands[0].Sequence, Is.EqualTo(1));
            Assert.That(replay.Records[0].Checksum, Is.EqualTo(tick.Checksum));
        }

        [Test]
        public void ReplayExportParseValidate_RoundTripsExecutedCommands()
        {
            byte[] manifest = SessionTestDriver.OneHitManifestBytes();
            ReplayFile recorded = ReplayFixture.RecordTerminalBattle(manifest);
            ReplayFile parsed = UnityReplayJsonCodec.Deserialize(
                UnityReplayJsonCodec.Serialize(recorded));
            using ISimBattleSession replaySession =
                new SimBattleSessionFactory().Create(manifest);

            ReplayPlaybackResult replayed = ReplayPlayback.Run(replaySession, parsed);

            Assert.That(replayed.Valid, Is.True);
            Assert.That(replayed.FinalChecksum, Is.EqualTo(recorded.FinalChecksum));
            Assert.That(replayed.ExecutedCommandCount,
                Is.EqualTo(recorded.Records.Sum(record => record.Commands.Count)));
            Assert.That(replaySession.State.CompletedTick,
                Is.EqualTo(recorded.FinalTick));
            AssertCanonicalBattleResult(recorded.FinalResult, parsed.FinalResult);
            AssertCanonicalBattleResult(
                recorded.FinalResult, replaySession.State.Result);
        }

        [Test]
        public void UnityAndValidator_SameBattleInitAndCommands_SameTickResults()
        {
            byte[] manifest = SessionTestDriver.OneHitManifestBytes();
            ReplayFile recorded = ReplayFixture.RecordTerminalBattle(manifest);
            using ISimBattleSession validatorSession =
                new SimBattleSessionFactory().Create(manifest);

            ReplayPlaybackResult replayed = ReplayPlayback.Run(
                validatorSession, recorded);

            Assert.That(replayed.Valid, Is.True);
            Assert.That(replayed.FinalChecksum, Is.EqualTo(recorded.FinalChecksum));
            Assert.That(validatorSession.State.CompletedTick,
                Is.EqualTo(recorded.FinalTick));
            AssertCanonicalBattleResult(
                recorded.FinalResult, validatorSession.State.Result);
        }

        private static void AssertCanonicalBattleResult(
            SimBattleResult expected, SimBattleResult actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.MatchId, Is.EqualTo(expected.MatchId));
            Assert.That(actual.ResultId, Is.EqualTo(expected.ResultId));
            Assert.That(actual.WinnerTeamId, Is.EqualTo(expected.WinnerTeamId));
            Assert.That(actual.Stars, Is.EqualTo(expected.Stars));
            Assert.That(actual.LootGold, Is.EqualTo(expected.LootGold));
            Assert.That(actual.LootElixir, Is.EqualTo(expected.LootElixir));
            Assert.That(actual.FinalTick, Is.EqualTo(expected.FinalTick));
            Assert.That(actual.FinalChecksum, Is.EqualTo(expected.FinalChecksum));
        }
    }
}
