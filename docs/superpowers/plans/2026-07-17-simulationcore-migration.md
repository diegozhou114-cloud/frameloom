# SimulationCore Single-Authority Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the dual Legacy/SimulationCore combat paths with one host-neutral, deterministic `ISimBattleSession`, prove all 38 assigned audit issues with red-green evidence, and delete Legacy Combat after the replacement gates pass.

**Architecture:** SimulationCore owns all mutable battle state behind a single-use Session. Unity, ReplayValidator, frame synchronization, recording, and presentation exchange only versioned inputs and immutable outputs; deterministic content, commands, snapshots, presentation frames, and results share canonical contracts across Unity 2022.3.62f3c1 and .NET 8. Migration proceeds from contracts to deterministic state, commands and rules, presentation, production integration, and finally physical Legacy removal.

**Tech Stack:** Unity `2022.3.62f3c1`, C# 9, NUnit, UniTask, .NET 8, SHA-256, custom fixed little-endian codecs, Unity Test Framework.

## Global Constraints

- Keep `Assets/Scripts/SimulationCore/ZWebGLFrameWork.SimulationCore.asmdef` at `noEngineReferences: true`; no file under `Assets/Scripts/SimulationCore/` may reference `UnityEngine`, Unity collections, `Time`, or Unity serialization.
- SimulationCore APIs must compile from the same source under Unity `2022.3.62f3c1` and `net8.0`; deterministic code uses integers, `LogicInt`, fixed little-endian encoding, and UTF-8 with unsigned length prefixes.
- `ISimBattleSession` is single-use: `Created -> Running -> Ended -> Disposed`; invalid calls return typed results and do not mutate state.
- Initial state is `CompletedTick=-1, NextTick=0`; a snapshot after tick `T` restores with `NextTick=T+1`.
- Commands use `(tick, playerId, sequence)` total order, a 64-tick future window, immutable sealed ticks, and authority-only NoOp creation.
- `village_attack`, `builder_base`, `clan_war`, and `campaign` are the complete supported mode set for this plan.
- Production code outside SimulationCore must not construct, read, or mutate `SimWorld` or `SimOrchestrator` after Task 15.
- Every task follows red-green: capture the named failure, implement only the required behavior, rerun the focused checks, then run the listed broader check.
- Commands below assume the worktree is `/Users/diegozhou/Documents/unityframework/zweb-audit-fix` and Unity is `/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity`.
- Test/build output goes to `/tmp/zweb-sim-migration/`; do not write generated evidence into the repository.
- Task 17 generates `/tmp/zweb-sim-migration/closure-input.tsv` from the explicit 38-row closure specification in this plan. Its five pipe-delimited columns are exactly `issue|red-file|green-file|fix-commit|Fixed-or-RemovedByReplacement`; hand-edited or incrementally appended manifests are forbidden. When a contract task's behavioral proof lands in a later task, use the later green artifact and the first commit that completes the acceptance behavior.
- Do not close an issue from a benchmark, skipped test, inconclusive test, source comment, or unexecuted test name.
- A compiler error or Unity log without a structured test case is diagnostic evidence only. Before advancing from red, arrange the minimum compile-neutral test seam needed for the named acceptance method to execute and produce a non-empty NUnit XML or TRX result whose matching case is failed; Task 17 rejects compile-only red artifacts.

---

## Mandatory Unity metadata staging gate

Before Task 1, create this external audit helper exactly once. It does not modify the repository. Every task that changes anything under `Assets/` must stage with `git add -A -- <that task's exact Assets paths>`, then invoke this helper with its task directory before committing. The helper rejects a changed Unity asset without an indexed `.meta`, an added asset or directory without an added `.meta`, a deleted asset without its paired `.meta` deletion, an orphaned added `.meta`, and a removed directory whose directory `.meta` remains indexed.

```bash
mkdir -p /tmp/zweb-sim-migration
cat > /tmp/zweb-sim-migration/audit-unity-meta.sh <<'BASH'
#!/usr/bin/env bash
set -euo pipefail

task_dir="${1:?usage: audit-unity-meta.sh TASK_DIR}"
mkdir -p "$task_dir"
report="$task_dir/meta-audit.txt"
: > "$report"
failures=0

fail() {
  printf 'FAIL %s\n' "$1" | tee -a "$report"
  failures=$((failures + 1))
}

index_has() { git cat-file -e ":$1" 2>/dev/null; }
head_has() { git cat-file -e "HEAD:$1" 2>/dev/null; }

while IFS=$'\t' read -r status path; do
  test -n "$path" || continue
  case "$path" in *.meta) continue ;; esac
  meta="$path.meta"
  case "$status" in
    A|M|T)
      index_has "$meta" || fail "$status $path missing indexed $meta"
      ;;
    D)
      head_has "$meta" || fail "D $path had no tracked $meta before deletion"
      if index_has "$meta"; then fail "D $path left $meta indexed"; fi
      ;;
    *) fail "unsupported staged status $status for $path" ;;
  esac

  dir="${path%/*}"
  while [[ "$dir" == Assets/* ]]; do
    if [[ "$status" == D ]]; then
      if ! git ls-files --cached -- "$dir/" | grep -q . && head_has "$dir.meta" && index_has "$dir.meta"; then
        fail "deleted directory $dir left $dir.meta indexed"
      fi
    elif ! index_has "$dir.meta"; then
      fail "$status $path missing directory metadata $dir.meta"
    fi
    dir="${dir%/*}"
  done
done < <(git diff --cached --name-status --no-renames -- Assets)

while IFS=$'\t' read -r status meta; do
  test -n "$meta" || continue
  asset="${meta%.meta}"
  case "$status" in
    A|M|T)
      if [[ ! -e "$asset" ]] && ! index_has "$asset" && ! git ls-files --cached -- "$asset/" | grep -q .; then
        fail "$status $meta is orphaned from $asset"
      fi
      ;;
    D)
      if index_has "$asset" || git ls-files --cached -- "$asset/" | grep -q .; then
        fail "D $meta while paired asset $asset remains indexed"
      fi
      ;;
    *) fail "unsupported staged status $status for $meta" ;;
  esac
done < <(git diff --cached --name-status --no-renames -- 'Assets/**/*.meta' 'Assets/*.meta')

printf 'failures=%d\n' "$failures" | tee -a "$report"
test "$failures" -eq 0
BASH
chmod +x /tmp/zweb-sim-migration/audit-unity-meta.sh
```

The per-task audit receipt must be non-empty and end in `failures=0`; Task 17 reruns the same helper over the complete staged deletion set. Do not use `git add Assets` as a recovery shortcut: stage only the paths listed by the current task so unrelated user changes cannot enter its commit.

---

## Target File Structure

### New SimulationCore files

- `Assets/Scripts/SimulationCore/Session/ISimBattleSession.cs`: the only mutable battle boundary and its factory.
- `Assets/Scripts/SimulationCore/Session/BattleInit.cs`: serializable match, player, initial-entity, and mode inputs.
- `Assets/Scripts/SimulationCore/Session/SessionResults.cs`: lifecycle, start, tick, command, and restore result codes.
- `Assets/Scripts/SimulationCore/Session/SimSnapshot.cs`: immutable identity-bound snapshot envelope.
- `Assets/Scripts/SimulationCore/Session/TickResult.cs`: immutable completed-tick output and canonical final result.
- `Assets/Scripts/SimulationCore/Session/SimBattleSession.cs`: lifecycle, command admission, ticking, snapshots, rollback, and disposal.
- `Assets/Scripts/SimulationCore/Session/SimBattleSessionFactory.cs`: content-bound Session construction used by every host.
- `Assets/Scripts/SimulationCore/Content/DeterministicContentManifest.cs`: canonical content rows, bytes, and SHA-256.
- `Assets/Scripts/SimulationCore/Core/DeterministicBinary.cs`: bounds-checked little-endian writer/reader.
- `Assets/Scripts/SimulationCore/Core/DeterministicHash64.cs`: checksum primitive with explicit integer/byte operations.
- `Assets/Scripts/SimulationCore/Core/SimComponentSchema.cs`: versioned component IDs independent of registration order.
- `Assets/Scripts/SimulationCore/Core/SimPath.cs`: immutable, deep-copyable, hashable waypoint value.
- `Assets/Scripts/SimulationCore/Core/SessionSnapshotCodec.cs`: complete deterministic Session snapshot encoding.
- `Assets/Scripts/SimulationCore/Command/CommandKey.cs`: `(tick, playerId, sequence)` ordering.
- `Assets/Scripts/SimulationCore/Command/CommandExecutionResult.cs`: immutable per-command execution outcome.
- `Assets/Scripts/SimulationCore/Modes/SimModeRules.cs`: approved four-mode behavior matrix and deterministic result rules.
- `Assets/Scripts/SimulationCore/Systems/SimTargetSelectionSystem.cs`: stable attacker and defender targeting.
- `Assets/Scripts/SimulationCore/Systems/SimPathPlanningSystem.cs`: deterministic path assignment inside Core.
- `Assets/Scripts/SimulationCore/Presentation/PresentationFrame.cs`: immutable full frames, deltas, and timeline revisions.
- `Assets/Scripts/SimulationCore/Properties/AssemblyInfo.cs`: test-only `InternalsVisibleTo` declarations.

### New host and test files

- `Server/SimulationCore/ZWebGLFrameWork.SimulationCore.csproj`: one .NET 8 project linking the Unity-owned Core source.
- `Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj`: NUnit cross-runtime tests.
- `Server/SimulationCore.Tests/ArchitectureTests.cs`: host-neutral source and assembly checks.
- `Server/SimulationCore.Tests/GoldenDeterminismTests.cs`: canonical content, command, checksum, and replay vectors.
- `Assets/Tests/SimulationCore/SessionContractTests.cs`: Session lifecycle and immutable contract tests.
- `Assets/Tests/SimulationCore/SessionTestDriver.cs`: shared deterministic manifests, BattleInit values, and Session fixtures.
- `Assets/Tests/SimulationCore/ContentManifestTests.cs`: canonical bytes and component schema tests.
- `Assets/Tests/SimulationCore/SessionDeterminismTests.cs`: checksum, snapshots, rollback, and command tests.
- `Assets/Tests/SimulationCore/ModeBehaviorTests.cs`: approved four-mode matrix tests.
- `Assets/Tests/SimulationCore/PresentationFrameTests.cs`: immutable frame and revision tests.
- `Assets/Scripts/Combat/Presentation/IEntityViewFactory.cs`: Unity view creation/destruction seam.
- `Assets/Scripts/Combat/Presentation/SessionPresentationBridge.cs`: consumes `PresentationFrame`, never `SimWorld`.
- `Assets/Scripts/Combat/Presentation/PresentationInterpolator.cs`: adjacent-tick interpolation and revision reset.
- `Assets/Scripts/Combat/BattleInitFactory.cs`: maps `BattleParams` and authoritative content to `BattleInit`.
- `Assets/Tests/PlayMode/Combat/SimulationBattleFlowPlayModeTests.cs`: production-path deployment-to-cleanup proof.
- `Assets/Tests/PlayMode/Combat/ZWebGLFrameWork.Combat.PlayModeTests.asmdef`: non-Editor PlayMode assembly.
- `Assets/Editor/SimulationBenchmark.cs`: manual benchmark runner outside correctness tests.
- `Assets/Tests/SimulationCore/SimulationBenchmarkFixtureTests.cs`: validates benchmark workload only.

### Files removed in Task 17

- `Assets/Scripts/Combat/AI/`
- `Assets/Scripts/Combat/Components/`
- `Assets/Scripts/Combat/Core/`
- `Assets/Scripts/Combat/Events/`
- `Assets/Scripts/Combat/Modes/`
- `Assets/Scripts/Combat/Spawner/`
- `Assets/Scripts/Combat/Systems/`
- `Assets/Scripts/Combat/SimInitializerImpl.cs`

---

### Task 1: Freeze the Session, BattleInit, TickResult, and Presentation contracts

**Closes:** COMBAT-002, SIM-003, SIM-019

**Files:**
- Create: `Assets/Scripts/SimulationCore/Session/ISimBattleSession.cs`
- Create: `Assets/Scripts/SimulationCore/Session/BattleInit.cs`
- Create: `Assets/Scripts/SimulationCore/Session/SessionResults.cs`
- Create: `Assets/Scripts/SimulationCore/Session/SimSnapshot.cs`
- Create: `Assets/Scripts/SimulationCore/Session/TickResult.cs`
- Create: `Assets/Scripts/SimulationCore/Command/CommandExecutionResult.cs`
- Create: `Assets/Scripts/SimulationCore/Presentation/PresentationFrame.cs`
- Create: `Assets/Tests/SimulationCore/SessionContractTests.cs`

**Interfaces:**
- Consumes: existing `ISimCommand`, `SimCombatPhase`, and `ISimLogger` only as migration inputs.
- Produces: `ISimBattleSession`, `ISimBattleSessionFactory`, `BattleInit`, typed lifecycle results, immutable `TickResult`, `PresentationFrame`, and `SimBattleResult`.

- [ ] **Step 1: Write the failing contract tests**

```csharp
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class SessionContractTests
    {
        [Test]
        public void SessionContract_UsesTypedLifecycleResultsAndImmutableOutputs()
        {
            Assert.That(typeof(ISimBattleSession).GetMethod(nameof(ISimBattleSession.Start)).ReturnType,
                Is.EqualTo(typeof(StartResult)));
            Assert.That(typeof(ISimBattleSession).GetMethod(nameof(ISimBattleSession.RestoreSnapshot)).ReturnType,
                Is.EqualTo(typeof(SnapshotRestoreResult)));
            Assert.That(typeof(TickResult).GetProperty(nameof(TickResult.Presentation)).CanWrite, Is.False);
            Assert.That(typeof(SimBattleState).GetProperty(nameof(SimBattleState.NextTick)).CanWrite, Is.False);
        }

        [Test]
        public void BattleInit_UsesRequestedDefenderAndSeed()
        {
            var entity = new InitialEntitySpec(7, 21, 9, SimTeam.Defender, 1);
            var init = new BattleInit(
                1, 1, "match-001", "session-001", 8675309, 20,
                SimCombatMode.VillageAttack, new byte[32],
                new[] { new SimPlayerIdentity(0, "attacker"), new SimPlayerIdentity(1, "defender") },
                new[] { entity }, 6);

            Assert.That(init.Seed, Is.EqualTo(8675309));
            Assert.That(init.InitialEntities[0].OwnerPlayerId, Is.EqualTo(1));
            Assert.That(init.InitialEntities[0].GridX, Is.EqualTo(21));
        }
    }
}
```

- [ ] **Step 2: Run the tests and capture the red result**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-01
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SessionContractTests -testResults /tmp/zweb-sim-migration/task-01/red.xml -logFile /tmp/zweb-sim-migration/task-01/red.log
```

Expected: Unity compilation fails because the Session boundary and immutable result types do not exist.

- [ ] **Step 3: Add the exact Session boundary**

```csharp
using System;

namespace ZWebGLFrameWork.SimulationCore
{
    public interface ISimBattleSession : IDisposable
    {
        SimBattleState State { get; }
        StartResult Start(BattleInit init);
        CommandSubmitResult Submit(ISimCommand command);
        TickResult Tick();
        SimSnapshot CaptureSnapshot();
        SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot);
    }

    public interface ISimBattleSessionFactory
    {
        ISimBattleSession Create(byte[] canonicalManifestBytes, ISimLogger logger = null);
    }
}
```

Add `BattleInit.cs` with immutable, cloning constructors:

```csharp
using System;

namespace ZWebGLFrameWork.SimulationCore
{
    public enum SimCombatMode : byte { VillageAttack = 1, BuilderBase = 2, ClanWar = 3, Campaign = 4 }

    public readonly struct SimPlayerIdentity
    {
        public int PlayerId { get; }
        public string AccountId { get; }
        public SimPlayerIdentity(int playerId, string accountId)
        {
            PlayerId = playerId;
            AccountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        }
    }

    public readonly struct InitialEntitySpec
    {
        public int ArchetypeId { get; }
        public int GridX { get; }
        public int GridY { get; }
        public int TeamId { get; }
        public int OwnerPlayerId { get; }
        public InitialEntitySpec(int archetypeId, int gridX, int gridY, int teamId, int ownerPlayerId)
        {
            ArchetypeId = archetypeId;
            GridX = gridX;
            GridY = gridY;
            TeamId = teamId;
            OwnerPlayerId = ownerPlayerId;
        }
    }

    public sealed class BattleInit
    {
        public int ProtocolVersion { get; }
        public int SchemaVersion { get; }
        public string MatchId { get; }
        public string SessionId { get; }
        public int Seed { get; }
        public int TickRate { get; }
        public SimCombatMode Mode { get; }
        private readonly byte[] _contentHash;
        private readonly SimPlayerIdentity[] _players;
        private readonly InitialEntitySpec[] _initialEntities;
        public byte[] ContentHash => (byte[])_contentHash.Clone();
        public SimPlayerIdentity[] Players => (SimPlayerIdentity[])_players.Clone();
        public InitialEntitySpec[] InitialEntities => (InitialEntitySpec[])_initialEntities.Clone();
        public int InputDeadlineTicks { get; }

        public BattleInit(int protocolVersion, int schemaVersion, string matchId, string sessionId,
            int seed, int tickRate, SimCombatMode mode, byte[] contentHash,
            SimPlayerIdentity[] players, InitialEntitySpec[] initialEntities, int inputDeadlineTicks)
        {
            ProtocolVersion = protocolVersion;
            SchemaVersion = schemaVersion;
            MatchId = matchId ?? throw new ArgumentNullException(nameof(matchId));
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            Seed = seed;
            TickRate = tickRate;
            Mode = mode;
            _contentHash = (byte[])(contentHash ?? throw new ArgumentNullException(nameof(contentHash))).Clone();
            _players = (SimPlayerIdentity[])(players ?? throw new ArgumentNullException(nameof(players))).Clone();
            _initialEntities = (InitialEntitySpec[])(initialEntities ?? throw new ArgumentNullException(nameof(initialEntities))).Clone();
            InputDeadlineTicks = inputDeadlineTicks;
        }
    }
}
```

Add these exact result codes to `SessionResults.cs`: `SessionLifecycle { Created, Running, Ended, Disposed }`, `StartCode { Accepted, AlreadyStarted, UnsupportedProtocol, UnsupportedSchema, InvalidMatch, InvalidContent, InvalidPlayer, InvalidEntity, InvalidMode }`, `TickCode { Advanced, NotRunning, Ended, Disposed }`, `CommandDisposition { Accepted, Duplicate, NonMonotonicSequence, LateTick, FutureWindowExceeded, SealedTick, UnknownPlayer, WrongPhase, InvalidPayload, UnauthorizedEntity, ClientNoOpForbidden }`, and `SnapshotRestoreCode { Restored, SnapshotUnavailable, Disposed, ForeignMatch, ForeignSession, UnsupportedSchema, ContentMismatch, StaleRevision, InvalidTick, Oversized, CorruptPayload }`.

Implement the result structs with these members:

```csharp
public readonly struct StartResult
{
    public StartCode Code { get; }
    public PresentationFrame InitialFrame { get; }
    public bool Accepted => Code == StartCode.Accepted;
    public StartResult(StartCode code, PresentationFrame initialFrame)
    {
        Code = code;
        InitialFrame = initialFrame;
    }
}

public readonly struct CommandSubmitResult
{
    public CommandDisposition Disposition { get; }
    public bool Accepted => Disposition == CommandDisposition.Accepted ||
        Disposition == CommandDisposition.Duplicate;
    public CommandSubmitResult(CommandDisposition disposition) => Disposition = disposition;
}

public readonly struct SnapshotRestoreResult
{
    public SnapshotRestoreCode Code { get; }
    public PresentationFrame FullFrame { get; }
    public bool Restored => Code == SnapshotRestoreCode.Restored;
    public SnapshotRestoreResult(SnapshotRestoreCode code, PresentationFrame fullFrame)
    {
        Code = code;
        FullFrame = fullFrame;
    }
}
```

Add the final snapshot envelope in Task 1 so the Session interface compiles from the first green step. Task 6 implements its codec and validation without changing this shape:

```csharp
public sealed class SimSnapshot
{
    private readonly byte[] _contentHash;
    private readonly byte[] _payload;
    private readonly byte[] _payloadSha256;
    public string MatchId { get; }
    public string SessionId { get; }
    public int SchemaVersion { get; }
    public byte[] ContentHash => (byte[])_contentHash.Clone();
    public int CompletedTick { get; }
    public int NextTick { get; }
    public long TimelineRevision { get; }
    public int UncompressedLength { get; }
    public ulong StateChecksum { get; }
    public byte[] Payload => (byte[])_payload.Clone();
    public byte[] PayloadSha256 => (byte[])_payloadSha256.Clone();

    public SimSnapshot(string matchId, string sessionId, int schemaVersion, byte[] contentHash,
        int completedTick, int nextTick, long timelineRevision, int uncompressedLength,
        ulong stateChecksum, byte[] payload, byte[] payloadSha256)
    {
        MatchId = matchId ?? throw new System.ArgumentNullException(nameof(matchId));
        SessionId = sessionId ?? throw new System.ArgumentNullException(nameof(sessionId));
        SchemaVersion = schemaVersion;
        _contentHash = (byte[])(contentHash ?? throw new System.ArgumentNullException(nameof(contentHash))).Clone();
        CompletedTick = completedTick;
        NextTick = nextTick;
        TimelineRevision = timelineRevision;
        UncompressedLength = uncompressedLength;
        StateChecksum = stateChecksum;
        _payload = (byte[])(payload ?? throw new System.ArgumentNullException(nameof(payload))).Clone();
        _payloadSha256 = (byte[])(payloadSha256 ?? throw new System.ArgumentNullException(nameof(payloadSha256))).Clone();
    }
}
```

Use this immutable Session summary and tick result shape:

```csharp
public sealed class SimBattleState
{
    public SessionLifecycle Lifecycle { get; }
    public SimCombatPhase Phase { get; }
    public int CompletedTick { get; }
    public int NextTick { get; }
    public long TimelineRevision { get; }
    public ulong Checksum { get; }
    public PresentationFrame Presentation { get; }
    public SimBattleResult Result { get; }

    public SimBattleState(SessionLifecycle lifecycle, SimCombatPhase phase, int completedTick,
        int nextTick, long timelineRevision, ulong checksum,
        PresentationFrame presentation, SimBattleResult result)
    {
        Lifecycle = lifecycle; Phase = phase; CompletedTick = completedTick; NextTick = nextTick;
        TimelineRevision = timelineRevision; Checksum = checksum;
        Presentation = presentation; Result = result;
    }
}

public sealed class TickResult
{
    public TickCode Code { get; }
    public int CompletedTick { get; }
    public int NextTick { get; }
    public long TimelineRevision { get; }
    public System.Collections.Generic.IReadOnlyList<ISimCommand> ExecutedCommands { get; }
    public System.Collections.Generic.IReadOnlyList<CommandExecutionResult> CommandOutcomes { get; }
    public System.Collections.Generic.IReadOnlyList<SimBattleEvent> Events { get; }
    public PresentationFrame Presentation { get; }
    public ulong Checksum { get; }
    public SimBattleResult Result { get; }

