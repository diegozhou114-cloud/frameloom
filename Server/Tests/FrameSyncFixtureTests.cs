using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using ZWebGLFrameWork.Server;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class FrameSyncFixtureTests
    {
        [Test]
        public void GoldenFrameSyncFixtures_MatchTheAuthoritativeReplayCorpus()
        {
            string root = FindRepositoryRoot();
            string fixtureDirectory = Path.Combine(root, "Server", "TestData", "FrameSync");
            ReplayFile replay = ServerReplayJsonCodec.Deserialize(File.ReadAllText(
                Path.Combine(root, "Server", "TestData", "Replay", "valid-replay.json")));
            using JsonDocument sourceMatch = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(root, "Server", "TestData", "Replay", "valid-match.json")));

            using JsonDocument match = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(fixtureDirectory, "golden-match.json")));
            JsonElement golden = match.RootElement;
            Assert.That(JsonSerializer.Serialize(golden.GetProperty("Match")), Is.EqualTo(
                JsonSerializer.Serialize(sourceMatch.RootElement)));
            Assert.That(golden.GetProperty("Match").GetProperty("MatchId").GetString(),
                Is.EqualTo(replay.MatchId));
            Assert.That(golden.GetProperty("ExpectedFinalTick").GetInt32(),
                Is.EqualTo(replay.FinalTick));
            Assert.That(golden.GetProperty("ExpectedChecksum").GetUInt64(),
                Is.EqualTo(replay.FinalChecksum));
            AssertResult(golden.GetProperty("ExpectedResult"), replay.FinalResult);

            using JsonDocument commands = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(fixtureDirectory, "golden-commands.json")));
            Assert.That(commands.RootElement.GetArrayLength(),
                Is.EqualTo(replay.Records.Count));
            for (int index = 0; index < replay.Records.Count; index++)
            {
                ReplayTickRecord expected = replay.Records[index];
                JsonElement actual = commands.RootElement[index];
                Assert.That(actual.GetProperty("Tick").GetInt32(), Is.EqualTo(expected.Tick));
                Assert.That(actual.GetProperty("Checksum").GetUInt64(),
                    Is.EqualTo(expected.Checksum));
                JsonElement actualCommands = actual.GetProperty("Commands");
                Assert.That(actualCommands.GetArrayLength(),
                    Is.EqualTo(expected.Commands.Count));
                for (int commandIndex = 0; commandIndex < expected.Commands.Count;
                     commandIndex++)
                    AssertCommand(actualCommands[commandIndex],
                        expected.Commands[commandIndex]);
            }
        }

        private static void AssertResult(JsonElement actual, SimBattleResult expected)
        {
            Assert.That(actual.GetProperty("MatchId").GetString(), Is.EqualTo(expected.MatchId));
            Assert.That(actual.GetProperty("ResultId").GetString(), Is.EqualTo(expected.ResultId));
            Assert.That(actual.GetProperty("WinnerTeamId").GetInt32(),
                Is.EqualTo(expected.WinnerTeamId));
            Assert.That(actual.GetProperty("Stars").GetInt32(), Is.EqualTo(expected.Stars));
            Assert.That(actual.GetProperty("LootGold").GetInt32(),
                Is.EqualTo(expected.LootGold));
            Assert.That(actual.GetProperty("LootElixir").GetInt32(),
                Is.EqualTo(expected.LootElixir));
            Assert.That(actual.GetProperty("FinalTick").GetInt32(),
                Is.EqualTo(expected.FinalTick));
            Assert.That(actual.GetProperty("FinalChecksum").GetUInt64(),
                Is.EqualTo(expected.FinalChecksum));
        }

        private static void AssertCommand(JsonElement actual, SerializableCommand expected)
        {
            Assert.That(actual.GetProperty("Type").GetString(), Is.EqualTo(expected.Type));
            Assert.That(actual.GetProperty("Tick").GetInt32(), Is.EqualTo(expected.Tick));
            Assert.That(actual.GetProperty("PlayerId").GetInt32(),
                Is.EqualTo(expected.PlayerId));
            Assert.That(actual.GetProperty("Sequence").GetInt32(),
                Is.EqualTo(expected.Sequence));
            Assert.That(actual.GetProperty("UnitTypeId").GetInt32(),
                Is.EqualTo(expected.UnitTypeId));
            Assert.That(actual.GetProperty("EntityId").GetInt32(),
                Is.EqualTo(expected.EntityId));
            Assert.That(actual.GetProperty("CasterId").GetInt32(),
                Is.EqualTo(expected.CasterId));
            Assert.That(actual.GetProperty("SkillId").GetInt32(),
                Is.EqualTo(expected.SkillId));
            Assert.That(actual.GetProperty("GridX").GetInt32(), Is.EqualTo(expected.GridX));
            Assert.That(actual.GetProperty("TargetX").GetInt32(),
                Is.EqualTo(expected.TargetX));
            Assert.That(actual.GetProperty("GridY").GetInt32(), Is.EqualTo(expected.GridY));
            Assert.That(actual.GetProperty("TargetY").GetInt32(),
                Is.EqualTo(expected.TargetY));
        }

        private static string FindRepositoryRoot()
        {
            for (DirectoryInfo directory = new DirectoryInfo(
                     TestContext.CurrentContext.WorkDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "Server")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Repository root was not found.");
        }
    }
}
