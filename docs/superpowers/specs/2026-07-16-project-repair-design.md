# ZWebGLFrameWork Project Repair Design

**Status:** Architecture option A approved on 2026-07-16.

**Goal:** Repair every issue confirmed by the project-wide audit, restore a reproducible Unity project, and converge combat on SimulationCore as the only production battle engine.

## Success Definition

The repair is complete only when all of the following are true:

1. The repository imports with the pinned editor and contains no C# compiler or assembly-definition errors.
2. Every confirmed audit issue is either fixed with a regression test or removed together with the unsupported code path.
3. Launcher can reach an interactive login or offline village, persist a complete save, enter battle, pause/resume, finish battle, apply rewards, and return to the village without exceptions.
4. SimulationCore is the only production battle state authority. Unity views, local play, replay validation, and frame synchronization all consume the same battle-session API.
5. Identical initial state and ordered commands produce identical per-tick checksums in Unity and .NET 8; rollback and replay reproduce the original checksum sequence.
6. WebGL and Windows builds succeed. WebGL uses browser-compatible HTTPS/WSS transports; native socket transports never enter a WebGL assembly.
7. ReplayValidator and the authoritative frame-sync server build and test on .NET 8.
8. A clean checkout remains Git-clean after Unity import, tests, and builds, excluding declared build artifacts.

This definition is evidence-based. It does not claim that arbitrary future code can contain no bugs; it requires direct proof for all identified defects and all supported end-to-end workflows.

## Supported Targets

- Unity editor: `2022.3.62f3c1`, as pinned by `ProjectSettings/ProjectVersion.txt`. Unity 6 migration is outside this repair.
- Clients: WebGL and Windows Standalone.
- Server: .NET 8 on Linux and Windows.
- Android and iOS remain roadmap targets and are not release gates in this repair.
- Offline mode remains supported. Online WebGL uses HTTPS and WSS; online Windows uses HTTPS plus the native realtime adapter.

## Target Architecture

### Application Flow

`Launcher -> GameLauncher -> AppConfig -> GameFSM -> Launch -> Login -> MainVillage -> Battle/Pause`

FSM owns application state transitions. State entry receives a typed context that survives pause/resume. Async state work is tied to a cancellation token that is cancelled on exit. A transition from no state to the configured initial state bypasses normal transition guards exactly once.

### Data and UI

`DataManager` owns runtime player and village models. `SaveManager` converts those models to versioned DTOs and performs first-write-safe atomic persistence. DTOs preserve building identity, level, state, position, and upgrade timing.

`UIManager` owns one EventSystem, one root Canvas, layer stacks, and one cached instance per panel type. A panel builds its static hierarchy once, binds current input on every open, and removes subscriptions on close. Scene-bound input and camera services rebind on `sceneLoaded`.

### Single Battle Authority

SimulationCore exposes one host-neutral session boundary:

```csharp
public interface ISimBattleSession : IDisposable
{
    SimBattleState State { get; }
    void Start(BattleInit init);
    CommandSubmitResult Submit(ISimCommand command);
    TickResult Tick();
    SimSnapshot CaptureSnapshot();
    void RestoreSnapshot(SimSnapshot snapshot);
}
```

`BattleInit` is fully serializable and contains protocol/schema version, seed, tick rate, combat mode, deterministic config hash, initial entities, and player identities. Unknown versions, archetypes, modes, players, commands, or coordinates are rejected explicitly.

`TickResult` contains the completed tick number, commands actually executed in stable total order, phase/result events, entity lifecycle changes, and the checksum after that tick. Recorder, Unity presentation, ReplayValidator, and FrameSync consume this result instead of reading or mutating `SimWorld` directly.

Legacy Combat remains available only while parity tests are being built. It is deleted when the migration gates below pass; the Feature Toggle is then removed rather than becoming a permanent dual-engine switch.

### Deterministic State Contract

