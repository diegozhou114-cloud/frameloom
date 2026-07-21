using System.Linq;
using System.Threading;
using NUnit.Framework;
using ZWebGLFrameWork.Server;
using ZWebGLFrameWork.SimulationCore.Tests;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class ReplayRoundTripTests
    {
        [Test]
        public void ServerCodec_LegacyReplayWithoutBattleInit_RoundTrips()
        {
            var replay = new ReplayFile
            {
                FormatVersion = ReplayFormat.CurrentVersion,
                TickRate = ReplayFormat.TickRate,
                InitialLayout = new[]
                {
                    new SimEntitySpawnInfo(10, 2, 2, SimTeam.Attacker),
                },
                Records = new System.Collections.Generic.List<ReplayTickRecord>
                {
                    new ReplayTickRecord(),
                }
            };

            ReplayFile parsed = ServerReplayJsonCodec.Deserialize(
                ServerReplayJsonCodec.Serialize(replay));

            Assert.That(parsed.BattleInit, Is.Null);
            Assert.That(parsed.InitialLayout, Has.Length.EqualTo(1));
        }

        [Test]
        public void ReplayValidator_UsesSharedSessionPlaybackForRecordedReplay()
        {
            byte[] manifest = SessionTestDriver.OneHitManifestBytes();
            ReplayFile replay = ReplayFixture.RecordTerminalBattle(manifest);
            var validator = new ReplayValidator(
                new SimBattleSessionFactory(), manifest);

            ReplayResult result = validator.Validate(replay);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.FailedTick, Is.EqualTo(-1));
        }

        [Test]
        public async System.Threading.Tasks.Task Recorder_WithAuthorityMetadata_ValidatesAsync()
        {
            byte[] manifest = SessionTestDriver.OneHitManifestBytes();
            BattleInit init = SessionTestDriver.OneHitBattleInit(manifest);
            SimSnapshot initialSnapshot;
            TickResult terminal;
            ReplayFile replay;
            using (ISimBattleSession session =
                new SimBattleSessionFactory().Create(manifest))
            {
                Assert.That(session.Start(init).Accepted, Is.True);
                initialSnapshot = session.CaptureSnapshot();
                var recorder = new CommandRecorder(init,
                    new ReplayAuthorityMetadata(new string('1', 64),
                        HashText.ToLowerHex(init.ContentHash),
                        HashText.ToLowerHex(initialSnapshot.PayloadSha256),
                        "nonce-1"));
                Assert.That(session.Submit(new EndDeploymentCmd
                {
                    Tick = 0, PlayerId = 0, Sequence = 1,
                }).Accepted, Is.True);
                terminal = session.Tick();
                recorder.Record(terminal);
                replay = recorder.Export();
            }

            var match = new AuthoritativeMatchRecord(init.MatchId,
                replay.InitialStateHash, replay.SubmissionNonce,
                replay.SimulationSchemaHash, replay.ContentConfigHash,
                manifest, init, initialSnapshot, terminal.Result,
                new[] { "attacker" });
            var validator = new ReplayValidator(new SimBattleSessionFactory(),
                ReplayValidationLimits.Approved,
                new UnlimitedReplayValidationBudget());

            ReplayValidationResult result = await validator.ValidateAsync(replay,
                match, "attacker", 512, CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo(ReplayValidationCode.Valid));
        }

        [Test]
        public void ReplayValidator_BattleInitWithNoInitialEntities_IsPlayable()
        {
            byte[] manifest = SessionTestDriver.OneHitManifestBytes();
            BattleInit template = SessionTestDriver.OneHitBattleInit(manifest);
            var init = new BattleInit(template.ProtocolVersion,
                template.SchemaVersion, template.MatchId, template.SessionId,
                template.Seed, template.TickRate, template.Mode,
                template.ContentHash, template.Players,
                System.Array.Empty<InitialEntitySpec>(),
                template.InputDeadlineTicks);
            using ISimBattleSession session =
                new SimBattleSessionFactory().Create(manifest);
            Assert.That(session.Start(init).Accepted, Is.True);
            var recorder = new CommandRecorder(init);
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            TickResult terminal = session.Tick();
            recorder.Record(terminal);

            ReplayResult result = new ReplayValidator(
                new SimBattleSessionFactory(), manifest).Validate(recorder.Export());

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void ReplayExportParseValidate_RoundTripsExecutedCommands()
        {
            byte[] manifest = SessionTestDriver.OneHitManifestBytes();
            ReplayFile recorded = ReplayFixture.RecordTerminalBattle(manifest);
            ReplayFile parsed = ServerReplayJsonCodec.Deserialize(
                ServerReplayJsonCodec.Serialize(recorded));
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