    public TickResult(TickCode code, int completedTick, int nextTick, long timelineRevision,
        ISimCommand[] executedCommands, CommandExecutionResult[] commandOutcomes,
        SimBattleEvent[] events,
        PresentationFrame presentation, ulong checksum, SimBattleResult result)
    {
        Code = code; CompletedTick = completedTick; NextTick = nextTick;
        TimelineRevision = timelineRevision;
        ExecutedCommands = System.Array.AsReadOnly((ISimCommand[])executedCommands.Clone());
        CommandOutcomes = System.Array.AsReadOnly((CommandExecutionResult[])commandOutcomes.Clone());
        Events = System.Array.AsReadOnly((SimBattleEvent[])events.Clone());
        Presentation = presentation; Checksum = checksum; Result = result;
    }
}
```

Use these exact outcome and result signatures:

```csharp
public readonly struct CommandExecutionResult
{
    public ISimCommand Command { get; }
    public CommandDisposition Disposition { get; }
    public int EntityId { get; }
    public int EntityGeneration { get; }
    public CommandExecutionResult(ISimCommand command, CommandDisposition disposition,
        int entityId, int entityGeneration)
    {
        Command = command;
        Disposition = disposition;
        EntityId = entityId;
        EntityGeneration = entityGeneration;
    }
}

public sealed class SimBattleResult
{
    public string MatchId { get; }
    public string ResultId { get; }
    public int WinnerTeamId { get; }
    public int Stars { get; }
    public int LootGold { get; }
    public int LootElixir { get; }
    public int FinalTick { get; }
    public ulong FinalChecksum { get; }

    public SimBattleResult(string matchId, string resultId, int winnerTeamId, int stars,
        int lootGold, int lootElixir, int finalTick, ulong finalChecksum)
    {
        MatchId = matchId ?? throw new System.ArgumentNullException(nameof(matchId));
        ResultId = resultId ?? throw new System.ArgumentNullException(nameof(resultId));
        WinnerTeamId = winnerTeamId;
        Stars = stars;
        LootGold = lootGold;
        LootElixir = lootElixir;
        FinalTick = finalTick;
        FinalChecksum = finalChecksum;
    }
}
```

Freeze the presentation payload in Task 1 as well; later tasks implement projection and Unity consumption without changing the network-visible fields:

```csharp
public readonly struct EntityPresentationKey
{
    public int EntityId { get; }
    public int Generation { get; }
    public EntityPresentationKey(int entityId, int generation)
    {
        EntityId = entityId;
        Generation = generation;
    }
}

public readonly struct EntityPresentationState
{
    public int EntityId { get; }
    public int Generation { get; }
    public int ArchetypeId { get; }
    public int OwnerPlayerId { get; }
    public int TeamId { get; }
    public int GridX { get; }
    public int GridY { get; }
    public long PosXRaw { get; }
    public long PosYRaw { get; }
    public int OrientationMilliDegrees { get; }
    public int Health { get; }

    public EntityPresentationState(int entityId, int generation, int archetypeId,
        int ownerPlayerId, int teamId, int gridX, int gridY,
        long posXRaw, long posYRaw, int orientationMilliDegrees, int health)
    {
        EntityId = entityId;
        Generation = generation;
        ArchetypeId = archetypeId;
        OwnerPlayerId = ownerPlayerId;
        TeamId = teamId;
        GridX = gridX;
        GridY = gridY;
        PosXRaw = posXRaw;
        PosYRaw = posYRaw;
        OrientationMilliDegrees = orientationMilliDegrees;
        Health = health;
    }
}

public sealed class PresentationFrame
{
    public bool IsFull { get; }
    public int CompletedTick { get; }
    public int NextTick { get; }
    public long TimelineRevision { get; }
    public System.Collections.Generic.IReadOnlyList<EntityPresentationState> Entities { get; }
    public System.Collections.Generic.IReadOnlyList<EntityPresentationState> Created { get; }
    public System.Collections.Generic.IReadOnlyList<EntityPresentationState> Updated { get; }
    public System.Collections.Generic.IReadOnlyList<EntityPresentationKey> Destroyed { get; }

    public PresentationFrame(bool isFull, int completedTick, int nextTick, long timelineRevision,
        EntityPresentationState[] entities, EntityPresentationState[] created,
        EntityPresentationState[] updated, EntityPresentationKey[] destroyed)
    {
        IsFull = isFull;
        CompletedTick = completedTick;
        NextTick = nextTick;
        TimelineRevision = timelineRevision;
        Entities = Freeze(entities);
        Created = Freeze(created);
        Updated = Freeze(updated);
        Destroyed = Freeze(destroyed);
    }

    private static System.Collections.Generic.IReadOnlyList<T> Freeze<T>(T[] values) =>
        System.Array.AsReadOnly((T[])(values ?? System.Array.Empty<T>()).Clone());
}
```

`SimBattleEvent` has get-only event type, source entity, target entity, and integer amount. `SimBattleState.Presentation` is always the current full projection; `TickResult.Presentation` is a full frame after Start/Restore and a delta after a normal Tick. Initial presentation is full with `CompletedTick=-1`, `NextTick=0`, and `TimelineRevision=0`.

- [ ] **Step 4: Run the focused contract tests**

Run the Step 2 command with output paths `/tmp/zweb-sim-migration/task-01/green.xml` and `/tmp/zweb-sim-migration/task-01/green.log`.

Expected: two tests pass and Unity reports no missing Session contract type.

- [ ] **Step 5: Commit the contract slice**

```bash
git add -A -- Assets/Scripts/SimulationCore/Session Assets/Scripts/SimulationCore/Presentation Assets/Scripts/SimulationCore/Command/CommandExecutionResult.cs Assets/Scripts/SimulationCore/Command/CommandExecutionResult.cs.meta Assets/Tests/SimulationCore/SessionContractTests.cs Assets/Tests/SimulationCore/SessionContractTests.cs.meta
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-01
git commit -m "feat(sim): define single battle session contracts"
```

---

### Task 2: Canonical content manifest and stable component schema

**Closes:** SIM-004, SIM-022

**Files:**
- Create: `Assets/Scripts/SimulationCore/Core/DeterministicBinary.cs`
- Create: `Assets/Scripts/SimulationCore/Core/SimComponentSchema.cs`
- Create: `Assets/Scripts/SimulationCore/Content/DeterministicContentManifest.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/ComponentTypeRegistry.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/SimChunkHelper.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/SimEntityArchetype.cs`
- Create: `Assets/Tests/SimulationCore/ContentManifestTests.cs`

**Interfaces:**
- Consumes: Task 1 `BattleInit.ContentHash` and the existing component structs.
- Produces: `DeterministicContentManifest.Parse(byte[])`, `ToCanonicalBytes()`, `ContentHash`, `GetArchetype(int)`, and `SimComponentSchema.CreateRegistry()`.

- [ ] **Step 1: Write schema-order and canonical-byte tests**

```csharp
using System.Linq;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class ContentManifestTests
    {
        [Test]
        public void ComponentRegistry_PermutedRegistration_PreservesTypeMapping()
        {
            var registrations = SimComponentSchema.Registrations.Reverse().ToArray();
            var registry = new ComponentTypeRegistry(registrations);
            Assert.That(registry.GetId(typeof(SimTransformComponent)), Is.EqualTo(SimComponentTypeId.Transform));
            Assert.That(registry.GetId(typeof(SimTargetComponent)), Is.EqualTo(SimComponentTypeId.Target));
        }

        [Test]
        public void ContentManifest_UnityAndDotNet_ProduceSameCanonicalHash()
        {
            var manifest = new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 20, 10000, 20)
            });
            byte[] bytes = manifest.ToCanonicalBytes();
            Assert.That(bytes.Take(8).ToArray(),
                Is.EqualTo(new byte[] { 0x5A, 0x53, 0x49, 0x4D, 0x01, 0x00, 0x00, 0x00 }));
            Assert.That(manifest.ContentHash.Length, Is.EqualTo(32));
            Assert.That(DeterministicContentManifest.Parse(bytes).ContentHash,
                Is.EqualTo(manifest.ContentHash));
        }
    }
}
```

- [ ] **Step 2: Run the focused tests and capture the red result**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-02
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.ContentManifestTests -testResults /tmp/zweb-sim-migration/task-02/red.xml -logFile /tmp/zweb-sim-migration/task-02/red.log
```

Expected: compilation fails because the explicit registrations and manifest codec do not exist.

- [ ] **Step 3: Implement explicit IDs and canonical bytes**

```csharp
using System;

namespace ZWebGLFrameWork.SimulationCore
{
    public static class SimComponentTypeId
    {
        public const int Transform = 1;
        public const int Health = 2;
        public const int Team = 3;
        public const int Movement = 4;
        public const int Attack = 5;
        public const int Target = 6;
        public const int Identity = 7;
    }

    public readonly struct ComponentRegistration
    {
        public Type Type { get; }
        public int Id { get; }
        public ComponentRegistration(Type type, int id) { Type = type; Id = id; }
    }

    public struct SimIdentityComponent : IHashableComponent
    {
        public int ArchetypeId;
        public int OwnerPlayerId;
        public ulong HashInto(ulong hash)
        {
            const ulong prime = 1099511628211UL;
            hash ^= unchecked((uint)ArchetypeId);
            hash *= prime;
            hash ^= unchecked((uint)OwnerPlayerId);
            hash *= prime;
            return hash;
        }
    }

    public static class SimComponentSchema
    {
        public const int Version = 1;
        public static readonly ComponentRegistration[] Registrations =
        {
            new ComponentRegistration(typeof(SimTransformComponent), SimComponentTypeId.Transform),
            new ComponentRegistration(typeof(SimHealthComponent), SimComponentTypeId.Health),
            new ComponentRegistration(typeof(SimTeamComponent), SimComponentTypeId.Team),
            new ComponentRegistration(typeof(SimMovementComponent), SimComponentTypeId.Movement),
            new ComponentRegistration(typeof(SimAttackComponent), SimComponentTypeId.Attack),
            new ComponentRegistration(typeof(SimTargetComponent), SimComponentTypeId.Target),
            new ComponentRegistration(typeof(SimIdentityComponent), SimComponentTypeId.Identity)
        };

        public static ComponentTypeRegistry CreateRegistry() => new ComponentTypeRegistry(Registrations);
    }
}
```

Change `ComponentTypeRegistry` to fill its maps from `ComponentRegistration.Id`, and throw `ArgumentException` for duplicate type, duplicate ID, IDs below one, or unknown component types. Change every `SimChunkHelper` branch to use the constants above and add the identity column.

Use this immutable content row:

```csharp
public sealed class SimArchetypeDefinition
{
    public int ArchetypeId { get; }
    public string Name { get; }
    public int MaxHealth { get; }
    public long SpeedRaw { get; }
    public int Damage { get; }
    public long RangeRaw { get; }
    public int CooldownTicks { get; }

    public SimArchetypeDefinition(int archetypeId, string name, int maxHealth,
        long speedRaw, int damage, long rangeRaw, int cooldownTicks)
    {
        ArchetypeId = archetypeId;
        Name = name;
        MaxHealth = maxHealth;
        SpeedRaw = speedRaw;
        Damage = damage;
        RangeRaw = rangeRaw;
        CooldownTicks = cooldownTicks;
    }
}
```

`DeterministicContentManifest` exposes `DeterministicContentManifest(int schemaVersion, SimArchetypeDefinition[] archetypes)`, clones and sorts the supplied rows, and rejects duplicate IDs before storing them.

`DeterministicBinaryWriter` must implement `WriteByte`, `WriteUInt32`, `WriteInt32`, `WriteInt64`, `WriteBytes`, and `WriteUtf8` by writing fixed little-endian bytes to a `MemoryStream`. The reader implements matching bounds-checked methods and an `AtEnd` property. `DeterministicContentManifest.ToCanonicalBytes()` writes magic bytes `5A 53 49 4D`, schema `1`, archetype count, and rows sorted by `ArchetypeId`; each row writes ID, name, health, speed raw, damage, range raw, and cooldown. `ContentHash` is `SHA256.Create().ComputeHash(ToCanonicalBytes())`. `Parse` rejects wrong magic/schema, duplicate or unsorted IDs, invalid lengths, and trailing bytes.

- [ ] **Step 4: Run the focused tests and verify green**

Run the Step 2 command with output paths `/tmp/zweb-sim-migration/task-02/green.xml` and `/tmp/zweb-sim-migration/task-02/green.log`.

Expected: two tests pass; reversing registration input does not change IDs; canonical bytes round-trip and produce a 32-byte hash.

- [ ] **Step 5: Commit content and schema**

```bash
git add Assets/Scripts/SimulationCore/Core Assets/Scripts/SimulationCore/Content Assets/Tests/SimulationCore/ContentManifestTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-02/meta
git commit -m "feat(sim): canonicalize content and component schema"
```

---

### Task 3: Restore host-neutral Unity and .NET compilation

**Closes:** SIM-001, SIM-002

**Files:**
- Create: `Server/SimulationCore/ZWebGLFrameWork.SimulationCore.csproj`
- Create: `Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj`
- Create: `Server/SimulationCore.Tests/ArchitectureTests.cs`
- Create: `Server/SimulationCore.Tests/ContentManifestGoldenTests.cs`
- Create: `Assets/Scripts/SimulationCore/Session/SimBattleSession.cs`
- Create: `Assets/Scripts/SimulationCore/Session/SimBattleSessionFactory.cs`
- Create: `Assets/Scripts/SimulationCore/Properties/AssemblyInfo.cs`
- Create: `Assets/Tests/SimulationCore/SessionTestDriver.cs`
- Modify: `Server/ReplayValidator/ReplayValidator.csproj`
- Modify: `Server/ReplayValidator/ReplayValidator.cs`
- Modify: `Server/ReplayValidator/Program.cs`
- Modify: `Assets/Scripts/SimulationCore/Orchestrator/SimOrchestrator.cs`
- Modify: `Assets/Scripts/SimulationCore/Network/FrameInput.cs`
- Move: `Assets/Scripts/SimulationCore/Network/KcpTransport.cs` to `Assets/Scripts/Network/Simulation/KcpTransport.cs`
- Modify: `Assets/Scripts/Network/ZWebGLFrameWork.Network.asmdef`

**Interfaces:**
- Consumes: Task 1 Session contracts and Task 2 manifest/schema.
- Produces: one `net8.0` Core project, a minimally functional single-use `SimBattleSession`, `SimBattleSessionFactory.Create(byte[], ISimLogger)`, and a ReplayValidator project reference instead of a second source glob.

- [ ] **Step 1: Add the .NET architecture and golden tests**

```csharp
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class ArchitectureTests
    {
        [Test]
        public void SimulationCore_HasNoUnityEngineReferences()
        {
            string root = Path.GetFullPath(System.Environment.GetEnvironmentVariable("ZWEB_REPO_ROOT")
                ?? Directory.GetCurrentDirectory());
            string core = Path.Combine(root, "Assets/Scripts/SimulationCore");
            string[] offenders = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("UnityEngine"))
                .Select(path => Path.GetRelativePath(root, path))
                .ToArray();
            Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
        }

        [Test]
        public void ReplayValidator_DotNet8_BuildsAgainstCurrentSimulationCore()
        {
            string root = Path.GetFullPath(System.Environment.GetEnvironmentVariable("ZWEB_REPO_ROOT")
                ?? Directory.GetCurrentDirectory());
            ProcessResult result = RunDotNet(root, "build",
                "Server/ReplayValidator/ReplayValidator.csproj",
                "--configuration", "Release",
                "--artifacts-path", "/tmp/zweb-sim-migration/task-03/contract-build",
                "--warnaserror");
            Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
        }

        private static ProcessResult RunDotNet(string root, params string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            using Process process = Process.Start(start)
                ?? throw new System.InvalidOperationException("failed to start dotnet");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(stdout, stderr);
            return new ProcessResult(process.ExitCode, stdout.Result + stderr.Result);
        }

        private sealed class ProcessResult
        {
            public int ExitCode { get; }
            public string Output { get; }

            public ProcessResult(int exitCode, string output)
            {
                ExitCode = exitCode;
                Output = output;
            }
        }
    }
}
```

For the red phase, create `ZWebGLFrameWork.SimulationCore.ServerTests.csproj` in this compile-neutral form. It compiles only `ArchitectureTests.cs`; therefore both named acceptance methods execute even while the production Core and ReplayValidator projects are broken.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="ArchitectureTests.cs" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" PrivateAssets="all" />
    <PackageReference Include="NUnit" Version="4.2.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Create the shared test driver now so every later task extends one type rather than inventing fixtures:

```csharp
namespace ZWebGLFrameWork.SimulationCore.Tests
{
public static partial class SessionTestDriver
{
    public static byte[] ManifestBytes()
    {
        return new DeterministicContentManifest(1, new[]
        {
            new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
            new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
            new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 20, 10000, 20)
        }).ToCanonicalBytes();
    }

    public static BattleInit BattleInit(int seed, InitialEntitySpec[] entities = null)
    {
        byte[] manifest = ManifestBytes();
        byte[] hash = DeterministicContentManifest.Parse(manifest).ContentHash;
        return new BattleInit(1, 1, "test-match", "test-session", seed, 20,
            SimCombatMode.VillageAttack, hash,
            new[] { new SimPlayerIdentity(0, "attacker"), new SimPlayerIdentity(1, "defender") },
            entities ?? new[]
            {
                new InitialEntitySpec(10, 2, 2, SimTeam.Attacker, 0),
                new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1)
            }, 6);
    }

    public static SimBattleSession StartedBattle(int seed = 42)
    {
        var session = (SimBattleSession)new SimBattleSessionFactory().Create(ManifestBytes());
        StartResult result = session.Start(BattleInit(seed));
        if (!result.Accepted) throw new System.InvalidOperationException(result.Code.ToString());
        return session;
    }

    public static BattleInit DeployingBattleInit(int seed = 42) =>
        BattleInit(seed, new[]
        {
            new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1)
        });

    public static SimBattleSession StartedDeployingBattle()
    {
        var session = (SimBattleSession)new SimBattleSessionFactory().Create(ManifestBytes());
        BattleInit init = DeployingBattleInit();
        StartResult result = session.Start(init);
        if (!result.Accepted) throw new System.InvalidOperationException(result.Code.ToString());
        return session;
    }
}
}
```

```csharp
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class ContentManifestGoldenTests
    {
        [Test]
        public void ContentManifest_UnityAndDotNet_ProduceSameCanonicalHash()
        {
            var manifest = new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 20, 10000, 20)
            });
            byte[] bytes = manifest.ToCanonicalBytes();
            Assert.That(bytes[0], Is.EqualTo(0x5A));
            Assert.That(bytes[4], Is.EqualTo(0x01));
            Assert.That(manifest.ContentHash.Length, Is.EqualTo(32));
        }
    }
}
```

After capturing red evidence, create the shared test driver and golden test above, then use this exact Core project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>ZWebGLFrameWork.SimulationCore</RootNamespace>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../Assets/Scripts/SimulationCore/**/*.cs" Link="SimulationCore/%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

Replace the compile-neutral test project with this final form after the production fix. It retains the two process/source acceptance methods and adds the actual Core/ReplayValidator references plus all remaining server tests.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../SimulationCore/ZWebGLFrameWork.SimulationCore.csproj" />
    <ProjectReference Include="../ReplayValidator/ReplayValidator.csproj" />
    <Compile Include="../../Assets/Tests/SimulationCore/SessionTestDriver.cs" Link="SharedTests/SessionTestDriver.cs" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" PrivateAssets="all" />
    <PackageReference Include="NUnit" Version="4.2.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Invoking `dotnet test` from the repository root, or setting `ZWEB_REPO_ROOT` explicitly, keeps source audits independent of the relocated artifacts directory. Generate `Server/SimulationCore.Tests/packages.lock.json` from these exact package versions and commit it; all later plans consume it with locked restore.

- [ ] **Step 2: Run the server checks and capture the red result**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-03
dotnet restore Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --use-lock-file --force-evaluate --artifacts-path /tmp/zweb-sim-migration/task-03/red-artifacts
ZWEB_REPO_ROOT="$PWD" dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --no-restore --artifacts-path /tmp/zweb-sim-migration/task-03/red-artifacts --filter FullyQualifiedName~ArchitectureTests --logger "trx;LogFileName=task-03-red.trx" --results-directory /tmp/zweb-sim-migration/task-03
```

Expected: the compile-neutral test project builds and executes two named cases; `SimulationCore_HasNoUnityEngineReferences` and `ReplayValidator_DotNet8_BuildsAgainstCurrentSimulationCore` both appear as `Failed` in the non-empty TRX, listing Unity references and the stale ReplayValidator build failure respectively.

- [ ] **Step 3: Remove Unity calls and add the shared Session implementation**

```csharp
namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class SimBattleSessionFactory : ISimBattleSessionFactory
    {
        public ISimBattleSession Create(byte[] canonicalManifestBytes, ISimLogger logger = null)
        {
            var manifest = DeterministicContentManifest.Parse(canonicalManifestBytes);
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 256, logger);
            return new SimBattleSession(world, manifest, logger);
        }
    }
}
```

`SimBattleSession` stores the injected world, manifest, logger, immutable state, and lifecycle. `Start` validates protocol/schema `1`, non-empty match/session IDs, tick rate `20`, content hash equality, unique player IDs, supported mode, archetypes, owners, and coordinates before mutating; on success it creates initial entities and returns the full `CompletedTick=-1` frame. Before later tasks fill command and snapshot behavior, `Submit` returns `WrongPhase` outside Running, `Tick` returns `NotRunning` outside Running, `CaptureSnapshot` returns the current immutable envelope, and `Dispose` is idempotent.

Use this exact `AssemblyInfo.cs` so only the named Unity and .NET test assemblies can reach internal deterministic hooks:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ZWebGLFrameWork.SimulationCore.Tests")]
[assembly: InternalsVisibleTo("ZWebGLFrameWork.SimulationCore.ServerTests")]
```

Replace `UnityEngine.Debug` calls in Core with `ISimLogger`; validation-only structs throw `ArgumentOutOfRangeException` rather than log-and-clamp. Move `KcpTransport` under the Network asmdef and change its namespace to `ZWebGLFrameWork.Network`. Add `ZWebGLFrameWork.SimulationCore` to the Network asmdef references.

Change ReplayValidator to accept `ISimBattleSessionFactory` and canonical manifest bytes and never construct `SimWorld`. Replace its source glob with:

```xml
<ItemGroup>
  <ProjectReference Include="../SimulationCore/ZWebGLFrameWork.SimulationCore.csproj" />
</ItemGroup>
```

Keep this constructor unchanged in later tasks:

```text
public ReplayValidator(ISimBattleSessionFactory sessionFactory,
    byte[] canonicalManifestBytes, ISimLogger logger = null)
```

Change `Program` usage to `ReplayValidator <replay.json> <authoritative-manifest.bin>`, read the second file as bytes, and construct `ReplayValidator(new SimBattleSessionFactory(), manifestBytes, new ConsoleLogger())`. Missing either file returns exit `1`; replay validity exit behavior remains unchanged until the Replay hardening plan.

- [ ] **Step 4: Run both compilers and capture the focused green result**

Run:

```bash
dotnet restore Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --use-lock-file --force-evaluate --artifacts-path /tmp/zweb-sim-migration/dotnet
dotnet build Server/ReplayValidator/ReplayValidator.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --warnaserror
ZWEB_REPO_ROOT="$PWD" dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --no-restore --artifacts-path /tmp/zweb-sim-migration/dotnet --filter FullyQualifiedName~ArchitectureTests --logger "trx;LogFileName=task-03-architecture.trx" --results-directory /tmp/zweb-sim-migration/task-03
ZWEB_REPO_ROOT="$PWD" dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --no-restore --artifacts-path /tmp/zweb-sim-migration/dotnet --filter FullyQualifiedName~ContentManifestGoldenTests --logger "trx;LogFileName=task-03-content.trx" --results-directory /tmp/zweb-sim-migration/task-03
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testResults /tmp/zweb-sim-migration/task-03/unity.xml -logFile /tmp/zweb-sim-migration/task-03/unity.log
```