- Snapshot semantics are "state after tick T"; restoring it sets the next executable tick to `T + 1`.
- Snapshot and checksum cover world components, entity allocator/generations, current tick, phase, phase timers, winner/result, deterministic RNG, system clocks, pending deterministic events, and command-order state.
- Component values containing collections are deep-copied or replaced with deterministic value containers.
- Entity iteration and command ordering use explicit stable keys, independent of chunk layout, arrival order, or swap-remove history.
- SimulationCore contains no Unity references and targets APIs shared by Unity's configured compatibility level and .NET 8.

### Command, Replay, and Validation

Deploy, Move, and Skill commands have tick, player ID, monotonic sequence, validated payload, and real simulation behavior. Empty ticks are explicit and still advance simulation.

Replay files contain format version, simulation schema hash, content/config hash, tick rate, match/initial-state identity, continuous tick records, final tick, final result, and final checksum. The server resolves the authoritative initial state by match ID; it never trusts a client-provided seed or layout as authority.

ReplayValidator rejects empty, truncated, duplicated, unordered, unknown-version, over-limit, or trailing records. It validates every tick including the terminal tick and binds the replay result to the expected match result.

### Realtime Networking

Shared, Unity-free contracts define versioned command envelopes and authority frames. Transport and framing are adapters:

- `IHttpApi`: HTTPS login, configuration, update metadata, and replay upload.
- `IRealtimeChannel`: connect, send complete message, receive complete message, close, and state.
- WebGL adapter: WSS, with browser message boundaries and no raw sockets or threads.
- Native adapter: authenticated realtime channel backed by a pinned, Unity-free `kcp2k` revision; the current custom KCP core is removed after protocol-vector and impairment tests pass.
- Server ingress: WSS and native sessions decode to the same authority frame and feed the same match actor and SimulationCore session.

Authority frames contain tick, ordered inputs for both players, accepted sequence acknowledgements, checksum after tick, and optional resync snapshot. `BattleInit.InputDeadlineTicks` is agreed by all peers and defaults to six ticks (300 ms at 20 Hz); the server inserts an explicit NoOp when that deadline expires. A client outside the 64-tick rollback window requests a full resync rather than silently accepting authority.

## Repair Workstreams

### 1. Repository and Compilation Baseline

- Restore all missing and orphaned `.meta` files with the pinned editor and commit stable GUIDs.
- Register `Assets/Scence/Launcher.unity` as enabled build scene zero.
- Repair Editor, Gameplay, Combat, Network, and SimulationCore compiler errors.
- Remove Unity and unsupported framework calls from SimulationCore.
- Align Unity-generated project files, package manifest, CI, and server projects with explicit dependencies.
- Ensure required AppConfig/config assets and unit/view prefabs are present or fail validation before play/build.

### 2. Boot, Persistence, Village, and UI

- Correct initial FSM transition and typed pause/resume context.
- Make scene-load failure and timeout observable failures; never publish completion for a failed load.
- Create the EventSystem and assemble the interactive village runtime.
- Implement first-write-safe atomic save/local-store writes and complete building DTO round trips.
- Save all relevant state on change/interval/exit without advancing the saved baseline after failure.
- Make multi-cell occupancy authoritative and update resource production on village changes.
- Correct Dark Elixir classification and persist battle rewards.
- Make panel build/open/close contracts idempotent and repair hidden Unity lifecycle methods.
- Cancel Launch/hot-update work on state exit and honor failed downloads and restart requirements.
- Rebind scene-dependent cameras/controllers after scene changes.

### 3. SimulationCore Migration

- Add the single session boundary and deterministic `BattleInit`/`TickResult` contracts.
- Implement real command validation and Deploy/Move/Skill behavior.
- Move AI, deployment, waves, targeting, victory, result, and cleanup into SimulationCore.
- Correct defense targeting, no-attacker mode startup, interpolation, and view lifecycle.
- Correct generation checks, deterministic ordering, complete checksum, deep snapshots, rollback, and replay.
- Route all CombatManager public operations through the session.
- Build behavior-parity tests, switch production to SimulationCore, then remove Legacy ECS combat and the toggle.

### 4. Network and Server

