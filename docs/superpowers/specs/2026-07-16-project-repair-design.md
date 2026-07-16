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

`DataManager` owns runtime player and village models. `SaveManager` converts those models to versioned DTOs and performs first-write-safe atomic persistence. A load first parses and validates a detached model, then commits the whole model to `DataManager`; invalid versions, buildings, ranges, or timestamps leave runtime state unchanged. DTOs preserve village name/capacity plus building identity, level, state, position, and upgrade timing.

`ResourceManager` scopes cache entries to the active provider and invalidates the old provider cache before a switch. AssetBundle loading resolves manifest dependencies before the requested bundle and keeps bundle identity separate from asset path so unload operations release the correct bundle and dependency references.

`UIManager` owns one EventSystem, one root Canvas, layer stacks, and one cached instance per panel type. A panel builds its static hierarchy once, binds current input on every open, and removes subscriptions on close. Scene-bound input and camera services rebind on `sceneLoaded`.

### Single Battle Authority

SimulationCore exposes one host-neutral session boundary:

```csharp
public interface ISimBattleSession : IDisposable
{
    SimBattleState State { get; }
    StartResult Start(BattleInit init);
    CommandSubmitResult Submit(ISimCommand command);
    TickResult Tick();
    SimSnapshot CaptureSnapshot();
    SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot);
}
```

`BattleInit` is fully serializable and contains protocol/schema version, seed, tick rate, combat mode, deterministic config hash, initial entities, and player identities. Unknown versions, archetypes, modes, players, commands, or coordinates are rejected explicitly.

`TickResult` contains the completed tick number, commands actually executed in stable total order, phase/result events, entity lifecycle changes, an immutable `PresentationFrame`, timeline revision, and the checksum after that tick. Recorder, Unity presentation, ReplayValidator, and FrameSync consume this result instead of reading or mutating `SimWorld` directly.

The session is single-use: `Created -> Running -> Ended -> Disposed`. `Start` succeeds once; invalid init returns a typed rejection without changing state. `Tick` outside Running and `Submit` outside an accepted window return typed failures without mutation. Restore accepts only a compatible snapshot for the same match/session and moves Running or Ended to the restored Running state while incrementing `TimelineRevision`. Dispose is idempotent. `State` is an immutable summary, not a mutable world handle.

`PresentationFrame` contains a full initial frame after Start (`CompletedTick=-1`) and per-tick immutable entity state/lifecycle deltas thereafter. Each entity state carries stable entity/generation, gameplay archetype, position, and orientation. A rollback or authority resync increments `TimelineRevision` and emits a full frame so the Unity bridge can discard stale interpolation history without reading `SimWorld`.

Legacy Combat remains available only while parity tests are being built. It is deleted when the migration gates below pass; the Feature Toggle is then removed rather than becoming a permanent dual-engine switch.

Approved mode behavior is the migration oracle:

| Mode | Manual deployment | Wave source | Zero-attacker rule | Timeout/result |
|---|---|---|---|---|
| `village_attack` | Attacker deploys validated units | None | Valid during deployment; defender wins only after deployment seals with no attackers | Defender wins on timeout; deterministic destruction/stars/loot |
| `builder_base` | Attacker deploys validated units | None | Same as village attack | Defender wins on timeout; deterministic destruction/stars/loot |
| `clan_war` | Attacker deploys validated units | None | Same as village attack | Defender wins on timeout; deterministic war stars/score/loot |
| `campaign` | None | Deterministic scheduled attacker waves | Valid while future waves remain; defender wins only after all waves emitted and attackers are dead | Result derives from waves cleared, surviving defenders, timeout, and deterministic rewards |

Mutual elimination is a draw unless a mode row explicitly overrides it. These rows intentionally correct known Legacy defects and therefore replace blind state-by-state parity.

### Deterministic State Contract