Expected: ReplayValidator and Core build with zero warnings; both .NET tests pass; Unity exits zero without a SimulationCore assembly error.

- [ ] **Step 5: Commit host-neutral compilation**

```bash
git add Server/SimulationCore Server/SimulationCore.Tests Server/ReplayValidator Assets/Scripts/SimulationCore Assets/Scripts/Network/Simulation Assets/Scripts/Network/ZWebGLFrameWork.Network.asmdef
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-03/meta
git commit -m "build(sim): share host-neutral core with dotnet"
```

---

### Task 4: Enforce entity validity and stable world iteration

**Closes:** SIM-005, SIM-006

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Core/SimEntity.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/SimWorld.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/TickSnapshot.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/SimChunk.cs`
- Modify: `Assets/Tests/SimulationCore/SimWorldDeterminismTests.cs`
- Modify: `Assets/Tests/SimulationCore/SimWorldTests.cs`

**Interfaces:**
- Consumes: Task 2 stable component IDs.
- Produces: epoch-aware `SimEntity`, generation checks on every world access, and ascending `(Id, Generation)` traversal independent of dictionary/chunk layout.

- [ ] **Step 1: Add stale-handle and swap-history tests**

```csharp
[Test]
public void RollbackThenReuseId_StaleEntityHandleIsRejected()
{
    var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
    TickSnapshot beforeFutureEntity = world.ExportFullSnapshot();
    SimEntity stale = world.CreateEntity(typeof(SimHealthComponent));

    world.RestoreFromFull(beforeFutureEntity);
    SimEntity replacement = world.CreateEntity(typeof(SimHealthComponent));

    Assert.That(replacement.Id, Is.EqualTo(stale.Id));
    Assert.That(world.TryGetComponent<SimHealthComponent>(stale, out _), Is.False);
    Assert.That(world.TryGetComponent<SimHealthComponent>(replacement, out _), Is.True);
}

[Test]
public void EquivalentWorlds_DifferentChunkHistories_SameOrderAndChecksum()
{
    SimWorld left = BuildWorldWithEntities(130);
    SimWorld right = BuildWorldWithEntities(130);
    left.DestroyEntity(FindEntity(left, 3));
    left.DestroyEntity(FindEntity(left, 5));
    right.DestroyEntity(FindEntity(right, 5));
    right.DestroyEntity(FindEntity(right, 3));

    var ids = new System.Collections.Generic.List<int>();
    left.ForEachComponent<SimHealthComponent>((entity, health) => ids.Add(entity.Id));

    Assert.That(ids, Is.Ordered.Ascending);
    Assert.That(left.ComputeChecksum(), Is.EqualTo(right.ComputeChecksum()));
}
```

Add `BuildWorldWithEntities(int)` and `FindEntity(SimWorld, int)` to the test fixture. The builder creates IDs `0..count-1`, each with `SimHealthComponent.Create(100 + id)`; the finder traverses health components and returns the exact ID or fails the test.

- [ ] **Step 2: Run the focused tests and capture the red result**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-04
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SimWorldDeterminismTests -testResults /tmp/zweb-sim-migration/task-04/red.xml -logFile /tmp/zweb-sim-migration/task-04/red.log
```

Expected: stale access succeeds after restore or the traversal/checksum differs after opposite destroy orders.

- [ ] **Step 3: Add a non-hashed world epoch and canonical traversal**

```csharp
public readonly struct SimEntity : System.IEquatable<SimEntity>
{
    public int Id { get; }
    public int Generation { get; }
    internal int WorldEpoch { get; }
    public bool IsValid => Id >= 0;

    internal SimEntity(int id, int generation, int worldEpoch)
    {
        Id = id;
        Generation = generation;
        WorldEpoch = worldEpoch;
    }

    public bool Equals(SimEntity other) =>
        Id == other.Id && Generation == other.Generation && WorldEpoch == other.WorldEpoch;
    public override bool Equals(object obj) => obj is SimEntity other && Equals(other);
    public override int GetHashCode() => unchecked((Id * 397) ^ Generation ^ WorldEpoch);
}
```

`SimWorld` owns `_worldEpoch`; increment it only after a detached snapshot validates and immediately before applying that snapshot. `IsAlive(SimEntity)` verifies ID bounds, matching generation, matching epoch, and non-null archetype key. Route `GetComponentRef`, `TryGetComponent`, `HasComponent`, `SetComponent`, and `DestroyEntity` through `IsAlive`. Rebind restored `SimTargetComponent.TargetEntity` values to the new epoch after restoring all chunks. World epoch is a host validity token and is not serialized or hashed.

For each public traversal, gather `(entity, chunk, localIndex)` locations into a reused list, sort by entity ID then generation, and invoke the callback in that order. `ComputeChecksum` must consume the same sorted locations rather than chunk order.

- [ ] **Step 4: Run focused and existing world tests**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SimWorldDeterminismTests -testResults /tmp/zweb-sim-migration/task-04/green.xml -logFile /tmp/zweb-sim-migration/task-04/green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SimWorldTests -testResults /tmp/zweb-sim-migration/task-04/world.xml -logFile /tmp/zweb-sim-migration/task-04/world.log
```

Expected: stale handle rejection, ascending traversal, equivalent checksum, and all existing world tests pass.

- [ ] **Step 5: Commit entity and iteration invariants**

```bash
git add Assets/Scripts/SimulationCore/Core Assets/Tests/SimulationCore/SimWorldDeterminismTests.cs Assets/Tests/SimulationCore/SimWorldTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-04/meta
git commit -m "fix(sim): validate entities and stabilize iteration"
```

---

### Task 5: Define fixed-point overflow semantics and complete state checksums

**Closes:** SIM-007, SIM-011, TEST-001, TEST-007

**Files:**
- Create: `Assets/Scripts/SimulationCore/Core/DeterministicHash64.cs`
- Create: `Assets/Scripts/SimulationCore/Core/SessionChecksumState.cs`
- Create: `Assets/Tests/SimulationCore/SessionDeterminismTests.cs`
- Modify: `Assets/Scripts/SimulationCore/Math/LogicInt.cs`
- Modify: `Assets/Scripts/SimulationCore/Math/DeterministicRandom.cs`
- Modify: `Assets/Scripts/SimulationCore/Components/SimMovementComponent.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/SimWorld.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SimBattleSession.cs`
- Modify: `Assets/Tests/SimulationCore/LogicIntTests.cs`
- Modify: `Assets/Tests/SimulationCore/SimBattleDeterminismTests.cs`
- Modify: `Assets/Tests/SimulationCore/SessionTestDriver.cs`
- Create: `Server/SimulationCore.Tests/GoldenDeterminismTests.cs`

**Interfaces:**
- Consumes: stable traversal from Task 4 and Session state from Task 3.
- Produces: saturating `LogicInt` arithmetic, `DeterministicHash64`, hashable RNG/system/event/order state, and `SimBattleSession.LastChecksum`.

- [ ] **Step 1: Replace false math/seed tests with direct public-operator tests**

```csharp
[Test]
public void LogicInt_Extremes_UsePublicOperatorsAndGoldenVectors()
{
    LogicInt max = LogicInt.FromRaw(long.MaxValue);
    LogicInt min = LogicInt.FromRaw(long.MinValue);
    Assert.That((max * LogicInt.FromInt(2)).RawValue, Is.EqualTo(long.MaxValue));
    Assert.That((min * LogicInt.FromInt(2)).RawValue, Is.EqualTo(long.MinValue));
    Assert.That((max / LogicInt.FromInt(2)).RawValue, Is.EqualTo(long.MaxValue / 2));
    Assert.That(LogicInt.ApplyPercent(max, 5000).RawValue, Is.EqualTo(long.MaxValue / 2));
}

[Test]
public void LogicInt_ExtremeMultiplyDividePercent_HasDefinedExactResult()
{
    LogicInt max = LogicInt.FromRaw(long.MaxValue);
    LogicInt min = LogicInt.FromRaw(long.MinValue);
    Assert.That((max * LogicInt.FromInt(2)).RawValue, Is.EqualTo(long.MaxValue));
    Assert.That((min / LogicInt.FromInt(-1)).RawValue, Is.EqualTo(long.MaxValue));
    Assert.That(LogicInt.ApplyPercent(min, 20000).RawValue, Is.EqualTo(long.MinValue));
}

[Test]
public void DifferentSeed_ChangesSeededInitialStateChecksum()
{
    ulong seed42 = SessionChecksumFixture.InitialChecksum(42);
    ulong seed99 = SessionChecksumFixture.InitialChecksum(99);
    Assert.That(seed42, Is.Not.EqualTo(seed99));
}

[Test]
public void Checksum_ChangesForPathPhaseAllocatorAndPendingEventDifferences()
{
    var fixture = SessionChecksumFixture.Create();
    ulong baseline = fixture.Compute();
    Assert.That(fixture.WithDifferentPath().Compute(), Is.Not.EqualTo(baseline));
    Assert.That(fixture.WithDifferentPhase().Compute(), Is.Not.EqualTo(baseline));
    Assert.That(fixture.WithDifferentAllocator().Compute(), Is.Not.EqualTo(baseline));
    Assert.That(fixture.WithPendingDamage().Compute(), Is.Not.EqualTo(baseline));
    Assert.That(fixture.WithDifferentCommandOrder().Compute(), Is.Not.EqualTo(baseline));
}
```

Give the test-only immutable builder this exact public surface in `SessionDeterminismTests.cs`:

```text
internal sealed class SessionChecksumFixture
{
    public static ulong InitialChecksum(int seed);
    public static SessionChecksumFixture Create();
    public ulong Compute();
    public SessionChecksumFixture WithDifferentPath();
    public SessionChecksumFixture WithDifferentPhase();
    public SessionChecksumFixture WithDifferentAllocator();
    public SessionChecksumFixture WithPendingDamage();
    public SessionChecksumFixture WithDifferentCommandOrder();
}
```

Implement those declarations with method bodies in the test file. `Create` builds one detached fixture containing transform, health, team, movement, attack, target, and identity components plus explicit Session state. Each `With` method deep-clones it and changes only the named field; no variant may mutate or share a mutable path, event list, command list, or world with the baseline. `Compute` runs the same `SessionChecksumState` plus `SimWorld.HashInto` path as `SimBattleSession.ComputeChecksum`, and `InitialChecksum` starts and disposes a Session from `SessionTestDriver` with the supplied seed.

- [ ] **Step 2: Run the three focused tests and verify red**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-05
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter LogicInt_Extremes_UsePublicOperatorsAndGoldenVectors -testResults /tmp/zweb-sim-migration/task-05/math-red.xml -logFile /tmp/zweb-sim-migration/task-05/math-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter LogicInt_ExtremeMultiplyDividePercent_HasDefinedExactResult -testResults /tmp/zweb-sim-migration/task-05/math-defined-red.xml -logFile /tmp/zweb-sim-migration/task-05/math-defined-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SessionDeterminismTests -testResults /tmp/zweb-sim-migration/task-05/hash-red.xml -logFile /tmp/zweb-sim-migration/task-05/hash-red.log
```

Expected: extreme arithmetic wraps or differs from saturation; seed/path/phase/order variants collide with the baseline.

- [ ] **Step 3: Implement saturation and hash every future-affecting field**

```csharp
using System.Numerics;

private static long Saturate(BigInteger value)
{
    if (value > long.MaxValue) return long.MaxValue;
    if (value < long.MinValue) return long.MinValue;
    return (long)value;
}

public static LogicInt operator *(LogicInt left, LogicInt right) =>
    FromRaw(Saturate((BigInteger)left.RawValue * right.RawValue / Scale));

public static LogicInt operator /(LogicInt left, LogicInt right)
{
    if (right.RawValue == 0) throw new DivideByZeroException();
    return FromRaw(Saturate((BigInteger)left.RawValue * Scale / right.RawValue));
}

public static LogicInt ApplyPercent(LogicInt value, int basisPoints) =>
    FromRaw(Saturate((BigInteger)value.RawValue * basisPoints / 10000));
```

`DeterministicHash64` starts at `14695981039346656037UL` and provides `AddByte`, `AddBool`, `AddInt32`, `AddUInt32`, `AddInt64`, `AddUInt64`, and `AddBytes`; each numeric method emits fixed little-endian bytes through `AddByte` using FNV-1a prime `1099511628211UL`.

Replace `SimMovementComponent.Path` hashing by hashing path length and every packed waypoint in order. `SimWorld.HashInto` hashes component schema, entity count, `_nextId`, every generation entry through `_nextId`, stable entity IDs/generations, component type IDs, and every component value. Event queues expose a stable type ID and insertion-order payload hash. `DeterministicRandom` exposes its internal state to `HashInto` without advancing it. Every system with a clock implements `IDeterministicSystemState.HashInto`.

`SessionChecksumState` contains protocol/schema, content hash, seed/RNG state, completed/next tick, phase, deploy/battle timers, terminal winner/stars/loot/final tick, system state hash, event hash, and command-order hash. Timeline revision and the derived `ResultId`/`FinalChecksum` fields are deliberately excluded: revision is presentation metadata, while hashing either derived result field would make terminal checksum construction circular. On the terminal tick, hash the canonical numeric outcome first, then construct `SimBattleResult` with that checksum and its derived result ID. `SimBattleSession.ComputeChecksum()` hashes the state followed by `SimWorld.HashInto` and stores the value after every accepted Start, Tick, and Restore.

- [ ] **Step 4: Run Unity and .NET golden checks**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SessionDeterminismTests -testResults /tmp/zweb-sim-migration/task-05/hash-green.xml -logFile /tmp/zweb-sim-migration/task-05/hash-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.LogicIntOverflowTests -testResults /tmp/zweb-sim-migration/task-05/math-green.xml -logFile /tmp/zweb-sim-migration/task-05/math-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter LogicInt_ExtremeMultiplyDividePercent_HasDefinedExactResult -testResults /tmp/zweb-sim-migration/task-05/math-defined-green.xml -logFile /tmp/zweb-sim-migration/task-05/math-defined-green.log
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --filter FullyQualifiedName~GoldenDeterminismTests --logger "trx;LogFileName=task-05-golden.trx" --results-directory /tmp/zweb-sim-migration/task-05
```

Expected: all variants produce distinct hashes, same inputs produce the committed golden hash in Unity and .NET, and public fixed-point operators pass all extreme vectors.

- [ ] **Step 5: Commit deterministic math and checksums**

```bash
git add Assets/Scripts/SimulationCore Assets/Tests/SimulationCore/LogicIntTests.cs Assets/Tests/SimulationCore/SimBattleDeterminismTests.cs Assets/Tests/SimulationCore/SessionDeterminismTests.cs Server/SimulationCore.Tests/GoldenDeterminismTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-05/meta
git commit -m "fix(sim): hash complete state with saturating math"
```

---

### Task 6: Deep, identity-bound, atomic snapshots

**Closes:** SIM-008, SIM-021

**Files:**
- Create: `Assets/Scripts/SimulationCore/Core/SimPath.cs`
- Create: `Assets/Scripts/SimulationCore/Core/SessionSnapshotCodec.cs`
- Modify: `Assets/Scripts/SimulationCore/Components/SimMovementComponent.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/SimChunk.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/ChunkSnapshot.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/TickSnapshot.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SessionResults.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SimSnapshot.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SimBattleSession.cs`
- Modify: `Assets/Tests/SimulationCore/SessionDeterminismTests.cs`

**Interfaces:**
- Consumes: deterministic binary/hash utilities and complete checksum from Task 5.
- Produces: `SimPath`, an 8 MiB-bounded `SimSnapshot` envelope, detached `SessionSnapshotData`, and atomic `RestoreSnapshot`.

- [ ] **Step 1: Write mutation-isolation and hostile-envelope tests**

```csharp
[Test]
public void Snapshot_PathMutationAndSystemClock_AreIsolatedAndRestored()
{
    SimBattleSession session = SessionTestDriver.StartedDeployingBattle();
    SimSnapshot snapshot = session.CaptureSnapshot();
    ulong expected = snapshot.StateChecksum;

    session.Tick();
    session.Tick();
    SnapshotRestoreResult restored = session.RestoreSnapshot(snapshot);

    Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.Restored));
    Assert.That(session.State.Checksum, Is.EqualTo(expected));
    Assert.That(session.State.NextTick, Is.EqualTo(snapshot.NextTick));
}

[TestCase(SnapshotMutation.ForeignMatch, SnapshotRestoreCode.ForeignMatch)]
[TestCase(SnapshotMutation.ForeignSchema, SnapshotRestoreCode.UnsupportedSchema)]
[TestCase(SnapshotMutation.CorruptPayload, SnapshotRestoreCode.CorruptPayload)]
[TestCase(SnapshotMutation.StaleRevision, SnapshotRestoreCode.StaleRevision)]
public void SnapshotRestore_ForeignSchemaCorruptOrStale_IsRejectedAtomically(
    SnapshotMutation mutation, SnapshotRestoreCode expectedCode)
{
    SimBattleSession session = SessionTestDriver.StartedDeployingBattle();
    ulong before = session.State.Checksum;
    SimSnapshot invalid = SnapshotTestMutator.Apply(session.CaptureSnapshot(), mutation);

    SnapshotRestoreResult result = session.RestoreSnapshot(invalid);

    Assert.That(result.Code, Is.EqualTo(expectedCode));
    Assert.That(session.State.Checksum, Is.EqualTo(before));
}
```

Define the mutation helper exactly in `SessionDeterminismTests.cs`:

```csharp
internal enum SnapshotMutation
{
    ForeignMatch,
    ForeignSchema,
    CorruptPayload,
    StaleRevision
}

internal static class SnapshotTestMutator
{
    public static SimSnapshot Apply(SimSnapshot source, SnapshotMutation mutation)
    {
        byte[] payload = source.Payload;
        if (mutation == SnapshotMutation.CorruptPayload)
            payload[0] ^= 0x01;

        return new SimSnapshot(
            mutation == SnapshotMutation.ForeignMatch ? source.MatchId + "-foreign" : source.MatchId,
            source.SessionId,
            mutation == SnapshotMutation.ForeignSchema ? source.SchemaVersion + 1 : source.SchemaVersion,
            source.ContentHash,
            source.CompletedTick,
            source.NextTick,
            mutation == SnapshotMutation.StaleRevision ? source.TimelineRevision - 1 : source.TimelineRevision,
            source.UncompressedLength,
            source.StateChecksum,
            payload,
            source.PayloadSha256);
    }
}
```

- [ ] **Step 2: Run the snapshot tests and capture the red result**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-06
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter Snapshot_ -testResults /tmp/zweb-sim-migration/task-06/red.xml -logFile /tmp/zweb-sim-migration/task-06/red.log
```

Expected: the path snapshot changes with the live list, or malformed/foreign envelopes mutate state or are accepted.

- [ ] **Step 3: Replace path lists and implement detached decode-before-apply**

```csharp
public readonly struct SimPath
{
    private readonly int[] _waypoints;
    public int Count => _waypoints == null ? 0 : _waypoints.Length;
    public int this[int index] => _waypoints[index];
    public SimPath(int[] waypoints) => _waypoints = (int[])waypoints.Clone();
    public int[] ToArray() => _waypoints == null ? System.Array.Empty<int>() : (int[])_waypoints.Clone();
    public SimPath DeepClone() => new SimPath(ToArray());
}
```

Keep the exact immutable `SimSnapshot` envelope introduced in Task 1. Task 6 adds codec behavior and hostile-input validation; it does not change the constructor, property types, or cloned-array getters.

`SessionSnapshotCodec.Encode` writes allocator/generations, stable entities/components including every path element, completed/next tick, phase/timers/result, RNG, system clocks, pending events, command sequence watermarks, accepted pending commands, and sealed tick state. `Decode` reads into a detached `SessionSnapshotData`, checks every count before allocation, rejects trailing bytes, and never touches the live Session.

`RestoreSnapshot` checks lifecycle, exact match/session/schema/content identity, `UncompressedLength == Payload.Length`, length at most `8 * 1024 * 1024`, SHA-256, tick relationship `NextTick == CompletedTick + 1`, and non-stale revision. It decodes and validates the detached graph, then swaps all state in one method, increments timeline revision once, recomputes checksum, and returns a full presentation frame. Any failure returns the corresponding code without changing any field.

- [ ] **Step 4: Run snapshot and checksum suites**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter Snapshot_ -testResults /tmp/zweb-sim-migration/task-06/green.xml -logFile /tmp/zweb-sim-migration/task-06/green.log
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --filter FullyQualifiedName~GoldenDeterminismTests --logger "trx;LogFileName=task-06-golden.trx" --results-directory /tmp/zweb-sim-migration/task-06
```

Expected: all snapshot mutations are rejected atomically, restored checksum equals the captured checksum, and .NET golden state remains unchanged.

- [ ] **Step 5: Commit snapshot isolation and validation**

```bash
git add Assets/Scripts/SimulationCore/Core Assets/Scripts/SimulationCore/Components/SimMovementComponent.cs Assets/Scripts/SimulationCore/Session Assets/Tests/SimulationCore/SessionDeterminismTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-06/meta
git commit -m "feat(sim): add atomic identity-bound snapshots"
```

---

### Task 7: Correct after-T rollback, replay, and single-use restart behavior

**Closes:** SIM-009, SIM-010, TEST-003

**Files:**
- Create: `Assets/Scripts/SimulationCore/Core/SnapshotHistory.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SimBattleSession.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SessionResults.cs`
- Modify: `Assets/Tests/SimulationCore/SimReplayRoundTripTests.cs`
- Modify: `Assets/Tests/SimulationCore/SessionDeterminismTests.cs`

**Interfaces:**
- Consumes: atomic snapshots from Task 6.
- Produces: `TryGetSnapshotAfter(int, out SimSnapshot)`, exact T+1 replay, explicit `SnapshotUnavailable`, and immutable rejection of a second Start.

- [ ] **Step 1: Replace nonzero assertions with exact sequences**

```csharp
[Test]
public void RollbackReplay_MatchesOriginalChecksumSequence()
{
    SimBattleSession session = SessionTestDriver.StartedDeployingBattle();
    var original = new System.Collections.Generic.List<ulong>();
    for (int tick = 0; tick < 40; tick++)
        original.Add(session.Tick().Checksum);

    Assert.That(session.TryGetSnapshotAfter(19, out SimSnapshot after19), Is.True);
    Assert.That(session.RestoreSnapshot(after19).Code, Is.EqualTo(SnapshotRestoreCode.Restored));
    Assert.That(session.State.NextTick, Is.EqualTo(20));

    var replayed = new System.Collections.Generic.List<ulong>();
    for (int tick = 20; tick < 40; tick++)
        replayed.Add(session.Tick().Checksum);

    Assert.That(replayed, Is.EqualTo(original.GetRange(20, 20)));
}

[Test]
public void Rollback_AfterTickT_ResumesAtTPlusOneWithOriginalChecksumSequence()
{
    SimBattleSession session = SessionTestDriver.StartedDeployingBattle();
    for (int tick = 0; tick <= 19; tick++) session.Tick();
    Assert.That(session.TryGetSnapshotAfter(19, out SimSnapshot after19), Is.True);
    ulong originalTick20 = session.Tick().Checksum;

    Assert.That(session.RestoreSnapshot(after19).Restored, Is.True);
    Assert.That(session.State.NextTick, Is.EqualTo(20));
    Assert.That(session.Tick().Checksum, Is.EqualTo(originalTick20));
}

