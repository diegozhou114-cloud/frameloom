# Task 15 Session-Only Battle Host Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route the Unity battle flow through exactly one `ISimBattleSession`, using explicit framework-stage authority and content providers whose production replacement points remain searchable.

**Architecture:** `FrameworkBattleAuthority` creates complete `BattleParams`, `FrameworkBattleContent` owns immutable canonical manifest bytes and temporary ID mappings, and `BattleInitFactory` performs strict conversion. `CombatManager` is the only Unity Session host, advances it at 20 Hz, applies immutable presentation frames, and publishes `SimCombatPhase`/`SimBattleResult` to the FSM and UI.

**Tech Stack:** Unity 2022.3.62f3c1, C# 9, UniTask, NUnit, Unity Test Framework, SimulationCore Session API.

## Global Constraints

- Work only in `/Users/diegozhou/Documents/unityframework/zweb-task15-session` on `codex/task15-session-host`.
- Preserve the existing untracked architecture and PlayMode red-test drafts; incorporate them intentionally instead of deleting or resetting them.
- Production code outside `Assets/Scripts/SimulationCore/` must not reference `SimWorld` or `SimOrchestrator`.
- `CombatManager` owns at most one Session and advances it at exactly 20 Hz.
- Framework defaults remain runnable in Debug and Release, emit a one-time warning, and carry literal `TODO(PRODUCTION-AUTHORITY)` or `TODO(PRODUCTION-CONTENT)` markers.
- `BattleInitFactory` must reject incomplete inputs and must not create fallback IDs, Seed, account identities, or manifest bytes.
- The project adds no package dependency, network service, save-schema migration, general dependency injection container, or unrelated refactor.
- Building configuration IDs are not SimulationCore archetype IDs: map Town Hall `1 -> 1` and Cannon `5 -> 2`; do not repeat the current draft fixture's incorrect `2 -> 2` assumption.
- Unit configuration ID `1` (Barbarian) maps to SimulationCore archetype ID `10`.
- Use `apply_patch` for edits and retain Unity `.meta` files for every added asset.
- Store Task 15 evidence under `/tmp/zweb-sim-migration/task-15/`.

---

### Task 1: Framework Authority And Content Defaults

**Files:**
- Create: `Assets/Scripts/Combat/FrameworkBattleAuthority.cs`
- Create: `Assets/Scripts/Combat/FrameworkBattleAuthority.cs.meta`
- Create: `Assets/Scripts/Combat/FrameworkBattleContent.cs`
- Create: `Assets/Scripts/Combat/FrameworkBattleContent.cs.meta`
- Create: `Assets/Scripts/Combat/Properties.meta`
- Create: `Assets/Scripts/Combat/Properties/AssemblyInfo.cs`
- Create: `Assets/Scripts/Combat/Properties/AssemblyInfo.cs.meta`
- Create: `Assets/Scripts/Combat/Tests/FrameworkBattleDefaultsTests.cs`
- Create: `Assets/Scripts/Combat/Tests/FrameworkBattleDefaultsTests.cs.meta`
- Modify: `Assets/Scripts/Combat/BattleParams.cs`
- Modify: `Assets/Scripts/Combat/Tests/BattleE2ETests.cs`

**Interfaces:**
- Produces: `BattleParams FrameworkBattleAuthority.CreateVillageAttack(PlayerData, VillageData, VillageData)`.
- Produces: `byte[] FrameworkBattleContent.CanonicalBytes()`.
- Produces: `bool FrameworkBattleContent.TryGetBuildingArchetypeId(int, out int)` and `bool TryGetUnitArchetypeId(int, out int)`.
- Produces: `SimModeProfile FrameworkBattleContent.GetModeProfile(string)`.

- [ ] **Step 1: Write failing authority and content tests**

Add `FrameworkBattleDefaultsTests.cs` with these concrete cases:

```csharp
using System;
using System.Linq;
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat.Tests
{
    public sealed class FrameworkBattleDefaultsTests
    {
        [Test]
        public void Authority_CreatesCompleteDistinctReplayInputs()
        {
            string[] ids = { "match-token", "session-token" };
            int index = 0;
            var authority = new FrameworkBattleAuthority(() => ids[index++]);

            BattleParams result = authority.CreateVillageAttack(
                new PlayerData(), new VillageData(), DefenderVillage());

            Assert.That(result.MatchId, Is.EqualTo("framework-match-match-token"));
            Assert.That(result.SessionId, Is.EqualTo("framework-session-session-token"));
            Assert.That(result.Seed, Is.Not.Zero);
            Assert.That(result.AttackerPlayerId, Is.Not.EqualTo(result.DefenderPlayerId));
            Assert.That(result.AttackerAccountId, Is.Not.Empty);
            Assert.That(result.DefenderAccountId, Is.Not.Empty);
            Assert.That(result.AttackerVillage, Is.Not.Null);
            Assert.That(result.DefenderVillage, Is.Not.Null);
        }

        [Test]
        public void Authority_SameGeneratedIds_ProduceSameReplaySeed()
        {
            BattleParams left = CreateWithIds("same-match", "same-session");
            BattleParams right = CreateWithIds("same-match", "same-session");
            Assert.That(right.Seed, Is.EqualTo(left.Seed));
        }

        [Test]
        public void Authority_DifferentGeneratedIds_ProduceDistinctBattleIdentity()
        {
            BattleParams left = CreateWithIds("left-match", "left-session");
            BattleParams right = CreateWithIds("right-match", "right-session");
            Assert.That(right.MatchId, Is.Not.EqualTo(left.MatchId));
            Assert.That(right.SessionId, Is.Not.EqualTo(left.SessionId));
            Assert.That(right.Seed, Is.Not.EqualTo(left.Seed));
        }

        [Test]
        public void Content_IsCanonicalCompleteAndMutationSafe()
        {
            byte[] first = FrameworkBattleContent.CanonicalBytes();
            first[0] ^= 0xFF;
            byte[] second = FrameworkBattleContent.CanonicalBytes();
            var manifest = DeterministicContentManifest.Parse(second);

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(manifest.Archetypes.Select(row => row.ArchetypeId),
                Is.EquivalentTo(new[] { 1, 2, 10 }));
            Assert.That(manifest.Modes.Select(row => row.Mode), Is.EquivalentTo(new[]
            {
                SimCombatMode.VillageAttack, SimCombatMode.BuilderBase,
                SimCombatMode.ClanWar, SimCombatMode.Campaign
            }));
            Assert.That(FrameworkBattleContent.TryGetBuildingArchetypeId(5, out int cannon), Is.True);
            Assert.That(cannon, Is.EqualTo(2));
            Assert.That(FrameworkBattleContent.TryGetBuildingArchetypeId(2, out _), Is.False);
            Assert.That(FrameworkBattleContent.TryGetUnitArchetypeId(1, out int barbarian), Is.True);
            Assert.That(barbarian, Is.EqualTo(10));
        }

        private static BattleParams CreateWithIds(string match, string session)
        {
            string[] ids = { match, session };
            int index = 0;
            return new FrameworkBattleAuthority(() => ids[index++]).CreateVillageAttack(
                new PlayerData(), new VillageData(), DefenderVillage());
        }

        private static VillageData DefenderVillage()
        {
            var village = new VillageData();
            village.AddBuilding(new BuildingData
            {
                Id = "cannon", TypeId = 5, GridX = { Value = 8 }, GridY = { Value = 2 }
            });
            return village;
        }
    }
}
```

Replace `BattleParams_VillageAttack_SetsCorrectMode` in `BattleE2ETests.cs` with an assertion against a complete authority-created result; the removed incomplete `BattleParams.VillageAttack(null, null)` API must have no remaining caller.

- [ ] **Step 2: Run the focused tests and capture red**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-15
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-task15-session \
  -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Combat.Tests.FrameworkBattleDefaultsTests \
  -testResults /tmp/zweb-sim-migration/task-15/framework-defaults-red.xml \
  -logFile /tmp/zweb-sim-migration/task-15/framework-defaults-red.log
```

Expected: compilation fails because `FrameworkBattleAuthority`, `FrameworkBattleContent`, and complete `BattleParams` fields do not exist.

- [ ] **Step 3: Implement complete framework defaults**

Expand `BattleParams` to contain exactly these public fields and remove the incomplete static factory:

```csharp
public string ModeId;
public VillageData AttackerVillage;
public VillageData DefenderVillage;
public string MatchId;
public string SessionId;
public int Seed;
public int AttackerPlayerId;
public int DefenderPlayerId;
public string AttackerAccountId;
public string DefenderAccountId;
```

Implement authority with an internal deterministic test seam and an explicit framework fallback:

```csharp
public sealed class FrameworkBattleAuthority
{
    private const int FrameworkAttackerPlayerId = 1000;
    private const int FrameworkDefenderPlayerId = 2000;
    private readonly Func<string> _idFactory;
    private static bool _warningEmitted;

    public FrameworkBattleAuthority() : this(() => Guid.NewGuid().ToString("N")) { }
    internal FrameworkBattleAuthority(Func<string> idFactory) =>
        _idFactory = idFactory ?? throw new ArgumentNullException(nameof(idFactory));

