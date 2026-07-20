using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Server;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class ReplayValidatorCliTests
    {
        [TestCase(CliCase.Valid, 0)]
        [TestCase(CliCase.MissingReplay, 1)]
        [TestCase(CliCase.ChecksumMismatch, 2)]
        [TestCase(CliCase.UnsupportedVersion, 3)]
        [TestCase(CliCase.UnauthorizedSubmitter, 4)]
        [TestCase(CliCase.TooManyTicks, 5)]
        public async Task Cli_EmitsOneJsonLineAndUsesStableExitCodes(
            CliCase testCase, int expectedExitCode)
        {
            using CliFixture fixture = CliFixture.Create(testCase);
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            int exitCode = await ReplayCli.RunAsync(
                fixture.Arguments, stdout, stderr, CancellationToken.None);

            string output = stdout.ToString().TrimEnd('\r', '\n');
            Assert.That(exitCode, Is.EqualTo(expectedExitCode), output);
            Assert.That(output.Split('\n'), Has.Length.EqualTo(1));
            using JsonDocument json = JsonDocument.Parse(output);
            Assert.That(json.RootElement.GetProperty("exitCode").GetInt32(),
                Is.EqualTo(expectedExitCode));
            Assert.That(json.RootElement.GetProperty("isValid").GetBoolean(),
                Is.EqualTo(expectedExitCode == 0));
            if (expectedExitCode == 0)
                Assert.That(stderr.ToString(), Is.Empty);
        }

        [Test]
        public async Task MatchProvider_RejectsUnknownAndMissingFields()
        {
            using CliFixture fixture = CliFixture.Create(CliCase.Valid);
            string valid = await File.ReadAllTextAsync(fixture.MatchPath);
            string unknown = valid.TrimEnd().TrimEnd('}') + ",\"Unknown\":1}";
            string missing = valid.Replace(
                "\"SubmissionNonce\":\"nonce-1\",", string.Empty,
                StringComparison.Ordinal);

            await File.WriteAllTextAsync(fixture.MatchPath, unknown);
            Assert.ThrowsAsync<JsonException>(async () =>
                await new JsonReplayMatchProvider().LoadAsync(
                    fixture.MatchPath, CancellationToken.None));

            await File.WriteAllTextAsync(fixture.MatchPath, missing);
            Assert.ThrowsAsync<JsonException>(async () =>
                await new JsonReplayMatchProvider().LoadAsync(
                    fixture.MatchPath, CancellationToken.None));
        }

        [Test]
        public async Task MatchProvider_RoundTripsInitialAuthorityState()
        {
            using CliFixture fixture = CliFixture.Create(CliCase.Valid);
            AuthoritativeMatchRecord expected = fixture.Match;

            AuthoritativeMatchRecord actual =
                await new JsonReplayMatchProvider().LoadAsync(
                    fixture.MatchPath, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(actual.BattleInit.ProtocolVersion,
                    Is.EqualTo(expected.BattleInit.ProtocolVersion));
                Assert.That(actual.BattleInit.SchemaVersion,
                    Is.EqualTo(expected.BattleInit.SchemaVersion));
                Assert.That(actual.BattleInit.MatchId,
                    Is.EqualTo(expected.BattleInit.MatchId));
                Assert.That(actual.BattleInit.SessionId,
                    Is.EqualTo(expected.BattleInit.SessionId));
                Assert.That(actual.BattleInit.Seed,
                    Is.EqualTo(expected.BattleInit.Seed));
                Assert.That(actual.BattleInit.TickRate,
                    Is.EqualTo(expected.BattleInit.TickRate));
                Assert.That(actual.BattleInit.Mode,
                    Is.EqualTo(expected.BattleInit.Mode));
                Assert.That(actual.BattleInit.ContentHash,
                    Is.EqualTo(expected.BattleInit.ContentHash));
                Assert.That(actual.BattleInit.Players.Select(player =>
                        (player.PlayerId, player.AccountId)),
                    Is.EqualTo(expected.BattleInit.Players.Select(player =>
                        (player.PlayerId, player.AccountId))));
                Assert.That(actual.BattleInit.InitialEntities.Select(entity =>
                        (entity.ArchetypeId, entity.GridX, entity.GridY,
                            entity.TeamId, entity.OwnerPlayerId)),
                    Is.EqualTo(expected.BattleInit.InitialEntities.Select(entity =>
                        (entity.ArchetypeId, entity.GridX, entity.GridY,
                            entity.TeamId, entity.OwnerPlayerId))));
                Assert.That(actual.BattleInit.InputDeadlineTicks,
                    Is.EqualTo(expected.BattleInit.InputDeadlineTicks));
                Assert.That(actual.InitialSnapshot.Payload,
                    Is.EqualTo(expected.InitialSnapshot.Payload));
                Assert.That(actual.InitialSnapshot.PayloadSha256,
                    Is.EqualTo(expected.InitialSnapshot.PayloadSha256));
                Assert.That(actual.InitialSnapshot.StateChecksum,
                    Is.EqualTo(expected.InitialSnapshot.StateChecksum));
            });
        }

        [Test]
        public void ReplayLimitsJson_RequiresExactApprovedDocument()
        {
            string valid = ReplayLimitsJson.SerializeApproved();
            Assert.That(ReplayLimitsJson.DeserializeApproved(valid),
                Is.SameAs(ReplayValidationLimits.Approved));
            Assert.Throws<JsonException>(() => ReplayLimitsJson.DeserializeApproved(
                valid.TrimEnd().TrimEnd('}') + ",\"Unknown\":1}"));
            Assert.Throws<JsonException>(() => ReplayLimitsJson.DeserializeApproved(
                valid.Replace("16777216", "16777215", StringComparison.Ordinal)));
            Assert.Throws<JsonException>(() => ReplayLimitsJson.DeserializeApproved(
                valid.Replace("\"MaxTicks\":12000,", string.Empty,
                    StringComparison.Ordinal)));
            Assert.Throws<JsonException>(() => ReplayLimitsJson.DeserializeApproved(
                valid.Replace("12000", "-12000", StringComparison.Ordinal)));
            Assert.Throws<JsonException>(() => ReplayLimitsJson.DeserializeApproved(
                valid.Replace("12000", "12000.5", StringComparison.Ordinal)));
        }

        [Test]
        public void ReplayJson_RejectsUnknownOrWrongCaseProperties()
        {
            using CliFixture fixture = CliFixture.Create(CliCase.Valid);
            string valid = File.ReadAllText(fixture.ReplayPath);
            Assert.Throws<JsonException>(() => ServerReplayJsonCodec.Deserialize(
                valid.TrimEnd().TrimEnd('}') + ",\"Unknown\":1}"));
            Assert.Throws<JsonException>(() => ServerReplayJsonCodec.Deserialize(
                valid.Replace("\"MatchId\"", "\"matchId\"",
                    StringComparison.Ordinal)));
            string nestedUnknown = valid.Replace(
                "\"ArchetypeId\": 10", "\"ArchetypeId\": 10, \"Unknown\": 1",
                StringComparison.Ordinal);
            Assert.That(nestedUnknown, Is.Not.EqualTo(valid));
            Assert.Throws<JsonException>(() =>
                ServerReplayJsonCodec.Deserialize(nestedUnknown));
            Assert.Throws<JsonException>(() => ServerReplayJsonCodec.Deserialize(
                valid.Replace("  \"TickRate\": 20,\n", string.Empty,
                    StringComparison.Ordinal)));
            Assert.Throws<JsonException>(() => ServerReplayJsonCodec.Deserialize(
                valid.Replace("          \"Sequence\": 1,\n", string.Empty,
                    StringComparison.Ordinal)));
        }

        [Test]
        public async Task MatchProvider_RejectsMissingOrWrongCaseNestedFields()
        {
            using CliFixture fixture = CliFixture.Create(CliCase.Valid);
            string valid = await File.ReadAllTextAsync(fixture.MatchPath);
            string[] invalidDocuments =
            {
                valid.Replace("\"Seed\":42,", string.Empty,
                    StringComparison.Ordinal),
                valid.Replace("\"TimelineRevision\":0,", string.Empty,
                    StringComparison.Ordinal),
                valid.Replace("\"Stars\":3,", string.Empty,
                    StringComparison.Ordinal),
                valid.Replace("\"InputDeadlineTicks\":6",
                    "\"inputDeadlineTicks\":6", StringComparison.Ordinal)
            };

            foreach (string invalid in invalidDocuments)
            {
                Assert.That(invalid, Is.Not.EqualTo(valid));
                await File.WriteAllTextAsync(fixture.MatchPath, invalid);
                Assert.ThrowsAsync<JsonException>(async () =>
                    await new JsonReplayMatchProvider().LoadAsync(
                        fixture.MatchPath, CancellationToken.None));
            }
        }

        [Test]
        public async Task StrictJsonDocuments_RejectNullRequiredArrays()
        {
            using CliFixture fixture = CliFixture.Create(CliCase.Valid);
            string replay = File.ReadAllText(fixture.ReplayPath);
            JsonObject replayRoot = JsonNode.Parse(replay).AsObject();
            replayRoot["Records"] = null;
            Assert.Throws<JsonException>(() =>
                ServerReplayJsonCodec.Deserialize(replayRoot.ToJsonString()));

            string match = File.ReadAllText(fixture.MatchPath);
            JsonObject nullPlayersRoot = JsonNode.Parse(match).AsObject();
            nullPlayersRoot["BattleInit"]["Players"] = null;
            JsonObject nullSubmittersRoot = JsonNode.Parse(match).AsObject();
            nullSubmittersRoot["AllowedSubmitterPlayerIds"] = null;
            string nullPlayers = nullPlayersRoot.ToJsonString();
            string nullSubmitters = nullSubmittersRoot.ToJsonString();
            foreach (string invalid in new[] { nullPlayers, nullSubmitters })
            {
                Assert.That(invalid, Is.Not.EqualTo(match));
                await File.WriteAllTextAsync(fixture.MatchPath, invalid);
                Assert.ThrowsAsync<JsonException>(async () =>
                    await new JsonReplayMatchProvider().LoadAsync(
                        fixture.MatchPath, CancellationToken.None));
            }
        }

        [Test]
        public async Task StrictJsonDocuments_RejectDuplicateProperties()
        {
            using CliFixture fixture = CliFixture.Create(CliCase.Valid);
            string replay = File.ReadAllText(fixture.ReplayPath);
            Assert.Throws<JsonException>(() => ServerReplayJsonCodec.Deserialize(
                replay.Replace("\"MatchId\": \"match-1\"",
                    "\"MatchId\": \"match-1\", \"MatchId\": \"other\"",
                    StringComparison.Ordinal)));

            string match = File.ReadAllText(fixture.MatchPath);
            await File.WriteAllTextAsync(fixture.MatchPath,
                match.Replace("\"SubmissionNonce\":\"nonce-1\"",
                    "\"SubmissionNonce\":\"nonce-1\",\"SubmissionNonce\":\"other\"",
                    StringComparison.Ordinal));
            Assert.ThrowsAsync<JsonException>(async () =>
                await new JsonReplayMatchProvider().LoadAsync(
                    fixture.MatchPath, CancellationToken.None));

            string limits = ReplayLimitsJson.SerializeApproved();
            Assert.Throws<JsonException>(() => ReplayLimitsJson.DeserializeApproved(
                limits.Replace("\"MaxTicks\":12000",
                    "\"MaxTicks\":12000,\"MaxTicks\":12000",
                    StringComparison.Ordinal)));
        }

        [Test]
        public async Task Cli_MissingAuthorityFile_IsAuthorityFailure()
        {
            using CliFixture fixture = CliFixture.Create(CliCase.Valid);
            File.Delete(fixture.MatchPath);
            var stdout = new StringWriter();

            int exitCode = await ReplayCli.RunAsync(
                fixture.Arguments, stdout, TextWriter.Null,
                CancellationToken.None);

            Assert.That(exitCode, Is.EqualTo(4));
            using JsonDocument json = JsonDocument.Parse(stdout.ToString());
            Assert.That(json.RootElement.GetProperty("category").GetString(),
                Is.EqualTo("authority"));
        }

        public enum CliCase
        {
            Valid,
            MissingReplay,
            ChecksumMismatch,
            UnsupportedVersion,
            UnauthorizedSubmitter,
            TooManyTicks
        }

        private sealed class CliFixture : IDisposable
        {
            private readonly string _directory;
            public string ReplayPath { get; }
            public string MatchPath { get; }
            public string LimitsPath { get; }
            public AuthoritativeMatchRecord Match { get; }
            public string[] Arguments { get; }

            private CliFixture(string directory, string replayPath,
                string matchPath, string limitsPath, string[] arguments,
                AuthoritativeMatchRecord match)
            {
                _directory = directory;
                ReplayPath = replayPath;
                MatchPath = matchPath;
                LimitsPath = limitsPath;
                Arguments = arguments;
                Match = match;
            }

            public static CliFixture Create(CliCase testCase)
            {
                string directory = Path.Combine(Path.GetTempPath(),
                    "zweb-replay-cli-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string replayPath = Path.Combine(directory, "replay.json");
                string matchPath = Path.Combine(directory, "match.json");
                string limitsPath = Path.Combine(directory, "limits.json");

                CreateCompletedBattle(out ReplayFile replay,
                    out AuthoritativeMatchRecord match);
                if (testCase == CliCase.ChecksumMismatch)
                    replay.Records[0].Checksum ^= 1;
                if (testCase == CliCase.UnsupportedVersion)
                    replay.FormatVersion++;
                if (testCase == CliCase.TooManyTicks)
                {
                    replay.Records = Enumerable.Range(0,
                        ReplayValidationLimits.Approved.MaxTicks + 1)
                        .Select(tick => new ReplayTickRecord
                        {
                            Tick = tick,
                            Commands = new List<SerializableCommand>(),
                            Checksum = 1
                        }).ToList();
                    replay.FinalTick = replay.Records.Count - 1;
                }

                File.WriteAllText(replayPath, ServerReplayJsonCodec.Serialize(replay));
                File.WriteAllText(matchPath, JsonReplayMatchProvider.Serialize(match));
                File.WriteAllText(limitsPath, ReplayLimitsJson.SerializeApproved());
                if (testCase == CliCase.MissingReplay) File.Delete(replayPath);

                string submitter = testCase == CliCase.UnauthorizedSubmitter
                    ? "foreign-player" : "player-0";
                string[] arguments =
                {
                    "validate", "--replay", replayPath,
                    "--match", matchPath,
                    "--limits", limitsPath,
                    "--submitter", submitter
                };
                return new CliFixture(directory, replayPath,
                    matchPath, limitsPath, arguments, match);
            }

            public void Dispose()
            {
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, true);
            }

            private static void CreateCompletedBattle(
                out ReplayFile replay, out AuthoritativeMatchRecord match)
            {
                byte[] manifestBytes = ManifestBytes();
                byte[] contentHash =
                    DeterministicContentManifest.Parse(manifestBytes).ContentHash;
                var init = new BattleInit(
                    1, SimComponentSchema.Version, "match-1", "session-1", 42,
                    ReplayFormat.TickRate, SimCombatMode.VillageAttack, contentHash,
                    new[]
                    {
                        new SimPlayerIdentity(0, "player-0"),
                        new SimPlayerIdentity(1, "player-1")
                    },
                    new[]
                    {
                        new InitialEntitySpec(10, 7, 2, SimTeam.Attacker, 0),
                        new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1)
                    }, 6);
                SimSnapshot snapshot;
                TickResult terminal;
                using (ISimBattleSession session =
                    new SimBattleSessionFactory().Create(manifestBytes))
                {
                    Assert.That(session.Start(init).Accepted, Is.True);
                    snapshot = session.CaptureSnapshot();
                    Assert.That(session.Submit(new EndDeploymentCmd
                    {
                        Tick = 0, PlayerId = 0, Sequence = 1
                    }).Accepted, Is.True);
                    terminal = session.Tick();
                    Assert.That(terminal.Code, Is.EqualTo(TickCode.Ended));
                }
                string initialHash = HashText.ToLowerHex(snapshot.PayloadSha256);
                replay = new ReplayFile
                {
                    FormatVersion = ReplayFormat.CurrentVersion,
                    SimulationSchemaHash = new string('1', 64),
                    ContentConfigHash = HashText.ToLowerHex(contentHash),
                    TickRate = ReplayFormat.TickRate,
                    MatchId = init.MatchId,
                    InitialStateHash = initialHash,
                    SubmissionNonce = "nonce-1",
                    InitialLayout = new[]
                    {
                        new SimEntitySpawnInfo(10, 7, 2, SimTeam.Attacker),
                        new SimEntitySpawnInfo(2, 8, 2, SimTeam.Defender)
                    },
                    FinalTick = 0,
                    FinalChecksum = terminal.Checksum,
                    FinalResult = terminal.Result,
                    Records = new List<ReplayTickRecord>
                    {
                        new ReplayTickRecord
                        {
                            Tick = 0,
                            Checksum = terminal.Checksum,
                            Commands = new List<SerializableCommand>
                            {
                                new SerializableCommand
                                {
                                    Type = SimCommandType.EndDeployment.ToString(),
                                    Tick = 0, PlayerId = 0, Sequence = 1
                                }
                            }
                        }
                    }
                };
                match = new AuthoritativeMatchRecord(
                    init.MatchId, initialHash, replay.SubmissionNonce,
                    replay.SimulationSchemaHash, replay.ContentConfigHash,
                    manifestBytes, init, snapshot, terminal.Result,
                    new[] { "player-0" });
            }

            private static byte[] ManifestBytes() =>
                new DeterministicContentManifest(1, new[]
                {
                    new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
                    new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
                    new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 500, 10000, 20)
                }, new[]
                {
                    new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 600, 3600,
                        Array.Empty<int>()),
                    new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 3600,
                        Array.Empty<int>()),
                    new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 3600,
                        Array.Empty<int>()),
                    new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 2400,
                        new[] { 0, 100, 200, 300, 400 })
                }).ToCanonicalBytes();
        }
    }
}