[Test]
public void SessionRestart_MatchesFreshSessionState()
{
    byte[] manifest = SessionTestDriver.ManifestBytes();
    BattleInit init = SessionTestDriver.BattleInit(seed: 42);
    var factory = new SimBattleSessionFactory();
    using ISimBattleSession first = factory.Create(manifest);
    StartResult accepted = first.Start(init);
    ulong initialChecksum = first.State.Checksum;

    StartResult rejected = first.Start(init);
    using ISimBattleSession fresh = factory.Create(manifest);
    StartResult freshStart = fresh.Start(init);

    Assert.That(accepted.Code, Is.EqualTo(StartCode.Accepted));
    Assert.That(rejected.Code, Is.EqualTo(StartCode.AlreadyStarted));
    Assert.That(first.State.Checksum, Is.EqualTo(initialChecksum));
    Assert.That(freshStart.Code, Is.EqualTo(StartCode.Accepted));
    Assert.That(fresh.State.Checksum, Is.EqualTo(initialChecksum));
}
```

- [ ] **Step 2: Run rollback and restart tests and verify red**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-07
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter RollbackReplay_MatchesOriginalChecksumSequence -testResults /tmp/zweb-sim-migration/task-07/rollback-red.xml -logFile /tmp/zweb-sim-migration/task-07/rollback-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter Rollback_AfterTickT_ResumesAtTPlusOneWithOriginalChecksumSequence -testResults /tmp/zweb-sim-migration/task-07/after-t-red.xml -logFile /tmp/zweb-sim-migration/task-07/after-t-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter SessionRestart_MatchesFreshSessionState -testResults /tmp/zweb-sim-migration/task-07/restart-red.xml -logFile /tmp/zweb-sim-migration/task-07/restart-red.log
```

Expected: restore repeats tick 19/20, reconstructed counters diverge, or a second Start mutates/duplicates world state.

- [ ] **Step 3: Index snapshots by completed tick and replay from NextTick**

```csharp
public sealed class SnapshotHistory
{
    private readonly SimSnapshot[] _slots;
    public SnapshotHistory(int capacity) => _slots = new SimSnapshot[capacity];

    public void Store(SimSnapshot snapshot) =>
        _slots[PositiveModulo(snapshot.CompletedTick, _slots.Length)] = snapshot;

    public bool TryGetAfter(int completedTick, out SimSnapshot snapshot)
    {
        snapshot = _slots[PositiveModulo(completedTick, _slots.Length)];
        return snapshot != null && snapshot.CompletedTick == completedTick;
    }

    private static int PositiveModulo(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
```

Store the initial snapshot at completed tick `-1` and one snapshot after every completed tick. `RestoreSnapshot` applies the exact stored phase/timers/result/RNG/system/command state and sets `NextTick` from the envelope; it never derives counters from tick. `ReplayAuthority` begins at `snapshot.NextTick` and stops after the requested final completed tick. Missing ring entries return `SnapshotUnavailable` from `TryGetSnapshotAfter` and do not change Session state. A second `Start` always returns `AlreadyStarted`; callers obtain a new Session from the factory.

- [ ] **Step 4: Run focused tests plus ring boundaries**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter RollbackReplay_MatchesOriginalChecksumSequence -testResults /tmp/zweb-sim-migration/task-07/rollback-green.xml -logFile /tmp/zweb-sim-migration/task-07/rollback-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SessionDeterminismTests -testResults /tmp/zweb-sim-migration/task-07/session-green.xml -logFile /tmp/zweb-sim-migration/task-07/session-green.log
```

Expected: initial `-1`, after-0, after-19, slot-wrap, terminal, and unavailable snapshot cases all pass; replayed checksums equal the original sequence exactly.

- [ ] **Step 5: Commit rollback semantics**

```bash
git add Assets/Scripts/SimulationCore/Core/SnapshotHistory.cs Assets/Scripts/SimulationCore/Session Assets/Tests/SimulationCore/SimReplayRoundTripTests.cs Assets/Tests/SimulationCore/SessionDeterminismTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-07/meta
git commit -m "fix(sim): restore after-t snapshots without replay drift"
```

---

### Task 8: Deterministic command admission, ordering, and sealed ticks

**Closes:** SIM-013, SIM-020

**Files:**
- Create: `Assets/Scripts/SimulationCore/Command/CommandKey.cs`
- Modify: `Assets/Scripts/SimulationCore/Command/ISimCommand.cs`
- Modify: `Assets/Scripts/SimulationCore/Command/SimCommands.cs`
- Modify: `Assets/Scripts/SimulationCore/Command/CommandBuffer.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SessionResults.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SimBattleSession.cs`
- Modify: `Assets/Tests/SimulationCore/SessionDeterminismTests.cs`

**Interfaces:**
- Consumes: Task 7 `NextTick` and snapshot command-order state.
- Produces: `ISimCommand.Sequence`, `CommandKey`, stable `CommandDisposition`, `SealTick(int)`, and ordered commands in `TickResult`.

- [ ] **Step 1: Write admission-window and total-order tests**

```csharp
[Test]
public void CommandSubmit_LateFutureDuplicate_HasDeterministicDisposition()
{
    SimBattleSession session = SessionTestDriver.StartedDeployingBattle();
    var accepted = new DeployCmd(0, 0, 1, 10, 2, 2);

    Assert.That(session.Submit(accepted).Disposition, Is.EqualTo(CommandDisposition.Accepted));
    Assert.That(session.Submit(accepted).Disposition, Is.EqualTo(CommandDisposition.Duplicate));
    Assert.That(session.Submit(new DeployCmd(0, 0, 0, 10, 3, 2)).Disposition,
        Is.EqualTo(CommandDisposition.NonMonotonicSequence));
    Assert.That(session.Submit(new DeployCmd(-1, 0, 2, 10, 3, 2)).Disposition,
        Is.EqualTo(CommandDisposition.LateTick));
    Assert.That(session.Submit(new DeployCmd(65, 0, 2, 10, 3, 2)).Disposition,
        Is.EqualTo(CommandDisposition.FutureWindowExceeded));

    session.SealTick(0);
    Assert.That(session.Submit(new DeployCmd(0, 0, 2, 10, 4, 2)).Disposition,
        Is.EqualTo(CommandDisposition.SealedTick));
}

[Test]
public void Commands_SameTickSamePlayer_AreOrderedBySequence()
{
    SimBattleSession session = SessionTestDriver.StartedDeployingBattle();
    session.Submit(new DeployCmd(0, 0, 1, 10, 2, 2));
    session.Submit(new DeployCmd(0, 0, 2, 10, 3, 2));

    TickResult result = session.Tick();

    Assert.That(result.ExecutedCommands[0].PlayerId, Is.EqualTo(0));
    Assert.That(result.ExecutedCommands[0].Sequence, Is.EqualTo(1));
    Assert.That(result.ExecutedCommands[1].Sequence, Is.EqualTo(2));
}
```

- [ ] **Step 2: Run command tests and capture the red result**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-08
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter CommandSubmit_LateFutureDuplicate_HasDeterministicDisposition -testResults /tmp/zweb-sim-migration/task-08/admission-red.xml -logFile /tmp/zweb-sim-migration/task-08/admission-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter Commands_SameTickSamePlayer_AreOrderedBySequence -testResults /tmp/zweb-sim-migration/task-08/order-red.xml -logFile /tmp/zweb-sim-migration/task-08/order-red.log
```

Expected: command types lack sequence constructors, duplicate/late/future inputs are accepted, or execution follows arrival order.

- [ ] **Step 3: Implement immutable command keys and admission state**

```csharp
public readonly struct CommandKey : System.IComparable<CommandKey>
{
    public int Tick { get; }
    public int PlayerId { get; }
    public uint Sequence { get; }
    public CommandKey(int tick, int playerId, uint sequence)
    {
        Tick = tick;
        PlayerId = playerId;
        Sequence = sequence;
    }
    public int CompareTo(CommandKey other)
    {
        int tick = Tick.CompareTo(other.Tick);
        if (tick != 0) return tick;
        int player = PlayerId.CompareTo(other.PlayerId);
        return player != 0 ? player : Sequence.CompareTo(other.Sequence);
    }
}
```

Add `uint Sequence { get; }` to `ISimCommand`. Give every concrete command a constructor that sets all fields and get-only properties. Add `EndDeploymentCmd(tick, playerId, sequence)` as the validated player request to seal manual deployment; authority NoOp remains internal. `CommandBuffer` uses `SortedDictionary<CommandKey, ISimCommand>` plus per-player accepted-sequence watermarks and a set of sealed ticks. Admission checks, in order: lifecycle, known player, client NoOp, completed/sealed tick, 64-tick future bound, exact duplicate key/payload, sequence monotonicity, then payload/phase authorization. Duplicate identical retransmission returns `Duplicate` without adding a second entry; a reused key with different payload returns `NonMonotonicSequence`.

`Tick()` seals `NextTick`, drains only that tick in key order, exposes a cloned `ISimCommand[]` through `TickResult.ExecutedCommands`, and advances once even when that array is empty. `SealTick` is internal and exposed to test/server assemblies through `InternalsVisibleTo`; client-submitted NoOp always returns `ClientNoOpForbidden`.

- [ ] **Step 4: Run admission, order, and snapshot tests**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter CommandSubmit_LateFutureDuplicate_HasDeterministicDisposition -testResults /tmp/zweb-sim-migration/task-08/admission-green.xml -logFile /tmp/zweb-sim-migration/task-08/admission-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter Commands_SameTickSamePlayer_AreOrderedBySequence -testResults /tmp/zweb-sim-migration/task-08/order-green.xml -logFile /tmp/zweb-sim-migration/task-08/order-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter Snapshot_ -testResults /tmp/zweb-sim-migration/task-08/snapshot-green.xml -logFile /tmp/zweb-sim-migration/task-08/snapshot-green.log
```

Expected: both tests pass; snapshot tests still restore watermarks, sealed ticks, and pending commands exactly.

- [ ] **Step 5: Commit command admission**

```bash
git add Assets/Scripts/SimulationCore/Command Assets/Scripts/SimulationCore/Session Assets/Tests/SimulationCore/SessionDeterminismTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-08/meta
git commit -m "feat(sim): seal ticks and order command admission"
```

---

### Task 9: Implement validated Deploy, Move, and Skill behavior

**Closes:** COMBAT-006, SIM-012

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Command/SimCommands.cs`
- Modify: `Assets/Scripts/SimulationCore/Command/CommandExecutionResult.cs`
- Modify: `Assets/Scripts/SimulationCore/Content/DeterministicContentManifest.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/SimEntityArchetype.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SimBattleSession.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/TickResult.cs`
- Modify: `Assets/Tests/SimulationCore/SessionDeterminismTests.cs`

**Interfaces:**
- Consumes: Task 8 admitted command stream and Task 2 archetype manifest.
- Produces: immutable concrete commands, deterministic execution outcomes, occupancy/ownership validation, and real state changes.

- [ ] **Step 1: Write one end-to-end assertion for each command plus deployment rejection**

```csharp
[Test]
public void DeployMoveSkill_ValidCommandsMutateState_InvalidCommandsDoNot()
{
    SimBattleSession session = SessionTestDriver.StartedDeployingBattle();
    ulong initial = session.State.Checksum;

    Assert.That(session.Submit(new DeployCmd(0, 0, 1, 10, 2, 2)).Disposition,
        Is.EqualTo(CommandDisposition.Accepted));
    TickResult deploy = session.Tick();
    EntityPresentationState unit = deploy.Presentation.Entities.Single(e => e.OwnerPlayerId == 0);
    Assert.That(deploy.Checksum, Is.Not.EqualTo(initial));
    Assert.That(session.Submit(new DeployCmd(1, 0, 2, 10, 2, 2)).Disposition,
        Is.EqualTo(CommandDisposition.InvalidPayload));

    Assert.That(session.Submit(new EndDeploymentCmd(1, 0, 3)).Accepted, Is.True);
    TickResult deploymentEnded = session.Tick();
    Assert.That(deploymentEnded.CommandOutcomes.Single().Disposition,
        Is.EqualTo(CommandDisposition.Accepted));

    Assert.That(session.Submit(new DeployCmd(2, 0, 4, 10, 4, 2)).Disposition,
        Is.EqualTo(CommandDisposition.WrongPhase));
    Assert.That(session.Submit(new MoveCmd(2, 0, 5, unit.EntityId, unit.Generation, 5, 2)).Disposition,
        Is.EqualTo(CommandDisposition.Accepted));
    Assert.That(session.Submit(new SkillCmd(2, 0, 6, unit.EntityId, unit.Generation, 1, 9, 9)).Disposition,
        Is.EqualTo(CommandDisposition.Accepted));

    TickResult actions = session.Tick();
    Assert.That(actions.CommandOutcomes.All(o => o.Disposition == CommandDisposition.Accepted), Is.True);
    Assert.That(actions.Presentation.Entities.Single(e => e.EntityId == unit.EntityId).GridX,
        Is.GreaterThanOrEqualTo(2));
    Assert.That(actions.Events.Any(e => e.Type == SimBattleEventType.DamageApplied), Is.True);
}

[Test]
public void DeployCommand_RejectsOutOfPhaseDisallowedOrOverLimit()
{
    SimBattleSession session = SessionTestDriver.StartedBattle();
    Assert.That(session.Submit(new DeployCmd(0, 0, 1, 999, 2, 2)).Disposition,
        Is.EqualTo(CommandDisposition.InvalidPayload));
    Assert.That(session.Submit(new DeployCmd(0, 0, 2, 10, -1, 2)).Disposition,
        Is.EqualTo(CommandDisposition.InvalidPayload));
    for (int index = 0; index < 40; index++)
        Assert.That(session.Submit(new DeployCmd(0, 0, (uint)(3 + index), 10,
            2 + index % 10, 3 + index / 10)).Accepted, Is.True);
    Assert.That(session.Submit(new DeployCmd(0, 0, 43, 10, 15, 3)).Disposition,
        Is.EqualTo(CommandDisposition.InvalidPayload));
}
```

- [ ] **Step 2: Run both tests and capture the red result**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-09
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter DeployMoveSkill_ValidCommandsMutateState_InvalidCommandsDoNot -testResults /tmp/zweb-sim-migration/task-09/actions-red.xml -logFile /tmp/zweb-sim-migration/task-09/actions-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter DeployCommand_RejectsOutOfPhaseDisallowedOrOverLimit -testResults /tmp/zweb-sim-migration/task-09/deploy-red.xml -logFile /tmp/zweb-sim-migration/task-09/deploy-red.log
```

Expected: handlers only log, entity count/position/health do not change, and invalid deployments are accepted.

- [ ] **Step 3: Implement immutable payloads and execution dispatch**

```csharp
public readonly struct DeployCmd : ISimCommand
{
    public SimCommandType Type => SimCommandType.Deploy;
    public int Tick { get; }
    public int PlayerId { get; }
    public uint Sequence { get; }
    public int UnitTypeId { get; }
    public int GridX { get; }
    public int GridY { get; }
    public DeployCmd(int tick, int playerId, uint sequence, int unitTypeId, int gridX, int gridY)
    {
        Tick = tick; PlayerId = playerId; Sequence = sequence;
        UnitTypeId = unitTypeId; GridX = gridX; GridY = gridY;
    }
}
```

Give `MoveCmd` the same header plus `EntityId`, `EntityGeneration`, `TargetX`, and `TargetY`. Give `SkillCmd` the same header plus caster ID/generation, `SkillId`, `TargetX`, and `TargetY`. Add manifest `SimSkillDefinition` rows with integer damage/radius/cooldown and include them in canonical bytes; fixture skill `1` applies 30 damage at radius zero.

At `Submit`, validate phase, player, sequence, manifest ID, grid bounds, deployment region, occupancy, mode allowlist/count, and entity ownership/generation without mutation. At execution, dispatch by concrete command type; Deploy creates all manifest-defined components plus identity, Move computes a deterministic path and stores `SimPath`, Skill enqueues damage against stable-ID-sorted enemy targets in radius, and EndDeployment seals manual deployment after all earlier commands at the same total-order position execute. `TickResult.CommandOutcomes` contains every drained command and its final disposition; `ExecutedCommands` contains only commands whose execution disposition is `Accepted`.

- [ ] **Step 4: Run command, checksum, and cross-host tests**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter DeployMoveSkill_ValidCommandsMutateState_InvalidCommandsDoNot -testResults /tmp/zweb-sim-migration/task-09/actions-green.xml -logFile /tmp/zweb-sim-migration/task-09/actions-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter DeployCommand_RejectsOutOfPhaseDisallowedOrOverLimit -testResults /tmp/zweb-sim-migration/task-09/deploy-green.xml -logFile /tmp/zweb-sim-migration/task-09/deploy-green.log
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --filter FullyQualifiedName~GoldenDeterminismTests --logger "trx;LogFileName=task-09-golden.trx" --results-directory /tmp/zweb-sim-migration/task-09
```

Expected: valid commands mutate only authorized state, every rejection is non-mutating, and Unity/.NET checksums remain identical.

- [ ] **Step 5: Commit real commands**

```bash
git add Assets/Scripts/SimulationCore/Command Assets/Scripts/SimulationCore/Content Assets/Scripts/SimulationCore/Core/SimEntityArchetype.cs Assets/Scripts/SimulationCore/Session Assets/Tests/SimulationCore/SessionDeterminismTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-09/meta
git commit -m "feat(sim): execute validated deploy move and skill commands"
```

---

### Task 10: Record the commands actually executed and replay them through Session

**Closes:** SIM-014, TEST-002

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Command/CommandRecorder.cs`
- Modify: `Assets/Scripts/SimulationCore/Command/ReplayFile.cs`
- Create: `Assets/Scripts/SimulationCore/Command/ReplayPlayback.cs`
- Create: `Assets/Scripts/Combat/Replay/UnityReplayJsonCodec.cs`
- Modify: `Assets/Tests/SimulationCore/SimReplayRoundTripTests.cs`
- Create: `Assets/Tests/SimulationCore/ReplayFixture.cs`
- Modify: `Assets/Tests/SimulationCore/ZWebGLFrameWork.SimulationCore.Tests.asmdef`
- Create: `Server/SimulationCore.Tests/ReplayRoundTripTests.cs`
- Modify: `Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj`
- Modify: `Server/ReplayValidator/ReplayValidator.cs`
- Modify: `Server/ReplayValidator/Program.cs`
- Create: `Server/ReplayValidator/ServerReplayJsonCodec.cs`

**Interfaces:**
- Consumes: Task 9 concrete commands and Task 1 `TickResult`.
- Produces: `CommandRecorder.Record(TickResult)`, concrete sequence-preserving replay DTOs, and `ReplayPlayback.Run(ISimBattleSession, ReplayFile)`.

- [ ] **Step 1: Write a true export-parse-replay test**

```csharp
[Test]
public void Recorder_RecordsExactlyTheCommandsExecutedThisTick()
{
    SimBattleSession session = SessionTestDriver.StartedDeployingBattle();
    var recorder = new CommandRecorder(SessionTestDriver.DeployingBattleInit());
    session.Submit(new DeployCmd(0, 0, 1, 10, 2, 2));

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
    byte[] manifest = SessionTestDriver.ManifestBytes();
    ReplayFile recorded = ReplayFixture.RecordTicks(manifest, tickCount: 8);
    string json = UnityReplayJsonCodec.Serialize(recorded);
    ReplayFile parsed = UnityReplayJsonCodec.Deserialize(json);
    using ISimBattleSession replaySession = new SimBattleSessionFactory().Create(manifest);

    ReplayPlaybackResult result = ReplayPlayback.Run(replaySession, parsed);

    Assert.That(result.Valid, Is.True);
    Assert.That(result.FinalChecksum, Is.EqualTo(recorded.FinalChecksum));
    Assert.That(result.ExecutedCommandCount, Is.EqualTo(recorded.Records.Sum(r => r.Commands.Count)));
    Assert.That(recorded.FinalResult, Is.Null);
}

[Test]
public void UnityAndValidator_SameBattleInitAndCommands_SameTickResults()
{
    byte[] manifest = SessionTestDriver.ManifestBytes();
    ReplayFile recorded = ReplayFixture.RecordTicks(manifest, tickCount: 8);
    using ISimBattleSession validatorSession = new SimBattleSessionFactory().Create(manifest);

    ReplayPlaybackResult replayed = ReplayPlayback.Run(validatorSession, recorded);

    Assert.That(replayed.Valid, Is.True);
    Assert.That(replayed.FinalChecksum, Is.EqualTo(recorded.FinalChecksum));
    Assert.That(validatorSession.State.CompletedTick, Is.EqualTo(recorded.FinalTick));
    Assert.That(validatorSession.State.Result, Is.Null);
}
```

Define the Core-only recording fixture in `Assets/Tests/SimulationCore/ReplayFixture.cs` with the exact signature used above:

```csharp
internal static class ReplayFixture
{
    public static ReplayFile RecordTicks(byte[] canonicalManifestBytes, int tickCount)
    {
        if (tickCount <= 0) throw new System.ArgumentOutOfRangeException(nameof(tickCount));
        BattleInit init = SessionTestDriver.DeployingBattleInit();
        using ISimBattleSession session =
            new SimBattleSessionFactory().Create(canonicalManifestBytes);
        StartResult started = session.Start(init);
        if (!started.Accepted) throw new System.InvalidOperationException(started.Code.ToString());

        var recorder = new CommandRecorder(init);
        session.Submit(new DeployCmd(0, 0, 1, 10, 2, 2));
        session.Submit(new EndDeploymentCmd(0, 0, 2));
        for (int count = 0; count < tickCount; count++)
        {
            TickResult tick = session.Tick();
            recorder.Record(tick);
            if (tick.Result != null)
                throw new System.InvalidOperationException("Task 10 fixture ended before mode and combat rules exist.");
        }
        return recorder.Export();
    }
}
```

Keep `ReplayFixture.cs` free of NUnit and Unity references. In this task add it to the server test project so Unity and .NET compile the same fixture source:

```xml
<Compile Include="../../Assets/Tests/SimulationCore/ReplayFixture.cs" Link="SharedTests/ReplayFixture.cs" />
```

Both server test files import `ZWebGLFrameWork.SimulationCore.Tests` to access `SessionTestDriver` and `ReplayFixture`; those are the only linked test helpers.

- [ ] **Step 2: Run the round-trip tests and verify red**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-10
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter Recorder_RecordsExactlyTheCommandsExecutedThisTick -testResults /tmp/zweb-sim-migration/task-10/record-red.xml -logFile /tmp/zweb-sim-migration/task-10/record-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ReplayExportParseValidate_RoundTripsExecutedCommands -testResults /tmp/zweb-sim-migration/task-10/replay-red.xml -logFile /tmp/zweb-sim-migration/task-10/replay-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SimReplayRoundTripTests -testResults /tmp/zweb-sim-migration/task-10/red.xml -logFile /tmp/zweb-sim-migration/task-10/red.log
```

Expected: Recorder sees an empty buffer after Drain, sequence is absent, or playback does not reproduce the recorded checksum.

- [ ] **Step 3: Make TickResult the recorder's only command source**