    public BattleParams CreateVillageAttack(PlayerData player, VillageData attacker,
        VillageData defender)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (attacker == null) throw new ArgumentNullException(nameof(attacker));
        if (defender == null) throw new ArgumentNullException(nameof(defender));
        WarnOnce();
        string matchId = "framework-match-" + RequireToken(_idFactory());
        string sessionId = "framework-session-" + RequireToken(_idFactory());
        return new BattleParams
        {
            ModeId = "village_attack",
            AttackerVillage = attacker,
            DefenderVillage = defender,
            MatchId = matchId,
            SessionId = sessionId,
            Seed = StableSeed(matchId + ":" + sessionId),
            // TODO(PRODUCTION-AUTHORITY): Replace reserved local identities with persisted/server authority.
            AttackerPlayerId = FrameworkAttackerPlayerId,
            DefenderPlayerId = FrameworkDefenderPlayerId,
            AttackerAccountId = "framework-local-attacker",
            DefenderAccountId = "framework-local-defender",
        };
    }

    private static int StableSeed(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
        int result = (hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3];
        return result == 0 ? 1 : result;
    }
}
```

Create `Properties/AssemblyInfo.cs` now so the Task 1 tests can use the deterministic internal seam:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ZWebGLFrameWork.Combat.Tests")]
```

`RequireToken` throws `InvalidOperationException` for null/empty output. `WarnOnce` calls
`Debug.LogWarning` only on the first use.

Implement `FrameworkBattleContent` with one private canonical byte array built from schema version
`SimComponentSchema.Version`, archetypes `(1, TownHall)`, `(2, Cannon)`, `(10, Barbarian)`, and the
four exact mode rows already present in `SessionPlayModeFixture`. Add the literal marker:

```csharp
// TODO(PRODUCTION-CONTENT): Replace framework manifest and ID mappings with versioned project content.
```

Return a clone from `CanonicalBytes`. Implement mappings with explicit switches:

```csharp
public static bool TryGetBuildingArchetypeId(int buildingTypeId, out int archetypeId)
{
    if (buildingTypeId == 1) { archetypeId = 1; return true; }
    if (buildingTypeId == 5) { archetypeId = 2; return true; }
    archetypeId = 0;
    return false;
}

public static bool TryGetUnitArchetypeId(int unitConfigId, out int archetypeId)
{
    archetypeId = unitConfigId == 1 ? 10 : 0;
    return archetypeId != 0;
}
```

- [ ] **Step 4: Run focused tests and verify green**

Repeat the Step 2 command with output files `framework-defaults-green.xml` and
`framework-defaults-green.log`.

Expected: Unity exits `0`; XML reports all `FrameworkBattleDefaultsTests` passed and zero failed.

- [ ] **Step 5: Commit Task 1**

```bash
git add Assets/Scripts/Combat/BattleParams.cs \
  Assets/Scripts/Combat/FrameworkBattleAuthority.cs \
  Assets/Scripts/Combat/FrameworkBattleAuthority.cs.meta \
  Assets/Scripts/Combat/FrameworkBattleContent.cs \
  Assets/Scripts/Combat/FrameworkBattleContent.cs.meta \
  Assets/Scripts/Combat/Properties.meta \
  Assets/Scripts/Combat/Properties/AssemblyInfo.cs \
  Assets/Scripts/Combat/Properties/AssemblyInfo.cs.meta \
  Assets/Scripts/Combat/Tests/FrameworkBattleDefaultsTests.cs \
  Assets/Scripts/Combat/Tests/FrameworkBattleDefaultsTests.cs.meta \
  Assets/Scripts/Combat/Tests/BattleE2ETests.cs
git diff --cached --check
git commit -m "feat(combat): add framework battle defaults"
```

---

### Task 2: Strict BattleInit Conversion

**Files:**
- Create: `Assets/Scripts/Combat/BattleInitFactory.cs`
- Create: `Assets/Scripts/Combat/BattleInitFactory.cs.meta`
- Create: `Assets/Scripts/Combat/Tests/BattleInitFactoryTests.cs`
- Create: `Assets/Scripts/Combat/Tests/BattleInitFactoryTests.cs.meta`

**Interfaces:**
- Consumes: complete `BattleParams` and exact canonical manifest bytes.
- Produces: `BattleInitFactory(byte[])` and `BattleInit Create(BattleParams)`.

- [ ] **Step 1: Write failing conversion tests**

Cover the successful mapping and every strict rejection in `BattleInitFactoryTests.cs`:

```csharp
[Test]
public void Create_MapsIdentityContentAndSupportedDefenders()
{
    byte[] bytes = FrameworkBattleContent.CanonicalBytes();
    BattleParams parameters = CompleteParams();
    parameters.DefenderVillage.AddBuilding(Building("gold", 2, 3, 3));
    parameters.DefenderVillage.AddBuilding(Building("cannon", 5, 8, 2));

    BattleInit init = new BattleInitFactory(bytes).Create(parameters);

    Assert.That(init.ProtocolVersion, Is.EqualTo(1));
    Assert.That(init.SchemaVersion, Is.EqualTo(SimComponentSchema.Version));
    Assert.That(init.TickRate, Is.EqualTo(20));
    Assert.That(init.MatchId, Is.EqualTo(parameters.MatchId));
    Assert.That(init.SessionId, Is.EqualTo(parameters.SessionId));
    Assert.That(init.Seed, Is.EqualTo(parameters.Seed));
    Assert.That(init.ContentHash,
        Is.EqualTo(DeterministicContentManifest.Parse(bytes).ContentHash));
    Assert.That(init.Players.Select(player => player.PlayerId),
        Is.EquivalentTo(new[] { 1000, 2000 }));
    Assert.That(init.InitialEntities.Length, Is.EqualTo(1));
    Assert.That(init.InitialEntities[0].ArchetypeId, Is.EqualTo(2));
    Assert.That(init.InitialEntities[0].OwnerPlayerId, Is.EqualTo(2000));
    Assert.That(init.InitialEntities[0].TeamId, Is.EqualTo(SimTeam.Defender));
}

[TestCase(nameof(BattleParams.ModeId))]
[TestCase(nameof(BattleParams.MatchId))]
[TestCase(nameof(BattleParams.SessionId))]
[TestCase(nameof(BattleParams.AttackerAccountId))]
[TestCase(nameof(BattleParams.DefenderAccountId))]
public void Create_RejectsMissingRequiredString(string field)
{
    BattleParams parameters = CompleteParams();
    typeof(BattleParams).GetField(field).SetValue(parameters, null);
    Assert.Throws<ArgumentException>(() =>
        new BattleInitFactory(FrameworkBattleContent.CanonicalBytes()).Create(parameters));
}

[Test]
public void Create_RejectsAliasedPlayers()
{
    BattleParams parameters = CompleteParams();
    parameters.DefenderPlayerId = parameters.AttackerPlayerId;
    Assert.Throws<ArgumentException>(() =>
        new BattleInitFactory(FrameworkBattleContent.CanonicalBytes()).Create(parameters));
}
[Test]
public void Create_RejectsNullVillages()
{
    BattleParams missingAttacker = CompleteParams();
    missingAttacker.AttackerVillage = null;
    BattleParams missingDefender = CompleteParams();
    missingDefender.DefenderVillage = null;
    var factory = new BattleInitFactory(FrameworkBattleContent.CanonicalBytes());
    Assert.Throws<ArgumentException>(() => factory.Create(missingAttacker));
    Assert.Throws<ArgumentException>(() => factory.Create(missingDefender));
}
[Test]
public void Create_RejectsNoSupportedDefenderEntity()
{
    BattleParams parameters = CompleteParams();
    parameters.DefenderVillage.Buildings.Clear();
    parameters.DefenderVillage.AddBuilding(Building("gold", 2, 3, 3));
    Assert.Throws<ArgumentException>(() =>
        new BattleInitFactory(FrameworkBattleContent.CanonicalBytes()).Create(parameters));
}
[Test]
public void Constructor_RejectsInvalidManifest()
{
    Assert.Throws<FormatException>(() => new BattleInitFactory(new byte[] { 1 }));
}

private static BattleParams CompleteParams()
{
    string[] ids = { "factory-match", "factory-session" };
    int index = 0;
    var defender = new VillageData();
    defender.AddBuilding(Building("town-hall", 1, 8, 2));
    return new FrameworkBattleAuthority(() => ids[index++]).CreateVillageAttack(
        new PlayerData(), new VillageData(), defender);
}

private static BuildingData Building(string id, int typeId, int x, int y) =>
    new BuildingData
    {
        Id = id, TypeId = typeId, GridX = { Value = x }, GridY = { Value = y }
    };
```

Do not share mutated `BattleParams` between test cases.

- [ ] **Step 2: Run tests and capture red**

Run the Unity EditMode command from Task 1 with filter
`ZWebGLFrameWork.Combat.Tests.BattleInitFactoryTests` and output prefix
`/tmp/zweb-sim-migration/task-15/battle-init-factory-red`.

Expected: compilation fails because `BattleInitFactory` does not exist.

- [ ] **Step 3: Implement strict conversion**

Implement `BattleInitFactory` as a sealed class. Its constructor clones the supplied bytes and parses
them once. `Create` performs all validation before allocating `BattleInit`, maps mode IDs with a
closed switch, filters only explicitly supported building mappings, and rejects an empty mapped
defender set.

The returned object must use these exact values:

```csharp
return new BattleInit(
    protocolVersion: 1,
    schemaVersion: SimComponentSchema.Version,
    matchId: parameters.MatchId,
    sessionId: parameters.SessionId,
    seed: parameters.Seed,
    tickRate: 20,
    mode: mode,
    contentHash: _manifest.ContentHash,
    players: new[]
    {
        new SimPlayerIdentity(parameters.AttackerPlayerId, parameters.AttackerAccountId),
        new SimPlayerIdentity(parameters.DefenderPlayerId, parameters.DefenderAccountId),
    },
    initialEntities: entities.ToArray(),
    inputDeadlineTicks: 6);
```

Each mapped defender uses its building grid coordinates, `SimTeam.Defender`, and
`parameters.DefenderPlayerId`. Seed value `0` is valid SimulationCore input and must not be replaced;
the authority provider is responsible for avoiding it in framework-generated parameters.

- [ ] **Step 4: Run factory and Session regression tests**

Run filters:

```text
ZWebGLFrameWork.Combat.Tests.BattleInitFactoryTests
ZWebGLFrameWork.SimulationCore.Tests
```

Expected: both Unity EditMode runs exit `0` with zero failures.

- [ ] **Step 5: Commit Task 2**

```bash
git add Assets/Scripts/Combat/BattleInitFactory.cs \
  Assets/Scripts/Combat/BattleInitFactory.cs.meta \
  Assets/Scripts/Combat/Tests/BattleInitFactoryTests.cs \
  Assets/Scripts/Combat/Tests/BattleInitFactoryTests.cs.meta
git diff --cached --check
git commit -m "feat(combat): build strict session battle init"
```

---

### Task 3: Session-Only CombatManager Host

**Files:**
- Rewrite: `Assets/Scripts/Combat/CombatManager.cs`
- Rewrite: `Assets/Scripts/Combat/SimulationSyncBridge.cs`
- Create: `Assets/Scripts/Combat/Presentation/UnityEntityViewFactory.cs`
- Create: `Assets/Scripts/Combat/Presentation/UnityEntityViewFactory.cs.meta`
- Modify: `Assets/Scripts/Combat/Properties/AssemblyInfo.cs`
- Add existing draft: `Assets/Tests/Editor/SimulationArchitectureTests.cs`
- Add existing draft: `Assets/Tests/Editor/SimulationArchitectureTests.cs.meta`
- Add existing draft: `Assets/Tests/PlayMode.meta`
- Add existing draft: `Assets/Tests/PlayMode/Combat.meta`
- Add existing draft: `Assets/Tests/PlayMode/Combat/ZWebGLFrameWork.Combat.PlayModeTests.asmdef`
- Add existing draft: `Assets/Tests/PlayMode/Combat/ZWebGLFrameWork.Combat.PlayModeTests.asmdef.meta`
- Modify existing draft: `Assets/Tests/PlayMode/Combat/SimulationBattleFlowPlayModeTests.cs`
- Add existing draft: `Assets/Tests/PlayMode/Combat/SimulationBattleFlowPlayModeTests.cs.meta`

**Interfaces:**
- Produces: `UniTask<SimBattleResult> StartBattleAsync(BattleParams, CancellationToken)`.
- Produces: `CommandSubmitResult SubmitDeployUnit(Vector2Int, int)`, `SubmitMove(int, Vector2Int)`, `SubmitSkill(int, int, Vector2Int)`, and `EndDeployPhase()`.
- Produces: `SimCombatPhase CurrentPhase`, `SimBattleResult LastResult`, tick/count/mode display properties, `StopBattle()`, and internal test configuration/status.

- [ ] **Step 1: Correct and run the existing red tests**

In `SessionPlayModeFixture`, change the defender Cannon from configuration `TypeId = 2` to
`TypeId = 5`. Replace its duplicate inline manifest with `FrameworkBattleContent.CanonicalBytes()`.
Keep both named PlayMode tests and the recording view factory.

Run the architecture and both PlayMode filters from the migration plan against this worktree.

Expected red evidence:

- architecture XML lists `CombatManager.cs` and `SimulationSyncBridge.cs`;
- PlayMode compilation reports missing Session-only manager methods until the rewrite exists.

- [ ] **Step 2: Implement the Unity view adapter**

`UnityEntityViewFactory` wraps a prefab instance in `IEntityView`. Load
`Resources/Config/ViewConfig`; use `ViewConfig.GetPrefab(archetypeId)` when present. When no prefab is
configured, create a primitive placeholder named from `EntityPresentationKey` and annotate that line
with `TODO(PRODUCTION-CONTENT)`. `Destroy` must tolerate null and call `UnityEngine.Object.Destroy`.

The nested adapter implements only:

```csharp
public void SetPosition(float x, float y)
{
    _gameObject.transform.position = new Vector3(x, 0f, y);
}
```

- [ ] **Step 3: Rewrite CombatManager around one Session**

Remove all imports, fields, methods, and branches involving Legacy ECS, `SimWorld`,
`SimOrchestrator`, `SimulationSyncBridge`, `CombatMode`, `UnitSpawner`, pathfinding, and AI.

Retain `SingletonDontDestroy<CombatManager>` and implement these state fields:

```csharp
private const float TickInterval = 1f / 20f;
private ISimBattleSessionFactory _sessionFactory;
private ISimBattleSession _session;
private BattleInitFactory _battleInitFactory;
private SessionPresentationBridge _presentation;
private UniTaskCompletionSource<SimBattleResult> _completion;
private byte[] _canonicalManifestBytes;
private ISimLogger _logger;
private float _accumulator;
private int _nextSequence = 1;
private int _attackerPlayerId;
private SimModeProfile _modeProfile;

public SimCombatPhase CurrentPhase { get; private set; } = SimCombatPhase.Idle;
public SimBattleResult LastResult { get; private set; }
public int CompletedTick { get; private set; } = -1;
public int ActiveViewCount => _presentation?.ViewCount ?? 0;
internal bool HasActiveSession => _session != null;
public int MaxDeployCount => _modeProfile?.MaxDeployCount ?? 0;
public int DeployDurationTicks => _modeProfile?.DeployDurationTicks ?? 0;
public int RemainingBattleTicks => _modeProfile == null
    ? 0 : Math.Max(0, _modeProfile.TimeoutTicks - Math.Max(0, CompletedTick));
public int AttackerAliveCount => CountAlive(SimTeam.Attacker);
public int DefenderAliveCount => CountAlive(SimTeam.Defender);
public int CurrentWave => _modeProfile == null
    ? 0 : _modeProfile.WaveTicks.Count(tick => tick <= CompletedTick);
```

`Awake` configures a `SimBattleSessionFactory`, framework canonical bytes,
`BattleInitFactory`, loads `Resources.Load<ViewConfig>("Config/ViewConfig")`, and constructs
`SessionPresentationBridge(new UnityEntityViewFactory(viewConfig))` after calling `base.Awake()`.

Implement startup using exact ownership order:

```csharp
public async UniTask<SimBattleResult> StartBattleAsync(BattleParams parameters,
    CancellationToken cancellationToken)
{
    StopBattle();
    cancellationToken.ThrowIfCancellationRequested();
    BattleInit init = _battleInitFactory.Create(parameters);
    ISimBattleSession candidate = _sessionFactory.Create(_canonicalManifestBytes, _logger);
    StartResult started = candidate.Start(init);
    if (!started.Accepted)
    {
        candidate.Dispose();
        throw new InvalidOperationException("Session start rejected: " + started.Code);
    }

    _session = candidate;
    _attackerPlayerId = parameters.AttackerPlayerId;
    _modeProfile = FrameworkBattleContent.GetModeProfile(parameters.ModeId);
    _nextSequence = 1;
    _accumulator = 0f;
    LastResult = null;
    CompletedTick = -1;
    SetPhase(_session.State.Phase);
    _presentation.Apply(started.InitialFrame);
    var completion = new UniTaskCompletionSource<SimBattleResult>();
    _completion = completion;
    using (cancellationToken.Register(() => CancelBattle(completion, cancellationToken)))
        return await completion.Task;
}
```

`Update` adds `Time.deltaTime`, ticks while the accumulator is at least `TickInterval`, applies
`TickResult.Presentation`, copies `CompletedTick`, publishes phase changes, and resolves a non-null
terminal result exactly once before disposing the Session and cleaning presentation views. Sample
presentation interpolation using `_accumulator / TickInterval` after ticking. Wrap the tick loop in
one `try/catch`; an unexpected exception must call `TrySetException`, log once, and use the same
cleanup routine as cancellation before returning.

`CountAlive` reads the current full `Session.State.Presentation.Entities` snapshot and counts rows
whose `TeamId` matches and whose `Health` is greater than zero. It returns zero without an active
Session. Do not derive these counts from delta frames.

All command methods return `WrongPhase` when no Session is active and otherwise create their exact
command struct with `Tick = _session.State.NextTick`, the attacker player ID, and `_nextSequence++`.
Do not increment sequence after a locally rejected no-Session call.

`CancelBattle` first checks `ReferenceEquals(_completion, completion)`, calls
`completion.TrySetCanceled(token)`, then performs idempotent cleanup. `StopBattle` cancels any pending
completion before cleanup, clears views/timing/sequence/session references, and leaves a previously
published terminal `LastResult` available until the next start. `OnDestroy` calls `StopBattle`
before `base.OnDestroy()`.