- Initial state is represented as `CompletedTick=-1, NextTick=0`. Every normal snapshot is "state after tick T" and stores both values explicitly; restoring it sets `NextTick=T+1` without guessing from array position.
- Snapshot and checksum cover world components, entity allocator/generations, current tick, phase, phase timers, winner/result, deterministic RNG, system clocks, pending deterministic events, and command-order state.
- Component values containing collections are deep-copied or replaced with deterministic value containers.
- Entity iteration and command ordering use explicit stable keys, independent of chunk layout, arrival order, or swap-remove history.
- SimulationCore contains no Unity references and targets APIs shared by Unity's configured compatibility level and .NET 8.
- `DeterministicContentManifest` is versioned canonical binary data: fixed little-endian integers, UTF-8 strings with unsigned length prefixes, no floating-point values, and collections sorted by stable ID. Its SHA-256 is the config/content hash in BattleInit, Replay, snapshots, and authority messages. Unity and .NET consume the same manifest bytes.

### Command, Replay, and Validation

Deploy, Move, and Skill commands have tick, player ID, monotonic sequence, validated payload, and real simulation behavior. Accepted order is `(tick, playerId, sequence)`. Commands for completed/sealed ticks, duplicate or non-monotonic sequences, and commands more than 64 ticks ahead are rejected with stable codes. Client NoOp is not accepted; only the authoritative scheduler can seal a missing input as NoOp. Empty authoritative ticks are explicit and still advance simulation.

Replay files contain format version, simulation schema hash, content/config hash, tick rate, match/initial-state identity, submission nonce, continuous tick records, final tick, canonical `SimBattleResult`, and final checksum. `SimBattleResult` contains MatchId, ResultId, winner, rewards, final tick, and final checksum. The server resolves `AuthoritativeMatchRecord` by match ID and ownership; it never trusts a client-provided seed, layout, result, or content hash as authority. Validation API is `Validate(ReplayFile replay, AuthoritativeMatchRecord match)`.

ReplayValidator rejects empty, truncated, duplicated, unordered, unknown-version, over-limit, or trailing records. It validates every tick including the terminal tick and binds the replay result and submission nonce to the authoritative match. Limits are 16 MiB input, 12,000 ticks, 64 commands per tick, 100,000 total commands, 4,096 initial entities, and 8 MiB decompressed snapshot. Deterministic count/byte limits are correctness gates; wall-clock timeout is only a safety cancellation.

Replay CLI accepts a replay plus an authoritative fixture/provider, emits a machine-readable JSON result and uses stable exits: `0` valid, `1` I/O/internal error, `2` invalid replay, `3` unsupported version/schema, `4` authoritative match missing/mismatch, and `5` resource limit exceeded.

### Realtime Networking

Shared, Unity-free contracts define versioned command envelopes and authority frames. Transport and framing are adapters:

- `IHttpApi`: HTTPS login, configuration, update metadata, and replay upload.
- `IRealtimeChannel`: connect, send complete message, receive complete message, close, and state.
- WebGL adapter: a dependency-locked `NativeWebSocket` revision requires an absolute `wss` RealtimeEndpoint whose path is `/v1/realtime`, using subprotocol `zweb.framesync.v1`; browser message boundaries are preserved and raw sockets/threads are absent.
- Native adapter: an immutable-source, Unity-free `kcp2k` revision carries authenticated realtime envelopes; the current custom KCP core is deleted after replacement vectors and impairment tests pass.
- Dependency provenance: before import, the implementation plan records upstream URL, immutable Git commit, SHA-256 of vendored/package content, and license for NativeWebSocket and kcp2k in a checked-in dependency manifest and lock file. Mutable branch/tag references are forbidden.
- Server WSS ingress uses ASP.NET Core 8/Kestrel TLS. WSS and native sessions decode to the same authority frame and feed the same match actor and SimulationCore session.