```csharp
public void Record(TickResult result)
{
    var commands = new SerializableCommand[result.ExecutedCommands.Count];
    for (int index = 0; index < commands.Length; index++)
        commands[index] = SerializableCommand.From(result.ExecutedCommands[index]);
    _replay.Records.Add(new TickRecord(result.CompletedTick, commands, result.Checksum));
    _replay.FinalTick = result.CompletedTick;
    _replay.FinalChecksum = result.Checksum;
    if (result.Result != null) _replay.FinalResult = result.Result;
}
```

Remove `CommandBuffer` from the Recorder constructor. `SerializableCommand` stores numeric command type, tick, player ID, sequence, and every concrete payload field; `ToCommand` uses a strict switch and never `Enum.Parse`. `ReplayPlayback.Run` starts one supplied Session from replay `BattleInit`, submits each continuous record's concrete commands, ticks once per record, compares that tick's checksum including terminal tick, and returns the executed count and final checksum.

`UnityReplayJsonCodec` uses `UnityEngine.JsonUtility` in the Combat assembly; SimulationCore contains DTOs but no JSON library. The .NET test uses `System.Text.Json` in the test/server host against the same concrete DTO fields. Task 10 deliberately records exactly eight non-terminal ticks: mode-terminal behavior and the real Attack -> Damage -> Death pipeline do not exist until Tasks 11 and 12, so this task must not synthesize a result or depend on those later tasks.

Update the server host in this task as well. `ServerReplayJsonCodec.Deserialize(string)` uses `System.Text.Json` to create the same DTO graph; `Program` calls it instead of the removed `ReplayFile.FromJson`. `ReplayValidator` keeps the Task 3 constructor signature, creates one Session through its injected factory, calls `ReplayPlayback.Run`, and maps that result to the existing stable CLI exits. Neither server file constructs `SimWorld` or maintains a second playback loop.

- [ ] **Step 4: Run Unity and .NET round trips**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SimReplayRoundTripTests -testResults /tmp/zweb-sim-migration/task-10/green.xml -logFile /tmp/zweb-sim-migration/task-10/green.log
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --filter FullyQualifiedName~ReplayRoundTripTests --logger "trx;LogFileName=task-10-replay.trx" --results-directory /tmp/zweb-sim-migration/task-10
```

Expected: all executed commands, sequences, empty ticks, eight per-tick checksums, and the final non-terminal checksum survive both host codecs and Session playback; `FinalResult` remains null. Task 12 upgrades this fixture and reruns both codecs and Validator against a real terminal result.

- [ ] **Step 5: Commit recorder and playback**

```bash
git add Assets/Scripts/SimulationCore/Command Assets/Scripts/Combat/Replay Assets/Tests/SimulationCore/SimReplayRoundTripTests.cs Assets/Tests/SimulationCore/ReplayFixture.cs Server/SimulationCore.Tests/ReplayRoundTripTests.cs Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj Server/ReplayValidator
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-10/meta
git commit -m "fix(sim): record executed commands and replay session results"
```

---

### Task 11: Implement the approved four-mode oracle and deterministic results

**Closes:** COMBAT-005, COMBAT-007, SIM-016, TEST-008

**Files:**
- Create: `Assets/Scripts/SimulationCore/Modes/SimModeRules.cs`
- Modify: `Assets/Scripts/SimulationCore/Content/DeterministicContentManifest.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SimBattleSession.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/TickResult.cs`
- Create: `Assets/Tests/SimulationCore/ModeBehaviorTests.cs`
- Modify: `Assets/Tests/SimulationCore/SessionTestDriver.cs`
- Modify: `Assets/Tests/SimulationCore/ReplayFixture.cs`
- Create: `Server/SimulationCore.Tests/ModeBehaviorGoldenTests.cs`

**Interfaces:**
- Consumes: deterministic commands, content, Session tick state, and canonical result contract.
- Produces: `SimModeProfile`, `SimModeRules`, deterministic wave schedules, zero-attacker policy, terminal evaluation, and canonical `SimBattleResult`.

- [ ] **Step 1: Encode the approved behavior matrix as executable tests**

```csharp
[TestCase(SimCombatMode.VillageAttack, true, false)]
[TestCase(SimCombatMode.BuilderBase, true, false)]
[TestCase(SimCombatMode.ClanWar, true, false)]
[TestCase(SimCombatMode.Campaign, false, true)]
public void SupportedModes_MatchApprovedBehaviorMatrix(
    SimCombatMode mode, bool manualDeployment, bool scheduledWaves)
{
    SimModeProfile profile = ModeTestFixture.Manifest().GetMode(mode);
    Assert.That(profile.ManualDeployment, Is.EqualTo(manualDeployment));
    Assert.That(profile.WaveTicks.Length > 0, Is.EqualTo(scheduledWaves));
    Assert.That(profile.TimeoutTicks, Is.GreaterThan(0));
}

[Test]
public void SimulationModes_ImplementApprovedStartupWaveAndTerminalMatrix()
{
    SimCombatMode[] modes =
    {
        SimCombatMode.VillageAttack,
        SimCombatMode.BuilderBase,
        SimCombatMode.ClanWar,
        SimCombatMode.Campaign
    };
    Assert.That(modes.Select(mode => ModeTestFixture.Manifest().GetMode(mode).Mode),
        Is.EqualTo(modes));
    using SimBattleSession campaign = SessionTestDriver.StartedMode(SimCombatMode.Campaign);
    using SimBattleSession builder = SessionTestDriver.StartedMode(SimCombatMode.BuilderBase);
    Assert.That(campaign.Tick().Result, Is.Null);
    Assert.That(builder.Tick().Result, Is.Null);
}

[Test]
public void CampaignAndBuilderBase_StartupDoesNotPrematurelyEnd()
{
    SimBattleSession campaign = SessionTestDriver.StartedMode(SimCombatMode.Campaign);
    TickResult campaignTick0 = campaign.Tick();
    Assert.That(campaignTick0.Result, Is.Null);

    SimBattleSession builder = SessionTestDriver.StartedMode(SimCombatMode.BuilderBase);
    TickResult builderDeployTick = builder.Tick();
    Assert.That(builderDeployTick.Result, Is.Null);
    Assert.That(builder.State.Phase, Is.EqualTo(SimCombatPhase.Deploying));
}

[Test]
public void CombatResult_UnityAndDotNet_AreIdentical()
{
    SimBattleResult result = ModeTestFixture.EvaluateVillageAttackVector();
    Assert.That(result.WinnerTeamId, Is.EqualTo(SimTeam.Attacker));
    Assert.That(result.Stars, Is.EqualTo(3));
    Assert.That(result.LootGold, Is.EqualTo(500));
    Assert.That(result.LootElixir, Is.EqualTo(300));
    Assert.That(result.FinalChecksum, Is.Not.EqualTo(0UL));
}
```

- [ ] **Step 2: Run mode tests and capture the red result**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-11
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.ModeBehaviorTests -testResults /tmp/zweb-sim-migration/task-11/red.xml -logFile /tmp/zweb-sim-migration/task-11/red.log
```

Expected: profiles do not exist, zero attackers end a mode immediately, campaign has no authoritative wave ownership, or loot uses host time/randomness.

- [ ] **Step 3: Add manifest-backed mode profiles and integer result formulas**

```csharp
public sealed class SimModeProfile
{
    public SimCombatMode Mode { get; }
    public bool ManualDeployment { get; }
    public int MaxDeployCount { get; }
    public int DeployDurationTicks { get; }
    public int TimeoutTicks { get; }
    private readonly int[] _waveTicks;
    public int[] WaveTicks => (int[])_waveTicks.Clone();

    public SimModeProfile(SimCombatMode mode, bool manualDeployment, int maxDeployCount,
        int deployDurationTicks, int timeoutTicks, int[] waveTicks)
    {
        Mode = mode;
        ManualDeployment = manualDeployment;
        MaxDeployCount = maxDeployCount;
        DeployDurationTicks = deployDurationTicks;
        TimeoutTicks = timeoutTicks;
        _waveTicks = (int[])waveTicks.Clone();
    }
}
```

Extend the manifest with the exact APIs `DeterministicContentManifest(int schemaVersion, SimArchetypeDefinition[] archetypes, SimModeProfile[] modes)` and `SimModeProfile GetMode(SimCombatMode mode)`. Clone and sort modes by numeric mode ID, reject duplicates or missing supported modes, and encode every profile field plus each sorted wave tick after the archetype rows. The original two-argument constructor delegates to the three-argument constructor with an empty mode array only for Task 2 compatibility; Session startup rejects an empty mode table after this task.

Replace `SessionTestDriver.ManifestBytes` and add the mode helpers in the same partial class:

```csharp
public static byte[] ManifestBytes() => new DeterministicContentManifest(1, new[]
{
    new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
    new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
    new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 20, 10000, 20)
}, new[]
{
    new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 600, 3600, System.Array.Empty<int>()),
    new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 3600, System.Array.Empty<int>()),
    new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 3600, System.Array.Empty<int>()),
    new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 2400, new[] { 0, 100, 200, 300, 400 })
}).ToCanonicalBytes();

public static SimBattleSession StartedMode(SimCombatMode mode)
{
    InitialEntitySpec[] entities = mode == SimCombatMode.Campaign ||
        mode == SimCombatMode.BuilderBase
        ? new[] { new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1) }
        : null;
    BattleInit baseline = BattleInit(42, entities);
    var init = new BattleInit(baseline.ProtocolVersion, baseline.SchemaVersion,
        baseline.MatchId, baseline.SessionId, baseline.Seed, baseline.TickRate, mode,
        baseline.ContentHash, baseline.Players, baseline.InitialEntities,
        baseline.InputDeadlineTicks);
    var session = (SimBattleSession)new SimBattleSessionFactory().Create(ManifestBytes());
    StartResult started = session.Start(init);
    if (!started.Accepted) throw new System.InvalidOperationException(started.Code.ToString());
    return session;
}
```

Define the test fixture in `ModeBehaviorTests.cs`:

```csharp
internal static class ModeTestFixture
{
    public static DeterministicContentManifest Manifest() =>
        DeterministicContentManifest.Parse(SessionTestDriver.ManifestBytes());

    public static SimBattleResult EvaluateVillageAttackVector()
    {
        SimModeRules rules = new SimModeRules(
            Manifest().GetMode(SimCombatMode.VillageAttack));
        SimTerminalOutcome outcome = rules.Evaluate(new SimModeEvaluationInput(
            completedTick: 120, attackerAlive: 1, defenderAlive: 0,
            destructionBasisPoints: 10000, futureWaves: 0, timedOut: false));
        return SimBattleResult.FromOutcome(
            "mode-vector", outcome, finalTick: 120, finalChecksum: 0xA11CEUL);
    }
}
```

Commit these profiles to the canonical manifest: VillageAttack `(true,40,600,3600,[])`, BuilderBase `(true,30,600,3600,[])`, ClanWar `(true,40,600,3600,[])`, Campaign `(false,0,0,2400,[0,100,200,300,400])`. Include every field and sorted wave tick in manifest bytes/hash.

Define immutable `SimModeEvaluationInput` and `SimTerminalOutcome` in this task. `SimModeRules.Evaluate` is a pure mode oracle over completed tick, alive counts, destruction basis points, remaining waves, and timeout; it does not require the Attack/Damage/Death systems introduced in Task 12. `SimBattleResult.FromOutcome` binds the numeric outcome to match ID, final tick, and the already-computed full-state checksum. `SimModeRules` keeps deployment open without treating zero attackers as defeat. Manual modes seal deployment on explicit host command or tick 600; only then does zero attackers mean defender victory. Campaign emits scheduled attacker waves inside Session and permits zero attackers while a future wave remains. Mutual elimination returns draw. Timeout returns defender victory. Result calculations use integer destruction basis points: village three stars at 10000, two at 5000, one above zero; max deterministic loot is 500 gold/300 elixir scaled by destruction. Builder/Clan/Campaign formulas are fixed in the manifest-backed rules and include no clock or RNG calls. `ResultId` is `MatchId + ":" + FinalTick + ":" + FinalChecksum.ToString("X16")`.

- [ ] **Step 4: Run Unity and .NET mode matrices**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.ModeBehaviorTests -testResults /tmp/zweb-sim-migration/task-11/green.xml -logFile /tmp/zweb-sim-migration/task-11/green.log
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --filter FullyQualifiedName~ModeBehaviorGoldenTests --logger "trx;LogFileName=task-11-modes.trx" --results-directory /tmp/zweb-sim-migration/task-11
```

Expected: all four rows, no-attacker startup, waves, draw, timeout, stars, loot, result ID, and supplied final checksum match across Unity and .NET without depending on Task 12's combat systems.

- [ ] **Step 5: Commit mode ownership and deterministic results**

```bash
git add Assets/Scripts/SimulationCore/Modes Assets/Scripts/SimulationCore/Content Assets/Scripts/SimulationCore/Session Assets/Tests/SimulationCore/ModeBehaviorTests.cs Server/SimulationCore.Tests/ModeBehaviorGoldenTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-11/meta
git commit -m "feat(sim): own supported modes and deterministic results"
```

---

### Task 12: Move targeting, path planning, defense, and damage flow into Core

**Closes:** COMBAT-003, COMBAT-004, SIM-015, TEST-005

**Files:**
- Create: `Assets/Scripts/SimulationCore/Systems/SimTargetSelectionSystem.cs`
- Create: `Assets/Scripts/SimulationCore/Systems/SimPathPlanningSystem.cs`
- Modify: `Assets/Scripts/SimulationCore/AI/IntAStarPathfinding.cs`
- Modify: `Assets/Scripts/SimulationCore/Systems/SimAttackSystem.cs`
- Modify: `Assets/Scripts/SimulationCore/Systems/SimDamageSystem.cs`
- Modify: `Assets/Scripts/SimulationCore/Systems/SimDeathSystem.cs`
- Modify: `Assets/Scripts/SimulationCore/Orchestrator/SimCombatBootstrap.cs`
- Modify: `Assets/Tests/SimulationCore/SimWorldTests.cs`
- Modify: `Assets/Tests/SimulationCore/SimBattleDeterminismTests.cs`
- Modify: `Assets/Tests/SimulationCore/SimReplayRoundTripTests.cs`
- Replace: `Assets/Scripts/Combat/Tests/BattleE2ETests.cs` with `Assets/Tests/SimulationCore/BattleSessionE2ETests.cs`
- Create: `Server/SimulationCore.Tests/BattleSessionE2ETests.cs`
- Modify: `Server/SimulationCore.Tests/ReplayRoundTripTests.cs`

**Interfaces:**
- Consumes: stable world iteration, manifest combat stats, Task 10 recorder/playback, Task 11 pure mode rules, and real commands.
- Produces: Core-owned target acquisition/path planning, a real Attack -> Damage -> Death -> Result pipeline, and terminal replay export/parse/Validator evidence.

- [ ] **Step 1: Write target-neutral path, defense, and full-pipeline tests**

```csharp
[Test]
public void BattlePathfinding_DiagonalBesideSingleWall_IsRejected()
{
    var blocked = new System.Collections.Generic.HashSet<(int, int)> { (1, 0) };
    var pathfinder = new IntAStarPathfinding(4, 4,
        (x, y) => x >= 0 && x < 4 && y >= 0 && y < 4 && !blocked.Contains((x, y)));
    var path = pathfinder.FindPath(0, 0, 1, 1);
    Assert.That(path[0], Is.Not.EqualTo((1, 1)));
}

[Test]
public void DefenseBuilding_AcquiresAndDamagesAttacker()
{
    SimBattleSession session = SessionTestDriver.StartedDefenseRangeBattle();
    int attackerHp = session.TestQueryHealth(ownerPlayerId: 0);
    session.Tick();
    session.Tick();
    Assert.That(session.TestQueryHealth(ownerPlayerId: 0), Is.LessThan(attackerHp));
}

[Test]
public void DefenseTargetingAndAI_UnityAndDotNet_ProduceSameChecksums()
{
    using SimBattleSession first = SessionTestDriver.StartedDefenseRangeBattle();
    using SimBattleSession second = SessionTestDriver.StartedDefenseRangeBattle();
    for (int tick = 0; tick < 25; tick++)
        Assert.That(first.Tick().Checksum, Is.EqualTo(second.Tick().Checksum));
}

[Test]
public void BattleSession_AttackDamageDeathResult_EndToEnd()
{
    SimBattleSession session = SessionTestDriver.StartedOneHitBattle();
    TickResult terminal = null;
    for (int tick = 0; tick < 20 && terminal == null; tick++)
    {
        TickResult result = session.Tick();
        if (result.Result != null) terminal = result;
    }
    Assert.That(terminal, Is.Not.Null);
    Assert.That(terminal.Events.Any(e => e.Type == SimBattleEventType.DamageApplied), Is.True);
    Assert.That(terminal.Events.Any(e => e.Type == SimBattleEventType.EntityDestroyed), Is.True);
    Assert.That(terminal.Result.WinnerTeamId, Is.EqualTo(SimTeam.Attacker));
}
```

Add these exact partial test-driver methods. They consume only Session contracts and the full `SimBattleState.Presentation`; tests never access `SimWorld`:

```csharp
public static SimBattleSession StartedDefenseRangeBattle()
{
    BattleInit init = BattleInit(42, new[]
    {
        new InitialEntitySpec(10, 7, 2, SimTeam.Attacker, 0),
        new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1)
    });
    var session = (SimBattleSession)new SimBattleSessionFactory().Create(ManifestBytes());
    StartResult started = session.Start(init);
    if (!started.Accepted) throw new System.InvalidOperationException(started.Code.ToString());
    session.Submit(new EndDeploymentCmd(0, 0, 1));
    return session;
}

public static SimBattleSession StartedOneHitBattle()
{
    byte[] manifest = OneHitManifestBytes();
    BattleInit init = OneHitBattleInit(manifest);
    var session = (SimBattleSession)new SimBattleSessionFactory().Create(manifest);
    StartResult started = session.Start(init);
    if (!started.Accepted) throw new System.InvalidOperationException(started.Code.ToString());
    session.Submit(new EndDeploymentCmd(0, 0, 1));
    return session;
}

public static BattleInit OneHitBattleInit(byte[] canonicalManifestBytes)
{
    byte[] hash = DeterministicContentManifest.Parse(canonicalManifestBytes).ContentHash;
    return new BattleInit(1, 1, "one-hit-match", "one-hit-session", 42, 20,
        SimCombatMode.VillageAttack, hash,
        new[] { new SimPlayerIdentity(0, "attacker"), new SimPlayerIdentity(1, "defender") },
        new[]
        {
            new InitialEntitySpec(10, 7, 2, SimTeam.Attacker, 0),
            new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1)
        }, 6);
}

public static byte[] OneHitManifestBytes() => new DeterministicContentManifest(1, new[]
{
    new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
    new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
    new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 500, 10000, 20)
}, new[]
{
    new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 600, 3600, System.Array.Empty<int>()),
    new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 3600, System.Array.Empty<int>()),
    new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 3600, System.Array.Empty<int>()),
    new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 2400, new[] { 0, 100, 200, 300, 400 })
}).ToCanonicalBytes();

public static int TestQueryHealth(this SimBattleSession session, int ownerPlayerId) =>
    session.State.Presentation.Entities.Single(entity =>
        entity.OwnerPlayerId == ownerPlayerId).Health;
```

`OneHitManifestBytes()` is exact test data, not a production combat override. Add this fixture assertion to `BattleSessionE2ETests.cs`:

```csharp
[Test]
public void OneHitManifest_ChangesOnlyBarbarianDamage()
{
    DeterministicContentManifest baseline =
        DeterministicContentManifest.Parse(SessionTestDriver.ManifestBytes());
    DeterministicContentManifest oneHit =
        DeterministicContentManifest.Parse(SessionTestDriver.OneHitManifestBytes());

    AssertArchetypeEqual(baseline.GetArchetype(1), oneHit.GetArchetype(1), true);
    AssertArchetypeEqual(baseline.GetArchetype(2), oneHit.GetArchetype(2), true);
    AssertArchetypeEqual(baseline.GetArchetype(10), oneHit.GetArchetype(10), false);
    Assert.That(baseline.GetArchetype(10).Damage, Is.EqualTo(20));
    Assert.That(oneHit.GetArchetype(10).Damage, Is.EqualTo(500));

    foreach (SimCombatMode mode in new[]
    {
        SimCombatMode.VillageAttack, SimCombatMode.BuilderBase,
        SimCombatMode.ClanWar, SimCombatMode.Campaign
    })
    {
        SimModeProfile expected = baseline.GetMode(mode);
        SimModeProfile actual = oneHit.GetMode(mode);
        Assert.Multiple(() =>
        {
            Assert.That(actual.Mode, Is.EqualTo(expected.Mode));
            Assert.That(actual.ManualDeployment, Is.EqualTo(expected.ManualDeployment));
            Assert.That(actual.MaxDeployCount, Is.EqualTo(expected.MaxDeployCount));
            Assert.That(actual.DeployDurationTicks, Is.EqualTo(expected.DeployDurationTicks));
            Assert.That(actual.TimeoutTicks, Is.EqualTo(expected.TimeoutTicks));
            Assert.That(actual.WaveTicks, Is.EqualTo(expected.WaveTicks));
        });
    }
}

private static void AssertArchetypeEqual(
    SimArchetypeDefinition expected, SimArchetypeDefinition actual, bool compareDamage)
{
    Assert.Multiple(() =>
    {
        Assert.That(actual.ArchetypeId, Is.EqualTo(expected.ArchetypeId));
        Assert.That(actual.Name, Is.EqualTo(expected.Name));
        Assert.That(actual.MaxHealth, Is.EqualTo(expected.MaxHealth));
        Assert.That(actual.SpeedRaw, Is.EqualTo(expected.SpeedRaw));
        if (compareDamage) Assert.That(actual.Damage, Is.EqualTo(expected.Damage));
        Assert.That(actual.RangeRaw, Is.EqualTo(expected.RangeRaw));
        Assert.That(actual.CooldownTicks, Is.EqualTo(expected.CooldownTicks));
    });
}
```

Now that both the pure mode oracle and real combat pipeline exist, replace Task 10's non-terminal calls in `ReplayExportParseValidate_RoundTripsExecutedCommands` and `UnityAndValidator_SameBattleInitAndCommands_SameTickResults` with `ReplayFixture.RecordTerminalBattle(OneHitManifestBytes())`. Add this exact fixture method; the complete Unity and .NET terminal assertions follow it:

```csharp
public static ReplayFile RecordTerminalBattle(byte[] canonicalManifestBytes)
{
    BattleInit init = SessionTestDriver.OneHitBattleInit(canonicalManifestBytes);
    using ISimBattleSession session =
        new SimBattleSessionFactory().Create(canonicalManifestBytes);
    StartResult started = session.Start(init);
    if (!started.Accepted) throw new System.InvalidOperationException(started.Code.ToString());
    var recorder = new CommandRecorder(init);
    session.Submit(new EndDeploymentCmd(0, 0, 1));
    for (int count = 0; count < 20; count++)
    {
        TickResult tick = session.Tick();
        recorder.Record(tick);
        if (tick.Result != null)
        {
            ReplayFile replay = recorder.Export();
            if (replay.FinalResult == null)
                throw new System.InvalidOperationException("terminal result was not recorded");
            return replay;
        }
    }
    throw new System.InvalidOperationException("terminal replay fixture did not finish");
}
```

`OneHitBattleInit(byte[])` is the detached `BattleInit` construction used by `StartedOneHitBattle`; the latter delegates to it so the replay and E2E fixtures cannot drift. Replace both Task 10 test bodies with the terminal fixture and add this helper unchanged to both `SimReplayRoundTripTests.cs` and `Server/SimulationCore.Tests/ReplayRoundTripTests.cs`:

```csharp
private static void AssertCanonicalBattleResult(
    SimBattleResult expected, SimBattleResult actual)
{
    Assert.That(actual, Is.Not.Null);
    Assert.Multiple(() =>
    {
        Assert.That(actual.MatchId, Is.EqualTo(expected.MatchId));
        Assert.That(actual.ResultId, Is.EqualTo(expected.ResultId));
        Assert.That(actual.WinnerTeamId, Is.EqualTo(expected.WinnerTeamId));
        Assert.That(actual.Stars, Is.EqualTo(expected.Stars));
        Assert.That(actual.LootGold, Is.EqualTo(expected.LootGold));
        Assert.That(actual.LootElixir, Is.EqualTo(expected.LootElixir));
        Assert.That(actual.FinalTick, Is.EqualTo(expected.FinalTick));
        Assert.That(actual.FinalChecksum, Is.EqualTo(expected.FinalChecksum));
    });
}
```

Use these exact Unity test bodies:

```csharp
[Test]
public void ReplayExportParseValidate_RoundTripsExecutedCommands()
{
    byte[] manifest = SessionTestDriver.OneHitManifestBytes();
    ReplayFile recorded = ReplayFixture.RecordTerminalBattle(manifest);
    ReplayFile parsed = UnityReplayJsonCodec.Deserialize(
        UnityReplayJsonCodec.Serialize(recorded));
    using ISimBattleSession replaySession = new SimBattleSessionFactory().Create(manifest);

    ReplayPlaybackResult replayed = ReplayPlayback.Run(replaySession, parsed);

    Assert.That(replayed.Valid, Is.True);
    Assert.That(replayed.FinalChecksum, Is.EqualTo(recorded.FinalChecksum));
    Assert.That(replaySession.State.CompletedTick, Is.EqualTo(recorded.FinalTick));
    AssertCanonicalBattleResult(recorded.FinalResult, parsed.FinalResult);
    AssertCanonicalBattleResult(recorded.FinalResult, replaySession.State.Result);
}