Add:

```csharp
[assembly: InternalsVisibleTo("ZWebGLFrameWork.Combat.Tests")]
[assembly: InternalsVisibleTo("ZWebGLFrameWork.Combat.PlayModeTests")]
```

`ConfigureForTests` rejects null dependencies or an active Session, clones manifest bytes, and
constructs fresh factory/presentation collaborators.

Rewrite `SimulationSyncBridge` so it no longer consumes `SimWorld`. Keep the filename until Task 17,
mark the type `[Obsolete("Use SessionPresentationBridge.")]`, and delegate `Apply(PresentationFrame)`,
`Sample(float)`, `ViewCount`, and `Cleanup()` to a constructor-injected
`SessionPresentationBridge`. This preserves the approved file-deletion boundary without leaving a
production architecture bypass.

- [ ] **Step 4: Run architecture and PlayMode tests to green**

Run the three exact Task 15 test commands, writing:

```text
/tmp/zweb-sim-migration/task-15/architecture-green.xml
/tmp/zweb-sim-migration/task-15/play-green.xml
/tmp/zweb-sim-migration/task-15/battle-cancel-green.xml
```

Expected: architecture has zero offenders; end-to-end completion succeeds; cancellation leaves no
active Session, views, or late result.

- [ ] **Step 5: Commit Task 3**

Stage only the files listed in this task, audit Unity metadata, inspect the staged diff, then commit:

```bash
git commit -m "feat(combat): host battles through simulation session"
```

---

### Task 4: Route FSM And UI Through Session Contracts

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/MainVillageState.cs`
- Modify: `Assets/Scripts/FSM/Game/States/BattleState.cs`
- Modify: `Assets/Scripts/FSM/ZWebGLFrameWork.FSM.asmdef`
- Modify: `Assets/Scripts/UI/Panels/DeployPanel.cs`
- Modify: `Assets/Scripts/UI/Panels/BattleHUD.cs`
- Modify: `Assets/Scripts/UI/Panels/ResultPanel.cs`
- Modify: `Assets/Scripts/UI/ZWebGLFrameWork.UI.asmdef`

**Interfaces:**
- Consumes: complete authority parameters and the Session-only manager surface from Tasks 1-3.
- Produces: state-lifetime cancellation and UI based only on `SimCombatPhase`/`SimBattleResult`.

- [ ] **Step 1: Keep the cancellation PlayMode test red while switching BattleState**

The existing `BattleState_ExitBeforeStartCompletes_CancelsLateStart` test is the acceptance test.
Run it immediately before production edits and retain its failing XML/log as
`battle-state-routing-red`.

- [ ] **Step 2: Route MainVillageState and BattleState**

`MainVillageState.OnAttackClicked` must construct parameters through one
`FrameworkBattleAuthority` instance:

```csharp
BattleParams parameters = _battleAuthority.CreateVillageAttack(
    DataManager.Instance.Player, village, village);
ChangeState(GameState.Battle, parameters);
```

The shared village object is acceptable only as current local framework content; identities remain
distinct and the provider line carries the production authority marker. Do not reintroduce an
incomplete `BattleParams` factory.

`BattleState` stores an injected `CombatManager` and one `CancellationTokenSource` per entry:

```csharp
public BattleState() : this(CombatManager.Instance) { }
internal BattleState(CombatManager combatManager) : base(GameState.Battle) =>
    _combatManager = combatManager ?? throw new ArgumentNullException(nameof(combatManager));