All realtime transports use the same authentication state machine. HTTPS issues a short-lived session ID, resume token, expiry, and 256-bit session key. WSS sends an `AuthHello` as its first message because browser APIs cannot reliably set arbitrary authorization headers. Native datagrams carry session ID, monotonic packet sequence, and HMAC-SHA256; a 1,024-packet replay window rejects duplicates, and endpoint rebinding is accepted only after a valid MAC. KCP conv is routing metadata, never identity. Origin allowlist, token expiry, rate limits, and a 64 KiB maximum realtime frame are enforced. Protocol close codes are stable: 4001 auth, 4002 version, 4003 malformed, 4004 overload, 4005 expired session.

Each match actor single-threadedly owns one Session and a monotonic 20 Hz scheduler. `InputDeadlineTicks=6` means six 50 ms scheduler epochs from a tick's planned time, not six simulated ticks. At the deadline the actor seals that tick and inserts NoOp for a missing player; late real input is acknowledged as `LateRejected` and cannot rewrite a sealed tick. Reconnect `ResumeHello` carries resume token, last accepted command sequence, last authority tick, and timeline revision. The server retains a disconnected session for ten seconds.

Authority frames contain sealed tick, ordered inputs for both players, per-command disposition, accepted sequence acknowledgements, checksum after tick, and optional resync snapshot. Resync snapshots contain match/session ID, schema/content hash, completed/next tick, timeline revision, uncompressed length, payload checksum, and a maximum decompressed size of 8 MiB; application is atomic and the next execution is `T+1`. A client outside the 64-tick rollback window requests full resync rather than silently accepting authority.

## Repair Workstreams

### 1. Repository and Compilation Baseline

- Restore all missing and orphaned `.meta` files with the pinned editor and commit stable GUIDs.
- Register `Assets/Scence/Launcher.unity` as enabled build scene zero.
- Repair Editor, Gameplay, Combat, Network, and SimulationCore compiler errors.
- Remove Unity and unsupported framework calls from SimulationCore.
- Align Unity-generated project files, package manifest, CI, and server projects with explicit dependencies.
- Add a checked-in required-asset manifest generated deterministically from supported scenes, modes, archetypes, config rows, and UI panels. Missing, duplicate, or null references fail import/build.
- Ensure required AppConfig/config assets and unit/view prefabs are present or fail validation before play/build.
- Repair ResourceManager provider-scoped caching and AssetBundle dependency/load/unload identity.

### 2. Boot, Persistence, Village, and UI

- Correct initial FSM transition with explicit `HasCurrentState`/nullable previous state, typed pause/resume context, and queued reentrant transitions whose events are emitted in committed order.
- Make scene-load failure and timeout observable failures; never publish completion for a failed load.
- Create the EventSystem and assemble the interactive village runtime.
- Implement first-write-safe atomic save/local-store writes, observable persistence failures, complete building DTO round trips, schema/range validation, and detached transactional load.
- Save all relevant state on change/interval/exit without advancing the saved baseline after failure.
- Make multi-cell occupancy authoritative and update resource production on building add/remove/upgrade; use widened saturating arithmetic at resource caps.
- Correct Dark Elixir classification and persist battle rewards.
- Make panel build/open/close contracts idempotent and repair hidden Unity lifecycle methods.
- Give every async FSM state a lifetime token; cancel Launch/hot-update, Loading scene work, and Battle startup on exit, and honor failed downloads and restart requirements.
- Rebind scene-dependent cameras/controllers after scene changes.

### 3. SimulationCore Migration

- Add the single session boundary and deterministic `BattleInit`/`TickResult` contracts.
- Add immutable PresentationFrame, canonical SimBattleResult, deterministic content manifest, typed lifecycle errors, command disposition, and resync snapshot contracts.
- Implement real command validation and Deploy/Move/Skill behavior.
- Move AI, deployment, waves, targeting, victory, result, and cleanup into SimulationCore.
- Correct defense targeting, no-attacker mode startup, interpolation, and view lifecycle.
- Correct generation checks, deterministic ordering, complete checksum, deep snapshots, rollback, and replay.
- Route all CombatManager public operations through the session.
- Build behavior-parity tests, switch production to SimulationCore, then remove Legacy ECS combat and the toggle.
- Treat `village_attack`, `builder_base`, `clan_war`, and `campaign` as the supported mode manifest. Each mode defines deployment, wave source, zero-attacker startup, timeout, draw, terminal result, and rewards. Known Legacy defects are intentional divergences, not parity oracles.