[Test]
public void UnityAndValidator_SameBattleInitAndCommands_SameTickResults()
{
    byte[] manifest = SessionTestDriver.OneHitManifestBytes();
    ReplayFile recorded = ReplayFixture.RecordTerminalBattle(manifest);
    using ISimBattleSession validatorSession = new SimBattleSessionFactory().Create(manifest);

    ReplayPlaybackResult replayed = ReplayPlayback.Run(validatorSession, recorded);

    Assert.That(replayed.Valid, Is.True);
    Assert.That(replayed.FinalChecksum, Is.EqualTo(recorded.FinalChecksum));
    Assert.That(validatorSession.State.CompletedTick, Is.EqualTo(recorded.FinalTick));
    AssertCanonicalBattleResult(recorded.FinalResult, validatorSession.State.Result);
}
```

Use these exact .NET test bodies in `Server/SimulationCore.Tests/ReplayRoundTripTests.cs`:

```csharp
[Test]
public void ReplayExportParseValidate_RoundTripsExecutedCommands()
{
    byte[] manifest = SessionTestDriver.OneHitManifestBytes();
    ReplayFile recorded = ReplayFixture.RecordTerminalBattle(manifest);
    ReplayFile parsed = ServerReplayJsonCodec.Deserialize(
        ServerReplayJsonCodec.Serialize(recorded));
    using ISimBattleSession replaySession = new SimBattleSessionFactory().Create(manifest);

    ReplayPlaybackResult replayed = ReplayPlayback.Run(replaySession, parsed);

    Assert.That(replayed.Valid, Is.True);
    Assert.That(replayed.FinalChecksum, Is.EqualTo(recorded.FinalChecksum));
    Assert.That(replaySession.State.CompletedTick, Is.EqualTo(recorded.FinalTick));
    AssertCanonicalBattleResult(recorded.FinalResult, parsed.FinalResult);
    AssertCanonicalBattleResult(recorded.FinalResult, replaySession.State.Result);
}

[Test]
public void UnityAndValidator_SameBattleInitAndCommands_SameTickResults()
{
    byte[] manifest = SessionTestDriver.OneHitManifestBytes();
    ReplayFile recorded = ReplayFixture.RecordTerminalBattle(manifest);
    using ISimBattleSession validatorSession = new SimBattleSessionFactory().Create(manifest);

    ReplayPlaybackResult replayed = ReplayPlayback.Run(validatorSession, recorded);

    Assert.That(replayed.Valid, Is.True);
    Assert.That(replayed.FinalChecksum, Is.EqualTo(recorded.FinalChecksum));
    Assert.That(validatorSession.State.CompletedTick, Is.EqualTo(recorded.FinalTick));
    AssertCanonicalBattleResult(recorded.FinalResult, validatorSession.State.Result);
}
```

- [ ] **Step 2: Run the four acceptance tests and verify red**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-12
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter BattlePathfinding_DiagonalBesideSingleWall_IsRejected -testResults /tmp/zweb-sim-migration/task-12/path-red.xml -logFile /tmp/zweb-sim-migration/task-12/path-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter DefenseBuilding_AcquiresAndDamagesAttacker -testResults /tmp/zweb-sim-migration/task-12/defense-red.xml -logFile /tmp/zweb-sim-migration/task-12/defense-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter DefenseTargetingAndAI_UnityAndDotNet_ProduceSameChecksums -testResults /tmp/zweb-sim-migration/task-12/determinism-red.xml -logFile /tmp/zweb-sim-migration/task-12/determinism-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter BattleSession_AttackDamageDeathResult_EndToEnd -testResults /tmp/zweb-sim-migration/task-12/e2e-red.xml -logFile /tmp/zweb-sim-migration/task-12/e2e-red.log
```

Expected: the Legacy path cuts the corner, defenders never obtain targets, or the full Session never reaches a real damage/death/result chain.

- [ ] **Step 3: Add stable target selection before movement and attack**

```csharp
private static bool IsBetterTarget(
    int distance, SimEntity candidate, int bestDistance, SimEntity best)
{
    if (distance != bestDistance) return distance < bestDistance;
    if (candidate.Id != best.Id) return candidate.Id < best.Id;
    return candidate.Generation < best.Generation;
}
```

`SimTargetSelectionSystem` runs at order `5`; for every alive entity with attack and team, it scans alive enemies in stable order and chooses minimum Manhattan grid distance, then ID/generation. It works for buildings without Movement. `SimPathPlanningSystem` runs at order `8` and assigns/replaces `SimPath` only for targets outside range and entities with Movement. `IntAStarPathfinding` rejects a diagonal when either adjacent cardinal cell is blocked and keeps `F,H,X,Y` heap tie-breaks.

Bootstrap order is TargetSelection `5`, PathPlanning `8`, TargetCache `9`, Movement `10`, Attack `20`, Damage `30`, Death `90`. Attack uses the Session's completed/next tick clock, not a private un-restored counter. Damage emits deterministic events; Death removes entities after the scan and mode rules evaluate terminal state after Death. Delete every external `SimpleAI` helper from correctness tests and drive only `session.Tick()`.

- [ ] **Step 4: Run Core and cross-host battle suites**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.BattleSessionE2ETests -testResults /tmp/zweb-sim-migration/task-12/e2e-green.xml -logFile /tmp/zweb-sim-migration/task-12/e2e-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SimBattleDeterminismTests -testResults /tmp/zweb-sim-migration/task-12/determinism.xml -logFile /tmp/zweb-sim-migration/task-12/determinism.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.SimReplayRoundTripTests -testResults /tmp/zweb-sim-migration/task-12/replay-terminal.xml -logFile /tmp/zweb-sim-migration/task-12/replay-terminal.log
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --filter FullyQualifiedName~BattleSessionE2ETests --logger "trx;LogFileName=task-12-battle.trx" --results-directory /tmp/zweb-sim-migration/task-12
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --filter FullyQualifiedName~ReplayRoundTripTests --logger "trx;LogFileName=task-12-replay-terminal.trx" --results-directory /tmp/zweb-sim-migration/task-12
```

Expected: path, defender targeting, damage, death, terminal result, per-tick checksums, and terminal replay export/parse/Validator round-trip pass in Unity and .NET without external AI.

- [ ] **Step 5: Commit Core combat behavior**

```bash
git add Assets/Scripts/SimulationCore/AI Assets/Scripts/SimulationCore/Systems Assets/Scripts/SimulationCore/Orchestrator Assets/Tests/SimulationCore Assets/Scripts/Combat/Tests/BattleE2ETests.cs Server/SimulationCore.Tests/BattleSessionE2ETests.cs Server/SimulationCore.Tests/ReplayRoundTripTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-12/meta
git commit -m "feat(sim): move targeting and combat pipeline into core"
```

---

### Task 13: Emit immutable presentation frames and create correctly mapped initial views

**Closes:** SIM-017

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Presentation/PresentationFrame.cs`
- Modify: `Assets/Scripts/SimulationCore/Session/SimBattleSession.cs`
- Create: `Assets/Scripts/Combat/Presentation/IEntityViewFactory.cs`
- Create: `Assets/Scripts/Combat/Presentation/ViewConfigEntityFactory.cs`
- Create: `Assets/Scripts/Combat/Presentation/SessionPresentationBridge.cs`
- Modify: `Assets/Scripts/Combat/View/ViewConfig.cs`
- Replace: `Assets/Scripts/Combat/SimulationSyncBridge.cs` with a compatibility wrapper over `SessionPresentationBridge`
- Create: `Assets/Tests/SimulationCore/PresentationFrameTests.cs`
- Create: `Assets/Scripts/Combat/Tests/SessionPresentationBridgeTests.cs`
- Modify: `Assets/Scripts/Combat/Tests/ZWebGLFrameWork.Combat.Tests.asmdef`

**Interfaces:**
- Consumes: Task 1 presentation contracts, Task 2 identity components, and stable Session state.
- Produces: full initial/resync frames, per-tick deltas, `IEntityViewFactory`, and a bridge with no world dependency.

- [ ] **Step 1: Write full-frame, delta, and prefab-mapping tests**

```csharp
[Test]
public void PresentationFrames_ProvideInitialDeltaAndRollbackRevisionWithoutWorldAccess()
{
    SimBattleSession session = SessionTestDriver.StartedBattle();
    PresentationFrame initial = session.State.Presentation;
    Assert.That(initial.IsFull, Is.True);
    Assert.That(initial.CompletedTick, Is.EqualTo(-1));
    Assert.That(initial.Entities, Is.Not.Empty);

    TickResult tick0 = session.Tick();
    Assert.That(tick0.Presentation.IsFull, Is.False);
    Assert.That(tick0.Presentation.CompletedTick, Is.EqualTo(0));

    SimSnapshot snapshot = session.CaptureSnapshot();
    session.Tick();
    SnapshotRestoreResult restored = session.RestoreSnapshot(snapshot);
    Assert.That(restored.FullFrame.IsFull, Is.True);
    Assert.That(restored.FullFrame.TimelineRevision, Is.GreaterThan(tick0.TimelineRevision));
}
```

```csharp
[Test]
public void PresentationBridge_CreatesInitialViewsUsingCorrectArchetype()
{
    var factory = new RecordingEntityViewFactory();
    var bridge = new SessionPresentationBridge(factory);
    var frame = PresentationTestFrames.Full(
        new EntityPresentationState(1, 1, 2, 1, SimTeam.Defender, 4, 5, 45000, 55000, 0, 200));

    bridge.Apply(frame);

    Assert.That(factory.CreatedArchetypes, Is.EqualTo(new[] { 2 }));
    Assert.That(bridge.ViewCount, Is.EqualTo(1));
}
```

- [ ] **Step 2: Run Core and Combat presentation tests and verify red**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-13
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.PresentationFrameTests -testResults /tmp/zweb-sim-migration/task-13/frame-red.xml -logFile /tmp/zweb-sim-migration/task-13/frame-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.Combat.Tests.SessionPresentationBridgeTests -testResults /tmp/zweb-sim-migration/task-13/bridge-red.xml -logFile /tmp/zweb-sim-migration/task-13/bridge-red.log
```

Expected: initial entities are absent because the old bridge subscribes too late, every view uses entry zero, or the bridge requires `SimWorld`.

- [ ] **Step 3: Build frames from stable entity states and consume them by key**

Keep the exact `EntityPresentationState` and `PresentationFrame` contracts from Task 1. Add this Unity-facing view boundary:

```csharp
public interface IEntityView
{
    void SetPosition(float x, float y);
}

public interface IEntityViewFactory
{
    IEntityView Create(EntityPresentationKey key, int archetypeId);
    void Destroy(IEntityView view);
}
```

Define these exact test helpers in `SessionPresentationBridgeTests.cs`; Task 14 reuses them from the same test assembly:

```csharp
internal static class PresentationTestFrames
{
    public static PresentationFrame Full(params EntityPresentationState[] entities) =>
        new PresentationFrame(true, -1, 0, 0, entities,
            System.Array.Empty<EntityPresentationState>(),
            System.Array.Empty<EntityPresentationState>(),
            System.Array.Empty<EntityPresentationKey>());

    public static PresentationFrame FullAt(long revision, int completedTick, int entityId, long xRaw)
    {
        var entity = State(entityId, xRaw);
        return new PresentationFrame(true, completedTick, completedTick + 1, revision,
            new[] { entity }, System.Array.Empty<EntityPresentationState>(),
            System.Array.Empty<EntityPresentationState>(),
            System.Array.Empty<EntityPresentationKey>());
    }

    public static PresentationFrame DeltaAt(long revision, int completedTick, int entityId, long xRaw)
    {
        var entity = State(entityId, xRaw);
        return new PresentationFrame(false, completedTick, completedTick + 1, revision,
            new[] { entity }, System.Array.Empty<EntityPresentationState>(),
            new[] { entity }, System.Array.Empty<EntityPresentationKey>());
    }

    private static EntityPresentationState State(int entityId, long xRaw) =>
        new EntityPresentationState(entityId, 1, 10, 0, SimTeam.Attacker,
            (int)(xRaw / 10000), 0, xRaw, 0, 0, 100);
}

internal sealed class RecordingEntityViewFactory : IEntityViewFactory
{
    private readonly System.Collections.Generic.Dictionary<int, RecordingView> _views =
        new System.Collections.Generic.Dictionary<int, RecordingView>();
    private readonly System.Collections.Generic.List<int> _createdArchetypes =
        new System.Collections.Generic.List<int>();
    public System.Collections.Generic.IReadOnlyList<int> CreatedArchetypes => _createdArchetypes;
    public int CreatedCount { get; private set; }
    public int DestroyedCount { get; private set; }

    public IEntityView Create(EntityPresentationKey key, int archetypeId)
    {
        var view = new RecordingView();
        _views.Add(key.EntityId, view);
        _createdArchetypes.Add(archetypeId);
        CreatedCount++;
        return view;
    }

    public void Destroy(IEntityView view)
    {
        DestroyedCount++;
    }

    public float PositionX(int entityId) => _views[entityId].X;

    private sealed class RecordingView : IEntityView
    {
        public float X { get; private set; }
        public void SetPosition(float x, float y) => X = x;
    }
}
```

Session emits a full stable-ID-sorted entity array after Start and Restore. After each Tick it compares current immutable values with the prior frame and emits created, updated, and destroyed deltas; it never exposes component arrays or world references. `SimBattleState.Presentation` remains a full current projection for read-only diagnostics while `TickResult.Presentation` carries the delta. All collections are frozen at construction.

`SessionPresentationBridge` keys views by `(EntityId, Generation)`. Full frames destroy keys not present, create missing keys through `IEntityViewFactory.Create(key, archetypeId)`, and seed both interpolation endpoints. Delta frames apply creates/updates/destroys. `ViewConfigEntityFactory` calls `ViewConfig.GetPrefab(archetypeId)` and returns no view for a missing/null entry; it never indexes `Entries[0]`. `Cleanup` destroys every view and clears dictionaries.

- [ ] **Step 4: Run frame, bridge, and Session tests**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests.PresentationFrameTests -testResults /tmp/zweb-sim-migration/task-13/frame-green.xml -logFile /tmp/zweb-sim-migration/task-13/frame-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.Combat.Tests.SessionPresentationBridgeTests -testResults /tmp/zweb-sim-migration/task-13/bridge-green.xml -logFile /tmp/zweb-sim-migration/task-13/bridge-green.log
```

Expected: full/delta/revision semantics pass, correct archetypes create correct views, missing mappings create none, and no test accesses `SimWorld`.

- [ ] **Step 5: Commit immutable presentation projection**

```bash
git add Assets/Scripts/SimulationCore/Presentation Assets/Scripts/SimulationCore/Session Assets/Scripts/Combat/Presentation Assets/Scripts/Combat/View/ViewConfig.cs Assets/Scripts/Combat/SimulationSyncBridge.cs Assets/Tests/SimulationCore/PresentationFrameTests.cs Assets/Scripts/Combat/Tests
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-13/meta
git commit -m "feat(combat): render immutable session presentation frames"
```

---

### Task 14: Interpolate adjacent ticks, reset on revision, and clean up views

**Closes:** SIM-018

**Files:**
- Create: `Assets/Scripts/Combat/Presentation/PresentationInterpolator.cs`
- Modify: `Assets/Scripts/Combat/Presentation/SessionPresentationBridge.cs`
- Replace: `Assets/Scripts/Combat/View/Interpolator.cs` with a compatibility wrapper
- Modify: `Assets/Scripts/Combat/Tests/SessionPresentationBridgeTests.cs`

**Interfaces:**
- Consumes: Task 13 full/delta frames and stable entity keys.
- Produces: `BeginFrame(PresentationFrame)`, `Sample(float)`, revision reset, and idempotent `Cleanup()`.

- [ ] **Step 1: Write numeric interpolation, rollback, and leak tests**

```csharp
[Test]
public void Presentation_RendersBetweenTicks_AndResyncsAfterRollbackWithoutLeaks()
{
    var factory = new RecordingEntityViewFactory();
    var bridge = new SessionPresentationBridge(factory, tickRate: 20);
    bridge.Apply(PresentationTestFrames.FullAt(revision: 0, completedTick: -1, entityId: 1, xRaw: 0));
    bridge.Apply(PresentationTestFrames.DeltaAt(revision: 0, completedTick: 0, entityId: 1, xRaw: 10000));

    bridge.Sample(alpha: 0.5f);
    Assert.That(factory.PositionX(1), Is.EqualTo(0.5f).Within(0.0001f));

    bridge.Apply(PresentationTestFrames.FullAt(revision: 1, completedTick: 0, entityId: 1, xRaw: 30000));
    bridge.Sample(alpha: 0.25f);
    Assert.That(factory.PositionX(1), Is.EqualTo(3f).Within(0.0001f));

    bridge.Cleanup();
    bridge.Cleanup();
    Assert.That(bridge.ViewCount, Is.Zero);
    Assert.That(factory.CreatedCount, Is.EqualTo(factory.DestroyedCount));
}
```

- [ ] **Step 2: Run the test and verify red**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-14
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter Presentation_RendersBetweenTicks_AndResyncsAfterRollbackWithoutLeaks -testResults /tmp/zweb-sim-migration/task-14/red.xml -logFile /tmp/zweb-sim-migration/task-14/red.log
```

Expected: the old interpolator overwrites previous every render frame, rollback eases from stale history, or repeated cleanup leaks/double-destroys.

- [ ] **Step 3: Store endpoints only when logical frames arrive**

```csharp
public readonly struct InterpolationPair
{
    public EntityPresentationState Previous { get; }
    public EntityPresentationState Current { get; }
    public InterpolationPair(EntityPresentationState previous, EntityPresentationState current)
    {
        Previous = previous;
        Current = current;
    }
}
```

`PresentationInterpolator.BeginFrame` updates endpoints only on a new completed tick. A full frame or changed timeline revision clears all pairs and seeds `Previous == Current`, so the first sample after rollback snaps to authority state. `Sample(alpha)` clamps alpha to `[0,1]`, linearly interpolates raw integer X/Y before converting to float, and never modifies endpoints. `SessionPresentationBridge.Cleanup` is idempotent and releases all factory views, interpolation pairs, and event handlers.

- [ ] **Step 4: Run presentation and rollback suites**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.Combat.Tests.SessionPresentationBridgeTests -testResults /tmp/zweb-sim-migration/task-14/green.xml -logFile /tmp/zweb-sim-migration/task-14/green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter RollbackReplay_MatchesOriginalChecksumSequence -testResults /tmp/zweb-sim-migration/task-14/rollback.xml -logFile /tmp/zweb-sim-migration/task-14/rollback.log
```

Expected: exact 0.5 interpolation, authority snap on revision change, equal create/destroy counts, and unchanged rollback checksums.

- [ ] **Step 5: Commit interpolation lifecycle**

```bash
git add Assets/Scripts/Combat/Presentation Assets/Scripts/Combat/View/Interpolator.cs Assets/Scripts/Combat/Tests/SessionPresentationBridgeTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-14/meta
git commit -m "fix(combat): reset interpolation and clean views on rollback"
```

---

### Task 15: Route BattleState and CombatManager exclusively through Session

**Closes:** COMBAT-001, COMBAT-008

**Files:**
- Create: `Assets/Scripts/Combat/BattleInitFactory.cs`
- Create: `Assets/Scripts/Combat/Properties/AssemblyInfo.cs`
- Rewrite: `Assets/Scripts/Combat/CombatManager.cs`
- Modify: `Assets/Scripts/Combat/BattleParams.cs`
- Modify: `Assets/Scripts/FSM/Game/States/BattleState.cs`
- Modify: `Assets/Scripts/FSM/ZWebGLFrameWork.FSM.asmdef`
- Modify: `Assets/Scripts/UI/Panels/DeployPanel.cs`
- Modify: `Assets/Scripts/UI/Panels/BattleHUD.cs`
- Modify: `Assets/Scripts/UI/Panels/ResultPanel.cs`
- Modify: `Assets/Scripts/UI/ZWebGLFrameWork.UI.asmdef`
- Modify: `Assets/Scripts/Data/AppConfig.cs`
- Create: `Assets/Tests/PlayMode/Combat/ZWebGLFrameWork.Combat.PlayModeTests.asmdef`
- Create: `Assets/Tests/PlayMode/Combat/SimulationBattleFlowPlayModeTests.cs`
- Create: `Assets/Tests/Editor/SimulationArchitectureTests.cs`

**Interfaces:**
- Consumes: Session factory, canonical manifest, immutable Tick/Presentation/Result contracts, and EndDeployment command.
- Produces: `StartBattleAsync(BattleParams, CancellationToken)`, `SubmitDeployUnit`, `SubmitMove`, `SubmitSkill`, and one Session-driven Unity update loop.

- [ ] **Step 1: Write production-boundary and PlayMode flow tests**

Create the PlayMode asmdef before adding the test source:

```json
{
  "name": "ZWebGLFrameWork.Combat.PlayModeTests",
  "rootNamespace": "ZWebGLFrameWork.Combat.PlayModeTests",
  "references": [
    "ZWebGLFrameWork.Combat",
    "ZWebGLFrameWork.Data",
    "ZWebGLFrameWork.FSM",
    "ZWebGLFrameWork.Gameplay",
    "ZWebGLFrameWork.SimulationCore",
    "UniTask"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false,
  "optionalUnityReferences": ["TestAssemblies"]
}
```