```

On enter, subscribe to `SimCombatPhase` and `SimBattleResult`, allocate the token source, save, and
call `StartBattleAsync(parameters, token)`. Catch `OperationCanceledException` without logging an
error; on any other exception log once and return to Main Village only if the same lifetime is still
active. On exit, cancel/dispose the token source, call `_combatManager.StopBattle()`, unsubscribe,
close the three panels, and then call `base.OnExit()`.

Remove `CreateMode`, `CombatMode`, `CombatPhase`, and `CombatEndEvent` usage. Add
`ZWebGLFrameWork.SimulationCore` to the FSM asmdef references.

- [ ] **Step 3: Route battle panels to Session data**

Add `ZWebGLFrameWork.SimulationCore` to the UI asmdef.

`DeployPanel` checks `SimCombatPhase.Deploying`, displays remaining deployment seconds from
`Mathf.Max(0, manager.DeployDurationTicks - manager.CompletedTick) / 20`, limits deployments with
`manager.MaxDeployCount`, maps selected config ID through `FrameworkBattleContent`, and increments
the displayed count only when `manager.SubmitDeployUnit(gridPos, archetypeId).Accepted`.

`BattleHUD` checks `SimCombatPhase.Battling`, displays remaining seconds from
`manager.RemainingBattleTicks / 20`, and reads `CurrentWave`, `AttackerAliveCount`, and
`DefenderAliveCount` from manager Session-derived properties.

`ResultPanel.BindResult` accepts only `SimBattleResult`, considers
`WinnerTeamId == SimTeam.Attacker` a win, clamps `Stars`, and displays its loot values. Remove all
legacy result/team imports.

- [ ] **Step 4: Run FSM/UI PlayMode and compilation tests**

Run both Task 15 PlayMode filters plus the EditMode compilation baseline. Expected: all exit `0`,
zero failed tests, and no C# compiler error in any log.

- [ ] **Step 5: Commit Task 4**

```bash
git add Assets/Scripts/FSM/Game/States/MainVillageState.cs \
  Assets/Scripts/FSM/Game/States/BattleState.cs \
  Assets/Scripts/FSM/ZWebGLFrameWork.FSM.asmdef \
  Assets/Scripts/UI/Panels/DeployPanel.cs \
  Assets/Scripts/UI/Panels/BattleHUD.cs \
  Assets/Scripts/UI/Panels/ResultPanel.cs \
  Assets/Scripts/UI/ZWebGLFrameWork.UI.asmdef
git diff --cached --check
git commit -m "feat(combat): route battle state and ui through session"
```

---

### Task 5: Remove Feature Flag And Prove The Cutover

**Files:**
- Modify: `Assets/Scripts/Data/AppConfig.cs`
- Modify: `Assets/Tests/Editor/RepositoryIntegrityTests.cs`

**Interfaces:**
- Produces: no `UseSimulationCore` toggle and repository-level enforcement of framework replacement markers.

- [ ] **Step 1: Add the repository marker gate**

Add a test that reads the two provider files and asserts that each required literal marker is present:

```csharp
[Test]
public void FrameworkBattleDefaults_KeepProductionReplacementMarkers()
{
    string combat = Path.Combine(Application.dataPath, "Scripts/Combat");
    Assert.That(File.ReadAllText(Path.Combine(combat, "FrameworkBattleAuthority.cs")),
        Does.Contain("TODO(PRODUCTION-AUTHORITY)"));
    Assert.That(File.ReadAllText(Path.Combine(combat, "FrameworkBattleContent.cs")),
        Does.Contain("TODO(PRODUCTION-CONTENT)"));
}
```

Run this single test before editing providers. Expected: pass because Tasks 1 and 3 intentionally
installed the markers. Temporarily remove either marker only in the working copy, rerun and verify
the test fails, then restore it and rerun green; record both XML files to prove the gate is effective.

- [ ] **Step 2: Remove the obsolete feature flag**

Delete `_useSimulationCore` and `UseSimulationCore` from `AppConfig`. Run:

```bash
rg -n "UseSimulationCore|_useSimulationCore|StartSimBattle|StartBattleAsync\(CombatMode|\bSimWorld\b|\bSimOrchestrator\b" \
  Assets/Scripts --glob '!Assets/Scripts/SimulationCore/**'
```

Expected: no feature flag or old entry point; `SimWorld`/`SimOrchestrator` appear only in files under
`Assets/Scripts/SimulationCore/`. `SimulationSyncBridge.cs` remains as the Session presentation
delegate defined in Task 3; Task 17 owns its deletion together with other legacy combat files.

- [ ] **Step 3: Run the full Unity verification matrix**

Run fresh, separate Unity invocations for:

```text
ZWebGLFrameWork.Editor.Tests.SimulationArchitectureTests
ZWebGLFrameWork.Editor.Tests.RepositoryIntegrityTests
ZWebGLFrameWork.Combat.Tests
ZWebGLFrameWork.SimulationCore.Tests
ZWebGLFrameWork.Combat.PlayModeTests
```

For each invocation, require process exit `0`, an XML report with `failed="0"`, and no
`error CS####` line in the log. Do not infer PlayMode success from EditMode success.

- [ ] **Step 4: Audit requirements and working-tree scope**

Run:

```bash
git diff --check
git status --short
rg -n "TODO\(PRODUCTION-(AUTHORITY|CONTENT)\)" Assets/Scripts/Combat Assets/Tests/Editor
```

Confirm every approved design requirement maps to a passing test or inspected source invariant.
List scene interaction as unverified unless a real Launcher-to-battle manual run was performed.

- [ ] **Step 5: Commit the final Task 15 gate**

```bash
git add Assets/Scripts/Data/AppConfig.cs \
  Assets/Tests/Editor/RepositoryIntegrityTests.cs
git diff --cached --check
git commit -m "test(combat): enforce session-only production path"
```

Before integration, run the same complete verification matrix once more against the exact final
commit.