### 4. Network and Server

- Separate HTTP, realtime contracts, WebGL transport, and native transport assemblies.
- Add the shared authentication/resume state machine, scheduler-sealed tick policy, command dispositions, and atomic snapshot resync.
- Replace callback subscription leaks and signature mismatches with stored handlers and deterministic cleanup.
- Replace the custom KCP core with immutable, provenance-recorded `kcp2k` source and test fragmentation, acknowledgement, loss, duplication, and reordering before deleting every custom KCP symbol/source.
- Implement full-send loops for stream transports and correct dual-stack winner/loser ownership.
- Implement local input sending, fixed tick advancement including NoOp, prediction, rollback replay, and resync.
- Implement the .NET 8 authoritative frame-sync server and harden ReplayValidator.
- Add size, rate, protocol-version, source/session, and authentication checks at ingress boundaries.

### 5. Tests, CI, and Release Gates

- Convert each confirmed issue into automated closure evidence before its production fix. Production behavior uses red-green tests; compiler, asset, workflow, assembly-boundary, and source-removal issues use a failing compile/lint/audit check as the red phase.
- Repair false or contradictory tests; benchmarks do not count as correctness tests.
- Add a non-Editor PlayMode test assembly, a required-test manifest, and reject zero tests, count regressions, missing named closure checks, skipped tests, and inconclusive tests.
- Pass the exact Unity version to all GameCI actions and include tests, scenes, Resources, Packages, ProjectSettings, Server, and workflow changes in CI triggers.
- Add .NET 8 build/test jobs and WebGL/Windows build jobs that call the repository BuildScript explicitly.
- Maintain a machine-readable evidence ledger for every issue ID. Closed entries require red artifact, fix commit, green artifact, gate artifact, closure mode/date, and replacement/removal evidence where applicable.

## Verification Gates

### G0: Repository Integrity

- No missing or orphaned `.meta` files.
- Launcher is enabled scene zero.
- Every entry in the checked-in required-asset manifest resolves exactly once.
- From a clean checkout, record the baseline and after each gate require `git status --porcelain --untracked-files=all` to be empty. Build/test output lives outside the repository or in committed ignore rules; no verbal artifact exceptions are allowed.

### G1: Compilation

- Pinned Unity editor exits zero for EditMode import/test.
- Logs contain no compiler, assembly, serialization, missing-script, or package-resolution errors.
- A checked-in Server solution lists ReplayValidator, FrameSyncServer, and their tests. Linux and Windows CI run locked restore, `dotnet build -c Release --warnaserror --no-restore`, and `dotnet test --no-build`; zero tests fail the job.

### G2: Unit and Integration Tests

- EditMode tests have zero failed, skipped, or inconclusive cases and match the required-test manifest/minimum count per assembly.
- PlayMode tests open Launcher and exercise boot, UI interaction, save/load, scene changes, battle, pause/resume, result application, and cleanup.
- All former audit defects have named regression tests.

### G3: Determinism, Replay, and Rollback

- A versioned golden corpus contains canonical BattleInit/content bytes, ordered commands, wire bytes, PresentationFrames, and every per-tick checksum. .NET 8, Unity Editor, Windows player, and WebGL IL2CPP consume the same corpus.
- PR CI runs a fixed quick corpus; release CI runs at least 100 seeds for 3,600 ticks with equal per-tick checksums across all required hosts.
- Restore/replay reproduces the original checksum sequence for initial state (`CompletedTick=-1`), after tick 0, normal ticks, ring-buffer wrap, terminal tick, late commands, foreign/corrupt snapshots, and out-of-window resync.
- Replay CLI accepts valid files and rejects command/layout/seed/config/version/checksum tampering, truncation, duplication, reordering, trailing records, and size/time-limit violations.

### G4: Network