```csharp
[Test]
public void ProductionAssemblies_DoNotBypassSimulationSession()
{
    string scripts = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Scripts");
    string[] offenders = System.IO.Directory.GetFiles(scripts, "*.cs", System.IO.SearchOption.AllDirectories)
        .Where(path => !path.Replace('\\', '/').Contains("/SimulationCore/") &&
            System.Text.RegularExpressions.Regex.IsMatch(
                System.IO.File.ReadAllText(path), @"\b(?:SimWorld|SimOrchestrator)\b"))
        .Select(path => path.Substring(UnityEngine.Application.dataPath.Length + 1)
            .Replace('\\', '/'))
        .ToArray();
    Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
}
```

```csharp
[UnityTest]
public System.Collections.IEnumerator BattleState_UsesSingleSimulationSession_EndToEnd()
{
    var host = new UnityEngine.GameObject("CombatManagerTestHost");
    var manager = host.AddComponent<CombatManager>();
    manager.ConfigureForTests(new SimBattleSessionFactory(), SessionPlayModeFixture.ManifestBytes(),
        new RecordingEntityViewFactory());
    BattleParams parameters = SessionPlayModeFixture.VillageAttackParams();
    Cysharp.Threading.Tasks.UniTask<SimBattleResult> completion =
        manager.StartBattleAsync(parameters, System.Threading.CancellationToken.None);

    Assert.That(manager.SubmitDeployUnit(new UnityEngine.Vector2Int(2, 2), 10).Accepted, Is.True);
    Assert.That(manager.EndDeployPhase().Accepted, Is.True);
    for (int frame = 0; frame < 600 && completion.Status == Cysharp.Threading.Tasks.UniTaskStatus.Pending; frame++)
        yield return null;

    Assert.That(completion.Status, Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded));
    Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Ended));
    Assert.That(manager.LastResult, Is.Not.Null);
    manager.StopBattle();
    Assert.That(manager.ActiveViewCount, Is.Zero);
    UnityEngine.Object.DestroyImmediate(host);
}

[UnityTest]
public System.Collections.IEnumerator BattleState_ExitBeforeStartCompletes_CancelsLateStart()
{
    var host = new UnityEngine.GameObject("BattleStateCancellationHost");
    var manager = host.AddComponent<CombatManager>();
    manager.ConfigureForTests(new SimBattleSessionFactory(),
        SessionPlayModeFixture.ManifestBytes(), new RecordingEntityViewFactory());
    var state = new ZWebGLFrameWork.FSM.BattleState(manager);

    state.OnEnter(SessionPlayModeFixture.VillageAttackParams());
    yield return null;
    Assert.That(manager.HasActiveSession, Is.True);

    state.OnExit();
    yield return null;

    Assert.That(manager.HasActiveSession, Is.False);
    Assert.That(manager.ActiveViewCount, Is.Zero);
    Assert.That(manager.LastResult, Is.Null,
        "a late completion after state exit must not commit or present a result");
    UnityEngine.Object.DestroyImmediate(host);
}
```

In the same PlayMode file, define a separate runtime-safe fixture and recorder; do not reference the Editor-only Combat or SimulationCore test assemblies:

```csharp
internal static class SessionPlayModeFixture
{
    public static byte[] ManifestBytes()
    {
        return new DeterministicContentManifest(1, new[]
        {
            new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
            new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
            new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 500, 10000, 1)
        }, new[]
        {
            new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 600, 600, System.Array.Empty<int>()),
            new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 600, System.Array.Empty<int>()),
            new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 600, System.Array.Empty<int>()),
            new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 600, new[] { 0 })
        }).ToCanonicalBytes();
    }

    public static BattleParams VillageAttackParams()
    {
        var attacker = new ZWebGLFrameWork.Data.VillageData();
        var defender = new ZWebGLFrameWork.Data.VillageData();
        defender.AddBuilding(new ZWebGLFrameWork.Data.BuildingData
        {
            Id = "defender-cannon",
            TypeId = 2,
            GridX = { Value = 8 },
            GridY = { Value = 2 }
        });
        BattleParams result = BattleParams.VillageAttack(attacker, defender);
        result.MatchId = "playmode-match";
        result.SessionId = "playmode-session";
        result.Seed = 42;
        result.AttackerPlayerId = 0;
        result.DefenderPlayerId = 1;
        return result;
    }
}

internal sealed class RecordingEntityViewFactory : IEntityViewFactory
{
    private sealed class View : IEntityView
    {
        public void SetPosition(float x, float y) { }
    }

    public IEntityView Create(EntityPresentationKey key, int archetypeId) => new View();
    public void Destroy(IEntityView view) { }
}
```

Add `MatchId`, `SessionId`, `Seed`, `AttackerPlayerId`, and `DefenderPlayerId` to `BattleParams` and require `BattleInitFactory` to consume them; no production fallback may hard-code any of these values.

Add `internal BattleState(CombatManager combatManager)` plus a default constructor delegating to `CombatManager.Instance`; store the non-null injected host and route the state async start through it. Expose `internal bool HasActiveSession => _session != null` on `CombatManager` to the friend PlayMode assembly. The exact second test above exits the concrete `BattleState`, proving its lifetime token cancels the real pending manager operation, disposes the Session, releases every view, and prevents a late result. This is the green acceptance evidence for runtime-owned `FSM-002`, not a generic token surrogate.

- [ ] **Step 2: Run architecture and PlayMode tests and verify red**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-15
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ProductionAssemblies_DoNotBypassSimulationSession -testResults /tmp/zweb-sim-migration/task-15/architecture-red.xml -logFile /tmp/zweb-sim-migration/task-15/architecture-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform PlayMode -testFilter BattleState_UsesSingleSimulationSession_EndToEnd -testResults /tmp/zweb-sim-migration/task-15/play-red.xml -logFile /tmp/zweb-sim-migration/task-15/play-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform PlayMode -testFilter BattleState_ExitBeforeStartCompletes_CancelsLateStart -testResults /tmp/zweb-sim-migration/task-15/battle-cancel-red.xml -logFile /tmp/zweb-sim-migration/task-15/battle-cancel-red.log
```

Expected: the architecture test lists `CombatManager`; the PlayMode file initially fails to compile against the missing Session-only configuration/start signatures. After those signatures exist, the behavioral red is the Legacy path or a completion that remains pending.

- [ ] **Step 3: Replace CombatManager with a Session host**

```csharp
public async Cysharp.Threading.Tasks.UniTask<SimBattleResult> StartBattleAsync(
    BattleParams parameters, System.Threading.CancellationToken cancellationToken)
{
    StopBattle();
    BattleInit init = _battleInitFactory.Create(parameters);
    _session = _sessionFactory.Create(_canonicalManifestBytes, _logger);
    StartResult started = _session.Start(init);
    if (!started.Accepted) throw new System.InvalidOperationException(started.Code.ToString());
    _presentation.Apply(started.InitialFrame);
    var completion = new Cysharp.Threading.Tasks.UniTaskCompletionSource<SimBattleResult>();
    _completion = completion;
    using (cancellationToken.Register(() => CancelBattle(completion, cancellationToken)))
        return await completion.Task;
}
```

`CancelBattle` first verifies reference identity with the active completion, calls `TrySetCanceled(cancellationToken)`, and then calls `StopBattle`. `StopBattle` captures and clears the active completion only after resolving it, cleans presentation, disposes Session, clears the accumulator, and is idempotent. Normal terminal handling calls `TrySetResult` exactly once before cleanup. No path clears a pending completion source.

Use this exact test injection surface and friend assembly:

```text
internal void ConfigureForTests(ISimBattleSessionFactory sessionFactory,
    byte[] canonicalManifestBytes, IEntityViewFactory viewFactory, ISimLogger logger = null)
```

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ZWebGLFrameWork.Combat.PlayModeTests")]
```

`ConfigureForTests` rejects null arguments or an active battle, clones the manifest bytes, constructs `BattleInitFactory` and `SessionPresentationBridge`, and stores the optional logger. `CombatManager.Update` uses one 20 Hz accumulator, calls only `_session.Tick()`, applies `TickResult.Presentation`, publishes phase/result from that output, and resolves completion exactly once. `SubmitDeployUnit`, `SubmitMove`, `SubmitSkill`, and `EndDeployPhase` construct commands for `_session.State.NextTick` with a per-player monotonic sequence and return `CommandSubmitResult`. Remove every Legacy world/system/orchestrator field and all `UseSimulationCore` branches.

`BattleInitFactory` maps `BattleParams.DefenderVillage` to defender initial entities, keeps attacker/defender identities distinct, obtains seed/session/match IDs from parameters, and hashes the exact canonical manifest bytes. `BattleState` passes its original `BattleParams` plus state lifetime cancellation token; it no longer constructs `CombatMode`. UI panels consume `SimCombatPhase` and `SimBattleResult`. Remove `_useSimulationCore` and `UseSimulationCore` from `AppConfig`.

Add `"ZWebGLFrameWork.SimulationCore"` to the `references` arrays in both `ZWebGLFrameWork.FSM.asmdef` and `ZWebGLFrameWork.UI.asmdef` in this same step. These direct references must exist before the changed `BattleState`, `DeployPanel`, `BattleHUD`, and `ResultPanel` can compile; later Runtime tasks consume them and do not add them retroactively.

- [ ] **Step 4: Run architecture, PlayMode, and Session suites**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ProductionAssemblies_DoNotBypassSimulationSession -testResults /tmp/zweb-sim-migration/task-15/architecture-green.xml -logFile /tmp/zweb-sim-migration/task-15/architecture-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform PlayMode -testFilter BattleState_UsesSingleSimulationSession_EndToEnd -testResults /tmp/zweb-sim-migration/task-15/play-green.xml -logFile /tmp/zweb-sim-migration/task-15/play-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform PlayMode -testFilter BattleState_ExitBeforeStartCompletes_CancelsLateStart -testResults /tmp/zweb-sim-migration/task-15/battle-cancel-green.xml -logFile /tmp/zweb-sim-migration/task-15/battle-cancel-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests -testResults /tmp/zweb-sim-migration/task-15/sim.xml -logFile /tmp/zweb-sim-migration/task-15/sim.log
```

Expected: zero production bypass references, one complete real Session battle, terminal result, zero views after cleanup, and the concrete BattleState cancellation case passes with no active Session or late result.

- [ ] **Step 5: Commit the production cutover**

```bash
git add Assets/Scripts/Combat Assets/Scripts/FSM/Game/States/BattleState.cs Assets/Scripts/FSM/ZWebGLFrameWork.FSM.asmdef Assets/Scripts/UI/Panels Assets/Scripts/UI/ZWebGLFrameWork.UI.asmdef Assets/Scripts/Data/AppConfig.cs Assets/Tests/PlayMode/Combat Assets/Tests/Editor/SimulationArchitectureTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-15/meta
git commit -m "feat(combat): route production battle through session"
```

---

### Task 16: Repair authority reconciliation evidence and remove benchmarks from correctness gates

**Closes:** TEST-004, TEST-006

**Files:**
- Modify: `Assets/Tests/SimulationCore/SimReplayRoundTripTests.cs`
- Modify: `Assets/Tests/SimulationCore/SimBattleDeterminismTests.cs`
- Modify: `Assets/Tests/SimulationCore/SimWorldTests.cs`
- Create: `Assets/Tests/SimulationCore/AuthorityReconciliationFixture.cs`
- Create: `Assets/Tests/SimulationCore/SimulationBenchmarkFixtureTests.cs`
- Create: `Assets/Editor/SimulationBenchmark.cs`
- Modify: `Assets/Editor/ZWebGLFrameWork.Editor.asmdef`

**Interfaces:**
- Consumes: exact snapshot restore/replay and per-tick checksums from Tasks 6-8.
- Produces: a real final-state reconciliation assertion and a separate 100-entity benchmark fixture with no timing gate.

- [ ] **Step 1: Replace counter-only and always-pass tests**

```csharp
[Test]
public void FrameSyncMismatch_ReplaysToMatchingWorldChecksum()
{
    using AuthorityReconciliationFixture fixture =
        AuthorityReconciliationFixture.RunPredictedToTick(20);
    ulong authorityChecksum = fixture.RunAuthorityBranchFromTick(9, finalTick: 20);

    fixture.RestoreAuthorityAtTick(9);
    ulong reconciledChecksum = fixture.ReplayAuthorityCommandsToTick(20);

    Assert.That(reconciledChecksum, Is.EqualTo(authorityChecksum));
    Assert.That(fixture.Session.State.CompletedTick, Is.EqualTo(20));
    Assert.That(fixture.Session.State.TimelineRevision, Is.EqualTo(1));
}

[Test]
public void BenchmarkFixture_CreatesDeclaredEntityCount()
{
    using ISimBattleSession session = SimulationBenchmarkFixture.CreateSession(entityCount: 100);
    Assert.That(session.State.Presentation.Entities.Count, Is.EqualTo(100));
}
```

- [ ] **Step 2: Run both tests and capture the red result**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-16
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter FrameSyncMismatch_ReplaysToMatchingWorldChecksum -testResults /tmp/zweb-sim-migration/task-16/reconcile-red.xml -logFile /tmp/zweb-sim-migration/task-16/reconcile-red.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter BenchmarkFixture_CreatesDeclaredEntityCount -testResults /tmp/zweb-sim-migration/task-16/fixture-red.xml -logFile /tmp/zweb-sim-migration/task-16/fixture-red.log
```

Expected: the new fixture APIs initially do not compile; after their signatures exist, the behavioral red shows the old test only increments rollback counters or creates 50/malformed entities rather than exactly 100.

- [ ] **Step 3: Implement real test drivers and a manual benchmark command**

```csharp
using System;
using System.Linq;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
public static class SimulationBenchmarkFixture
{
    public static ISimBattleSession CreateSession(int entityCount)
    {
        byte[] manifest = SessionTestDriver.ManifestBytes();
        InitialEntitySpec[] entities = Enumerable.Range(0, entityCount)
            .Select(index => new InitialEntitySpec(
                index % 2 == 0 ? 10 : 2,
                index % 20, index / 20,
                index % 2 == 0 ? SimTeam.Attacker : SimTeam.Defender,
                index % 2))
            .ToArray();
        BattleInit init = SessionTestDriver.BattleInit(42, entities);
        ISimBattleSession session = new SimBattleSessionFactory().Create(manifest);
        StartResult started = session.Start(init);
        if (!started.Accepted) throw new InvalidOperationException(started.Code.ToString());
        return session;
    }
}
}
```

Give the authority fixture this exact surface in `AuthorityReconciliationFixture.cs`:

```text
internal sealed class AuthorityReconciliationFixture : System.IDisposable
{
    public SimBattleSession Session { get; }
    public static AuthorityReconciliationFixture RunPredictedToTick(int finalTick);
    public ulong RunAuthorityBranchFromTick(int completedTick, int finalTick);
    public void RestoreAuthorityAtTick(int completedTick);
    public ulong ReplayAuthorityCommandsToTick(int finalTick);
    public void Dispose();
}
```

Implement those signatures with concrete bodies. The fixture records a snapshot after tick 9 and immutable predicted/authority command arrays for ticks 10-20. The predicted branch uses one tick-12 Move target; the authority branch uses a different accepted tick-12 target. `RunAuthorityBranchFromTick` creates and disposes a separate Session, replays the shared prefix and authority suffix there, and never restores or mutates `Session`. `RestoreAuthorityAtTick` therefore performs the only restore on the predicted Session and increments its revision exactly once. `ReplayAuthorityCommandsToTick` submits the authority suffix, ticks exactly through the requested completed tick, and returns the full Session checksum.

Remove NUnit attributes from both timing benchmarks. Add `ZWebGLFrameWork.SimulationCore` to the Editor asmdef. `Assets/Editor/SimulationBenchmark.cs` adds menu item `Tools/ZWebGL/Benchmarks/Simulation 100x100` and owns a private `CreateBenchmarkSession(int entityCount)` with the same alternating archetypes, unique coordinates, and Session start checks as the test fixture; it does not reference the Editor-only SimulationCore test assembly. It runs 100 ticks with `Stopwatch`, logs entity count, completed ticks, total milliseconds, and checksum, and disposes the Session. Timing is informational and never asserted.

- [ ] **Step 4: Run corrected evidence and the full EditMode assembly**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter FrameSyncMismatch_ReplaysToMatchingWorldChecksum -testResults /tmp/zweb-sim-migration/task-16/reconcile-green.xml -logFile /tmp/zweb-sim-migration/task-16/reconcile-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter BenchmarkFixture_CreatesDeclaredEntityCount -testResults /tmp/zweb-sim-migration/task-16/fixture-green.xml -logFile /tmp/zweb-sim-migration/task-16/fixture-green.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.SimulationCore.Tests -testResults /tmp/zweb-sim-migration/task-16/all.xml -logFile /tmp/zweb-sim-migration/task-16/all.log
```

Expected: reconciled checksum equals authority, fixture count is exactly 100, and no timing-only test appears in the correctness result XML.

- [ ] **Step 5: Commit trustworthy test fixtures**

```bash
git add Assets/Tests/SimulationCore Assets/Editor/SimulationBenchmark.cs Assets/Editor/ZWebGLFrameWork.Editor.asmdef
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-16/meta
git commit -m "test(sim): assert authority state and isolate benchmarks"
```

---

### Task 17: Delete Legacy Combat and prove the post-deletion gates

**Files:**
- Create: `Assets/Tests/Editor/LegacyCombatRemovalTests.cs`
- Modify: `Assets/Tests/Editor/CompilationContractTests.cs`
- Delete: `Assets/Scripts/Combat/AI/`
- Delete: `Assets/Scripts/Combat/Components/`
- Delete: `Assets/Scripts/Combat/Core/`
- Delete: `Assets/Scripts/Combat/Events/`
- Delete: `Assets/Scripts/Combat/Modes/`
- Delete: `Assets/Scripts/Combat/Spawner/`
- Delete: `Assets/Scripts/Combat/Systems/`
- Delete: `Assets/Scripts/Combat/SimInitializerImpl.cs`
- Delete: `Assets/Scripts/Combat/SimulationSyncBridge.cs`
- Delete: `Assets/Scripts/Combat/View/Interpolator.cs`
- Modify: `Assets/Scripts/Combat/ZWebGLFrameWork.Combat.asmdef`
- Modify: `Assets/Scripts/Combat/Tests/ZWebGLFrameWork.Combat.Tests.asmdef`
- Modify: `ZWbeglFrameWork.sln`
- Modify: `scripts/repair/audit-compiler-contracts.sh`
- Modify through the ledger CLI: `docs/superpowers/specs/2026-07-16-project-repair-evidence.json`

**Interfaces:**
- Consumes: all green replacement behavior and production cutover from Tasks 1-16.
- Produces: a repository with no Legacy engine, feature toggle, direct-world bypass, stale serialized script GUID, or Legacy test dependency.

- [ ] **Step 1: Add removal and serialized-reference audits before deleting files**

```csharp
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class LegacyCombatRemovalTests
    {
        [Test]
        public void LegacyCombat_SourceAndToggleAreAbsent()
        {
            string root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string[] forbiddenPaths =
            {
                "Assets/Scripts/Combat/AI", "Assets/Scripts/Combat/Components",
                "Assets/Scripts/Combat/Core", "Assets/Scripts/Combat/Events",
                "Assets/Scripts/Combat/Modes", "Assets/Scripts/Combat/Spawner",
                "Assets/Scripts/Combat/Systems", "Assets/Scripts/Combat/SimInitializerImpl.cs",
                "Assets/Scripts/Combat/SimulationSyncBridge.cs",
                "Assets/Scripts/Combat/View/Interpolator.cs",
                "Assets/Scripts/Combat/Tests/BattleE2ETests.cs"
            };
            Assert.That(forbiddenPaths.Where(path => Directory.Exists(Path.Combine(root, path)) ||
                File.Exists(Path.Combine(root, path))), Is.Empty);
            string appConfig = File.ReadAllText(Path.Combine(root, "Assets/Scripts/Data/AppConfig.cs"));
            Assert.That(appConfig, Does.Not.Contain("UseSimulationCore"));
        }

        [Test]
        public void SerializedAssets_HaveNoMissingGuidReferences()
        {
            string[] assets = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.EndsWith(".prefab") || path.EndsWith(".unity") || path.EndsWith(".asset"))
                .ToArray();
            var missing = new System.Collections.Generic.List<string>();
            foreach (string path in assets)
            {
                string text = File.ReadAllText(path);
                foreach (System.Text.RegularExpressions.Match match in
                    System.Text.RegularExpressions.Regex.Matches(text, "guid: ([0-9a-f]{32})"))
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value)))
                        missing.Add(path + ":" + match.Groups[1].Value);
            }
            Assert.That(missing, Is.Empty, string.Join("\n", missing));
        }
    }
}
```

- [ ] **Step 2: Run the removal audit and verify it fails for Legacy source**

Run:

```bash
mkdir -p /tmp/zweb-sim-migration/task-17
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testFilter ZWebGLFrameWork.Editor.Tests.LegacyCombatRemovalTests -testResults /tmp/zweb-sim-migration/task-17/red.xml -logFile /tmp/zweb-sim-migration/task-17/red.log
```

Expected: `LegacyCombat_SourceAndToggleAreAbsent` fails and lists the tracked Legacy directories.

- [ ] **Step 3: Delete the old engine and remove assembly references**

Run these exact deletions during implementation:

```bash
git rm -r Assets/Scripts/Combat/AI Assets/Scripts/Combat/AI.meta Assets/Scripts/Combat/Components Assets/Scripts/Combat/Components.meta Assets/Scripts/Combat/Core Assets/Scripts/Combat/Core.meta Assets/Scripts/Combat/Events Assets/Scripts/Combat/Events.meta Assets/Scripts/Combat/Modes Assets/Scripts/Combat/Modes.meta Assets/Scripts/Combat/Spawner Assets/Scripts/Combat/Spawner.meta Assets/Scripts/Combat/Systems Assets/Scripts/Combat/Systems.meta
git rm Assets/Scripts/Combat/SimInitializerImpl.cs Assets/Scripts/Combat/SimInitializerImpl.cs.meta Assets/Scripts/Combat/SimulationSyncBridge.cs Assets/Scripts/Combat/SimulationSyncBridge.cs.meta Assets/Scripts/Combat/View/Interpolator.cs Assets/Scripts/Combat/View/Interpolator.cs.meta
```

The Task 12 replacement already removed `Assets/Scripts/Combat/Tests/BattleE2ETests.cs`; do not delete it a second time. Delete only the migration-only `CombatAssembly_CombatBootstrap_Compiles` method from `CompilationContractTests.cs` in this same deletion commit, after its BUILD-004 red/green evidence and baseline closure are recorded. Keep the Baseline plan's phase-aware `audit-compiler-contracts.sh`: while `CombatBootstrap.cs` exists it requires the missing-import fix, and after deletion it instead requires zero `CombatBootstrap` references. Remove `ZWebGLFrameWork.ECS` from Combat and Combat.Tests asmdef references; retain only references used by `CombatManager`, `BattleInitFactory`, presentation, and their tests. Remove obsolete generated project entries from `ZWbeglFrameWork.sln`; Unity will regenerate IDE projects, but the checked-in Server solution entries remain explicit. Consume CI Task 5's existing `ZWEB_EVIDENCE_ROOT` build contract without modifying `BuildScript` again. CI's final `required-tests.json` does not list the removed migration-only test.

- [ ] **Step 4: Run post-deletion source, test, build, and cleanliness gates**

Run:

```bash
set -euo pipefail
mkdir -p /tmp/zweb-sim-migration/task-17/editmode /tmp/zweb-sim-migration/task-17/playmode /tmp/zweb-sim-migration/task-17/dotnet
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform EditMode -testResults /tmp/zweb-sim-migration/task-17/editmode/results.xml -logFile /tmp/zweb-sim-migration/task-17/editmode/unity.log
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -runTests -testPlatform PlayMode -testResults /tmp/zweb-sim-migration/task-17/playmode/results.xml -logFile /tmp/zweb-sim-migration/task-17/playmode/unity.log
dotnet build Server/ReplayValidator/ReplayValidator.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --warnaserror
dotnet build Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --warnaserror
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --artifacts-path /tmp/zweb-sim-migration/dotnet --no-build --logger "trx;LogFileName=task-17-server.trx" --results-directory /tmp/zweb-sim-migration/task-17/dotnet
python3 scripts/ci/validate-unity-results.py --mode EditMode --manifest ci/required-tests.json --results /tmp/zweb-sim-migration/task-17/editmode
python3 scripts/ci/validate-unity-results.py --mode PlayMode --manifest ci/required-tests.json --results /tmp/zweb-sim-migration/task-17/playmode
python3 scripts/ci/validate-trx.py --results /tmp/zweb-sim-migration/task-17/dotnet --minimum 1
test -s /tmp/zweb-sim-migration/task-17/editmode/results.xml
test -s /tmp/zweb-sim-migration/task-17/editmode/unity.log
test -s /tmp/zweb-sim-migration/task-17/playmode/results.xml
test -s /tmp/zweb-sim-migration/task-17/playmode/unity.log
test -s /tmp/zweb-sim-migration/task-17/dotnet/task-17-server.trx
scripts/repair/audit-compiler-contracts.sh "$PWD"
if rg -n "UseSimulationCore|CombatOrchestrator|UnitSpawner|ZWebGLFrameWork\.ECS\.World" Assets/Scripts --glob "*.cs"; then exit 1; elif [ "$?" -ne 1 ]; then exit 1; fi
if rg -n "new SimWorld\(|new SimOrchestrator\(" Assets/Scripts --glob "*.cs" --glob "!Assets/Scripts/SimulationCore/**"; then exit 1; elif [ "$?" -ne 1 ]; then exit 1; fi
ZWEB_EVIDENCE_ROOT=/tmp/zweb-sim-migration/task-17 /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -executeMethod ZWebGLFrameWork.Editor.BuildScript.BuildWebGL -logFile /tmp/zweb-sim-migration/task-17/webgl.log -quit
ZWEB_EVIDENCE_ROOT=/tmp/zweb-sim-migration/task-17 /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix -executeMethod ZWebGLFrameWork.Editor.BuildScript.BuildWindows -logFile /tmp/zweb-sim-migration/task-17/windows.log -quit
git add -A -- Assets/Tests/Editor/LegacyCombatRemovalTests.cs \
  Assets/Tests/Editor/CompilationContractTests.cs Assets/Scripts/Combat \
  Assets/Scripts/Data/AppConfig.cs
