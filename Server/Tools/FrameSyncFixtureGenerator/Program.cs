using System.Text.Json;
using ZWebGLFrameWork.Server;
using ZWebGLFrameWork.SimulationCore;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
    throw new ArgumentException("Expected one output directory argument.");

string outputDirectory = Path.GetFullPath(args[0]);
string replayPath = Path.Combine("Server", "TestData", "Replay", "valid-replay.json");
string matchPath = Path.Combine("Server", "TestData", "Replay", "valid-match.json");
ReplayFile replay = ServerReplayJsonCodec.Deserialize(File.ReadAllText(replayPath));

var matchProvider = new JsonReplayMatchProvider();
await matchProvider.LoadAsync(matchPath, CancellationToken.None);
using JsonDocument matchDocument = JsonDocument.Parse(File.ReadAllText(matchPath));
Directory.CreateDirectory(outputDirectory);

var golden = new GoldenMatchFixture
{
    Match = matchDocument.RootElement.Clone(),
    ExpectedFinalTick = replay.FinalTick,
    ExpectedChecksum = replay.FinalChecksum,
    ExpectedResult = replay.FinalResult
};
File.WriteAllText(Path.Combine(outputDirectory, "golden-match.json"),
    JsonSerializer.Serialize(golden, JsonOptions.Canonical) + Environment.NewLine);
File.WriteAllText(Path.Combine(outputDirectory, "golden-commands.json"),
    JsonSerializer.Serialize(replay.Records, JsonOptions.Canonical) + Environment.NewLine);

internal sealed class GoldenMatchFixture
{
    public JsonElement Match { get; init; }
    public int ExpectedFinalTick { get; init; }
    public ulong ExpectedChecksum { get; init; }
    public SimBattleResult ExpectedResult { get; init; } = null!;
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Canonical = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };
}
