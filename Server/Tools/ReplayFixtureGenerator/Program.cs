using ZWebGLFrameWork.Server;
using ZWebGLFrameWork.SimulationCore;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
    throw new ArgumentException("Expected one output directory argument.");

string outputDirectory = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);
byte[] manifestBytes = ManifestBytes();
byte[] contentHash = DeterministicContentManifest.Parse(manifestBytes).ContentHash;
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

SimSnapshot initialSnapshot;
TickResult terminal;
using (ISimBattleSession session =
    new SimBattleSessionFactory().Create(manifestBytes))
{
    StartResult started = session.Start(init);
    if (!started.Accepted)
        throw new InvalidOperationException($"Session start failed: {started.Code}.");
    initialSnapshot = session.CaptureSnapshot();
    CommandSubmitResult submitted = session.Submit(new EndDeploymentCmd
    {
        Tick = 0, PlayerId = 0, Sequence = 1
    });
    if (!submitted.Accepted)
        throw new InvalidOperationException(
            $"EndDeployment failed: {submitted.Disposition}.");
    terminal = session.Tick();
    if (terminal.Code != TickCode.Ended || terminal.Result == null)
        throw new InvalidOperationException("Golden replay did not terminate.");
}

string initialHash = ToLowerHex(initialSnapshot.PayloadSha256);
var replay = new ReplayFile
{
    FormatVersion = ReplayFormat.CurrentVersion,
    SimulationSchemaHash = new string('1', 64),
    ContentConfigHash = ToLowerHex(contentHash),
    TickRate = ReplayFormat.TickRate,
    MatchId = init.MatchId,
    InitialStateHash = initialHash,
    SubmissionNonce = "nonce-1",
    Version = 1,
    Seed = init.Seed,
    InitialLayout = new[]
    {
        new SimEntitySpawnInfo(10, 7, 2, SimTeam.Attacker),
        new SimEntitySpawnInfo(2, 8, 2, SimTeam.Defender)
    },
    FinalTick = terminal.CompletedTick,
    FinalChecksum = terminal.Checksum,
    FinalResult = terminal.Result,
    Records = new List<ReplayTickRecord>
    {
        new ReplayTickRecord
        {
            Tick = terminal.CompletedTick,
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
var match = new AuthoritativeMatchRecord(
    init.MatchId, initialHash, replay.SubmissionNonce,
    replay.SimulationSchemaHash, replay.ContentConfigHash,
    manifestBytes, init, initialSnapshot, terminal.Result,
    new[] { "player-0" });

File.WriteAllText(Path.Combine(outputDirectory, "valid-replay.json"),
    ServerReplayJsonCodec.Serialize(replay) + Environment.NewLine);
File.WriteAllText(Path.Combine(outputDirectory, "valid-match.json"),
    JsonReplayMatchProvider.Serialize(match) + Environment.NewLine);
File.WriteAllText(Path.Combine(outputDirectory, "replay-limits.json"),
    ReplayLimitsJson.SerializeApproved() + Environment.NewLine);

static string ToLowerHex(byte[] bytes) =>
    Convert.ToHexString(bytes).ToLowerInvariant();

static byte[] ManifestBytes() =>
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