- WSS-WSS, native-native, and WSS-native clients all enter the same match actor and finish with the same result/checksum.
- WSS tests cover message boundaries, backpressure, 50-300 ms latency, disconnect/resume inside and outside the ten-second grace period, TLS trust, Origin allowlist, auth failure, and 64 KiB limit.
- kcp2k tests use fixed seeds and an impairment matrix of 0/5/20% loss, 0/5% duplication, 0/25% reordering, 512/1200-byte MTU, and 50/150/300 ms RTT; each case has a 30-second completion timeout and must deliver ordered messages without checksum divergence.
- Auth tests reject forged conv/session, invalid MAC, duplicate packet sequence, expired token, foreign source, replayed handshake, and unauthorized endpoint rebinding.
- Empty input advances exactly one tick through explicit NoOp.
- Both clients and server finish with equal checksum and result.

### G5: Builds and Smoke Tests

- WebGL and Windows BuildReports succeed with Launcher as scene zero.
- BuildScript writes a JSON BuildReport summary containing editor version, target, enabled scenes, artifact hashes, result, warnings, and errors.
- WebGL output contains HTML, WASM, and data artifacts. Playwright serves it over HTTPS with a trusted local CA, waits up to 60 seconds for a machine-readable `app-ready`/state marker, exercises a real button click, and requires no non-allowlisted console exception or mixed content.
- Windows output starts on a Windows runner, waits up to 60 seconds for the same structured readiness/state log marker, requests graceful exit, and scans logs against an explicit error allowlist.
- Build/test execution leaves tracked files unchanged.

## Legacy Removal Gates

Legacy Combat may be deleted only after:

1. SimulationCore LocalMode completes deployment, combat, result, and cleanup in PlayMode.
2. `village_attack`, `builder_base`, `clan_war`, and `campaign` each pass the approved mode behavior matrix, including intentional corrections to known Legacy defects.
3. Canonical `SimBattleResult` rewards are committed transactionally by MatchId/ResultId exactly once, clamped to resource caps, saved before returning to the village, and never applied for duplicate, foreign, rejected, or replay-invalid results.
4. Presentation creates initial views, interpolates across logical ticks, handles rollback, and leaks no views/subscriptions.
5. ReplayValidator uses the same session implementation and validates terminal state.
6. All `COMBAT-*`, `SIM-*`, `REPLAY-*`, and `DATA-005` issue IDs are Closed with evidence, and G1/G2/G3/G5 pass on the deletion commit.
7. No production caller reads or mutates Legacy `World`, `CombatOrchestrator`, Legacy systems, `SimWorld`, or `SimOrchestrator`; production consumers depend only on Session contracts and immutable outputs.
8. Feature toggle, Legacy source, serialized references, production callers, and custom KCP source/symbols are absent where their replacement gates apply.

## Removal Closure Rules

- A Legacy Combat issue closes by removal only when its named SimulationCore replacement behavior passes, all production/serialized references to the old symbol are zero, and build gates pass after deletion.
- Network issues NET-001 through NET-006 close by platform removal only after HTTPS/WSS/native replacement tests pass, all production callers migrate, WebGL/Windows builds pass, and old `INetworkClient`, `TcpClient`, `AddressResolver`, or related symbols targeted for removal have zero references.
- Custom KCP issues close only after immutable kcp2k provenance is recorded, Unity and .NET replacement vectors/impairment tests pass, and the repository and produced assemblies contain no custom KCP source or symbol.

## Change Boundaries

The repair may modify runtime scripts, tests, server projects, CI, assembly definitions, project settings, scenes, required Resources, prefabs, and metadata that directly support the gates above. It does not migrate to Unity 6, add unrelated gameplay, redesign visual art, or claim Android/iOS release support.

## Delivery Strategy

Each workstream is implemented as small red-green-refactor tasks on an isolated branch. Every task ends with its targeted tests and the broadest currently available gate. Cross-cutting deletion occurs only after consumers have migrated and parity tests pass. No bulk "audit fix" commit may combine unrelated root causes without independent regression evidence.