git add scripts/repair/audit-compiler-contracts.sh ZWbeglFrameWork.sln
git status --porcelain --untracked-files=all > /tmp/zweb-sim-migration/task-17/git-status.txt
test -z "$(git ls-files --others --exclude-standard)"
```

Expected: structured validators prove nonzero, pass-only EditMode, PlayMode, and .NET results; WebGL and Windows pass under the external evidence root. Both guarded `rg` commands observe exit `1` with no matches, while a real `rg` error still fails the gate. `git-status.txt` contains only the planned staged set and `git ls-files --others` is empty.

- [ ] **Step 5: Commit physical Legacy removal**

```bash
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
/tmp/zweb-sim-migration/audit-unity-meta.sh /tmp/zweb-sim-migration/task-17/meta
git commit -m "refactor(combat): remove legacy battle engine"
```

- [ ] **Step 6: Generate the exact closure manifest, close all owned entries, and verify the count**

Create the generator below outside the repository. Its embedded source has exactly six columns: `issue|acceptance-method|red-file|green-file|exact-fix-commit-subject|mode`. It cross-checks every method against the issue register, every issue against the ownership JSON, every commit subject against exactly one reachable commit, and every named test against a failed red result and passed green result. It emits the required five-column `/tmp/zweb-sim-migration/closure-input.tsv`: `issue|red-file|green-file|fix-commit|Fixed-or-RemovedByReplacement`. Do not hand-edit or append to either manifest.

```bash
cat > /tmp/zweb-sim-migration/generate-simulation-closure.rb <<'RUBY'
require "json"
require "open3"
require "rexml/document"
require "set"

ROOT = File.expand_path(ARGV.fetch(0))
OWNERSHIP = File.join(ROOT, "docs/superpowers/plans/2026-07-17-project-repair-ownership.json")
REGISTER = File.join(ROOT, "docs/superpowers/specs/2026-07-16-project-repair-issue-register.md")
OUTPUT = "/tmp/zweb-sim-migration/closure-input.tsv"

SPEC = <<~'ROWS'
COMBAT-001|BattleState_UsesSingleSimulationSession_EndToEnd|/tmp/zweb-sim-migration/task-15/play-red.xml|/tmp/zweb-sim-migration/task-15/play-green.xml|feat(combat): route production battle through session|RemovedByReplacement
COMBAT-002|BattleInit_UsesRequestedDefenderAndSeed|/tmp/zweb-sim-migration/task-01/red.xml|/tmp/zweb-sim-migration/task-01/green.xml|feat(sim): define single battle session contracts|Fixed
COMBAT-003|BattlePathfinding_DiagonalBesideSingleWall_IsRejected|/tmp/zweb-sim-migration/task-12/path-red.xml|/tmp/zweb-sim-migration/task-12/e2e-green.xml|feat(sim): move targeting and combat pipeline into core|RemovedByReplacement
COMBAT-004|DefenseBuilding_AcquiresAndDamagesAttacker|/tmp/zweb-sim-migration/task-12/defense-red.xml|/tmp/zweb-sim-migration/task-12/e2e-green.xml|feat(sim): move targeting and combat pipeline into core|RemovedByReplacement
COMBAT-005|CampaignAndBuilderBase_StartupDoesNotPrematurelyEnd|/tmp/zweb-sim-migration/task-11/red.xml|/tmp/zweb-sim-migration/task-11/green.xml|feat(sim): own supported modes and deterministic results|RemovedByReplacement
COMBAT-006|DeployCommand_RejectsOutOfPhaseDisallowedOrOverLimit|/tmp/zweb-sim-migration/task-09/deploy-red.xml|/tmp/zweb-sim-migration/task-09/deploy-green.xml|feat(sim): execute validated deploy move and skill commands|RemovedByReplacement
COMBAT-007|CombatResult_UnityAndDotNet_AreIdentical|/tmp/zweb-sim-migration/task-11/red.xml|/tmp/zweb-sim-migration/task-11/green.xml|feat(sim): own supported modes and deterministic results|RemovedByReplacement
COMBAT-008|ProductionAssemblies_DoNotBypassSimulationSession|/tmp/zweb-sim-migration/task-15/architecture-red.xml|/tmp/zweb-sim-migration/task-15/architecture-green.xml|feat(combat): route production battle through session|RemovedByReplacement
SIM-001|SimulationCore_HasNoUnityEngineReferences|/tmp/zweb-sim-migration/task-03/task-03-red.trx|/tmp/zweb-sim-migration/task-03/task-03-architecture.trx|build(sim): share host-neutral core with dotnet|Fixed
SIM-002|ReplayValidator_DotNet8_BuildsAgainstCurrentSimulationCore|/tmp/zweb-sim-migration/task-03/task-03-red.trx|/tmp/zweb-sim-migration/task-03/task-03-architecture.trx|build(sim): share host-neutral core with dotnet|Fixed
SIM-003|UnityAndValidator_SameBattleInitAndCommands_SameTickResults|/tmp/zweb-sim-migration/task-10/red.xml|/tmp/zweb-sim-migration/task-10/green.xml|fix(sim): record executed commands and replay session results|Fixed
SIM-004|ComponentRegistry_PermutedRegistration_PreservesTypeMapping|/tmp/zweb-sim-migration/task-02/red.xml|/tmp/zweb-sim-migration/task-02/green.xml|feat(sim): canonicalize content and component schema|Fixed
SIM-005|RollbackThenReuseId_StaleEntityHandleIsRejected|/tmp/zweb-sim-migration/task-04/red.xml|/tmp/zweb-sim-migration/task-04/green.xml|fix(sim): validate entities and stabilize iteration|Fixed
SIM-006|EquivalentWorlds_DifferentChunkHistories_SameOrderAndChecksum|/tmp/zweb-sim-migration/task-04/red.xml|/tmp/zweb-sim-migration/task-04/green.xml|fix(sim): validate entities and stabilize iteration|Fixed
SIM-007|Checksum_ChangesForPathPhaseAllocatorAndPendingEventDifferences|/tmp/zweb-sim-migration/task-05/hash-red.xml|/tmp/zweb-sim-migration/task-05/hash-green.xml|fix(sim): hash complete state with saturating math|Fixed
SIM-008|Snapshot_PathMutationAndSystemClock_AreIsolatedAndRestored|/tmp/zweb-sim-migration/task-06/red.xml|/tmp/zweb-sim-migration/task-06/green.xml|feat(sim): add atomic identity-bound snapshots|Fixed
SIM-009|Rollback_AfterTickT_ResumesAtTPlusOneWithOriginalChecksumSequence|/tmp/zweb-sim-migration/task-07/after-t-red.xml|/tmp/zweb-sim-migration/task-07/session-green.xml|fix(sim): restore after-t snapshots without replay drift|Fixed
SIM-010|SessionRestart_MatchesFreshSessionState|/tmp/zweb-sim-migration/task-07/restart-red.xml|/tmp/zweb-sim-migration/task-07/session-green.xml|fix(sim): restore after-t snapshots without replay drift|Fixed
SIM-011|LogicInt_ExtremeMultiplyDividePercent_HasDefinedExactResult|/tmp/zweb-sim-migration/task-05/math-defined-red.xml|/tmp/zweb-sim-migration/task-05/math-defined-green.xml|fix(sim): hash complete state with saturating math|Fixed
SIM-012|DeployMoveSkill_ValidCommandsMutateState_InvalidCommandsDoNot|/tmp/zweb-sim-migration/task-09/actions-red.xml|/tmp/zweb-sim-migration/task-09/actions-green.xml|feat(sim): execute validated deploy move and skill commands|Fixed
SIM-013|Commands_SameTickSamePlayer_AreOrderedBySequence|/tmp/zweb-sim-migration/task-08/order-red.xml|/tmp/zweb-sim-migration/task-08/order-green.xml|feat(sim): seal ticks and order command admission|Fixed
SIM-014|Recorder_RecordsExactlyTheCommandsExecutedThisTick|/tmp/zweb-sim-migration/task-10/record-red.xml|/tmp/zweb-sim-migration/task-10/green.xml|fix(sim): record executed commands and replay session results|Fixed
SIM-015|DefenseTargetingAndAI_UnityAndDotNet_ProduceSameChecksums|/tmp/zweb-sim-migration/task-12/determinism-red.xml|/tmp/zweb-sim-migration/task-12/determinism.xml|feat(sim): move targeting and combat pipeline into core|Fixed
SIM-016|SimulationModes_ImplementApprovedStartupWaveAndTerminalMatrix|/tmp/zweb-sim-migration/task-11/red.xml|/tmp/zweb-sim-migration/task-11/green.xml|feat(sim): own supported modes and deterministic results|Fixed
SIM-017|PresentationBridge_CreatesInitialViewsUsingCorrectArchetype|/tmp/zweb-sim-migration/task-13/bridge-red.xml|/tmp/zweb-sim-migration/task-13/bridge-green.xml|feat(combat): render immutable session presentation frames|Fixed
SIM-018|Presentation_RendersBetweenTicks_AndResyncsAfterRollbackWithoutLeaks|/tmp/zweb-sim-migration/task-14/red.xml|/tmp/zweb-sim-migration/task-14/green.xml|fix(combat): reset interpolation and clean views on rollback|Fixed
SIM-019|PresentationFrames_ProvideInitialDeltaAndRollbackRevisionWithoutWorldAccess|/tmp/zweb-sim-migration/task-13/frame-red.xml|/tmp/zweb-sim-migration/task-13/frame-green.xml|feat(combat): render immutable session presentation frames|Fixed
SIM-020|CommandSubmit_LateFutureDuplicate_HasDeterministicDisposition|/tmp/zweb-sim-migration/task-08/admission-red.xml|/tmp/zweb-sim-migration/task-08/admission-green.xml|feat(sim): seal ticks and order command admission|Fixed
SIM-021|SnapshotRestore_ForeignSchemaCorruptOrStale_IsRejectedAtomically|/tmp/zweb-sim-migration/task-06/red.xml|/tmp/zweb-sim-migration/task-06/green.xml|feat(sim): add atomic identity-bound snapshots|Fixed
SIM-022|ContentManifest_UnityAndDotNet_ProduceSameCanonicalHash|/tmp/zweb-sim-migration/task-02/red.xml|/tmp/zweb-sim-migration/task-02/green.xml|feat(sim): canonicalize content and component schema|Fixed
TEST-001|DifferentSeed_ChangesSeededInitialStateChecksum|/tmp/zweb-sim-migration/task-05/hash-red.xml|/tmp/zweb-sim-migration/task-05/hash-green.xml|fix(sim): hash complete state with saturating math|Fixed
TEST-002|ReplayExportParseValidate_RoundTripsExecutedCommands|/tmp/zweb-sim-migration/task-10/replay-red.xml|/tmp/zweb-sim-migration/task-10/green.xml|fix(sim): record executed commands and replay session results|Fixed
TEST-003|RollbackReplay_MatchesOriginalChecksumSequence|/tmp/zweb-sim-migration/task-07/rollback-red.xml|/tmp/zweb-sim-migration/task-07/rollback-green.xml|fix(sim): restore after-t snapshots without replay drift|Fixed
TEST-004|FrameSyncMismatch_ReplaysToMatchingWorldChecksum|/tmp/zweb-sim-migration/task-16/reconcile-red.xml|/tmp/zweb-sim-migration/task-16/reconcile-green.xml|test(sim): assert authority state and isolate benchmarks|Fixed
TEST-005|BattleSession_AttackDamageDeathResult_EndToEnd|/tmp/zweb-sim-migration/task-12/e2e-red.xml|/tmp/zweb-sim-migration/task-12/e2e-green.xml|feat(sim): move targeting and combat pipeline into core|Fixed
TEST-006|BenchmarkFixture_CreatesDeclaredEntityCount|/tmp/zweb-sim-migration/task-16/fixture-red.xml|/tmp/zweb-sim-migration/task-16/fixture-green.xml|test(sim): assert authority state and isolate benchmarks|Fixed
TEST-007|LogicInt_Extremes_UsePublicOperatorsAndGoldenVectors|/tmp/zweb-sim-migration/task-05/math-red.xml|/tmp/zweb-sim-migration/task-05/math-green.xml|fix(sim): hash complete state with saturating math|Fixed
TEST-008|SupportedModes_MatchApprovedBehaviorMatrix|/tmp/zweb-sim-migration/task-11/red.xml|/tmp/zweb-sim-migration/task-11/green.xml|feat(sim): own supported modes and deterministic results|Fixed
ROWS

def abort_with(message)
  warn message
  exit 1
end

rows = SPEC.lines(chomp: true).reject(&:empty?).map do |line|
  fields = line.split("|", -1)
  abort_with("closure row must have six non-empty columns: #{line}") unless fields.length == 6 && fields.none?(&:empty?)
  fields
end
abort_with("closure specification must have exactly 38 rows") unless rows.length == 38

issues = rows.map(&:first)
abort_with("duplicate closure issue") unless issues.uniq.length == issues.length
owned = JSON.parse(File.read(OWNERSHIP)).fetch("plans").fetch("simulationcore-migration").fetch("issues")
abort_with("closure issues do not exactly match simulation ownership") unless issues.to_set == owned.to_set && issues.length == owned.length

registered = {}
File.foreach(REGISTER) do |line|
  next unless line =~ /^\| ([A-Z]+-\d{3}) \|/
  issue = Regexp.last_match(1)
  cells = line.split("|").map(&:strip)
  methods = cells.fetch(5).scan(/`([^`]+)`/).flatten
  registered[issue] = methods
end

git_log, git_error, git_status = Open3.capture3("git", "-C", ROOT, "log", "--all", "--format=%H%x09%s")
abort_with("git log failed: #{git_error}") unless git_status.success?
commits_by_subject = Hash.new { |hash, key| hash[key] = [] }
git_log.each_line do |line|
  sha, subject = line.chomp.split("\t", 2)
  commits_by_subject[subject] << sha
end

def result_statuses(path, method)
  document = REXML::Document.new(File.read(path))
  statuses = []
  REXML::XPath.each(document, "//*") do |element|
    next unless ["test-case", "UnitTestResult"].include?(element.name)
    name = element.attributes["fullname"] || element.attributes["testName"] || element.attributes["name"]
    next unless name
    exact = name == method || name.end_with?(".#{method}") ||
      name.include?(".#{method}(") || name.start_with?("#{method}(")
    next unless exact
    statuses << (element.attributes["result"] || element.attributes["outcome"])
  end
  statuses.compact
rescue REXML::ParseException => error
  abort_with("invalid XML #{path}: #{error.message}")
end

output = rows.map do |issue, acceptance, red, green, subject, mode|
  abort_with("invalid closure mode for #{issue}") unless ["Fixed", "RemovedByReplacement"].include?(mode)
  methods = registered.fetch(issue) { abort_with("#{issue} missing from issue register") }
  abort_with("#{issue} acceptance mismatch: #{methods.inspect}") unless methods == [acceptance]
  abort_with("missing red evidence #{red}") unless File.file?(red) && File.size?(red)
  abort_with("missing green evidence #{green}") unless File.file?(green) && File.size?(green)
  abort_with("#{issue} red result does not contain failed #{acceptance}") unless result_statuses(red, acceptance).include?("Failed")
  abort_with("#{issue} green result does not contain passed #{acceptance}") unless result_statuses(green, acceptance).include?("Passed")
  commits = commits_by_subject.fetch(subject, [])
  abort_with("#{issue} expected one commit named #{subject.inspect}, found #{commits.length}") unless commits.length == 1
  [issue, red, green, commits.first, mode].join("|")
end

File.write(OUTPUT, output.join("\n") + "\n")
puts "generated=#{output.length} output=#{OUTPUT}"
RUBY

ruby /tmp/zweb-sim-migration/generate-simulation-closure.rb "$PWD"
```

Expected: the generator prints `generated=38`, and any missing/duplicate/extra issue, altered acceptance name, empty column, invalid mode, unreadable or malformed result, wrong result status, or ambiguous commit subject fails before the ledger changes.

Then run:

```bash
test -s /tmp/zweb-sim-migration/closure-input.tsv
task17_commit="$(git rev-parse HEAD)"
replacement_receipt=/tmp/zweb-sim-migration/task-17/editmode/results.xml
test -s "$replacement_receipt"
closure_date="$(date -u +%F)"
while IFS='|' read -r issue red green fix mode; do
  test -s "$red" && test -s "$green" && git cat-file -e "$fix^{commit}"
  if [ "$mode" = "RemovedByReplacement" ]; then
    node scripts/audit/evidence-ledger.mjs close --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json --issue "$issue" --red "$red" --fix "$fix" --fix "$task17_commit" --green "$green" --gate /tmp/zweb-sim-migration/task-17/editmode/results.xml --gate /tmp/zweb-sim-migration/task-17/playmode/results.xml --gate /tmp/zweb-sim-migration/task-17/dotnet/task-17-server.trx --replacement "$replacement_receipt" --mode "$mode" --date "$closure_date"
  else
    node scripts/audit/evidence-ledger.mjs close --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json --issue "$issue" --red "$red" --fix "$fix" --green "$green" --gate /tmp/zweb-sim-migration/task-17/editmode/results.xml --gate /tmp/zweb-sim-migration/task-17/playmode/results.xml --gate /tmp/zweb-sim-migration/task-17/dotnet/task-17-server.trx --mode "$mode" --date "$closure_date"
  fi
done < /tmp/zweb-sim-migration/closure-input.tsv
awk '/^\*\*Closes:\*\*/ { line=$0; sub(/^\*\*Closes:\*\* /, "", line); count += split(line, ids, /, /) } END { print count }' docs/superpowers/plans/2026-07-17-simulationcore-migration.md | tee /tmp/zweb-sim-migration/owned-count.txt
test "$(cat /tmp/zweb-sim-migration/owned-count.txt)" -eq 38
test "$(wc -l < /tmp/zweb-sim-migration/closure-input.tsv | tr -d ' ')" -eq 38
node scripts/audit/evidence-ledger.mjs validate --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json
git add docs/superpowers/specs/2026-07-16-project-repair-evidence.json docs/superpowers/evidence/artifacts
git commit -m "test(sim): close simulation migration evidence"
```

Expected: the plan-derived count and generated manifest count are both `38`; every close command succeeds against the generator-validated red/green/gate evidence, ledger validation passes, and the ledger-only commit succeeds.

---

## Self-Review

- [ ] Scope: every changed production/test file traces to one task; unrelated runtime, network, replay-hardening, data persistence, and CI ownership remains untouched.
- [ ] Issue ownership: the plan-derived count is `38`, every owned identifier occurs in exactly one `Closes` header, and no unowned identifier is present.
- [ ] Acceptance names: each issue-register regression symbol is an actual test method in the named Unity or .NET test assembly, not prose or a comment.
- [ ] Type consistency: `ProtocolVersion` is `int`, command `Sequence` is `uint`, Session start/restore use `StartResult`/`SnapshotRestoreResult`, and every later helper has one earlier definition with matching arguments and return type.
- [ ] Evidence: every Unity command writes XML/logs under the external root, every .NET test writes TRX plus artifacts there, and structured validators reject zero/skipped/inconclusive/failed tests.
- [ ] Determinism: timeline revision is excluded from world checksum, restore increments it exactly once, and original/replayed checksum sequences compare equal from `NextTick`.
- [ ] Removal: source, asmdef, serialized GUID, build, and clean-worktree gates execute after physical deletion and before ledger closure.
- [ ] Simplicity: no second world authority, replay loop, JSON codec inside Core, production test fixture, feature toggle, or compatibility path survives final deletion.

---

## Final Verification Checklist

- [ ] Run every focused command above from a clean worktree and retain XML/log artifacts under `/tmp/zweb-sim-migration/`.
- [ ] Run the complete Unity EditMode and PlayMode suites with zero failed, skipped, or inconclusive tests.
- [ ] Run `.NET 8` Release build with warnings as errors and the complete server test project.
- [ ] Compare Unity and .NET manifest bytes, command bytes, per-tick checksums, terminal result, and snapshot restore sequence for the same fixture.
- [ ] Confirm the four approved modes cover deployment, waves, zero-attacker state, timeout, draw, result, and cleanup.
- [ ] Confirm production source has no direct `SimWorld`/`SimOrchestrator` access and no Legacy source/toggle/reference.
- [ ] Confirm every assigned audit identifier appears once in this document and maps to one task.
- [ ] Confirm the prohibited-plan-pattern scan returns no matches.
- [ ] Confirm every type used by a later task is defined by an earlier task with the same spelling and signature.

## Execution Handoff

Plan execution should use `superpowers:subagent-driven-development`, one fresh implementation agent per task, with a specification review and code-quality review before advancing. Use `superpowers:executing-plans` only when execution must remain in one session with explicit checkpoints after Tasks 3, 7, 12, 15, and 17.