- Separate HTTP, realtime contracts, WebGL transport, and native transport assemblies.
- Replace callback subscription leaks and signature mismatches with stored handlers and deterministic cleanup.
- Replace the custom KCP core with the pinned `kcp2k` source and test fragmentation, acknowledgement, loss, duplication, and reordering.
- Implement full-send loops for stream transports and correct dual-stack winner/loser ownership.
- Implement local input sending, fixed tick advancement including NoOp, prediction, rollback replay, and resync.
- Implement the .NET 8 authoritative frame-sync server and harden ReplayValidator.
- Add size, rate, protocol-version, source/session, and authentication checks at ingress boundaries.

### 5. Tests, CI, and Release Gates

- Convert each confirmed issue into a failing regression test before its production fix.
- Repair false or contradictory tests; benchmarks do not count as correctness tests.
- Add a non-Editor PlayMode test assembly and reject zero-test runs.
- Pass the exact Unity version to all GameCI actions and include tests, scenes, Resources, Packages, ProjectSettings, Server, and workflow changes in CI triggers.
- Add .NET 8 build/test jobs and WebGL/Windows build jobs that call the repository BuildScript explicitly.

## Verification Gates

### G0: Repository Integrity

- No missing or orphaned `.meta` files.
- Launcher is enabled scene zero.
- Required Resources and prefabs resolve.
- Clean checkout remains clean after import.

### G1: Compilation

- Pinned Unity editor exits zero for EditMode import/test.
- Logs contain no compiler, assembly, serialization, missing-script, or package-resolution errors.
- `dotnet build --configuration Release --warnaserror` succeeds for server projects.

### G2: Unit and Integration Tests

- EditMode tests have zero failed, skipped, or inconclusive cases and a nonzero expected count per assembly.
- PlayMode tests open Launcher and exercise boot, UI interaction, save/load, scene changes, battle, pause/resume, result application, and cleanup.
- All former audit defects have named regression tests.

### G3: Determinism, Replay, and Rollback

- At least 100 seeds run for 3,600 ticks with equal per-tick checksums across repeated runs.
- Unity and .NET 8 produce identical golden checksums and protocol byte vectors.
- Restore/replay reproduces the original checksum sequence for tick zero, normal ticks, ring-buffer wrap, terminal tick, late commands, and out-of-window resync.
- Replay CLI accepts valid files and rejects command/layout/seed/config/version/checksum tampering, truncation, duplication, reordering, trailing records, and size/time-limit violations.

### G4: Network

- WebGL WSS reconnect and native KCP loopback pass under loss, duplication, reordering, and 50-300 ms latency.
- Empty input advances exactly one tick through explicit NoOp.
- Both clients and server finish with equal checksum and result.

### G5: Builds and Smoke Tests

- WebGL and Windows BuildReports succeed with Launcher as scene zero.
- WebGL output contains HTML, WASM, and data artifacts and passes an HTTPS browser smoke test with no console exceptions or mixed content.
- Windows output starts on a Windows runner and reaches the configured initial state without error logs.
- Build/test execution leaves tracked files unchanged.

## Legacy Removal Gates

Legacy Combat may be deleted only after:

1. SimulationCore LocalMode completes deployment, combat, result, and cleanup in PlayMode.
2. All supported combat modes have explicit SimulationCore tests and defined zero-attacker startup behavior.
3. `CombatEndEvent.LootGold` and `LootElixir` are applied to the player exactly once after a completed result, clamped to resource caps, saved before returning to the village, and never applied for a rejected or replay-invalid result.
4. Presentation creates initial views, interpolates across logical ticks, handles rollback, and leaks no views/subscriptions.
5. ReplayValidator uses the same session implementation and validates terminal state.
6. No production caller reads or mutates Legacy `World`, `CombatOrchestrator`, or Legacy systems.

## Change Boundaries

The repair may modify runtime scripts, tests, server projects, CI, assembly definitions, project settings, scenes, required Resources, prefabs, and metadata that directly support the gates above. It does not migrate to Unity 6, add unrelated gameplay, redesign visual art, or claim Android/iOS release support.

## Delivery Strategy

Each workstream is implemented as small red-green-refactor tasks on an isolated branch. Every task ends with its targeted tests and the broadest currently available gate. Cross-cutting deletion occurs only after consumers have migrated and parity tests pass. No bulk "audit fix" commit may combine unrelated root causes without independent regression evidence.
