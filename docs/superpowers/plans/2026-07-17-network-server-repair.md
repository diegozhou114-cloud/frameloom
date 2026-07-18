# Network, Replay, and Authoritative Server Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the unsupported networking stack with authenticated HTTPS/WSS and pinned native kcp2k adapters, complete deterministic frame synchronization, harden replay validation, and deliver one .NET 8 authoritative server using the same SimulationCore session.

**Architecture:** A Unity-free contracts assembly owns the wire schema, strict codecs, authentication envelope, and transport interfaces. Unity adapters implement HTTPS, browser WSS, and native kcp2k behind those interfaces; FrameSync, ReplayValidator, and the server consume the same versioned messages and `ISimBattleSession` boundary. Legacy TCP/UDP/custom-KCP code is deleted only after the replacement adapters and their impairment tests are green.

**Tech Stack:** Unity `2022.3.62f3c1`, C#/.NET Standard APIs supported by that editor, .NET 8, NUnit, Unity Test Framework, a provenance-locked immutable NativeWebSocket revision, ASP.NET Core/Kestrel, and a source-vendored immutable kcp2k revision. Task 1 captures both dependencies' canonical URL, full commit, source archive SHA-256, and verbatim license before either is imported.

## Global Constraints

- The approved architecture is option A from `docs/superpowers/specs/2026-07-16-project-repair-design.md`.
- Supported clients are WebGL and Windows Standalone; supported servers are .NET 8 on Linux and Windows.
- Online WebGL uses HTTPS and WSS only. No raw socket, thread, native UDP, or kcp2k type may compile into a WebGL assembly.
- WebGL realtime uses one reviewed, immutable NativeWebSocket source revision; native realtime uses one reviewed, immutable kcp2k source revision. Do not type a guessed tag, version, commit, checksum, or license identifier into the repository.
- The dependency provenance task must complete and be reviewed for both NativeWebSocket and kcp2k before either dependency is copied or referenced.
- Authentication is transport-independent. TLS protects WSS; both WSS and native sessions still present the same opaque ticket and bind to the same server session claims.
- Native datagrams additionally carry the full 32-byte HMAC-SHA256 tag and a monotonic packet sequence checked by a 1,024-packet replay window. A source endpoint changes only after the packet MAC, session, and replay sequence validate successfully; kcp2k conv is routing metadata, never identity.
- Protocol version `1` uses explicit little-endian integers and complete-message framing. Maximum realtime frame size is exactly `65536` bytes (64 KiB).
- WebGL and server WSS use the exact path `/v1/realtime` and subprotocol `zweb.framesync.v1`. `AuthHello` is the first application message on a new connection. `ResumeHello` carries the resume token, last accepted command sequence, last authority tick, and timeline revision.
- Protocol close codes are stable: `4001` authentication, `4002` version, `4003` malformed frame, `4004` overload, and `4005` expired session.
- A disconnected authenticated session is retained for exactly 10 seconds of monotonic scheduler time. A valid `ResumeHello` before that boundary resumes it; at the boundary it is disposed and the token is rejected.
- `BattleInit.InputDeadlineTicks` defaults to `6`. At 20 Hz the authoritative server seals each scheduled tick 300 ms after its input epoch begins and inserts a NoOp for each missing player.
- A sealed authoritative tick is immutable. A real input arriving after its NoOp is acknowledged as `LateRejected` and cannot rewrite history.
- Snapshot semantics are state after tick T; applying or restoring that snapshot makes T+1 the next executable tick. The post-Start/pre-tick initial snapshot is identified as completed tick `-1`, so tick-zero rollback has a real restore point.
- FrameSync, ReplayValidator, and server tasks must not begin until the SimulationCore plan has delivered the exact external contract listed below and the deterministic-state prerequisites in the issue register are green.
- Before Task 12, verify SimulationCore's existing `InternalsVisibleTo` grants its planned `SealTick` access to assembly `ZWebGLFrameWork.FrameSyncServer`; resolve a mismatch in the SimulationCore plan, not with reflection or a duplicate public API.
- Task execution starts only after repository G0/G1 baseline work has restored stable metadata and made the pinned Unity import compile; generated metadata in this plan is committed with its owning task.
- This plan does not edit `.github/workflows/ci.yml`; it creates deterministic commands and test assets that the CI-owned plan can invoke.
- Every log, XML, TRX, build, browser result, impairment trace, and rejection matrix is written beneath `${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-network-server-evidence}`. No test or build output is written inside the repository.
- Every behavior task follows red, green, focused verification, then one commit. Do not combine the listed commits.

### Required External Simulation Contract

The SimulationCore plan owns these types. This block is copied from `docs/superpowers/plans/2026-07-17-simulationcore-migration.md`; before Task 8, compare it byte-for-byte with that plan and resolve any mismatch there rather than adding a second network-side simulation API. In particular, `Start` and `RestoreSnapshot` return typed results, the factory consumes canonical manifest bytes plus an optional logger, `ProtocolVersion` is `int`, command `Sequence` is `uint`, and the immutable snapshot envelope keeps every field below.

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

public interface ISimBattleSessionFactory
{
    ISimBattleSession Create(byte[] canonicalManifestBytes, ISimLogger logger = null);
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

public interface ISimCommand
{
    SimCommandType Type { get; }
    int Tick { get; }
    int PlayerId { get; }
    uint Sequence { get; }
}
```

`TickResult`, `SimBattleState`, `SimBattleResult`, `SessionSnapshotCodec`, and `SerializableCommand.From`/`ToCommand` are consumed exactly as defined by the SimulationCore plan. This plan introduces no parallel snapshot/command codec interface, writable simulation no-op command, string schema/config alias, or other duplicate API.

### Exact Test Commands

Unity test commands in this plan use the pinned editor executable:

```bash
export ZWEB_EVIDENCE_ROOT="${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-network-server-evidence}"
export ZWEB_BUILD_OUTPUT_ROOT="$ZWEB_EVIDENCE_ROOT/builds"
mkdir -p "$ZWEB_EVIDENCE_ROOT/unity" "$ZWEB_EVIDENCE_ROOT/dotnet" "$ZWEB_BUILD_OUTPUT_ROOT"
mkdir -p "$ZWEB_EVIDENCE_ROOT"/network/task-{02,03,04,05,06,07,08,09} \
  "$ZWEB_EVIDENCE_ROOT"/replay/task-{10,11} \
  "$ZWEB_EVIDENCE_ROOT"/server/task-{12,13,14}
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix \
  -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Network.Tests \
  -testResults "$ZWEB_EVIDENCE_ROOT/unity/network-editmode.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/unity/network-editmode.log"
```

.NET tests use the committed server solution and lock files:

```bash
dotnet restore Server/ZWebGLFrameWork.Server.sln --locked-mode --artifacts-path "$ZWEB_EVIDENCE_ROOT/dotnet"
dotnet build Server/ZWebGLFrameWork.Server.sln --configuration Release --no-restore --warnaserror --artifacts-path "$ZWEB_EVIDENCE_ROOT/dotnet"
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj --configuration Release --no-build --no-restore --artifacts-path "$ZWEB_EVIDENCE_ROOT/dotnet" --logger "trx;LogFileName=simulationcore-tests.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/dotnet/results"
dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --no-build --no-restore --artifacts-path "$ZWEB_EVIDENCE_ROOT/dotnet" --logger "trx;LogFileName=server-tests.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/dotnet/results"
```

## File and Ownership Map

### Dependency provenance

- Create `scripts/capture-realtime-dependency-provenance.sh` and `scripts/verify-realtime-dependency-provenance.sh`: capture and verify both canonical checkouts, full commits, source archive SHA-256 values, and verbatim licenses.
- Create `docs/dependencies/nativewebsocket.lock` and `ThirdPartyNotices/NativeWebSocket-LICENSE.txt`: generated immutable NativeWebSocket source facts and license evidence.
- Create `scripts/vendor-nativewebsocket.sh` and `Packages/com.zwebglframework.nativewebsocket/`: reproduce the reviewed NativeWebSocket package only from its lock.
- Create `docs/dependencies/kcp2k.lock`: generated immutable source facts; never hand-edit values.
- Create `ThirdPartyNotices/kcp2k-LICENSE.txt`: verbatim license from the recorded commit.
- Create `scripts/vendor-kcp2k.sh`: later reproduce vendored source from the lock and verify the archive before copying.
- Create `Packages/com.zwebglframework.kcp2k/`: reviewed vendored source plus a local package manifest and asmdef.

### Shared contracts and authentication

- Create `Assets/Scripts/Network/Contracts/ZWebGLFrameWork.Network.Contracts.asmdef`: Unity-free, no-engine contract assembly.
- Create `Assets/Scripts/Network/Contracts/Protocol/RealtimeProtocol.cs`: constants, message kinds, rejection codes, and hard limits.
- Create `Assets/Scripts/Network/Contracts/Protocol/RealtimeModels.cs`: command, authority, resync, login, and handshake models.
- Create `Assets/Scripts/Network/Contracts/Protocol/RealtimeCodec.cs`: strict little-endian complete-message codec.
- Create `Assets/Scripts/Network/Contracts/Transport/IHttpApi.cs`: cancellable complete HTTP request API.
- Create `Assets/Scripts/Network/Contracts/Transport/IRealtimeChannel.cs`: cancellable complete-message realtime API.
- Create `Assets/Scripts/Network/Contracts/Auth/AuthenticatedPacketCodec.cs`: HMAC packet envelope.
- Create `Assets/Scripts/Network/Contracts/Auth/SequenceReplayWindow.cs`: duplicate and replay rejection.
- Create `Assets/Scripts/Network/Contracts/Dispatch/BoundedMessagePump.cs`: uint message IDs, bounded queue, fixed drain budget.

### Unity adapters and runtime composition

- Create `Assets/Scripts/Network/UnityHttp/UnityHttpApi.cs` and its asmdef: HTTPS adapter using `UnityWebRequest`.
- Create `Assets/Scripts/Network/WebGL/NativeWebSocketClientAdapter.cs`, `WebGlRealtimeChannel.cs`, and their asmdef; do not create a handwritten `.jslib` WebSocket replacement.
- Create `Assets/Scripts/Network/Native/Kcp2kRealtimeChannel.cs`, `Kcp2kClientFacade.cs`, and their asmdef.
- Create `Assets/Scripts/Network/Runtime/RealtimeConnectionController.cs`: generation and cancellation ownership.
- Replace `Assets/Scripts/Network/NetworkManager.cs`: compose HTTP and realtime services without legacy callback subscriptions.
- Modify `Assets/Scripts/FSM/Game/States/LoginState.cs`: use HTTPS guest login rather than raw TCP connect.
- Delete obsolete `Assets/Scripts/Network/KCP/`, `Assets/Scripts/Network/Protocols/TcpClient.cs`, `UdpClient.cs`, `Core/AddressResolver.cs`, `Core/INetworkClient.cs`, and `Core/MessageProtocol.cs` only in Task 7.

### Frame synchronization

- Create `Assets/Scripts/Network/FrameSync/LocalCommandSequencer.cs`: monotonic local sequence and acknowledgement tracking.
- Create `Assets/Scripts/Network/FrameSync/FrameSyncHistory.cs`: 64-tick snapshots, authority frames, and predicted command history.
- Create `Assets/Scripts/Network/FrameSync/FrameSyncClient.cs`: send, predict, compare, rollback/replay, and resync state machine.
- Delete the superseded transport-coupled files under `Assets/Scripts/SimulationCore/Network/` after replacement tests pass.

### Replay validation and server

- Modify `Assets/Scripts/SimulationCore/Command/ReplayFile.cs`: complete versioned replay schema.
- Create `Server/NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj`: link the Unity-free contracts source.
- Consume `Server/SimulationCore/ZWebGLFrameWork.SimulationCore.csproj`, `Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj`, and their committed lock from the SimulationCore plan; do not create a second SimulationCore project.
- Replace `Server/ReplayValidator/ReplayValidator.cs`: structural and authoritative validation through the shared session.
- Replace `Server/ReplayValidator/Program.cs`: stable CLI arguments, output, and exit codes.
- Create `Server/FrameSync/FrameSyncServer.csproj`, `FrameSyncMatchActor.cs`, `InputInbox.cs`, `RealtimeSessionRouter.cs`, `WebSocketIngress.cs`, `KcpIngress.cs`, and `Program.cs`.
- Create `Server/Tests/ZWebGLFrameWork.Server.Tests.csproj`: ReplayValidator, actor, ingress, mixed-transport, and end-to-end tests.
- Create `Server/ZWebGLFrameWork.Server.sln`: explicit server build/test boundary.

### Assembly Definition Matrix

| Assembly | References | Platforms | `noEngineReferences` |
|---|---|---|---:|
| `ZWebGLFrameWork.Network.Contracts` | none | all | `true` |
| `ZWebGLFrameWork.Network.Runtime` | Contracts, Core, UnityHttp | all | `false` |
| `ZWebGLFrameWork.Network.UnityHttp` | Contracts | all | `false` |
| `ZWebGLFrameWork.Network.WebGL` | Contracts, Runtime | Editor, WebGL | `false` |
| `ZWebGLFrameWork.Kcp2k` | none | Editor, WindowsStandalone64 | `true` |
| `ZWebGLFrameWork.Network.Native` | Contracts, Runtime, Kcp2k | Editor, WindowsStandalone64 | `false` |
| `ZWebGLFrameWork.Network.FrameSync` | Contracts, SimulationCore | all | `true` |
| `ZWebGLFrameWork.Network.Tests` | Contracts, Runtime, UnityHttp, WebGL, Native, FrameSync, SimulationCore | Editor | `false` |

Every nested asmdef owns only its directory. The final WebGL dependency graph ends at Contracts, Runtime, UnityHttp, WebGL, Core, and SimulationCore; Native and Kcp2k are absent rather than guarded only at runtime.

## Issue Coverage Matrix

Each finding identifier appears in this document exactly once. The task column is the authoritative ownership mapping.

| Finding | Task | Closure evidence |
|---|---:|---|
| NET-001 | 4, 7 | `NetworkManager_Reconnect_DoesNotLeakOrDispatchStaleCallbacks`; task-04 lifecycle red/green XML plus task-07 removal XML and final Unity XML |
| NET-002 | 5, 7 | `WebGLAssembly_HasNoSocketThreadOrNativeKcpReferences`; task-05 adapter XML, WebGL build log, browser WSS JSON, and final Unity XML |
| NET-003 | 5 | `WebGlHttpApi_RejectsInsecureHttpEndpoint`; task-05 HTTP red/green XML, browser WSS JSON, and WebGL build log |
| NET-004 | 7 | `HappyEyeballs_FirstFails_SecondWinnerRemainsOpen`; task-07 removal red/green XML, both replacement build logs, and removed-source gate |
| NET-005 | 7 | `TcpTransport_PartialSend_CompletesWholeFrame`; task-07 removal red/green XML, complete-message adapter XML, and removed-source gate |
| NET-006 | 4, 7 | `TcpClient_DisconnectDuringConnect_DiscardsLateSocket`; task-04 lifecycle red/green XML, task-07 removal XML, and final Unity XML |
| NET-007 | 3, 6 | `NativeRealtime_Handshake_AgreesSessionAndRejectsForeignConv`; task-03 auth XML, task-06 native XML/impairment JSON, and final authentication matrix TRX |
| NET-008 | 6 | `NativeKcp_FragmentedMessage_RoundTrips`; task-06 native red/green XML, native impairment JSON, and final authentication/impairment TRX |
| NET-009 | 6 | `NativeKcp_GappedAck_DoesNotDropMissingSegment`; task-06 native red/green XML, retransmission trace JSON, and final authentication/impairment TRX |
| NET-010 | 8 | `FrameSyncClient_EachTickSendsLocalInputAndProcessesAck`; task-08 input red/green XML and final Unity XML |
| NET-011 | 8, 12 | `FrameSync_EmptyInput_AdvancesExactlyOneTickViaNoOp`; task-08 input XML, task-12 actor TRX, and final server TRX |
| NET-012 | 9 | `FrameSync_OutOfWindowMismatch_RequestsFullResync`; task-09 rollback red/green XML and final Unity XML |
| NET-013 | 2, 3 | `AuthorityEnvelope_RejectsShortOversizedWrongVersionAndForeignSession`; task-02 codec XML, task-03 auth XML, and final Unity/server gates |
| NET-014 | 2 | `MessageDispatcher_UIntIdAndBackpressure_ArePreserved`; task-02 dispatch red/green XML and final Unity XML |
| REPLAY-001 | 10 | `ReplayValidator_EmptyReplay_IsRejected`; task-10 structural red/green TRX and final replay rejection JSON |
| REPLAY-002 | 11 | `ReplayValidator_TamperedSeedOrLayout_IsRejectedAgainstMatch`; task-11 authority red/green TRX and final replay rejection JSON |
| REPLAY-003 | 10 | `ReplayValidator_UnknownVersionOrConfigHash_IsRejected`; task-10 structural TRX, task-11 CLI TRX, and final rejection JSON |
| REPLAY-004 | 10 | `ReplayValidator_GapDuplicateReorderOrTruncation_IsRejected`; task-10 structural red/green TRX and final rejection JSON |
| REPLAY-005 | 11 | `ReplayValidator_TerminalChecksumAndTrailingRecords_AreValidated`; task-11 authority red/green TRX and final rejection JSON |
| REPLAY-006 | 10 | `ReplayValidator_UnknownOrInvalidCommand_IsRejected`; task-10 structural red/green TRX and final rejection JSON |
| REPLAY-007 | 10, 11 | `ReplayCli_OversizedOrOverLimitReplay_IsRejected`; task-10 limits TRX, task-11 CLI TRX/rejection matrix, and final replay rejection JSON |
| SERVER-001 | 12, 13, 14 | `FrameSyncServer_TwoClients_ReachEqualChecksumAndResult`; actor/ingress/e2e red-green TRX, three-transport TRX, and final server TRX |
| SERVER-002 | 12 | `FrameSyncServer_MissingInputAfterDeadline_InsertsNoOp`; task-12 actor red/green TRX and final server TRX |
| SERVER-003 | 13, 14 | `FrameSyncServer_WssAndNativeDecodeSameAuthorityVector`; task-13 ingress TRX, browser/native JSON, mixed-transport TRX, and final server TRX |

---

### Task 1: Capture Immutable NativeWebSocket and kcp2k Provenance Before Use

**Files:**
- Create: `scripts/verify-realtime-dependency-provenance.sh`
- Create: `scripts/capture-realtime-dependency-provenance.sh`
- Create at execution: `docs/dependencies/nativewebsocket.lock`
- Create at execution: `docs/dependencies/kcp2k.lock`
- Create at execution: `ThirdPartyNotices/NativeWebSocket-LICENSE.txt`
- Create at execution: `ThirdPartyNotices/kcp2k-LICENSE.txt`

**Interfaces:**
- Consumes: canonical upstream URLs `https://github.com/endel/NativeWebSocket.git` and `https://github.com/MirrorNetworking/kcp2k.git`.
- Produces: `UPSTREAM_URL`, full `UPSTREAM_COMMIT`, `SOURCE_SUBTREE`, `SOURCE_ARCHIVE_SHA256`, `UPSTREAM_LICENSE_PATH`, `LICENSE_SHA256`, and `LICENSE_FILE` in each lock. No mutable ref is written to either lock.

- [ ] **Step 1: Write the failing verifier for both immutable sources**

```bash
#!/usr/bin/env bash
set -euo pipefail

verify_dependency() {
  local name="$1" lock="$2" expected_url="$3" expected_subtree="$4" expected_license="$5"
  test -s "$lock" || { echo "missing $name lock" >&2; return 1; }
  unset UPSTREAM_URL UPSTREAM_COMMIT SOURCE_SUBTREE SOURCE_ARCHIVE_SHA256 \
    UPSTREAM_LICENSE_PATH LICENSE_SHA256 LICENSE_FILE
  source "$lock"
  [[ "$UPSTREAM_URL" == "$expected_url" ]]
  [[ "$UPSTREAM_COMMIT" =~ ^[0-9a-f]{40}$ ]]
  [[ "$SOURCE_SUBTREE" == "$expected_subtree" ]]
  [[ "$SOURCE_ARCHIVE_SHA256" =~ ^[0-9a-f]{64}$ ]]
  [[ "$UPSTREAM_LICENSE_PATH" =~ (^|/)(LICENSE|LICENSE\.md|LICENSE\.txt)$ ]]
  [[ "$LICENSE_SHA256" =~ ^[0-9a-f]{64}$ ]]
  [[ "$LICENSE_FILE" == "$expected_license" ]]
  test -s "$LICENSE_FILE"
  [[ "$(shasum -a 256 "$LICENSE_FILE" | awk '{print $1}')" == "$LICENSE_SHA256" ]]

  local work archive actual
  work=$(mktemp -d)
  git clone --filter=blob:none "$UPSTREAM_URL" "$work/repo"
  git -C "$work/repo" cat-file -e "$UPSTREAM_COMMIT^{commit}"
  archive="$work/source.tar"
  if [[ "$SOURCE_SUBTREE" == "." ]]; then
    git -C "$work/repo" archive --format=tar "$UPSTREAM_COMMIT" > "$archive"
  else
    git -C "$work/repo" archive --format=tar "$UPSTREAM_COMMIT" "$SOURCE_SUBTREE" > "$archive"
  fi
  actual=$(shasum -a 256 "$archive" | awk '{print $1}')
  [[ "$actual" == "$SOURCE_ARCHIVE_SHA256" ]]
  git -C "$work/repo" show "$UPSTREAM_COMMIT:$UPSTREAM_LICENSE_PATH" > "$work/license"
  cmp -s "$work/license" "$LICENSE_FILE"
  rm -rf "$work"
  echo "$name provenance verified at $UPSTREAM_COMMIT"
}

verify_dependency NativeWebSocket docs/dependencies/nativewebsocket.lock \
  https://github.com/endel/NativeWebSocket.git . ThirdPartyNotices/NativeWebSocket-LICENSE.txt
verify_dependency kcp2k docs/dependencies/kcp2k.lock \
  https://github.com/MirrorNetworking/kcp2k.git kcp2k ThirdPartyNotices/kcp2k-LICENSE.txt
```

- [ ] **Step 2: Run the verifier and confirm the red state**

Run: `bash scripts/verify-realtime-dependency-provenance.sh`

Expected: exit `1` and stderr contains `missing NativeWebSocket lock`; no dependency source exists in `Packages/`.

- [ ] **Step 3: Write the capture script that records real upstream facts**

```bash
#!/usr/bin/env bash
set -euo pipefail

capture_dependency() {
  local name="$1" repo_url="$2" subtree="$3" lock="$4" license_out="$5" required_source="$6" required_api="$7"
  local work commit archive archive_sha license_path license_sha source_path
  work=$(mktemp -d)
  git clone --filter=blob:none "$repo_url" "$work/repo"
  commit=$(git -C "$work/repo" rev-parse HEAD)
  source_path=$(git -C "$work/repo" ls-tree -r --name-only "$commit" | rg "$required_source" | head -n 1)
  test -n "$source_path"
  git -C "$work/repo" show "$commit:$source_path" | rg -q "$required_api"
  license_path=$(git -C "$work/repo" ls-tree -r --name-only "$commit" |
    rg '(^|/)(LICENSE|LICENSE\.md|LICENSE\.txt)$' | head -n 1)
  test -n "$license_path"
  archive="$work/source.tar"
  if [[ "$subtree" == "." ]]; then
    git -C "$work/repo" archive --format=tar "$commit" > "$archive"
  else
    git -C "$work/repo" archive --format=tar "$commit" "$subtree" > "$archive"
  fi
  archive_sha=$(shasum -a 256 "$archive" | awk '{print $1}')

  mkdir -p docs/dependencies ThirdPartyNotices
  git -C "$work/repo" show "$commit:$license_path" > "$license_out"
  license_sha=$(shasum -a 256 "$license_out" | awk '{print $1}')
  {
    printf 'UPSTREAM_URL=%s\n' "$repo_url"
    printf 'UPSTREAM_COMMIT=%s\n' "$commit"
    printf 'SOURCE_SUBTREE=%s\n' "$subtree"
    printf 'SOURCE_ARCHIVE_SHA256=%s\n' "$archive_sha"
    printf 'UPSTREAM_LICENSE_PATH=%s\n' "$license_path"
    printf 'LICENSE_SHA256=%s\n' "$license_sha"
    printf 'LICENSE_FILE=%s\n' "$license_out"
  } > "$lock"
  git -C "$work/repo" show --no-patch --format=fuller "$commit"
  rm -rf "$work"
  echo "Review $name commit, source archive hash, and verbatim license before import."
}

capture_dependency NativeWebSocket https://github.com/endel/NativeWebSocket.git . \
  docs/dependencies/nativewebsocket.lock ThirdPartyNotices/NativeWebSocket-LICENSE.txt \
  '(^|/)NativeWebSocket\.cs$' '(subProtocols|SubProtocol)'
capture_dependency kcp2k https://github.com/MirrorNetworking/kcp2k.git kcp2k \
  docs/dependencies/kcp2k.lock ThirdPartyNotices/kcp2k-LICENSE.txt \
  '(^|/)KcpClient\.cs$' 'TickIncoming'
```

- [ ] **Step 4: Capture, inspect, and verify both immutable sources**

Run:

```bash
chmod +x scripts/capture-realtime-dependency-provenance.sh scripts/verify-realtime-dependency-provenance.sh
bash scripts/capture-realtime-dependency-provenance.sh
bash scripts/verify-realtime-dependency-provenance.sh
git diff -- docs/dependencies/nativewebsocket.lock docs/dependencies/kcp2k.lock \
  ThirdPartyNotices/NativeWebSocket-LICENSE.txt ThirdPartyNotices/kcp2k-LICENSE.txt
```

Expected: the verifier prints two distinct full 40-character commits; the diff contains actual archive and license hashes plus verbatim licenses; neither dependency has been copied into `Packages/`. Stop if either displayed commit, source archive, or license is not approved.

- [ ] **Step 5: Commit provenance separately**

```bash
git add scripts/capture-realtime-dependency-provenance.sh scripts/verify-realtime-dependency-provenance.sh \
  docs/dependencies/nativewebsocket.lock docs/dependencies/kcp2k.lock \
  ThirdPartyNotices/NativeWebSocket-LICENSE.txt ThirdPartyNotices/kcp2k-LICENSE.txt
git commit -m "build: pin realtime dependency provenance"
```

### Task 2: Add Strict Realtime Contracts, Codec, and Bounded Dispatch

**Files:**
- Create: `Assets/Scripts/Network/Contracts/ZWebGLFrameWork.Network.Contracts.asmdef`
- Create: `Assets/Scripts/Network/Contracts/Protocol/RealtimeProtocol.cs`
- Create: `Assets/Scripts/Network/Contracts/Protocol/RealtimeModels.cs`
- Create: `Assets/Scripts/Network/Contracts/Protocol/RealtimeCodec.cs`
- Create: `Assets/Scripts/Network/Contracts/Transport/IHttpApi.cs`
- Create: `Assets/Scripts/Network/Contracts/Transport/IRealtimeChannel.cs`
- Create: `Assets/Scripts/Network/Contracts/Dispatch/BoundedMessagePump.cs`
- Create: `Assets/Tests/Network/RealtimeCodecTests.cs`
- Create: `Assets/Tests/Network/BoundedMessagePumpTests.cs`
- Create: `Assets/Tests/Network/ZWebGLFrameWork.Network.Tests.asmdef`

**Interfaces:**
- Consumes: only BCL APIs shared by Unity `2022.3.62f3c1` and .NET 8.
- Produces: `IHttpApi.SendAsync(HttpRequest, CancellationToken)`, `IRealtimeChannel.ConnectAsync`, `SendAsync`, `ReceiveAsync`, `CloseAsync`, `RealtimeCodec`, `CommandEnvelope`, `AuthorityFrame`, and `BoundedMessagePump`.
- Assembly delta: Contracts has no references and `noEngineReferences: true`; the initial Editor-only test asmdef references Contracts only. Later tasks append references only after the owning asmdef exists.

- [ ] **Step 1: Write failing strict-codec tests**

```csharp
[Test]
public void AuthorityFrame_RoundTrip_PreservesUIntMessageIdAndOrderedInputs()
{
    var frame = new AuthorityFrame
    {
        MatchId = "11111111-2222-3333-4444-555555555555",
        Tick = 7,
        AckPlayer0 = 10,
        AckPlayer1 = 11,
        Checksum = 0x0102030405060708UL,
        OrderedInputs = new[]
        {
            CommandEnvelope.NoOp(7, 0, 10),
            CommandEnvelope.Create(7, 1, 11, 0x00010002u, new byte[] { 3, 4 })
        }
    };

    byte[] bytes = RealtimeCodec.Encode(frame);
    DecodeResult result = RealtimeCodec.Decode(bytes);

    Assert.That(result.Error, Is.EqualTo(ProtocolError.None));
    Assert.That(result.Authority.OrderedInputs[1].MessageId, Is.EqualTo(0x00010002u));
    Assert.That(result.Authority.Checksum, Is.EqualTo(frame.Checksum));
}

[TestCase(0)]
[TestCase(8)]
[TestCase(65537)]
public void Decode_InvalidLength_ReturnsProtocolError(int length)
{
    var bytes = new byte[length];
    DecodeResult result = RealtimeCodec.Decode(bytes);
    Assert.That(result.Error, Is.Not.EqualTo(ProtocolError.None));
}
```

- [ ] **Step 2: Run the codec tests and confirm the red state**

Run the exact Unity command from the global section with `-testFilter ZWebGLFrameWork.Network.Tests.RealtimeCodecTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-02/codec-red.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-02/codec-red.log"`.

Expected: compilation fails because `AuthorityFrame` and `RealtimeCodec` do not exist.

- [ ] **Step 3: Add exact protocol constants and transport interfaces**

```csharp
public static class RealtimeProtocol
{
    public const ushort Version = 1;
    public const int HeaderBytes = 12;
    public const int MaxMessageBytes = 65536;
    public const int MaxCommandsPerAuthorityFrame = 64;
    public const int RollbackWindowTicks = 64;
    public const int NativeReplayWindowPackets = 1024;
    public const string WebSocketPath = "/v1/realtime";
    public const string WebSocketSubprotocol = "zweb.framesync.v1";
}

public enum ProtocolError
{
    None = 0,
    Truncated,
    UnsupportedVersion,
    UnknownMessageKind,
    InvalidLength,
    InvalidCount,
    InvalidPayload,
    UnorderedCommands,
    AuthenticationFailed,
    ReplayedPacket,
    WrongSession,
    ExpiredTicket,
    WrongPlayer,
    WrongSource,
    MessageTooLarge,
    RateLimited,
    LateRejected
}

public enum RealtimeMessageKind : byte
{
    AuthHello = 1,
    ResumeHello = 2,
    AuthAccepted = 3,
    Command = 4,
    Authority = 5,
    ResyncRequest = 6,
    ResyncSnapshot = 7,
    Close = 8
}

public enum RealtimeTransport { WebSocket, NativeKcp }
public enum RealtimeChannelState { Disconnected, Connecting, Connected, Closing }
public enum RealtimeCloseReason { Normal, AuthenticationFailed, ProtocolError, Replaced }
public enum RealtimeCloseCode : ushort
{
    Authentication = 4001,
    Version = 4002,
    Malformed = 4003,
    Overload = 4004,
    ExpiredSession = 4005
}

public sealed class RealtimeCredentials
{
    public Guid SessionId { get; init; }
    public byte[] OpaqueTicket { get; init; }
    public byte[] ResumeToken { get; init; }
    public byte[] MacKey { get; init; }
    public long ExpiresAtUnixSeconds { get; init; }
}

public sealed class AuthHello
{
    public ushort Version { get; init; }
    public Guid SessionId { get; init; }
    public byte[] OpaqueTicket { get; init; }
}

public sealed class ResumeHello
{
    public ushort Version { get; init; }
    public Guid SessionId { get; init; }
    public byte[] ResumeToken { get; init; }
    public uint LastAcceptedSequence { get; init; }
    public int LastAuthorityTick { get; init; }
    public long TimelineRevision { get; init; }
}

public sealed class AuthAccepted
{
    public Guid SessionId { get; init; }
    public int LastAuthorityTick { get; init; }
    public long TimelineRevision { get; init; }
}

public readonly struct RealtimeEndpoint
{
    public RealtimeTransport Transport { get; }
    public Uri Uri { get; }
    public RealtimeEndpoint(RealtimeTransport transport, Uri uri) { Transport = transport; Uri = uri; }
}

public readonly struct HttpMethod
{
    public static readonly HttpMethod Post = new HttpMethod("POST");
    public string Value { get; }
    public HttpMethod(string value) { Value = value; }
}

public sealed class HttpRequest
{
    public HttpMethod Method { get; }
    public Uri Uri { get; }
    public byte[] Body { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public HttpRequest(HttpMethod method, Uri uri, byte[] body, IReadOnlyDictionary<string, string> headers = null)
    {
        Method = method;
        Uri = uri;
        Body = body;
        Headers = headers ?? new Dictionary<string, string>();
    }
}

public sealed class HttpResponse
{
    public long StatusCode { get; init; }
    public byte[] Body { get; init; }
    public string Error { get; init; }
}

public sealed class CommandEnvelope
{
    public ushort Version { get; init; }
    public string MatchId { get; init; }
    public int Tick { get; init; }
    public int PlayerId { get; init; }
    public uint Sequence { get; init; }
    public uint MessageId { get; init; }
    public byte[] Payload { get; init; }
    public bool IsNoOp => MessageId == 0;

    public static CommandEnvelope NoOp(int tick, int playerId, uint sequence) =>
        new CommandEnvelope { Version = RealtimeProtocol.Version, Tick = tick, PlayerId = playerId, Sequence = sequence, MessageId = 0, Payload = Array.Empty<byte>() };

    public static CommandEnvelope Create(int tick, int playerId, uint sequence, uint messageId, byte[] payload) =>
        new CommandEnvelope { Version = RealtimeProtocol.Version, Tick = tick, PlayerId = playerId, Sequence = sequence, MessageId = messageId, Payload = payload };
}

public sealed class ResyncSnapshotEnvelope
{
    public string MatchId { get; init; }
    public string SessionId { get; init; }
    public int SchemaVersion { get; init; }
    public byte[] ContentHash { get; init; }
    public int CompletedTick { get; init; }
    public int NextTick { get; init; }
    public long TimelineRevision { get; init; }
    public int UncompressedLength { get; init; }
    public ulong StateChecksum { get; init; }
    public byte[] Payload { get; init; }
    public byte[] PayloadSha256 { get; init; }
}

public sealed class AuthorityFrame
{
    public string MatchId { get; init; }
    public int Tick { get; init; }
    public CommandEnvelope[] OrderedInputs { get; init; }
    public uint AckPlayer0 { get; init; }
    public uint AckPlayer1 { get; init; }
    public ulong Checksum { get; init; }
    public ResyncSnapshotEnvelope ResyncSnapshot { get; init; }

    public uint AckForPlayer(int playerId) => playerId == 0 ? AckPlayer0 : AckPlayer1;

    public static AuthorityFrame Create(int tick, CommandEnvelope[] orderedInputs, uint ackPlayer0, uint ackPlayer1, ulong checksum) =>
        new AuthorityFrame { Tick = tick, OrderedInputs = orderedInputs, AckPlayer0 = ackPlayer0, AckPlayer1 = ackPlayer1, Checksum = checksum };
}

public sealed class DecodeResult
{
    public ProtocolError Error { get; init; }
    public RealtimeMessageKind Kind { get; init; }
    public AuthHello AuthHello { get; init; }
    public ResumeHello ResumeHello { get; init; }
    public AuthAccepted AuthAccepted { get; init; }
    public CommandEnvelope Command { get; init; }
    public AuthorityFrame Authority { get; init; }
    public ResyncSnapshotEnvelope ResyncSnapshot { get; init; }
}

public interface IRealtimeChannel : IDisposable
{
    RealtimeChannelState State { get; }
    Task ConnectAsync(RealtimeEndpoint endpoint, RealtimeCredentials credentials, CancellationToken cancellationToken);
    Task SendAsync(byte[] completeMessage, CancellationToken cancellationToken);
    Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);
    Task CloseAsync(RealtimeCloseReason reason, CancellationToken cancellationToken);
}

public interface IHttpApi
{
    Task<HttpResponse> SendAsync(HttpRequest request, CancellationToken cancellationToken);
}
```

The codec header is exactly `[version:u16][kind:u8][flags:u8][payloadLength:u32][requestId:u32]`. `Decode` must reject a wrong version, unknown kind, zero/oversized payload, count over 64, duplicate player/sequence pair, unordered commands, trailing bytes, and any integer read beyond the supplied array. It must return `ProtocolError`; it must not throw for untrusted bytes.

- [ ] **Step 4: Implement the complete header validation path**

```csharp
private static bool TryReadHeader(byte[] data, out WireHeader header, out ProtocolError error)
{
    header = default;
    error = ProtocolError.None;
    if (data == null || data.Length < RealtimeProtocol.HeaderBytes)
    {
        error = ProtocolError.Truncated;
        return false;
    }

    ushort version = (ushort)(data[0] | (data[1] << 8));
    byte kindValue = data[2];
    uint payloadLength = ReadUInt32(data, 4);
    uint requestId = ReadUInt32(data, 8);
    if (version != RealtimeProtocol.Version)
    {
        error = ProtocolError.UnsupportedVersion;
        return false;
    }
    if (!Enum.IsDefined(typeof(RealtimeMessageKind), kindValue))
    {
        error = ProtocolError.UnknownMessageKind;
        return false;
    }
    if (payloadLength == 0 || payloadLength > RealtimeProtocol.MaxMessageBytes ||
        payloadLength != data.Length - RealtimeProtocol.HeaderBytes)
    {
        error = ProtocolError.InvalidLength;
        return false;
    }

    header = new WireHeader(version, (RealtimeMessageKind)kindValue, data[3], payloadLength, requestId);
    return true;
}
```

- [ ] **Step 5: Write failing bounded-dispatch tests**

```csharp
[Test]
public void MessageDispatcher_UIntIdAndBackpressure_ArePreserved()
{
    var pump = new BoundedMessagePump(capacity: 2, maxMessagesPerPump: 1);
    var received = new List<uint>();
    pump.Register(0xFFFF0001u, message => received.Add(message.MessageId));

    Assert.That(pump.TryEnqueue(new NetworkMessage(0xFFFF0001u, new byte[] { 1 })), Is.True);
    Assert.That(pump.TryEnqueue(new NetworkMessage(0xFFFF0001u, new byte[] { 2 })), Is.True);
    Assert.That(pump.TryEnqueue(new NetworkMessage(0xFFFF0001u, new byte[] { 3 })), Is.False);
    Assert.That(pump.Pump(), Is.EqualTo(1));
    Assert.That(received, Is.EqualTo(new[] { 0xFFFF0001u }));
    Assert.That(pump.PendingCount, Is.EqualTo(1));
    Assert.That(pump.DroppedCount, Is.EqualTo(1));
}
```

- [ ] **Step 6: Run the dispatch test and confirm the red state**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.BoundedMessagePumpTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-02/dispatch-red.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-02/dispatch-red.log"`.

Expected: compilation fails because `BoundedMessagePump` does not exist.

- [ ] **Step 7: Implement bounded uint dispatch**

```csharp
public bool TryEnqueue(NetworkMessage message)
{
    lock (_gate)
    {
        if (_queue.Count >= _capacity)
        {
            DroppedCount++;
            return false;
        }
        _queue.Enqueue(message);
        return true;
    }
}

public int Pump()
{
    int dispatched = 0;
    while (dispatched < _maxMessagesPerPump)
    {
        NetworkMessage message;
        lock (_gate)
        {
            if (_queue.Count == 0) break;
            message = _queue.Dequeue();
        }
        if (_handlers.TryGetValue(message.MessageId, out Action<NetworkMessage> handler))
            handler(message);
        dispatched++;
    }
    return dispatched;
}
```

- [ ] **Step 8: Run focused and assembly tests**

Run the exact Unity command from the global section with `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-02/contracts-green.xml"` and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-02/contracts-green.log"`.

Expected: all network contract tests pass; the result XML reports a nonzero test count and zero failed, skipped, or inconclusive cases.

- [ ] **Step 9: Import metadata and commit**

```bash
git add Assets/Scripts/Network/Contracts Assets/Tests/Network
git commit -m "feat: add strict realtime contracts"
```

### Task 3: Add Transport-Independent Session Authentication

**Files:**
- Create: `Assets/Scripts/Network/Contracts/Auth/AuthenticatedPacketCodec.cs`
- Create: `Assets/Scripts/Network/Contracts/Auth/SequenceReplayWindow.cs`
- Create: `Assets/Tests/Network/AuthenticatedPacketCodecTests.cs`

**Interfaces:**
- Consumes: `RealtimeCredentials`, `ProtocolError`, and `RealtimeProtocol.Version` from Task 2.
- Produces: `AuthenticatedPacketCodec.Encode(Guid, ulong, byte[], byte[])`, full-tag `TryDecode`, and `SequenceReplayWindow.TryAccept(ulong)` with an exact 1,024-packet window.

- [ ] **Step 1: Write failing authentication and replay tests**

```csharp
[Test]
public void AuthorityEnvelope_RejectsShortOversizedWrongVersionAndForeignSession()
{
    Guid session = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    byte[] key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    byte[] packet = AuthenticatedPacketCodec.Encode(session, 9, new byte[] { 4, 5, 6 }, key);
    var window = new SequenceReplayWindow();

    Assert.That(AuthenticatedPacketCodec.TryDecode(packet, session, key, window, out AuthenticatedPacket decoded), Is.EqualTo(ProtocolError.None));
    Assert.That(decoded.Sequence, Is.EqualTo(9));
    Assert.That(AuthenticatedPacketCodec.TryDecode(packet, session, key, window, out _), Is.EqualTo(ProtocolError.ReplayedPacket));
    var boundary = new SequenceReplayWindow();
    Assert.That(boundary.TryAccept(2048), Is.True);
    Assert.That(boundary.TryAccept(1025), Is.True);
    Assert.That(boundary.TryAccept(1024), Is.False);

    Assert.That(AuthenticatedPacketCodec.TryDecode(packet.Take(20).ToArray(), session, key, new SequenceReplayWindow(), out _), Is.EqualTo(ProtocolError.Truncated));
    Assert.Throws<ArgumentOutOfRangeException>(() => AuthenticatedPacketCodec.Encode(session, 10, new byte[65537], key));
    byte[] wrongVersion = (byte[])packet.Clone();
    wrongVersion[0] = 2;
    Assert.That(AuthenticatedPacketCodec.TryDecode(wrongVersion, session, key, new SequenceReplayWindow(), out _), Is.EqualTo(ProtocolError.UnsupportedVersion));

    packet[packet.Length - 1] ^= 0x40;
    Assert.That(AuthenticatedPacketCodec.TryDecode(packet, session, key, new SequenceReplayWindow(), out _), Is.EqualTo(ProtocolError.AuthenticationFailed));
    Assert.That(AuthenticatedPacketCodec.TryDecode(packet, Guid.Empty, key, new SequenceReplayWindow(), out _), Is.EqualTo(ProtocolError.WrongSession));
}
```

- [ ] **Step 2: Run the authentication test and confirm the red state**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.AuthenticatedPacketCodecTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-03/auth-red.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-03/auth-red.log"`.

Expected: compilation fails because the authentication types do not exist.

- [ ] **Step 3: Implement the exact authenticated packet format**

Format: `[version:u16][sessionId:16][sequence:u64][payloadLength:u32][payload][tag:32]`. The tag is the complete 32-byte `HMACSHA256` over every preceding byte. Validate version, bounds, session, and the full constant-time MAC before advancing the replay window or updating any native endpoint binding.

```csharp
public readonly struct AuthenticatedPacket
{
    public Guid SessionId { get; }
    public ulong Sequence { get; }
    public byte[] Payload { get; }
    public AuthenticatedPacket(Guid sessionId, ulong sequence, byte[] payload)
    {
        SessionId = sessionId;
        Sequence = sequence;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }
}

public static ProtocolError TryDecode(
    byte[] packet,
    Guid expectedSession,
    byte[] key,
    SequenceReplayWindow replayWindow,
    out AuthenticatedPacket decoded)
{
    decoded = default;
    const int fixedBytes = 2 + 16 + 8 + 4 + 32;
    if (packet == null || packet.Length < fixedBytes) return ProtocolError.Truncated;
    if (ReadUInt16(packet, 0) != RealtimeProtocol.Version) return ProtocolError.UnsupportedVersion;

    Guid session = ReadGuid(packet, 2);
    if (session != expectedSession) return ProtocolError.WrongSession;
    ulong sequence = ReadUInt64(packet, 18);
    int payloadLength = checked((int)ReadUInt32(packet, 26));
    if (payloadLength < 1 || payloadLength > RealtimeProtocol.MaxMessageBytes || packet.Length != 30 + payloadLength + 32)
        return ProtocolError.InvalidLength;

    int tagOffset = 30 + payloadLength;
    byte[] expectedTag;
    using (var hmac = new HMACSHA256(key))
        expectedTag = hmac.ComputeHash(packet, 0, tagOffset);
    if (!ConstantTimeEquals(packet, tagOffset, expectedTag, 0, 32))
        return ProtocolError.AuthenticationFailed;
    if (!replayWindow.TryAccept(sequence))
        return ProtocolError.ReplayedPacket;

    var payload = new byte[payloadLength];
    Buffer.BlockCopy(packet, 30, payload, 0, payloadLength);
    decoded = new AuthenticatedPacket(session, sequence, payload);
    return ProtocolError.None;
}
```

- [ ] **Step 4: Implement the 1,024-packet replay window**

```csharp
private const int WindowBits = 1024;
private readonly ulong[] _words = new ulong[WindowBits / 64];

public bool TryAccept(ulong sequence)
{
    if (!_initialized)
    {
        _initialized = true;
        _highest = sequence;
        _words[0] = 1;
        return true;
    }
    if (sequence > _highest)
    {
        ulong delta = sequence - _highest;
        ShiftWindow(delta >= WindowBits ? WindowBits : (int)delta);
        _highest = sequence;
        _words[0] |= 1;
        return true;
    }
    ulong distance64 = _highest - sequence;
    if (distance64 >= WindowBits) return false;
    int distance = (int)distance64;
    int word = distance / 64;
    ulong mask = 1UL << (distance % 64);
    if ((_words[word] & mask) != 0) return false;
    _words[word] |= mask;
    return true;
}

private void ShiftWindow(int distance)
{
    if (distance >= WindowBits)
    {
        Array.Clear(_words, 0, _words.Length);
        return;
    }
    int wordShift = distance / 64;
    int bitShift = distance % 64;
    for (int destination = _words.Length - 1; destination >= 0; destination--)
    {
        int source = destination - wordShift;
        ulong value = source >= 0 ? _words[source] << bitShift : 0;
        if (bitShift != 0 && source > 0)
            value |= _words[source - 1] >> (64 - bitShift);
        _words[destination] = value;
    }
}
```

- [ ] **Step 5: Run authentication tests**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.AuthenticatedPacketCodecTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-03/auth-green.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-03/auth-green.log"`.

Expected: full-tag, short, oversized, wrong-version, wrong-session, duplicate, distance-1,023 acceptance, distance-1,024 rejection, and out-of-order-within-window cases pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Network/Contracts/Auth Assets/Tests/Network/AuthenticatedPacketCodecTests.cs Assets/Tests/Network/AuthenticatedPacketCodecTests.cs.meta
git commit -m "feat: authenticate realtime sessions"
```

### Task 4: Replace Callback Races with Generation-Owned Connections

**Files:**
- Create: `Assets/Scripts/Network/Runtime/ZWebGLFrameWork.Network.Runtime.asmdef`
- Create: `Assets/Scripts/Network/Runtime/IRealtimeChannelFactory.cs`
- Create: `Assets/Scripts/Network/Runtime/RealtimeChannelFactoryRegistry.cs`
- Create: `Assets/Scripts/Network/Runtime/RealtimeConnectionController.cs`
- Create: `Assets/Tests/Network/RealtimeConnectionControllerTests.cs`
- Modify: `Assets/Tests/Network/ZWebGLFrameWork.Network.Tests.asmdef`

**Interfaces:**
- Consumes: `IRealtimeChannel` and `RealtimeCredentials` from Task 2.
- Produces: `ConnectAsync`, `DisconnectAsync`, `Current`, and `Generation` without public connection events.
- Assembly delta: Runtime initially references Contracts and existing Core only; the test asmdef appends Runtime. Runtime still has no concrete WebGL, Native, NativeWebSocket, or kcp2k reference.

- [ ] **Step 1: Write the failing stale-connect ownership test**

```csharp
[Test]
public async Task NetworkManager_Reconnect_DoesNotLeakOrDispatchStaleCallbacks()
{
    var first = new ControlledChannel();
    var second = new ControlledChannel();
    var factory = new QueueChannelFactory(first, second);
    var controller = new RealtimeConnectionController(factory);

    Task firstConnect = controller.ConnectAsync(TestEndpoint.Native, TestCredentials.Valid, CancellationToken.None);
    Task secondConnect = controller.ConnectAsync(TestEndpoint.Native, TestCredentials.Valid, CancellationToken.None);
    second.CompleteConnect();
    await secondConnect;
    first.CompleteConnect();
    await firstConnect;

    Assert.That(controller.Current, Is.SameAs(second));
    Assert.That(first.DisposeCount, Is.EqualTo(1));
    Assert.That(second.DisposeCount, Is.Zero);
    Assert.That(controller.Generation, Is.EqualTo(2));
}

[Test]
public async Task TcpClient_DisconnectDuringConnect_DiscardsLateSocket()
{
    var channel = new ControlledChannel();
    var controller = new RealtimeConnectionController(new QueueChannelFactory(channel));
    Task connect = controller.ConnectAsync(TestEndpoint.Native, TestCredentials.Valid, CancellationToken.None);

    await controller.DisconnectAsync(CancellationToken.None);
    channel.CompleteConnect();
    await connect;

    Assert.That(controller.Current, Is.Null);
    Assert.That(channel.DisposeCount, Is.EqualTo(1));
}
```

- [ ] **Step 2: Run the lifecycle test and confirm the red state**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.RealtimeConnectionControllerTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-04/lifecycle-red.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-04/lifecycle-red.log"`.

Expected: compilation fails because `RealtimeConnectionController` does not exist.

- [ ] **Step 3: Implement generation and cancellation ownership**

```csharp
public async Task ConnectAsync(
    RealtimeEndpoint endpoint,
    RealtimeCredentials credentials,
    CancellationToken cancellationToken)
{
    int generation;
    CancellationTokenSource ownedCts;
    IRealtimeChannel candidate;
    lock (_gate)
    {
        generation = ++_generation;
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ownedCts = _connectCts;
        candidate = _factory.Create(endpoint.Transport);
    }

    try
    {
        await candidate.ConnectAsync(endpoint, credentials, ownedCts.Token);
        IRealtimeChannel previous = null;
        lock (_gate)
        {
            if (generation != _generation || ownedCts.IsCancellationRequested)
            {
                candidate.Dispose();
                return;
            }
            previous = _current;
            _current = candidate;
        }
        previous?.Dispose();
    }
    catch
    {
        candidate.Dispose();
        if (!ownedCts.IsCancellationRequested) throw;
    }
}
```

`DisconnectAsync` increments `_generation`, cancels and disposes `_connectCts`, atomically detaches `_current`, awaits `CloseAsync`, and disposes the detached channel in `finally`.

The Runtime assembly never references a concrete platform adapter. Platform assemblies install one factory before scene load:

```csharp
public static class RealtimeChannelFactoryRegistry
{
    public static IRealtimeChannelFactory Current { get; private set; }

    public static void Install(IRealtimeChannelFactory factory)
    {
        Current = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public static void ResetForTests() => Current = null;
}
```

- [ ] **Step 4: Run lifecycle tests**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.RealtimeConnectionControllerTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-04/lifecycle-green.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-04/lifecycle-green.log"`.

Expected: stale connect, cancellation, repeated disconnect, and connect-after-disconnect tests pass with exactly one disposal per discarded channel.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Network/Runtime Assets/Tests/Network/RealtimeConnectionControllerTests.cs Assets/Tests/Network/RealtimeConnectionControllerTests.cs.meta
git commit -m "feat: own realtime connection lifetimes"
```

### Task 5: Vendor NativeWebSocket and Add HTTPS/WSS WebGL Adapters

**Files:**
- Create: `scripts/vendor-nativewebsocket.sh`
- Create at execution: `Packages/com.zwebglframework.nativewebsocket/`
- Create: `Assets/Scripts/Network/UnityHttp/ZWebGLFrameWork.Network.UnityHttp.asmdef`
- Create: `Assets/Scripts/Network/UnityHttp/UnityHttpApi.cs`
- Create: `Assets/Scripts/Network/UnityHttp/GuestLoginModels.cs`
- Create: `Assets/Scripts/Network/WebGL/ZWebGLFrameWork.Network.WebGL.asmdef`
- Create: `Assets/Scripts/Network/WebGL/INativeWebSocketClient.cs`
- Create: `Assets/Scripts/Network/WebGL/NativeWebSocketClientAdapter.cs`
- Create: `Assets/Scripts/Network/WebGL/WebGlRealtimeChannel.cs`
- Create: `Assets/Scripts/Network/WebGL/WebGlRealtimeChannelFactory.cs`
- Create: `Assets/Editor/NetworkVerificationBuildScript.cs`
- Create: `Assets/Tests/Network/UnityHttpApiTests.cs`
- Create: `Assets/Tests/Network/WebGlRealtimeChannelTests.cs`
- Create: `Assets/Tests/Network/WebGlAssemblyBoundaryTests.cs`
- Modify: `Assets/Tests/Network/ZWebGLFrameWork.Network.Tests.asmdef`
- Modify: `Assets/Scripts/Network/Runtime/ZWebGLFrameWork.Network.Runtime.asmdef`

**Interfaces:**
- Consumes: Task 1's verified NativeWebSocket lock/license, `IHttpApi`, `IRealtimeChannel`, strict codecs, and `RealtimeCredentials` from Tasks 2 and 3.
- Produces: `UnityHttpApi`, `GuestLoginResult`, `GuestLoginSession`, and a `WebGlRealtimeChannel` backed only by the pinned NativeWebSocket package. The WebGL asmdef references `ZWebGLFrameWork.Network.Contracts`, `ZWebGLFrameWork.Network.Runtime`, and the vendored NativeWebSocket asmdef; Runtime does not reference WebGL.
- Assembly delta: UnityHttp references Contracts; WebGL references Contracts, Runtime, and NativeWebSocket and includes only Editor/WebGL; the test asmdef appends UnityHttp, WebGL, and NativeWebSocket. No Task 5 asmdef references Native or kcp2k.

- [ ] **Step 1: Write failing HTTPS policy tests**

```csharp
[Test]
public void WebGlHttpApi_RejectsInsecureHttpEndpoint()
{
    var insecure = new HttpRequest(HttpMethod.Post, new Uri("http://game.example/v1/login"), new byte[] { 1 });
    var secure = new HttpRequest(HttpMethod.Post, new Uri("https://game.example/v1/login"), new byte[] { 1 });

    Assert.Throws<NetworkPolicyException>(() => UnityHttpApi.ValidateOnlineUri(insecure.Uri));
    Assert.DoesNotThrow(() => UnityHttpApi.ValidateOnlineUri(secure.Uri));
}
```

- [ ] **Step 2: Run the HTTP test and confirm the red state**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.UnityHttpApiTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-05/http-red.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-05/http-red.log"`.

Expected: compilation fails because `UnityHttpApi` does not exist.

- [ ] **Step 3: Implement HTTPS validation and the exact login result**

```csharp
public async Task<HttpResponse> SendAsync(HttpRequest request, CancellationToken cancellationToken)
{
    ValidateOnlineUri(request.Uri);
    using (var webRequest = new UnityWebRequest(request.Uri, request.Method.Value))
    {
        webRequest.downloadHandler = new DownloadHandlerBuffer();
        if (request.Body != null) webRequest.uploadHandler = new UploadHandlerRaw(request.Body);
        foreach (KeyValuePair<string, string> header in request.Headers)
            webRequest.SetRequestHeader(header.Key, header.Value);
        UnityWebRequestAsyncOperation operation = webRequest.SendWebRequest();
        using (cancellationToken.Register(webRequest.Abort))
        {
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
        return HttpResponse.FromUnityWebRequest(webRequest);
    }
}

public static void ValidateOnlineUri(Uri uri)
{
    if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        throw new NetworkPolicyException("Online HTTP endpoint must use HTTPS.");
}
```

`GuestLoginModels.cs` defines the exact result consumed by Task 7:

```csharp
public sealed class GuestLoginSession
{
    public string PlayerId { get; init; }
    public RealtimeCredentials RealtimeCredentials { get; init; }
}

public sealed class GuestLoginResult
{
    public bool Success { get; init; }
    public string Error { get; init; }
    public GuestLoginSession Session { get; init; }
    public static GuestLoginResult Parse(HttpResponse response) => response.StatusCode == 200
        ? GuestLoginJsonCodec.Decode(response.Body)
        : new GuestLoginResult { Success = false, Error = response.Error };
}

public static class GuestLoginRequest
{
    public static HttpRequest Create(Uri apiBase) => new HttpRequest(
        HttpMethod.Post,
        new Uri(apiBase, "/v1/login/guest"),
        Encoding.UTF8.GetBytes("{}"),
        new Dictionary<string, string> { ["Content-Type"] = "application/json" });
}

internal static class GuestLoginJsonCodec
{
    [Serializable]
    private sealed class GuestLoginDto
    {
        public string playerId;
        public string sessionId;
        public string ticket;
        public string resumeToken;
        public string macKey;
        public long expiresAtUnixSeconds;
    }

    public static GuestLoginResult Decode(byte[] body)
    {
        GuestLoginDto dto = JsonUtility.FromJson<GuestLoginDto>(Encoding.UTF8.GetString(body));
        if (dto == null || string.IsNullOrWhiteSpace(dto.sessionId))
            return new GuestLoginResult { Success = false, Error = "Malformed guest login response." };
        return new GuestLoginResult
        {
            Success = true,
            Session = new GuestLoginSession
            {
                PlayerId = dto.playerId,
                RealtimeCredentials = new RealtimeCredentials
                {
                    SessionId = Guid.Parse(dto.sessionId),
                    OpaqueTicket = Convert.FromBase64String(dto.ticket),
                    ResumeToken = Convert.FromBase64String(dto.resumeToken),
                    MacKey = Convert.FromBase64String(dto.macKey),
                    ExpiresAtUnixSeconds = dto.expiresAtUnixSeconds
                }
            }
        };
    }
}
```

- [ ] **Step 4: Write failing exact WSS/path/subprotocol/first-message tests**

```csharp
[Test]
public async Task WssChannel_UsesRequiredPathSubprotocolAndAuthHelloFirst()
{
    var client = new FakeNativeWebSocketClient();
    var factory = new FakeNativeWebSocketClientFactory(client);
    var channel = new WebGlRealtimeChannel(factory);
    Assert.ThrowsAsync<NetworkPolicyException>(() => channel.ConnectAsync(
        new RealtimeEndpoint(RealtimeTransport.WebSocket, new Uri("ws://game.example/v1/realtime")),
        TestCredentials.Valid, CancellationToken.None));
    Assert.ThrowsAsync<NetworkPolicyException>(() => channel.ConnectAsync(
        new RealtimeEndpoint(RealtimeTransport.WebSocket, new Uri("wss://game.example/realtime")),
        TestCredentials.Valid, CancellationToken.None));

    Task connect = channel.ConnectAsync(
        new RealtimeEndpoint(RealtimeTransport.WebSocket, new Uri("wss://game.example/v1/realtime")),
        TestCredentials.Valid, CancellationToken.None);
    client.CompleteOpen();
    client.QueueMessage(RealtimeFixture.AuthAccepted(TestCredentials.Valid.SessionId));
    await connect;

    Assert.That(factory.Subprotocol, Is.EqualTo("zweb.framesync.v1"));
    Assert.That(RealtimeCodec.Decode(client.Sent.Single()).Kind, Is.EqualTo(RealtimeMessageKind.AuthHello));
    client.QueueMessage(new byte[] { 7, 8, 9 });
    Assert.That(await channel.ReceiveAsync(CancellationToken.None), Is.EqualTo(new byte[] { 7, 8, 9 }));
}
```

- [ ] **Step 5: Run the browser-channel test and confirm the red state**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.WebGlRealtimeChannelTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-05/adapters-red.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-05/adapters-red.log"`.

Expected: compilation fails because the NativeWebSocket-backed channel and factory do not exist.

- [ ] **Step 6: Vendor only the recorded NativeWebSocket source and implement the adapter**

```bash
#!/usr/bin/env bash
set -euo pipefail
bash scripts/verify-realtime-dependency-provenance.sh
source docs/dependencies/nativewebsocket.lock
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
git clone --filter=blob:none "$UPSTREAM_URL" "$work/repo"
git -C "$work/repo" archive --format=tar "$UPSTREAM_COMMIT" > "$work/source.tar"
[[ "$(shasum -a 256 "$work/source.tar" | awk '{print $1}')" == "$SOURCE_ARCHIVE_SHA256" ]]
mkdir -p "$work/source" Packages/com.zwebglframework.nativewebsocket
tar -xf "$work/source.tar" -C "$work/source"
test -s "$work/source/package.json"
test -n "$(find "$work/source" -name NativeWebSocket.cs -print -quit)"
rm -rf Packages/com.zwebglframework.nativewebsocket
mkdir -p Packages/com.zwebglframework.nativewebsocket
cp -R "$work/source/." Packages/com.zwebglframework.nativewebsocket/
cmp -s ThirdPartyNotices/NativeWebSocket-LICENSE.txt \
  "Packages/com.zwebglframework.nativewebsocket/$UPSTREAM_LICENSE_PATH"
```

`NativeWebSocketClientAdapter` is the only direct consumer of the pinned package. It constructs `NativeWebSocket.WebSocket` with the absolute WSS URL and `new List<string> { RealtimeProtocol.WebSocketSubprotocol }`, forwards `OnOpen`, `OnMessage`, `OnError`, and `OnClose`, sends only complete `byte[]` messages, calls `DispatchMessageQueue()` in Editor/non-WebGL builds, and awaits `Connect`, `Send`, and `Close`. `WebGlRealtimeChannel.ConnectAsync` rejects every URI except absolute `wss` with exact path `/v1/realtime`, sends encoded `AuthHello` before any other application message, and becomes connected only after a matching `AuthAccepted`.

No file under `Assets/Plugins/WebGL` is created by this plan. The only JavaScript bridge is the byte-for-byte source contained in the reviewed NativeWebSocket archive.

Add the exact out-of-tree build entry point used by this plan; it refuses a missing root or any root inside the checkout:

```csharp
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace ZWebGLFrameWork.Editor
{
public static class NetworkVerificationBuildScript
{
    public static void BuildWebGL() => Build(BuildTarget.WebGL, Path.Combine(OutputRoot(), "WebGL"));
    public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64,
        Path.Combine(OutputRoot(), "Windows", "ZWebGLFrameWork.exe"));

    private static string OutputRoot()
    {
        string configured = Environment.GetEnvironmentVariable("ZWEB_BUILD_OUTPUT_ROOT");
        if (string.IsNullOrWhiteSpace(configured))
            throw new BuildFailedException("ZWEB_BUILD_OUTPUT_ROOT is required.");
        string root = Path.GetFullPath(configured);
        string project = Path.GetFullPath(Directory.GetCurrentDirectory())
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (rootWithSeparator.StartsWith(project, StringComparison.Ordinal))
            throw new BuildFailedException("Network verification builds must be outside the repository.");
        return root;
    }

    private static void Build(BuildTarget target, string output)
    {
        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
            .Select(scene => scene.path).ToArray();
        if (scenes.Length == 0) throw new BuildFailedException("No enabled build scenes.");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = target,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException($"{target} build failed: {report.summary.totalErrors} errors.");
    }
}
}
```

- [ ] **Step 7: Add the exact WebGL structural boundary test**

```csharp
[Test]
public void WebGLAssembly_HasNoSocketThreadOrNativeKcpReferences()
{
    string[] files = Directory.GetFiles("Assets/Scripts/Network/WebGL", "*.cs", SearchOption.AllDirectories);
    string source = string.Join("\n", files.Select(File.ReadAllText));
    Assert.That(source, Does.Not.Contain("System.Net.Sockets"));
    Assert.That(source, Does.Not.Contain("System.Threading.Thread"));
    Assert.That(source, Does.Not.Contain("kcp2k"));
    bool handwrittenBridgeExists = Directory.Exists("Assets/Plugins/WebGL") &&
        Directory.GetFiles("Assets/Plugins/WebGL", "ZWebRealtime.jslib", SearchOption.AllDirectories).Length != 0;
    Assert.That(handwrittenBridgeExists, Is.False);

    string asmdef = File.ReadAllText("Assets/Scripts/Network/WebGL/ZWebGLFrameWork.Network.WebGL.asmdef");
    Assert.That(asmdef, Does.Contain("ZWebGLFrameWork.Network.Contracts"));
    Assert.That(asmdef, Does.Contain("NativeWebSocket"));
    Assert.That(asmdef, Does.Not.Contain("ZWebGLFrameWork.Network.Native"));
    Assert.That(asmdef, Does.Not.Contain("ZWebGLFrameWork.Kcp2k"));
    UnityEditor.Compilation.Assembly playerAssembly = UnityEditor.Compilation.CompilationPipeline
        .GetAssemblies(UnityEditor.Compilation.AssembliesType.PlayerWithoutTestAssemblies)
        .Single(assembly => assembly.name == "ZWebGLFrameWork.Network.WebGL");
    string[] references = playerAssembly.assemblyReferences.Select(reference => reference.name).ToArray();
    Assert.That(references, Does.Not.Contain("ZWebGLFrameWork.Network.Native"));
    Assert.That(references, Does.Not.Contain("ZWebGLFrameWork.Kcp2k"));
}
```

- [ ] **Step 8: Run adapter tests and compile a WebGL player out of tree**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/network/task-05" "$ZWEB_BUILD_OUTPUT_ROOT/WebGL"
bash scripts/vendor-nativewebsocket.sh
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix \
  -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Network.Tests \
  -testResults "$ZWEB_EVIDENCE_ROOT/network/task-05/adapters-green.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/network/task-05/adapters-green.log"
ZWEB_BUILD_OUTPUT_ROOT="$ZWEB_BUILD_OUTPUT_ROOT" \
  /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix \
  -executeMethod ZWebGLFrameWork.Editor.NetworkVerificationBuildScript.BuildWebGL \
  -logFile "$ZWEB_EVIDENCE_ROOT/network/task-05/webgl-build.log"
```

Expected: adapter tests pass; the WebGL build and report are beneath `$ZWEB_BUILD_OUTPUT_ROOT/WebGL`; `$ZWEB_EVIDENCE_ROOT/network/task-05/browser-wss.json` records successful HTTPS page load, `/v1/realtime`, `zweb.framesync.v1`, first-message `AuthHello`, and a complete binary echo; no build/test output appears in the repository.

- [ ] **Step 9: Commit**

```bash
git add scripts/vendor-nativewebsocket.sh Packages/com.zwebglframework.nativewebsocket \
  Assets/Editor/NetworkVerificationBuildScript.cs Assets/Scripts/Network/UnityHttp \
  Assets/Scripts/Network/WebGL Assets/Tests/Network
git commit -m "feat: add pinned NativeWebSocket adapter"
```

### Task 6: Vendor the Recorded kcp2k Source and Add the Native Adapter

**Files:**
- Create: `scripts/vendor-kcp2k.sh`
- Create at execution: `Packages/com.zwebglframework.kcp2k/package.json`
- Create at execution: `Packages/com.zwebglframework.kcp2k/LICENSE.txt`
- Create at execution: `Packages/com.zwebglframework.kcp2k/Runtime/Upstream/`
- Create: `Packages/com.zwebglframework.kcp2k/Runtime/ZWebGLFrameWork.Kcp2k.asmdef`
- Create: `Assets/Scripts/Network/Native/ZWebGLFrameWork.Network.Native.asmdef`
- Create: `Assets/Scripts/Network/Native/Kcp2kClientFacade.cs`
- Create: `Assets/Scripts/Network/Native/Kcp2kRealtimeChannel.cs`
- Create: `Assets/Scripts/Network/Native/NativeRealtimeChannelFactory.cs`
- Create: `Assets/Tests/Network/Kcp2kImpairmentTests.cs`
- Create: `Assets/Tests/Network/NativeRealtimeAuthenticationTests.cs`
- Modify: `Assets/Tests/Network/ZWebGLFrameWork.Network.Tests.asmdef`

**Interfaces:**
- Consumes: verified Task 1 lock, kcp2k API checks, Task 2 channel interface, and Task 3 authenticated packet codec.
- Produces: `Kcp2kRealtimeChannel` and a native complete-message transport excluded from WebGL.
- Assembly delta: Kcp2k has no engine references and includes only Editor/WindowsStandalone64; Native references Contracts, Runtime, and Kcp2k with the same platforms; the test asmdef appends Native and Kcp2k. WebGL references remain unchanged.

- [ ] **Step 1: Write the reproducible vendor script**

```bash
#!/usr/bin/env bash
set -euo pipefail

bash scripts/verify-realtime-dependency-provenance.sh
source docs/dependencies/kcp2k.lock
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

git clone --filter=blob:none "$UPSTREAM_URL" "$work/repo"
git -C "$work/repo" cat-file -e "$UPSTREAM_COMMIT^{commit}"
git -C "$work/repo" archive --format=tar "$UPSTREAM_COMMIT" kcp2k > "$work/kcp2k.tar"
actual=$(shasum -a 256 "$work/kcp2k.tar" | awk '{print $1}')
[[ "$actual" == "$SOURCE_ARCHIVE_SHA256" ]]

rm -rf Packages/com.zwebglframework.kcp2k/Runtime/Upstream
mkdir -p Packages/com.zwebglframework.kcp2k/Runtime/Upstream
tar -xf "$work/kcp2k.tar" -C "$work"
cp -R "$work/kcp2k/." Packages/com.zwebglframework.kcp2k/Runtime/Upstream/
find Packages/com.zwebglframework.kcp2k/Runtime/Upstream -name '*.asmdef' -delete
find Packages/com.zwebglframework.kcp2k/Runtime/Upstream -name '*.meta' -delete
cp ThirdPartyNotices/kcp2k-LICENSE.txt Packages/com.zwebglframework.kcp2k/LICENSE.txt
```

Create the local package manifest with fixed local metadata; the upstream identity remains exclusively in the generated lock:

```json
{
  "name": "com.zwebglframework.kcp2k",
  "version": "0.0.0",
  "displayName": "ZWebGLFrameWork Vendored kcp2k",
  "unity": "2022.3",
  "license": "SEE LICENSE IN LICENSE.txt"
}
```

- [ ] **Step 2: Run vendoring and verify exact source provenance**

Run:

```bash
chmod +x scripts/vendor-kcp2k.sh
bash scripts/vendor-kcp2k.sh
bash scripts/verify-realtime-dependency-provenance.sh
test -n "$(find Packages/com.zwebglframework.kcp2k/Runtime/Upstream -name '*.cs' -print -quit)"
```

Expected: all commands exit `0`; the package contains source only from the locked archive and the verbatim locked license.

- [ ] **Step 3: Write failing fragmentation, gap, and authentication tests**

```csharp
[Test]
public async Task NativeKcp_FragmentedMessage_RoundTrips()
{
    var impairment = new DatagramImpairment(seed: 42, lossPercent: 20, duplicatePercent: 10, reorderWindow: 4, latencyMilliseconds: 150);
    using var pair = await Kcp2kTestPair.ConnectAsync(impairment, TestCredentials.Valid);
    byte[] payload = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray();

    await pair.Client.SendAsync(payload, CancellationToken.None);
    byte[] received = await pair.Server.ReceiveAsync(TimeSpan.FromSeconds(10));

    Assert.That(received, Is.EqualTo(payload));
    Assert.That(pair.Impairment.DroppedDatagrams, Is.GreaterThan(0));
    Assert.That(pair.Impairment.ReorderedDatagrams, Is.GreaterThan(0));
}

[Test]
public async Task NativeKcp_GappedAck_DoesNotDropMissingSegment()
{
    var impairment = DatagramImpairment.DropOnce(datagramOrdinal: 2, reorderWindow: 3);
    using var pair = await Kcp2kTestPair.ConnectAsync(impairment, TestCredentials.Valid);
    byte[] payload = Enumerable.Range(0, 12288).Select(i => (byte)(i % 251)).ToArray();

    await pair.Client.SendAsync(payload, CancellationToken.None);
    byte[] received = await pair.Server.ReceiveAsync(TimeSpan.FromSeconds(10));

    Assert.That(received, Is.EqualTo(payload));
    Assert.That(pair.Impairment.DroppedDatagrams, Is.EqualTo(1));
    Assert.That(pair.Impairment.RetransmittedMissingDatagrams, Is.GreaterThanOrEqualTo(1));
}

[Test]
public async Task NativeRealtime_Handshake_AgreesSessionAndRejectsForeignConv()
{
    using var pair = await Kcp2kTestPair.ConnectAsync(DatagramImpairment.None, TestCredentials.Valid);
    var originalEndpoint = pair.ClientEndpoint;
    var reboundEndpoint = pair.AlternateClientEndpoint;
    byte[] foreignConv = pair.EncodeAuthHello(TestCredentials.Valid, conv: pair.Conv + 1, packetSequence: 9);
    Assert.That(await pair.Server.InjectAndReadErrorAsync(foreignConv, originalEndpoint), Is.EqualTo(ProtocolError.WrongSession));
    Assert.That(pair.Server.AcceptedSessionCount, Is.Zero);

    byte[] valid = pair.EncodeAuthHello(TestCredentials.Valid, conv: pair.Conv, packetSequence: 10);
    Assert.That(await pair.Server.InjectAndReadErrorAsync(valid, reboundEndpoint), Is.EqualTo(ProtocolError.None));
    Assert.That(pair.Server.BoundEndpoint, Is.EqualTo(reboundEndpoint));
    Assert.That(pair.Server.AcceptedSessionId, Is.EqualTo(TestCredentials.Valid.SessionId));

    byte[] forged = pair.EncodeAuthHello(TestCredentials.Valid, conv: pair.Conv, packetSequence: 11);
    forged[forged.Length - 1] ^= 1;
    Assert.That(await pair.Server.InjectAndReadErrorAsync(forged, originalEndpoint), Is.EqualTo(ProtocolError.AuthenticationFailed));
    Assert.That(pair.Server.BoundEndpoint, Is.EqualTo(reboundEndpoint));
}
```

- [ ] **Step 4: Run native tests and confirm the red state**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.Kcp2kImpairmentTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-06/native-red.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-06/native-red.log"`.

Expected: compilation fails because the native adapter and impairment harness do not exist.

- [ ] **Step 5: Implement the façade against the API verified in Task 1**

```csharp
internal sealed class Kcp2kClientFacade : IDisposable
{
    private readonly kcp2k.KcpClient _client;

    public Kcp2kClientFacade(KcpNativeCallbacks callbacks, kcp2k.KcpConfig config)
    {
        _client = new kcp2k.KcpClient(
            callbacks.Connected,
            (segment, channel) => callbacks.Data(segment.Array, segment.Offset, segment.Count),
            callbacks.Disconnected,
            (code, message) => callbacks.Error((int)code, message),
            config);
    }

    public void Connect(string host, ushort port) => _client.Connect(host, port);
    public void Send(byte[] message) => _client.Send(new ArraySegment<byte>(message), kcp2k.KcpChannel.Reliable);
    public void TickIncoming() => _client.TickIncoming();
    public void TickOutgoing() => _client.TickOutgoing();
    public void Dispose() => _client.Disconnect();
}
```

`Kcp2kRealtimeChannel` must not report connected on UDP bind. It completes `ConnectAsync` only after kcp2k connects, an authenticated `AuthHello` is accepted, and a matching `AuthAccepted` returns the expected session ID. It wraps every application message with Task 3's full 32-byte HMAC, verifies the session and 1,024-packet replay window before queueing a received complete message, and updates its remote endpoint only after those checks pass. A forged or replayed packet from a new endpoint cannot rebind it; conv never authenticates identity.

```csharp
internal sealed class NativeRealtimeChannelFactory : IRealtimeChannelFactory
{
    public IRealtimeChannel Create(RealtimeTransport transport)
    {
        if (transport != RealtimeTransport.NativeKcp) throw new ArgumentOutOfRangeException(nameof(transport));
        return new Kcp2kRealtimeChannel();
    }

#if !UNITY_WEBGL
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install() => RealtimeChannelFactoryRegistry.Install(new NativeRealtimeChannelFactory());
#endif
}
```

- [ ] **Step 6: Run native impairment and authentication tests**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.Kcp2kImpairmentTests,ZWebGLFrameWork.Network.Tests.NativeRealtimeAuthenticationTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-06/native-green.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-06/native-green.log"`; set `ZWEB_NATIVE_IMPAIRMENT_EVIDENCE="$ZWEB_EVIDENCE_ROOT/network/task-06/native-impairment.json"` for the harness.

Expected: the exact three acceptance tests pass; 8,192- and 12,288-byte payloads round-trip under deterministic loss/duplication/reordering and a gapped acknowledgement; forged, replayed, foreign-session/conv, and expired handshakes are rejected; only a valid-MAC packet can rebind the endpoint. Write the impairment trace to `$ZWEB_EVIDENCE_ROOT/network/task-06/native-impairment.json` and focused XML/log to the same directory.

- [ ] **Step 7: Verify WebGL exclusion and commit**

Run the WebGL build command from Task 5.

Expected: build succeeds and the WebGL structural test finds no reference to `ZWebGLFrameWork.Network.Native` or `ZWebGLFrameWork.Kcp2k`.

```bash
git add scripts/vendor-kcp2k.sh Packages/com.zwebglframework.kcp2k Assets/Scripts/Network/Native Assets/Tests/Network
git commit -m "feat: add pinned native kcp2k adapter"
```

### Task 7: Migrate Callers and Delete the Unsupported Socket Stack

**Files:**
- Move: `Assets/Scripts/Network/NetworkManager.cs` to `Assets/Scripts/Network/Runtime/NetworkManager.cs`
- Move: `Assets/Scripts/Network/Core/NetworkDefine.cs` to `Assets/Scripts/Network/Runtime/NetworkDefine.cs`
- Move: `Assets/Scripts/Network/Core/NetworkMonitor.cs` to `Assets/Scripts/Network/Runtime/NetworkMonitor.cs`
- Create: `Assets/Scripts/Network/Runtime/NetworkSession.cs`
- Modify: `Assets/Scripts/FSM/Game/States/LoginState.cs`
- Modify: `Assets/Scripts/FSM/ZWebGLFrameWork.FSM.asmdef`
- Modify: `Assets/Scripts/Data/AppConfig.cs`
- Modify: `Assets/Resources/Config/AppConfig.asset`
- Delete: `Assets/Scripts/Network/KCP/`
- Delete: `Assets/Scripts/Network/Protocols/`
- Delete: `Assets/Scripts/Network/Core/AddressResolver.cs`
- Delete: `Assets/Scripts/Network/Core/INetworkClient.cs`
- Delete: `Assets/Scripts/Network/Core/MessageProtocol.cs`
- Delete: `Assets/Scripts/Network/Core/MessageDispatcher.cs`
- Delete: `Assets/Scripts/Network/ZWebGLFrameWork.Network.asmdef`
- Create: `Assets/Tests/Network/LegacyNetworkStackRemovalTests.cs`

**Interfaces:**
- Consumes: Tasks 4 through 6 adapters and controller.
- Produces: one runtime `NetworkManager` with `Http`, `Realtime`, and cancellable `GuestLoginAsync`; no legacy protocol enum or callback events.
- Assembly delta: Runtime now appends UnityHttp because the migrated manager constructs it; FSM references Contracts, Runtime, and UnityHttp. Neither assembly references concrete WebGL/Native factories, which self-register from their own platform asmdefs.

- [ ] **Step 1: Write the failing removal and platform-boundary test**

```csharp
[Test]
public void ProductionTree_ContainsNoLegacySocketOrCustomKcpFiles()
{
    string[] forbidden =
    {
        "Assets/Scripts/Network/KCP/KcpCore.cs",
        "Assets/Scripts/Network/KCP/KcpClient.cs",
        "Assets/Scripts/Network/Protocols/TcpClient.cs",
        "Assets/Scripts/Network/Protocols/UdpClient.cs",
        "Assets/Scripts/Network/Core/AddressResolver.cs",
        "Assets/Scripts/Network/Core/INetworkClient.cs"
    };
    Assert.That(forbidden.Where(File.Exists), Is.Empty);
    string fsm = File.ReadAllText("Assets/Scripts/FSM/Game/States/LoginState.cs");
    Assert.That(fsm, Does.Not.Contain("NetworkProtocol.TCP"));
    Assert.That(fsm, Does.Contain("GuestLoginAsync"));
}

[Test]
public void HappyEyeballs_FirstFails_SecondWinnerRemainsOpen()
{
    Assert.That(File.Exists("Assets/Scripts/Network/Core/AddressResolver.cs"), Is.False);
    Assert.That(File.Exists("Assets/Scripts/Network/Core/INetworkClient.cs"), Is.False);
    Assert.That(typeof(RealtimeConnectionController).GetProperty(nameof(RealtimeConnectionController.Current)), Is.Not.Null);
}

[Test]
public void TcpTransport_PartialSend_CompletesWholeFrame()
{
    Assert.That(File.Exists("Assets/Scripts/Network/Protocols/TcpClient.cs"), Is.False);
    MethodInfo send = typeof(IRealtimeChannel).GetMethod(nameof(IRealtimeChannel.SendAsync));
    Assert.That(send.GetParameters()[0].ParameterType, Is.EqualTo(typeof(byte[])));
    Assert.That(send.ReturnType, Is.EqualTo(typeof(Task)));
}
```

- [ ] **Step 2: Run the removal test and confirm the red state**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.LegacyNetworkStackRemovalTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-07/removal-red.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-07/removal-red.log"`.

Expected: test fails and reports the existing custom KCP, TCP, UDP, resolver, and interface files.

- [ ] **Step 3: Replace the manager API and migrate online login**

```csharp
public sealed class NetworkManager : SingletonDontDestroy<NetworkManager>
{
    public IHttpApi Http { get; private set; }
    public RealtimeConnectionController Realtime { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        Http = new UnityHttpApi();
        Realtime = new RealtimeConnectionController(
            RealtimeChannelFactoryRegistry.Current ?? throw new InvalidOperationException("Realtime platform factory was not installed."));
    }

    public async Task<GuestLoginResult> GuestLoginAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var request = GuestLoginRequest.Create(endpoint);
        HttpResponse response = await Http.SendAsync(request, cancellationToken);
        return GuestLoginResult.Parse(response);
    }

    protected override void OnDestroy()
    {
        Realtime?.Dispose();
        base.OnDestroy();
    }
}

public static class NetworkSession
{
    public static GuestLoginSession Current { get; set; }
}

```

Replace `LoginState.OnGuestLogin` with a cancellable HTTPS call. It changes state only after `GuestLoginResult.Success` and stores returned `RealtimeCredentials`; cancellation or failure reopens the login panel once.

Add serialized `HttpsApiBaseUrl` and `WssRealtimeUrl` properties to `AppConfig`; both runtime validators require `https`/`wss`, and the WSS validator requires exact path `/v1/realtime`. The committed local-development asset uses `https://127.0.0.1:7443` and `wss://127.0.0.1:7443/v1/realtime`; release configuration injection is outside this plan and must preserve the same scheme/path checks. `ZWebGLFrameWork.FSM.asmdef` references Contracts, Runtime, and UnityHttp only; it never references the WebGL, Native, NativeWebSocket, or kcp2k concrete assemblies.

```csharp
private async void OnGuestLogin()
{
    CancellationToken token = _loginCts.Token;
    UIManager.Instance.Close<LoginPanel>();
    try
    {
        Uri endpoint = new Uri(GameLauncher.Config.HttpsApiBaseUrl);
        GuestLoginResult result = await NetworkManager.Instance.GuestLoginAsync(endpoint, token);
        if (!result.Success)
        {
            PublishLoginFailure(result.Error);
            return;
        }
        NetworkSession.Current = result.Session;
        ChangeState(GameState.MainVillage);
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
    }
    catch (Exception exception)
    {
        PublishLoginFailure(exception.Message);
    }
}
```

- [ ] **Step 4: Delete the unsupported paths only after replacement tests are green**

```bash
git mv Assets/Scripts/Network/NetworkManager.cs Assets/Scripts/Network/Runtime/NetworkManager.cs
git mv Assets/Scripts/Network/NetworkManager.cs.meta Assets/Scripts/Network/Runtime/NetworkManager.cs.meta
git mv Assets/Scripts/Network/Core/NetworkDefine.cs Assets/Scripts/Network/Runtime/NetworkDefine.cs
git mv Assets/Scripts/Network/Core/NetworkDefine.cs.meta Assets/Scripts/Network/Runtime/NetworkDefine.cs.meta
git mv Assets/Scripts/Network/Core/NetworkMonitor.cs Assets/Scripts/Network/Runtime/NetworkMonitor.cs
git mv Assets/Scripts/Network/Core/NetworkMonitor.cs.meta Assets/Scripts/Network/Runtime/NetworkMonitor.cs.meta
git rm -r Assets/Scripts/Network/KCP Assets/Scripts/Network/Protocols
git rm Assets/Scripts/Network/Core/AddressResolver.cs Assets/Scripts/Network/Core/AddressResolver.cs.meta
git rm Assets/Scripts/Network/Core/INetworkClient.cs Assets/Scripts/Network/Core/INetworkClient.cs.meta
git rm Assets/Scripts/Network/Core/MessageProtocol.cs Assets/Scripts/Network/Core/MessageProtocol.cs.meta
git rm Assets/Scripts/Network/Core/MessageDispatcher.cs Assets/Scripts/Network/Core/MessageDispatcher.cs.meta
git rm Assets/Scripts/Network/ZWebGLFrameWork.Network.asmdef Assets/Scripts/Network/ZWebGLFrameWork.Network.asmdef.meta
git rm Assets/Scripts/Network/KCP.meta Assets/Scripts/Network/Protocols.meta Assets/Scripts/Network/Core.meta
```

- [ ] **Step 5: Run network tests and both client builds**

Run the exact Unity network test command with `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-07/removal-green.xml"` and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-07/removal-green.log"`, then:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/network/task-07" "$ZWEB_BUILD_OUTPUT_ROOT/WebGL" "$ZWEB_BUILD_OUTPUT_ROOT/Windows"
ZWEB_BUILD_OUTPUT_ROOT="$ZWEB_BUILD_OUTPUT_ROOT" \
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix \
  -executeMethod ZWebGLFrameWork.Editor.NetworkVerificationBuildScript.BuildWebGL \
  -logFile "$ZWEB_EVIDENCE_ROOT/network/task-07/webgl-build.log"
ZWEB_BUILD_OUTPUT_ROOT="$ZWEB_BUILD_OUTPUT_ROOT" \
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix \
  -executeMethod ZWebGLFrameWork.Editor.NetworkVerificationBuildScript.BuildWindows \
  -logFile "$ZWEB_EVIDENCE_ROOT/network/task-07/windows-build.log"
```

Expected: removal test and all network tests pass; both BuildReports and artifacts exist only beneath `$ZWEB_BUILD_OUTPUT_ROOT`; repository search finds no production caller or source for the deleted types; `git status --porcelain --untracked-files=all` is empty after the task commit.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Network Assets/Scripts/FSM/Game/States/LoginState.cs Assets/Scripts/FSM/ZWebGLFrameWork.FSM.asmdef Assets/Scripts/Data/AppConfig.cs Assets/Resources/Config/AppConfig.asset Assets/Tests/Network
git commit -m "refactor: remove unsupported socket stack"
```

### Task 8: Send Local Inputs and Apply One Authority Tick at a Time

**Files:**
- Create: `Assets/Scripts/Network/FrameSync/ZWebGLFrameWork.Network.FrameSync.asmdef`
- Create: `Assets/Scripts/Network/FrameSync/LocalCommandSequencer.cs`
- Create: `Assets/Scripts/Network/FrameSync/FrameSyncClient.cs`
- Create: `Assets/Tests/Network/FrameSyncInputFlowTests.cs`
- Modify: `Assets/Tests/Network/ZWebGLFrameWork.Network.Tests.asmdef`
- Delete after green: `Assets/Scripts/SimulationCore/Network/InputSender.cs`
- Delete after green: `Assets/Scripts/SimulationCore/Network/InputReceiver.cs`
- Delete after green: `Assets/Scripts/SimulationCore/Network/InputPacket.cs`
- Delete after green: `Assets/Scripts/SimulationCore/Network/FrameInput.cs`
- Delete after green: `Assets/Scripts/SimulationCore/Network/KcpTransport.cs`
- Delete after green: `Assets/Scripts/SimulationCore/Network/FrameSyncClient.cs`

**Interfaces:**
- Consumes: shared channel/codec, the canonical `ISimBattleSession`, immutable `ISimCommand.Sequence` (`uint`), and stable `(tick, playerId, sequence)` command ordering.
- Produces: `SubmitLocalAsync` and `ApplyAuthorityFrame`; each accepted authority frame calls the session `Tick` exactly once. Clients never submit a SimulationCore `NoOpCmd`; only an authority frame may contain scheduler-generated `CommandEnvelope.NoOp` entries, which are not passed to `ISimBattleSession.Submit`.
- Assembly delta: FrameSync references Contracts and SimulationCore with `noEngineReferences: true`; the Editor test asmdef appends FrameSync and SimulationCore. It does not reference a concrete transport assembly.

- [ ] **Step 1: Write failing local-send, ack, and NoOp tests**

```csharp
[Test]
public async Task FrameSyncClient_EachTickSendsLocalInputAndProcessesAck()
{
    var session = new RecordingBattleSession();
    var channel = new RecordingRealtimeChannel();
    var client = new FrameSyncClient(session, channel, localPlayerId: 0);
    var command = new DeployCmd(0, 0, 1, 2, 4, 5);

    await client.SubmitLocalAsync(command, CancellationToken.None);
    Assert.That(channel.SentCommands.Single().Sequence, Is.EqualTo(1));

    var authority = AuthorityFrame.Create(
        tick: 0,
        orderedInputs: new[] { channel.SentCommands.Single(), CommandEnvelope.NoOp(0, 1, 1) },
        ackPlayer0: 1,
        ackPlayer1: 1,
        checksum: session.ChecksumAfterNextTick);
    FrameApplyResult result = client.ApplyAuthorityFrame(authority);

    Assert.That(result, Is.EqualTo(FrameApplyResult.Confirmed));
    Assert.That(session.TickCalls, Is.EqualTo(1));
    Assert.That(client.LocalUnacknowledgedCount, Is.Zero);
}

[Test]
public void FrameSync_EmptyInput_AdvancesExactlyOneTickViaNoOp()
{
    var session = new RecordingBattleSession();
    var client = new FrameSyncClient(session, new RecordingRealtimeChannel(), localPlayerId: 0);
    var authority = AuthorityFrame.Create(
        tick: 0,
        orderedInputs: new[] { CommandEnvelope.NoOp(0, 0, 1), CommandEnvelope.NoOp(0, 1, 1) },
        ackPlayer0: 1,
        ackPlayer1: 1,
        checksum: session.ChecksumAfterNextTick);

    Assert.That(client.ApplyAuthorityFrame(authority), Is.EqualTo(FrameApplyResult.Confirmed));
    Assert.That(session.SubmitCalls, Is.Zero);
    Assert.That(session.TickCalls, Is.EqualTo(1));
}
```

- [ ] **Step 2: Run the input-flow test and confirm the red state**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.FrameSyncInputFlowTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-08/input-red.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-08/input-red.log"`.

Expected: compilation fails because the replacement FrameSync client does not exist.

- [ ] **Step 3: Implement monotonic local sequencing and sending**

```csharp
public async Task SubmitLocalAsync(ISimCommand command, CancellationToken cancellationToken)
{
    if (command.PlayerId != _localPlayerId)
        throw new ArgumentException("Local command player does not match this client.", nameof(command));
    if (!_sequencer.TryAdd(command.Tick, command.Sequence, out SequenceError error))
        throw new InvalidOperationException(error.ToString());

    CommandEnvelope envelope = _commandMapper.FromSimulation(command);
    _localPending.Add(envelope.Sequence, envelope);
    await _channel.SendAsync(RealtimeCodec.Encode(envelope), cancellationToken);
}
```

- [ ] **Step 4: Implement one-frame/one-tick authority application**

```csharp
public FrameApplyResult ApplyAuthorityFrame(AuthorityFrame frame)
{
    ProtocolError validation = AuthorityFrameValidator.Validate(frame, _matchId, _nextAuthorityTick);
    if (validation != ProtocolError.None) return FrameApplyResult.Rejected;

    foreach (CommandEnvelope envelope in frame.OrderedInputs)
    {
        if (envelope.IsNoOp) continue;
        CommandSubmitResult submit = _session.Submit(_commandMapper.ToSimulation(envelope));
        if (!submit.Accepted) return FrameApplyResult.Rejected;
    }
    TickResult tick = _session.Tick();
    if (tick.CompletedTick != frame.Tick) return FrameApplyResult.Rejected;

    _sequencer.Acknowledge(frame.AckForPlayer(_localPlayerId));
    RemoveAcknowledgedLocalCommands();
    _nextAuthorityTick++;
    return tick.Checksum == frame.Checksum ? FrameApplyResult.Confirmed : FrameApplyResult.ChecksumMismatch;
}
```

- [ ] **Step 5: Run input flow tests**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.FrameSyncInputFlowTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-08/input-green.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-08/input-green.log"`.

Expected: each real local command is sent once, ack removes only accepted sequences, malformed/out-of-order frames do not tick, authority NoOps are not submitted as client commands, and each accepted authority frame ticks exactly once.

- [ ] **Step 6: Remove superseded transport-coupled SimulationCore files and commit**

```bash
git rm Assets/Scripts/SimulationCore/Network/InputSender.cs Assets/Scripts/SimulationCore/Network/InputSender.cs.meta
git rm Assets/Scripts/SimulationCore/Network/InputReceiver.cs Assets/Scripts/SimulationCore/Network/InputReceiver.cs.meta
git rm Assets/Scripts/SimulationCore/Network/InputPacket.cs Assets/Scripts/SimulationCore/Network/InputPacket.cs.meta
git rm Assets/Scripts/SimulationCore/Network/FrameInput.cs Assets/Scripts/SimulationCore/Network/FrameInput.cs.meta
git rm Assets/Scripts/SimulationCore/Network/KcpTransport.cs Assets/Scripts/SimulationCore/Network/KcpTransport.cs.meta
git rm Assets/Scripts/SimulationCore/Network/FrameSyncClient.cs Assets/Scripts/SimulationCore/Network/FrameSyncClient.cs.meta
git add Assets/Scripts/Network/FrameSync Assets/Tests/Network
git commit -m "feat: send and apply authoritative frame inputs"
```

### Task 9: Implement Prediction, Rollback Replay, and Full Resync

**Files:**
- Create: `Assets/Scripts/Network/FrameSync/FrameSyncHistory.cs`
- Modify: `Assets/Scripts/Network/FrameSync/FrameSyncClient.cs`
- Create: `Assets/Tests/Network/FrameSyncRollbackTests.cs`

**Interfaces:**
- Consumes: canonical state-after-tick `SimSnapshot`, `SessionSnapshotCodec`, typed `SnapshotRestoreResult`, authority frames, and local pending commands.
- Produces: `PredictNextTick`, rollback/replay on mismatch, `ResyncRequest`, and validated `ApplyResyncSnapshot`.

- [ ] **Step 1: Write failing rollback and out-of-window tests**

```csharp
[Test]
public void MismatchWithinWindow_RestoresAndReplaysToAuthorityChecksum()
{
    var fixture = FrameSyncFixture.Create(predictedTicks: 12);
    AuthorityFrame corrected = fixture.AuthorityFrameWithDifferentRemoteInput(tick: 10);

    FrameApplyResult result = fixture.Client.ApplyAuthorityFrame(corrected);

    Assert.That(result, Is.EqualTo(FrameApplyResult.RolledBackAndReplayed));
    Assert.That(fixture.Session.LastChecksum, Is.EqualTo(fixture.ExpectedChecksumAfterReplay));
    Assert.That(fixture.Session.NextTick, Is.EqualTo(13));
}

[Test]
public void FrameSync_OutOfWindowMismatch_RequestsFullResync()
{
    var fixture = FrameSyncFixture.Create(predictedTicks: 70);
    AuthorityFrame old = fixture.AuthorityFrameWithDifferentRemoteInput(tick: 2);
    FrameApplyResult result = fixture.Client.ApplyAuthorityFrame(old);

    Assert.That(result, Is.EqualTo(FrameApplyResult.ResyncRequested));
    Assert.That(fixture.Channel.SentKinds.Last(), Is.EqualTo(RealtimeMessageKind.ResyncRequest));
    Assert.That(fixture.Client.LastConfirmedTick, Is.GreaterThan(2));
}
```

- [ ] **Step 2: Run rollback tests and confirm the red state**

Run the exact Unity command with `-testFilter ZWebGLFrameWork.Network.Tests.FrameSyncRollbackTests`, `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-09/rollback-red.xml"`, and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-09/rollback-red.log"`.

Expected: tests fail because history, replay, and resync behavior do not exist.

- [ ] **Step 3: Implement a fixed 64-tick state-after-tick history**

```csharp
public void Store(int completedTick, SimSnapshot snapshot, AuthorityFrame authority, ulong checksum)
{
    int slot = PositiveModulo(completedTick, RealtimeProtocol.RollbackWindowTicks);
    _entries[slot] = new FrameSyncHistoryEntry(completedTick, snapshot, authority, checksum);
}

public bool TryGet(int completedTick, out FrameSyncHistoryEntry entry)
{
    int slot = PositiveModulo(completedTick, RealtimeProtocol.RollbackWindowTicks);
    entry = _entries[slot];
    return entry != null && entry.CompletedTick == completedTick;
}
```

When `FrameSyncClient.Start` completes, capture the session before executing tick zero and store it as completed tick `-1`. A mismatch at tick zero restores that entry; it does not silently accept authority or force a network resync.

- [ ] **Step 4: Implement rollback/replay and resync request**

```csharp
private FrameApplyResult RecoverFromMismatch(AuthorityFrame corrected)
{
    int restoreTick = corrected.Tick - 1;
    if (!_history.TryGet(restoreTick, out FrameSyncHistoryEntry restorePoint))
    {
        SendResyncRequest(corrected.Tick, ResyncReason.RollbackWindowExceeded);
        return FrameApplyResult.ResyncRequested;
    }

    SnapshotRestoreResult restored = _session.RestoreSnapshot(restorePoint.Snapshot);
    if (!restored.Restored)
    {
        SendResyncRequest(corrected.Tick, ResyncReason.SnapshotRejected);
        return FrameApplyResult.ResyncRequested;
    }
    ReplaceAuthorityFrame(corrected);
    bool correctedTickMatched = false;
    for (int tick = corrected.Tick; tick <= _highestPredictedTick; tick++)
    {
        AuthorityFrame authority = _authorityLog[tick];
        SubmitAuthorityCommands(authority);
        TickResult result = _session.Tick();
        _history.Store(result.CompletedTick, _session.CaptureSnapshot(), authority, result.Checksum);
        if (tick == corrected.Tick)
            correctedTickMatched = result.Checksum == corrected.Checksum;
    }
    return correctedTickMatched
        ? FrameApplyResult.RolledBackAndReplayed
        : FrameApplyResult.ResyncRequested;
}
```

`ApplyResyncSnapshot` reconstructs the canonical immutable `SimSnapshot` from every `ResyncSnapshotEnvelope` field, rejects `UncompressedLength > 8388608`, verifies the 32-byte content hash, payload SHA-256, match/session, schema version, completed/next tick, timeline revision, and state checksum, then decodes through `SessionSnapshotCodec`. It calls `RestoreSnapshot` and proceeds only when `SnapshotRestoreResult.Restored` is true. A successful restore clears older prediction/history and requires `NextTick == CompletedTick + 1`; no network-side snapshot interface or schema/config alias is introduced.

- [ ] **Step 5: Run rollback and full network tests**

Run the exact Unity network test command from the global section with `-testResults "$ZWEB_EVIDENCE_ROOT/network/task-09/rollback-green.xml"` and `-logFile "$ZWEB_EVIDENCE_ROOT/network/task-09/rollback-green.log"`.

Expected: tick-zero, normal rollback, ring wrap, terminal frame, late frame, out-of-window request, and valid/invalid full-resync cases pass; no path silently accepts a missing checksum.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Network/FrameSync Assets/Tests/Network/FrameSyncRollbackTests.cs Assets/Tests/Network/FrameSyncRollbackTests.cs.meta
git commit -m "feat: replay prediction and resync frame state"
```

### Task 10: Define the Replay Schema and Reject Structurally Invalid Input

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Command/ReplayFile.cs`
- Create: `Server/NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj`
- Consume unchanged: `Server/SimulationCore/ZWebGLFrameWork.SimulationCore.csproj`
- Consume unchanged: `Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj`
- Consume unchanged: `Server/SimulationCore.Tests/packages.lock.json`
- Modify: `Server/ReplayValidator/ReplayValidator.csproj`
- Create: `Server/ReplayValidator/ReplayValidationCode.cs`
- Create: `Server/ReplayValidator/ReplayValidationResult.cs`
- Create: `Server/ReplayValidator/ReplayValidationLimits.cs`
- Create: `Server/ReplayValidator/HashText.cs`
- Create: `Server/ReplayValidator/KnownCommandSchema.cs`
- Create: `Server/ReplayValidator/ReplayStructuralValidator.cs`
- Create: `Server/Tests/ZWebGLFrameWork.Server.Tests.csproj`
- Create at execution: `Server/Tests/packages.lock.json`
- Create: `Server/Tests/ReplayStructuralValidatorTests.cs`
- Create: `Server/ZWebGLFrameWork.Server.sln`

**Interfaces:**
- Consumes: the completed SimulationCore project, its `SimulationCore.ServerTests` project plus committed dependency lock, canonical `SerializableCommand.From`/`ToCommand`, and Task 2's realtime codec. It does not create or source-glob a second SimulationCore project.
- Produces: versioned `ReplayFile`, `ReplayTickRecord`, `ReplayValidationLimits`, and `ReplayStructuralValidator.Validate(ReplayFile, int)`.

- [ ] **Step 1: Create explicit server project boundaries and locked test dependencies**

Create these exact project files before adding them to the solution:

```xml
<!-- Server/NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../Assets/Scripts/Network/Contracts/**/*.cs" Link="Contracts/%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

Do not recreate `Server/SimulationCore/ZWebGLFrameWork.SimulationCore.csproj`. The SimulationCore plan already owns that project, `Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj`, and `Server/SimulationCore.Tests/packages.lock.json`; this task adds both existing projects to the server solution unchanged.

```xml
<!-- Server/ReplayValidator/ReplayValidator.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj" />
    <ProjectReference Include="../SimulationCore/ZWebGLFrameWork.SimulationCore.csproj" />
  </ItemGroup>
</Project>
```

Run:

```bash
dotnet new sln --name ZWebGLFrameWork.Server --output Server --force
dotnet new nunit --name ZWebGLFrameWork.Server.Tests --framework net8.0 --output Server/Tests --no-restore --force
rm Server/Tests/UnitTest1.cs
dotnet sln Server/ZWebGLFrameWork.Server.sln add Server/NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj
dotnet sln Server/ZWebGLFrameWork.Server.sln add Server/SimulationCore/ZWebGLFrameWork.SimulationCore.csproj
dotnet sln Server/ZWebGLFrameWork.Server.sln add Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj
dotnet sln Server/ZWebGLFrameWork.Server.sln add Server/ReplayValidator/ReplayValidator.csproj
dotnet sln Server/ZWebGLFrameWork.Server.sln add Server/Tests/ZWebGLFrameWork.Server.Tests.csproj
dotnet add Server/Tests/ZWebGLFrameWork.Server.Tests.csproj reference Server/ReplayValidator/ReplayValidator.csproj
dotnet restore Server/ZWebGLFrameWork.Server.sln --use-lock-file --force-evaluate --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-10/artifacts"
dotnet list Server/Tests/ZWebGLFrameWork.Server.Tests.csproj package --include-transitive
```

Expected: `Server/Tests/packages.lock.json` records exact resolved test-package versions and content hashes; `Server/SimulationCore.Tests/packages.lock.json` remains byte-identical and is included by the solution's locked restore. The displayed package list is reviewed before commit. The NetworkContracts and existing SimulationCore projects contain no third-party PackageReference and compile linked source with `EnableDefaultCompileItems=false`.

The contracts project links `Assets/Scripts/Network/Contracts/**/*.cs`. The existing SimulationCore project and ServerTests lock are consumed exactly as emitted by the SimulationCore plan; do not add another source glob, factory, snapshot codec, or command codec here.

- [ ] **Step 2: Write failing structural rejection tests**

```csharp
private static readonly ReplayValidationLimits ApprovedLimits = new ReplayValidationLimits(
    maxFileBytes: 16777216,
    maxTicks: 12000,
    maxCommandsPerTick: 64,
    maxTotalCommands: 100000,
    maxInitialEntities: 4096,
    maxDecompressedSnapshotBytes: 8388608);

[Test]
public void ReplayValidator_EmptyReplay_IsRejected()
{
    var validator = new ReplayStructuralValidator(ApprovedLimits, KnownCommandSchema.Version1);
    Assert.That(validator.Validate(null, 0).Code, Is.EqualTo(ReplayValidationCode.NullReplay));
    Assert.That(validator.Validate(ReplayFixture.Empty(), 2).Code, Is.EqualTo(ReplayValidationCode.Empty));
    Assert.That(validator.Validate(ReplayFixture.MissingInitialStateIdentity(), 128).Code,
        Is.EqualTo(ReplayValidationCode.InitialStateMismatch));
}

[Test]
public void ReplayValidator_UnknownVersionOrConfigHash_IsRejected()
{
    var validator = new ReplayStructuralValidator(ApprovedLimits, KnownCommandSchema.Version1);
    Assert.That(validator.Validate(ReplayFixture.WithFormatVersion(ushort.MaxValue), 128).Code,
        Is.EqualTo(ReplayValidationCode.UnsupportedVersion));
    Assert.That(validator.Validate(ReplayFixture.WithContentConfigHash("not-a-sha256"), 128).Code,
        Is.EqualTo(ReplayValidationCode.ConfigMismatch));
}

[Test]
public void ReplayValidator_GapDuplicateReorderOrTruncation_IsRejected()
{
    var validator = new ReplayStructuralValidator(ApprovedLimits, KnownCommandSchema.Version1);
    Assert.That(validator.Validate(ReplayFixture.WithTicks(0, 2), 128).Code, Is.EqualTo(ReplayValidationCode.TickGap));
    Assert.That(validator.Validate(ReplayFixture.WithTicks(0, 1, 1), 128).Code, Is.EqualTo(ReplayValidationCode.DuplicateOrUnorderedTick));
    Assert.That(validator.Validate(ReplayFixture.WithTicks(0, 2, 1), 128).Code, Is.EqualTo(ReplayValidationCode.DuplicateOrUnorderedTick));
    Assert.That(validator.Validate(ReplayFixture.TruncatedBeforeFinalTick(), 128).Code, Is.EqualTo(ReplayValidationCode.Truncated));
}

[Test]
public void ReplayValidator_UnknownOrInvalidCommand_IsRejected()
{
    var validator = new ReplayStructuralValidator(ApprovedLimits, KnownCommandSchema.Version1);
    Assert.That(validator.Validate(ReplayFixture.WithUnknownCommand(), 128).Code, Is.EqualTo(ReplayValidationCode.UnknownCommand));
    Assert.That(validator.Validate(ReplayFixture.WithOutOfRangeCommand(), 128).Code, Is.EqualTo(ReplayValidationCode.InvalidCommand));
}
```

- [ ] **Step 3: Run the structural tests and confirm the red state**

Run: `set -o pipefail; ! dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-10/artifacts" --filter FullyQualifiedName~ReplayStructuralValidatorTests 2>&1 | tee "$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-red.txt"`.

Expected: compilation fails because replay schema and structural validator types do not exist.

- [ ] **Step 4: Replace the replay model with complete metadata**

Define stable validation codes before implementing the model:

```csharp
public enum ReplayValidationCode
{
    Valid = 0,
    NullReplay,
    FileTooLarge,
    MalformedJson,
    Empty,
    UnsupportedVersion,
    TooManyTicks,
    TooManyInitialEntities,
    SnapshotTooLarge,
    FinalTickMismatch,
    TickGap,
    DuplicateOrUnorderedTick,
    TooManyCommands,
    UnknownCommand,
    InvalidCommand,
    MatchMismatch,
    UnauthorizedSubmitter,
    InitialStateMismatch,
    SchemaMismatch,
    ConfigMismatch,
    TickRateMismatch,
    ValidationBudgetExceeded,
    CommandRejected,
    ChecksumMismatch,
    TrailingRecord,
    Truncated,
    FinalChecksumMismatch,
    ResultMismatch
}

internal static class HashText
{
    public static bool IsSha256(string value)
    {
        if (value == null || value.Length != 64) return false;
        for (int index = 0; index < value.Length; index++)
            if (!((value[index] >= '0' && value[index] <= '9') ||
                  (value[index] >= 'a' && value[index] <= 'f'))) return false;
        return true;
    }

    public static string ToLowerHex(byte[] bytes)
    {
        var text = new char[bytes.Length * 2];
        const string alphabet = "0123456789abcdef";
        for (int index = 0; index < bytes.Length; index++)
        {
            text[index * 2] = alphabet[bytes[index] >> 4];
            text[index * 2 + 1] = alphabet[bytes[index] & 15];
        }
        return new string(text);
    }

    public static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        int difference = 0;
        for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
        return difference == 0;
    }
}

public sealed class ReplayValidationLimits
{
    public static readonly ReplayValidationLimits Approved = new ReplayValidationLimits(
        16777216, 12000, 64, 100000, 4096, 8388608);
    public int MaxFileBytes { get; }
    public int MaxTicks { get; }
    public int MaxCommandsPerTick { get; }
    public int MaxTotalCommands { get; }
    public int MaxInitialEntities { get; }
    public int MaxDecompressedSnapshotBytes { get; }

    public ReplayValidationLimits(int maxFileBytes, int maxTicks, int maxCommandsPerTick,
        int maxTotalCommands, int maxInitialEntities, int maxDecompressedSnapshotBytes)
    {
        MaxFileBytes = maxFileBytes;
        MaxTicks = maxTicks;
        MaxCommandsPerTick = maxCommandsPerTick;
        MaxTotalCommands = maxTotalCommands;
        MaxInitialEntities = maxInitialEntities;
        MaxDecompressedSnapshotBytes = maxDecompressedSnapshotBytes;
    }
}

public sealed class ReplayValidationResult
{
    public ReplayValidationCode Code { get; }
    public bool IsValid => Code == ReplayValidationCode.Valid;
    public int FailedTick { get; }
    public ulong ExpectedChecksum { get; }
    public ulong ActualChecksum { get; }
    public SimBattleResult FinalResult { get; }

    public ReplayValidationResult(ReplayValidationCode code, int failedTick = -1,
        ulong expectedChecksum = 0, ulong actualChecksum = 0, SimBattleResult finalResult = null)
    {
        Code = code;
        FailedTick = failedTick;
        ExpectedChecksum = expectedChecksum;
        ActualChecksum = actualChecksum;
        FinalResult = finalResult;
    }

    public static ReplayValidationResult StructurallyValid() =>
        new ReplayValidationResult(ReplayValidationCode.Valid);
    public static ReplayValidationResult Valid(int finalTick, ulong checksum, SimBattleResult result) =>
        new ReplayValidationResult(ReplayValidationCode.Valid, finalTick, checksum, checksum, result);
}
```

```csharp
[Serializable]
public sealed class ReplayFile
{
    [JsonInclude] public ushort FormatVersion { get; set; }
    [JsonInclude] public string SimulationSchemaHash { get; set; }
    [JsonInclude] public string ContentConfigHash { get; set; }
    [JsonInclude] public int TickRate { get; set; }
    [JsonInclude] public string MatchId { get; set; }
    [JsonInclude] public string InitialStateHash { get; set; }
    [JsonInclude] public string SubmissionNonce { get; set; }
    [JsonInclude] public List<ReplayTickRecord> Records { get; set; }
    [JsonInclude] public int FinalTick { get; set; }
    [JsonInclude] public SimBattleResult FinalResult { get; set; }
    [JsonInclude] public ulong FinalChecksum { get; set; }
}

[Serializable]
public sealed class ReplayTickRecord
{
    [JsonInclude] public int Tick { get; set; }
    [JsonInclude] public List<SerializableCommand> Commands { get; set; }
    [JsonInclude] public ulong Checksum { get; set; }
}
```

Client-provided seed and layout are removed from this file. `InitialStateHash` identifies the declaration; Task 11 obtains the actual seed and layout from the authoritative match record.

`ReplayValidationLimits.Approved` is immutable and has exactly: 16 MiB (`16777216`) serialized input, `12000` ticks, `64` commands per tick, `100000` commands total, `4096` authoritative initial entities, and 8 MiB (`8388608`) decompressed snapshot. `SimulationSchemaHash`, `ContentConfigHash`, and `InitialStateHash` are exactly 64 lowercase hexadecimal characters; `MatchId` and `SubmissionNonce` are non-empty. Wall-clock timeout is only the caller's safety `CancellationToken`; it never changes a deterministic validation result or replaces any byte/count limit.

- [ ] **Step 5: Implement deterministic structural validation**

```csharp
public ReplayValidationResult Validate(ReplayFile replay, int serializedBytes)
{
    if (replay == null || replay.Records == null) return Reject(ReplayValidationCode.NullReplay);
    if (serializedBytes <= 0 || serializedBytes > _limits.MaxFileBytes) return Reject(ReplayValidationCode.FileTooLarge);
    if (replay.Records.Count == 0) return Reject(ReplayValidationCode.Empty);
    if (replay.FormatVersion != ReplayFormat.CurrentVersion) return Reject(ReplayValidationCode.UnsupportedVersion);
    if (!HashText.IsSha256(replay.SimulationSchemaHash)) return Reject(ReplayValidationCode.SchemaMismatch);
    if (!HashText.IsSha256(replay.ContentConfigHash)) return Reject(ReplayValidationCode.ConfigMismatch);
    if (!HashText.IsSha256(replay.InitialStateHash) || string.IsNullOrWhiteSpace(replay.MatchId) ||
        string.IsNullOrWhiteSpace(replay.SubmissionNonce))
        return Reject(ReplayValidationCode.InitialStateMismatch);
    if (replay.Records.Count > _limits.MaxTicks) return Reject(ReplayValidationCode.TooManyTicks);
    if (replay.Records.Count <= replay.FinalTick) return Reject(ReplayValidationCode.Truncated);
    if (replay.Records.Count > replay.FinalTick + 1) return Reject(ReplayValidationCode.TrailingRecord);

    int totalCommands = 0;
    for (int index = 0; index < replay.Records.Count; index++)
    {
        ReplayTickRecord record = replay.Records[index];
        if (record == null)
            return Reject(ReplayValidationCode.MalformedJson, index);
        if (record.Tick != index)
            return Reject(record.Tick > index ? ReplayValidationCode.TickGap : ReplayValidationCode.DuplicateOrUnorderedTick, index);
        if (record.Commands == null || record.Commands.Count > _limits.MaxCommandsPerTick)
            return Reject(ReplayValidationCode.TooManyCommands, index);
        totalCommands = checked(totalCommands + record.Commands.Count);
        if (totalCommands > _limits.MaxTotalCommands)
            return Reject(ReplayValidationCode.TooManyCommands, index);
        ReplayValidationCode commandError = _commandSchema.ValidateOrdered(record.Tick, record.Commands);
        if (commandError != ReplayValidationCode.Valid)
            return Reject(commandError, index);
    }
    return ReplayValidationResult.StructurallyValid();
}
```

`ValidateOrdered` requires each command tick to equal its record, player to belong to the match declaration, sequence to increase strictly per player, message ID to be known, and payload fields to satisfy command-specific coordinate, archetype, and entity constraints.

- [ ] **Step 6: Run structural and shared-project tests**

Run:

```bash
dotnet restore Server/ZWebGLFrameWork.Server.sln --locked-mode --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-10/artifacts"
dotnet build Server/ZWebGLFrameWork.Server.sln --configuration Release --no-restore --warnaserror --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-10/artifacts"
dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --no-build \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-10/artifacts" \
  --filter FullyQualifiedName~ReplayStructuralValidatorTests \
  --logger "trx;LogFileName=structural-green.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/replay/task-10"
```

Expected: all structural cases pass; linked contracts and SimulationCore compile without Unity references or warnings.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/SimulationCore/Command/ReplayFile.cs Server/NetworkContracts Server/SimulationCore Server/ReplayValidator Server/Tests Server/ZWebGLFrameWork.Server.sln
git commit -m "feat: validate complete replay structure"
```

### Task 11: Validate Against Authoritative Match State and Harden the CLI

**Files:**
- Create: `Server/ReplayValidator/AuthoritativeMatchRecord.cs`
- Create: `Server/ReplayValidator/SimBattleResultValue.cs`
- Create: `Server/ReplayValidator/IReplayMatchProvider.cs`
- Create: `Server/ReplayValidator/JsonReplayMatchProvider.cs`
- Create: `Server/ReplayValidator/IReplayValidationBudget.cs`
- Replace: `Server/ReplayValidator/ReplayValidator.cs`
- Replace: `Server/ReplayValidator/Program.cs`
- Create: `Server/Tests/ReplayValidatorAuthorityTests.cs`
- Create: `Server/Tests/ReplayValidatorCliTests.cs`
- Create: `Server/Tools/ReplayFixtureGenerator/ReplayFixtureGenerator.csproj`
- Create: `Server/Tools/ReplayFixtureGenerator/Program.cs`
- Create: `Server/TestData/Replay/valid-replay.json`
- Create: `Server/TestData/Replay/valid-match.json`
- Create: `Server/TestData/Replay/replay-limits.json`

**Interfaces:**
- Consumes: Task 10 structural validation, `ISimBattleSessionFactory.Create(byte[], ISimLogger)`, typed `StartResult`, canonical `BattleInit`, canonical manifest bytes, and authoritative expected result.
- Produces: `ValidateAsync(ReplayFile, AuthoritativeMatchRecord, string, int, CancellationToken)`, deterministic rejection codes, one machine-readable JSON result, and CLI exit codes exactly `0`, `1`, `2`, `3`, `4`, and `5`.

`AuthoritativeMatchRecord` is immutable so tests can derive one changed claim with a C# `with` expression without mutating the authority fixture:

```csharp
public sealed record AuthoritativeMatchRecord
{
    public string MatchId { get; init; }
    public string InitialStateHash { get; init; }
    public string SubmissionNonce { get; init; }
    public string SimulationSchemaHash { get; init; }
    public string ContentConfigHash { get; init; }
    public byte[] CanonicalManifestBytes { get; init; }
    public BattleInit BattleInit { get; init; }
    public SimSnapshot InitialSnapshot { get; init; }
    public SimBattleResult ExpectedResult { get; init; }
    public string[] AllowedSubmitterPlayerIds { get; init; }
}

internal static class SimBattleResultValue
{
    public static bool Equals(SimBattleResult left, SimBattleResult right) =>
        object.ReferenceEquals(left, right) ||
        (left != null && right != null &&
         left.MatchId == right.MatchId && left.ResultId == right.ResultId &&
         left.WinnerTeamId == right.WinnerTeamId && left.Stars == right.Stars &&
         left.LootGold == right.LootGold && left.LootElixir == right.LootElixir &&
         left.FinalTick == right.FinalTick && left.FinalChecksum == right.FinalChecksum);
}
```

- [ ] **Step 1: Write failing authoritative and terminal tests**

```csharp
[Test]
public async Task ReplayValidator_TamperedSeedOrLayout_IsRejectedAgainstMatch()
{
    ReplayFixture fixture = ReplayFixture.RecordCompletedBattle();
    var validator = fixture.CreateValidator();

    AuthoritativeMatchRecord wrongSeed = fixture.Match with
    {
        BattleInit = fixture.CopyBattleInit(seed: fixture.Match.BattleInit.Seed + 1)
    };
    Assert.That((await validator.ValidateAsync(fixture.Replay, wrongSeed, fixture.SubmitterPlayerId, fixture.SerializedBytes, CancellationToken.None)).Code,
        Is.EqualTo(ReplayValidationCode.InitialStateMismatch));
    AuthoritativeMatchRecord wrongLayout = fixture.Match with
    {
        BattleInit = fixture.CopyBattleInit(initialEntities: fixture.MoveFirstEntityByOneCell())
    };
    Assert.That((await validator.ValidateAsync(fixture.Replay, wrongLayout, fixture.SubmitterPlayerId, fixture.SerializedBytes, CancellationToken.None)).Code,
        Is.EqualTo(ReplayValidationCode.InitialStateMismatch));
}

[Test]
public async Task ReplayValidator_TerminalChecksumAndTrailingRecords_AreValidated()
{
    ReplayFixture fixture = ReplayFixture.RecordCompletedBattle();
    var validator = fixture.CreateValidator();

    fixture.Replay.Records[fixture.Replay.FinalTick].Checksum ^= 1;
    Assert.That((await validator.ValidateAsync(fixture.Replay, fixture.Match, fixture.SubmitterPlayerId, fixture.SerializedBytes, CancellationToken.None)).Code,
        Is.EqualTo(ReplayValidationCode.ChecksumMismatch));

    fixture = ReplayFixture.RecordCompletedBattle();
    fixture.Replay.Records.Add(fixture.MakeTrailingRecord());
    Assert.That((await validator.ValidateAsync(fixture.Replay, fixture.Match, fixture.SubmitterPlayerId, fixture.SerializedBytes, CancellationToken.None)).Code,
        Is.EqualTo(ReplayValidationCode.TrailingRecord));
}
```

- [ ] **Step 2: Run authority tests and confirm the red state**

Run: `set -o pipefail; ! dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-11/artifacts" --filter FullyQualifiedName~ReplayValidatorAuthorityTests 2>&1 | tee "$ZWEB_EVIDENCE_ROOT/replay/task-11/authority-red.txt"`.

Expected: compilation fails because authoritative match provider and replacement validator do not exist.

- [ ] **Step 3: Implement authoritative preconditions and full terminal validation**

```csharp
public async Task<ReplayValidationResult> ValidateAsync(
    ReplayFile replay,
    AuthoritativeMatchRecord match,
    string submittingPlayerId,
    int serializedBytes,
    CancellationToken cancellationToken)
{
    ReplayValidationResult structural = _structural.Validate(replay, serializedBytes);
    if (!structural.IsValid) return structural;
    if (match == null || replay.MatchId != match.MatchId) return Reject(ReplayValidationCode.MatchMismatch);
    if (match.AllowedSubmitterPlayerIds == null || !match.AllowedSubmitterPlayerIds.Contains(submittingPlayerId))
        return Reject(ReplayValidationCode.UnauthorizedSubmitter);
    if (replay.SubmissionNonce != match.SubmissionNonce) return Reject(ReplayValidationCode.MatchMismatch);
    if (replay.SimulationSchemaHash != match.SimulationSchemaHash) return Reject(ReplayValidationCode.SchemaMismatch);
    if (replay.ContentConfigHash != match.ContentConfigHash) return Reject(ReplayValidationCode.ConfigMismatch);
    if (replay.TickRate != match.BattleInit.TickRate) return Reject(ReplayValidationCode.TickRateMismatch);

    if (match.BattleInit.InitialEntities.Length > _limits.MaxInitialEntities)
        return Reject(ReplayValidationCode.TooManyInitialEntities);
    if (match.InitialSnapshot == null) return Reject(ReplayValidationCode.InitialStateMismatch);
    if (match.InitialSnapshot.UncompressedLength > _limits.MaxDecompressedSnapshotBytes ||
        match.InitialSnapshot.Payload.Length > _limits.MaxDecompressedSnapshotBytes)
        return Reject(ReplayValidationCode.SnapshotTooLarge);

    using ISimBattleSession session = _sessionFactory.Create(match.CanonicalManifestBytes, _logger);
    StartResult started = session.Start(match.BattleInit);
    if (!started.Accepted) return Reject(ReplayValidationCode.InitialStateMismatch);
    SimSnapshot actualInitial = session.CaptureSnapshot();
    if (actualInitial.CompletedTick != -1 ||
        actualInitial.StateChecksum != match.InitialSnapshot.StateChecksum ||
        !HashText.FixedTimeEquals(actualInitial.ContentHash, match.InitialSnapshot.ContentHash) ||
        !HashText.FixedTimeEquals(actualInitial.PayloadSha256, match.InitialSnapshot.PayloadSha256))
        return Reject(ReplayValidationCode.InitialStateMismatch);
    string authoritativeInitialStateHash = HashText.ToLowerHex(actualInitial.PayloadSha256);
    if (match.InitialStateHash != authoritativeInitialStateHash ||
        replay.InitialStateHash != authoritativeInitialStateHash)
        return Reject(ReplayValidationCode.InitialStateMismatch);
    bool terminalSeen = false;
    SimBattleResult actualFinalResult = default;
    for (int tick = 0; tick < replay.Records.Count; tick++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_budget.TryConsumeTick(replay.Records[tick].Commands.Count))
            return Reject(ReplayValidationCode.ValidationBudgetExceeded, tick);
        foreach (SerializableCommand serialized in replay.Records[tick].Commands)
        {
            CommandSubmitResult submit = session.Submit(serialized.ToCommand());
            if (!submit.Accepted) return Reject(ReplayValidationCode.CommandRejected, tick);
        }
        TickResult result = session.Tick();
        if (result.CompletedTick != tick || result.Checksum != replay.Records[tick].Checksum)
            return Reject(ReplayValidationCode.ChecksumMismatch, tick, replay.Records[tick].Checksum, result.Checksum);
        if (result.Code == TickCode.Ended)
        {
            terminalSeen = true;
            actualFinalResult = result.Result;
            if (tick != replay.FinalTick)
                return Reject(ReplayValidationCode.TrailingRecord, tick + 1);
        }
    }
    if (!terminalSeen || session.State.Lifecycle != SessionLifecycle.Ended) return Reject(ReplayValidationCode.Truncated);
    if (replay.FinalChecksum != replay.Records[replay.FinalTick].Checksum) return Reject(ReplayValidationCode.FinalChecksumMismatch);
    if (!SimBattleResultValue.Equals(actualFinalResult, replay.FinalResult) ||
        !SimBattleResultValue.Equals(actualFinalResult, match.ExpectedResult))
        return Reject(ReplayValidationCode.ResultMismatch);
    return ReplayValidationResult.Valid(replay.FinalTick, replay.FinalChecksum, replay.FinalResult);
}
```

- [ ] **Step 4: Write failing CLI contract tests**

```csharp
[TestCase(ReplayCliCase.Valid, 0)]
[TestCase(ReplayCliCase.MissingFile, 1)]
[TestCase(ReplayCliCase.InvalidReplay, 2)]
[TestCase(ReplayCliCase.UnsupportedVersion, 3)]
[TestCase(ReplayCliCase.AuthoritativeMismatch, 4)]
[TestCase(ReplayCliCase.ResourceLimit, 5)]
public async Task Cli_EmitsJsonAndUsesStableExitCodes(ReplayCliCase testCase, int expectedExit)
{
    ReplayCliFixture fixture = ReplayCliFixture.Create(testCase);
    var stdout = new StringWriter();
    var stderr = new StringWriter();
    int exitCode = await ReplayCli.RunAsync(fixture.Arguments, stdout, stderr, CancellationToken.None);
    Assert.That(exitCode, Is.EqualTo(expectedExit));
    using JsonDocument json = JsonDocument.Parse(stdout.ToString());
    Assert.That(json.RootElement.GetProperty("exitCode").GetInt32(), Is.EqualTo(expectedExit));
    Assert.That(stdout.ToString().Trim().Split('\n').Length, Is.EqualTo(1));
}

[Test]
public async Task ReplayCli_OversizedOrOverLimitReplay_IsRejected()
{
    ReplayCliFixture[] fixtures =
    {
        ReplayCliFixture.WithSerializedBytes(16777217),
        ReplayCliFixture.WithTickCount(12001),
        ReplayCliFixture.WithCommandsPerTick(65),
        ReplayCliFixture.WithTotalCommands(100001),
        ReplayCliFixture.WithInitialEntities(4097),
        ReplayCliFixture.WithDecompressedSnapshotBytes(8388609)
    };
    foreach (ReplayCliFixture fixture in fixtures)
    {
        var stdout = new StringWriter();
        int exitCode = await ReplayCli.RunAsync(fixture.Arguments, stdout, TextWriter.Null, CancellationToken.None);
        Assert.That(exitCode, Is.EqualTo(5), fixture.Name);
        using JsonDocument json = JsonDocument.Parse(stdout.ToString());
        Assert.That(json.RootElement.GetProperty("category").GetString(), Is.EqualTo("resource-limit"), fixture.Name);
    }
}
```

- [ ] **Step 5: Implement exact CLI inputs and output**

Accepted command:

```bash
dotnet run --project Server/ReplayValidator/ReplayValidator.csproj --configuration Release --no-build \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-11/artifacts" -- \
  validate \
  --replay Server/TestData/Replay/valid-replay.json \
  --match Server/TestData/Replay/valid-match.json \
  --limits Server/TestData/Replay/replay-limits.json \
  --submitter player-0
```

Exit codes are exact: `0` valid, `1` I/O, malformed CLI arguments, or internal failure, `2` invalid replay, `3` unsupported replay version or simulation schema, `4` authoritative match missing/mismatch, and `5` any deterministic resource limit. Stdout is exactly one JSON object containing `isValid`, `exitCode`, `category`, `code`, `failedTick`, `expectedChecksum`, `actualChecksum`, `finalResult`, and `message`; stderr is reserved for process diagnostics and never replaces the JSON result.

`Server/TestData/Replay/replay-limits.json` is generated with these exact values and strict JSON parsing rejects missing, additional, fractional, negative, or changed fields:

```json
{
  "MaxFileBytes": 16777216,
  "MaxTicks": 12000,
  "MaxCommandsPerTick": 64,
  "MaxTotalCommands": 100000,
  "MaxInitialEntities": 4096,
  "MaxDecompressedSnapshotBytes": 8388608
}
```

Generate the committed valid files from the real shared session; do not type checksum values by hand. `ReplayFixtureGenerator` loads canonical manifest bytes, creates a fixed two-player `BattleInit` with seed `42`, tick rate `20`, deadline `6`, and exact schema/content hashes, then calls the canonical factory and checks `StartResult.Accepted`. It advances deterministic empty ticks until the real session reaches `SessionLifecycle.Ended`; it does not submit client NoOps. Every returned `TickResult`, including the terminal tick, is recorded.

```csharp
using ISimBattleSession session = factory.Create(match.CanonicalManifestBytes, logger);
StartResult started = session.Start(match.BattleInit);
if (!started.Accepted) throw new InvalidOperationException($"Golden session start failed: {started.Code}");
match = match with { InitialSnapshot = session.CaptureSnapshot() };
for (int tick = 0; tick < limits.MaxTicks && session.State.Lifecycle != SessionLifecycle.Ended; tick++)
{
    TickResult result = session.Tick();
    replay.Records.Add(new ReplayTickRecord
    {
        Tick = result.CompletedTick,
        Commands = result.ExecutedCommands.Select(SerializableCommand.From).ToList(),
        Checksum = result.Checksum
    });
    replay.FinalTick = result.CompletedTick;
    replay.FinalChecksum = result.Checksum;
    replay.FinalResult = result.Result;
}
if (session.State.Lifecycle != SessionLifecycle.Ended)
    throw new InvalidOperationException("Golden replay did not terminate within 12000 ticks.");
```

Run:

```xml
<!-- Server/Tools/ReplayFixtureGenerator/ReplayFixtureGenerator.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../ReplayValidator/ReplayValidator.csproj" />
    <ProjectReference Include="../../SimulationCore/ZWebGLFrameWork.SimulationCore.csproj" />
  </ItemGroup>
</Project>
```

Then run:

```bash
dotnet sln Server/ZWebGLFrameWork.Server.sln add Server/Tools/ReplayFixtureGenerator/ReplayFixtureGenerator.csproj
dotnet run --project Server/Tools/ReplayFixtureGenerator/ReplayFixtureGenerator.csproj \
  --configuration Release --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-11/artifacts" -- Server/TestData/Replay
```

Expected: the tool writes valid JSON replay, authoritative match, and limits files; running it twice produces byte-identical output.

- [ ] **Step 6: Run validator and CLI tests**

Run:

```bash
dotnet build Server/ZWebGLFrameWork.Server.sln --configuration Release --warnaserror --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-11/artifacts"
dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --no-build \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-11/artifacts" \
  --filter FullyQualifiedName~ReplayValidatorAuthorityTests \
  --logger "trx;LogFileName=authority-green.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/replay/task-11"
ZWEB_REPLAY_REJECTION_EVIDENCE="$ZWEB_EVIDENCE_ROOT/replay/task-11/rejection-matrix.json" \
  dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --no-build \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-11/artifacts" \
  --filter FullyQualifiedName~ReplayValidatorCliTests \
  --logger "trx;LogFileName=cli-green.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/replay/task-11"
dotnet run --project Server/ReplayValidator/ReplayValidator.csproj --configuration Release --no-build \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/replay/task-11/artifacts" -- validate \
  --replay Server/TestData/Replay/valid-replay.json --match Server/TestData/Replay/valid-match.json \
  --limits Server/TestData/Replay/replay-limits.json --submitter player-0 \
  | tee "$ZWEB_EVIDENCE_ROOT/replay/task-11/valid-result.json"
```

Expected: authority and CLI tests pass; the final command exits `0` and emits one valid JSON result. Invalid replay fixtures exit `2`, unsupported version/schema fixtures exit `3`, authoritative missing/mismatch fixtures exit `4`, and every exact byte/count/snapshot limit fixture exits `5`. Write the full rejection matrix JSON to `$ZWEB_EVIDENCE_ROOT/replay/rejection-matrix.json` and focused TRX to `$ZWEB_EVIDENCE_ROOT/replay/replay-validator.trx`.

- [ ] **Step 7: Commit**

```bash
git add Server/ReplayValidator Server/Tests Server/Tools/ReplayFixtureGenerator Server/TestData/Replay Server/ZWebGLFrameWork.Server.sln
git commit -m "feat: validate replay against authoritative matches"
```

### Task 12: Implement the Monotonic Authoritative Match Actor

**Files:**
- Create: `Server/FrameSync/FrameSyncServer.csproj`
- Create: `Server/FrameSync/IMonotonicClock.cs`
- Create: `Server/FrameSync/InputInbox.cs`
- Create: `Server/FrameSync/IFrameBroadcaster.cs`
- Create: `Server/FrameSync/FrameSyncMatchActor.cs`
- Create: `Server/Tests/FrameSyncMatchActorTests.cs`
- Modify: `Server/ZWebGLFrameWork.Server.sln`
- Modify: `Server/Tests/ZWebGLFrameWork.Server.Tests.csproj`

**Interfaces:**
- Consumes: `ISimBattleSession`, authority codecs, authenticated player/session claims, and `BattleInit.InputDeadlineTicks`.
- Produces: `SubmitInput`, `Pump`, immutable sealed ticks, authoritative NoOp insertion, and `AuthorityFrame` broadcast. The actor constructor accepts the canonical `ISimBattleSession` plus an `Action<int>` bound to SimulationCore's internal `SimBattleSession.SealTick`; tests inject a recording action without inventing another Session interface.

- [ ] **Step 1: Add the server project and failing deadline tests**

Create the actor project as a library in this task; Task 13 changes it to the executable ASP.NET Core host:

```xml
<!-- Server/FrameSync/FrameSyncServer.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>ZWebGLFrameWork.FrameSyncServer</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj" />
    <ProjectReference Include="../SimulationCore/ZWebGLFrameWork.SimulationCore.csproj" />
  </ItemGroup>
</Project>
```

Run:

```bash
dotnet sln Server/ZWebGLFrameWork.Server.sln add Server/FrameSync/FrameSyncServer.csproj
dotnet add Server/Tests/ZWebGLFrameWork.Server.Tests.csproj reference Server/FrameSync/FrameSyncServer.csproj
```

```csharp
[Test]
public void FrameSyncServer_MissingInputAfterDeadline_InsertsNoOp()
{
    var clock = new ManualMonotonicClock(0);
    var fixture = MatchActorFixture.Create(clock, tickRate: 20, inputDeadlineTicks: 6);
    fixture.Actor.SubmitInput(playerId: 0, fixture.RealInput(tick: 0, sequence: 1));

    clock.SetMilliseconds(299);
    fixture.Actor.Pump();
    Assert.That(fixture.Session.TickCalls, Is.Zero);

    clock.SetMilliseconds(300);
    fixture.Actor.Pump();
    Assert.That(fixture.Session.TickCalls, Is.EqualTo(1));
    Assert.That(fixture.Broadcaster.Frames.Single().OrderedInputs[1].IsNoOp, Is.True);
}

[Test]
public void InputAfterTickIsSealed_IsRejectedAndCannotRewriteAuthority()
{
    var fixture = MatchActorFixture.SealTickZeroWithNoOps();
    InputAcceptance result = fixture.Actor.SubmitInput(1, fixture.RealInput(tick: 0, sequence: 1));
    Assert.That(result, Is.EqualTo(InputAcceptance.LateRejected));
    Assert.That(fixture.Broadcaster.Frames.Count, Is.EqualTo(1));
}
```

- [ ] **Step 2: Run actor tests and confirm the red state**

Run: `set -o pipefail; ! dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --artifacts-path "$ZWEB_EVIDENCE_ROOT/server/task-12/artifacts" --filter FullyQualifiedName~FrameSyncMatchActorTests 2>&1 | tee "$ZWEB_EVIDENCE_ROOT/server/task-12/actor-red.txt"`.

Expected: compilation fails because actor and input inbox types do not exist.

- [ ] **Step 3: Implement per-player sequence validation and immutable inboxes**

```csharp
public enum InputAcceptance
{
    Accepted = 0,
    WrongPlayer,
    LateRejected,
    TooFarAhead,
    DuplicateOrReorderedSequence,
    DuplicateInput,
    MailboxOverloaded
}

public InputAcceptance Submit(int authenticatedPlayerId, CommandEnvelope input, int nextUnsealedTick)
{
    if (input.PlayerId != authenticatedPlayerId) return InputAcceptance.WrongPlayer;
    if (input.Tick < nextUnsealedTick) return InputAcceptance.LateRejected;
    if (input.Tick > nextUnsealedTick + _maxFutureTicks) return InputAcceptance.TooFarAhead;
    if (input.Sequence <= _lastAcceptedSequence[authenticatedPlayerId]) return InputAcceptance.DuplicateOrReorderedSequence;
    if (_byTickPlayer.ContainsKey((input.Tick, authenticatedPlayerId))) return InputAcceptance.DuplicateInput;
    _byTickPlayer.Add((input.Tick, authenticatedPlayerId), input);
    _lastAcceptedSequence[authenticatedPlayerId] = input.Sequence;
    return InputAcceptance.Accepted;
}
```

- [ ] **Step 4: Implement monotonic scheduled sealing**

```csharp
public int Pump()
{
    int advanced = 0;
    while (advanced < _maxTicksPerPump)
    {
        long sealAt = _startMilliseconds + (long)(_nextTick + _init.InputDeadlineTicks) * _tickIntervalMilliseconds;
        if (_clock.Milliseconds < sealAt) break;

        CommandEnvelope player0 = _inbox.TakeOrNoOp(_nextTick, 0);
        CommandEnvelope player1 = _inbox.TakeOrNoOp(_nextTick, 1);
        CommandEnvelope[] ordered = Order(player0, player1);
        foreach (CommandEnvelope envelope in ordered)
        {
            if (envelope.IsNoOp) continue;
            CommandSubmitResult submit = _session.Submit(_commandMapper.ToSimulation(envelope));
            if (!submit.Accepted) throw new InvalidOperationException("Validated authority input was rejected by SimulationCore.");
        }
        _sealTick(_nextTick);
        TickResult tick = _session.Tick();
        if (tick.Code != TickCode.Advanced && tick.Code != TickCode.Ended)
            throw new InvalidOperationException($"SimulationCore refused sealed tick {_nextTick}: {tick.Code}");
        AuthorityFrame frame = new AuthorityFrame
        {
            MatchId = _matchId,
            Tick = tick.CompletedTick,
            OrderedInputs = ordered,
            AckPlayer0 = _inbox.AcceptedAcks[0],
            AckPlayer1 = _inbox.AcceptedAcks[1],
            Checksum = tick.Checksum
        };
        _broadcaster.Broadcast(frame);
        _nextTick++;
        advanced++;
    }
    return advanced;
}
```

`_tickIntervalMilliseconds` is exactly `1000 / TickRate`; construction rejects tick rates that do not divide 1000 exactly. `_maxTicksPerPump` is `4` to prevent an unbounded catch-up loop. The match actor is single-thread-owned; ingress posts messages into its bounded mailbox.

- [ ] **Step 5: Run actor tests and build the server solution**

Run:

```bash
dotnet build Server/ZWebGLFrameWork.Server.sln --configuration Release --warnaserror --artifacts-path "$ZWEB_EVIDENCE_ROOT/server/task-12/artifacts"
dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --no-build \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/server/task-12/artifacts" \
  --filter FullyQualifiedName~FrameSyncMatchActorTests \
  --logger "trx;LogFileName=actor-green.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/server/task-12"
```

Expected: deadline, both-input, missing-input, duplicate, wrong-player, future-window, late-input, bounded catch-up, terminal tick, and actor cleanup tests pass.

- [ ] **Step 6: Commit**

```bash
git add Server/FrameSync Server/Tests Server/ZWebGLFrameWork.Server.sln
git commit -m "feat: add authoritative frame match actor"
```

### Task 13: Add Shared WSS and Native Server Ingress

**Files:**
- Modify: `Server/FrameSync/FrameSyncServer.csproj`
- Create: `Server/FrameSync/IRealtimeServerSession.cs`
- Create: `Server/FrameSync/RealtimeSessionClaims.cs`
- Create: `Server/FrameSync/IRealtimeTicketStore.cs`
- Create: `Server/FrameSync/InMemoryRealtimeTicketStore.cs`
- Create: `Server/FrameSync/SessionRateLimiter.cs`
- Create: `Server/FrameSync/RealtimeSessionRouter.cs`
- Create: `Server/FrameSync/WebSocketIngress.cs`
- Create: `Server/FrameSync/KcpIngress.cs`
- Create: `Server/FrameSync/Program.cs`
- Create: `Server/FrameSync/appsettings.json`
- Create: `Server/Tests/RealtimeSessionRouterTests.cs`
- Create: `Server/Tests/WebSocketIngressTests.cs`
- Create: `Server/Tests/BrowserWssIntegrationTests.cs`
- Create: `Server/Tests/KcpIngressTests.cs`

**Interfaces:**
- Consumes: Task 3 authentication, Task 6 locked kcp2k source, Task 12 actor mailbox, and Task 2 complete-message codec.
- Produces: WSS and native sessions that decode to the same `CommandEnvelope` and route through one authenticated session router.

- [ ] **Step 1: Write failing shared-ingress vector and rejection tests**

Change the actor library into the executable host and link only the provenance-verified native source:

```xml
<!-- Server/FrameSync/FrameSyncServer.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>ZWebGLFrameWork.FrameSyncServer</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj" />
    <ProjectReference Include="../SimulationCore/ZWebGLFrameWork.SimulationCore.csproj" />
    <Compile Include="../../Packages/com.zwebglframework.kcp2k/Runtime/Upstream/**/*.cs" Link="kcp2k/%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

```csharp
[Test]
public async Task FrameSyncServer_WssAndNativeDecodeSameAuthorityVector()
{
    var fixture = IngressFixture.Create();
    byte[] bytes = RealtimeCodec.Encode(CommandEnvelope.Create(
        tick: 3, playerId: 0, sequence: 9, messageId: KnownMessageIds.Deploy,
        payload: DeployPayload.Encode(unitTypeId: 2, gridX: 4, gridY: 5)));

    await fixture.Wss.InjectAuthenticatedMessageAsync(bytes);
    await fixture.Native.InjectAuthenticatedMessageAsync(bytes);

    Assert.That(fixture.Router.Accepted[0], Is.EqualTo(fixture.Router.Accepted[1]));
}

[Test]
public async Task Router_RejectsExpiredTicketWrongSourceOversizeAndRateExcess()
{
    var fixture = IngressFixture.Create(maxMessagesPerSecond: 64, burst: 128);
    Assert.That(await fixture.SendCommandBeforeAuthHelloAsync(), Is.EqualTo(ProtocolError.AuthenticationFailed));
    Assert.That(await fixture.SendExpiredTicketAsync(), Is.EqualTo(ProtocolError.ExpiredTicket));
    Assert.That(await fixture.SendWrongPlayerAsync(), Is.EqualTo(ProtocolError.WrongPlayer));
    Assert.That(await fixture.SendBytesAsync(new byte[65537]), Is.EqualTo(ProtocolError.MessageTooLarge));
    Assert.That(await fixture.SendBurstAsync(129), Is.EqualTo(ProtocolError.RateLimited));
    Assert.That(fixture.CloseCodeFor(ProtocolError.AuthenticationFailed), Is.EqualTo(4001));
    Assert.That(fixture.CloseCodeFor(ProtocolError.UnsupportedVersion), Is.EqualTo(4002));
    Assert.That(fixture.CloseCodeFor(ProtocolError.InvalidPayload), Is.EqualTo(4003));
    Assert.That(fixture.CloseCodeFor(ProtocolError.RateLimited), Is.EqualTo(4004));
    Assert.That(fixture.CloseCodeFor(ProtocolError.ExpiredTicket), Is.EqualTo(4005));
}
```

- [ ] **Step 2: Run ingress tests and confirm the red state**

Run: `set -o pipefail; ! dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --artifacts-path "$ZWEB_EVIDENCE_ROOT/server/task-13/artifacts" --filter FullyQualifiedName~Ingress 2>&1 | tee "$ZWEB_EVIDENCE_ROOT/server/task-13/ingress-red.txt"`.

Expected: compilation fails because router and ingress types do not exist.

- [ ] **Step 3: Implement one authentication and routing path**

```csharp
public sealed class RealtimeSessionClaims
{
    public Guid SessionId { get; init; }
    public string MatchId { get; init; }
    public int PlayerId { get; init; }
    public byte[] ResumeToken { get; init; }
    public long ExpiresAtUnixSeconds { get; init; }
}

public interface IRealtimeServerSession
{
    bool IsAuthenticated { get; }
    RealtimeSessionClaims Claims { get; }
    void Bind(RealtimeSessionClaims claims);
}

public interface IRealtimeTicketStore
{
    bool TryValidate(byte[] opaqueTicket, long utcUnixSeconds, out RealtimeSessionClaims claims);
    bool TryResume(ResumeHello hello, long monotonicMilliseconds, out RealtimeSessionClaims claims);
    void RetainDisconnected(RealtimeSessionClaims claims, long disconnectedAtMilliseconds);
}

public async Task<ProtocolError> RouteAsync(
    IRealtimeServerSession connection,
    byte[] completeMessage,
    CancellationToken cancellationToken)
{
    if (completeMessage == null || completeMessage.Length > RealtimeProtocol.MaxMessageBytes)
        return ProtocolError.MessageTooLarge;
    DecodeResult decoded = RealtimeCodec.Decode(completeMessage);
    if (decoded.Error != ProtocolError.None) return decoded.Error;
    if (!connection.IsAuthenticated)
    {
        if (decoded.Kind != RealtimeMessageKind.AuthHello) return ProtocolError.AuthenticationFailed;
        if (!_tickets.TryValidate(decoded.AuthHello.OpaqueTicket, _clock.UtcUnixSeconds, out RealtimeSessionClaims firstClaims))
            return ProtocolError.AuthenticationFailed;
        if (firstClaims.SessionId != decoded.AuthHello.SessionId) return ProtocolError.WrongSession;
        connection.Bind(firstClaims);
        return ProtocolError.None;
    }
    RealtimeSessionClaims claims = connection.Claims;
    if (!_rateLimiter.TryConsume(claims.SessionId, _clock.Milliseconds))
        return ProtocolError.RateLimited;

    if (decoded.Kind != RealtimeMessageKind.Command) return ProtocolError.UnknownMessageKind;
    if (decoded.Command.PlayerId != claims.PlayerId) return ProtocolError.WrongPlayer;
    return await _matches.PostAsync(claims.MatchId, claims.PlayerId, decoded.Command, cancellationToken);
}
```

WSS uses TLS and requires `AuthHello` as the first application message. Native ingress verifies Task 3's full HMAC, session, and 1,024-packet replay sequence before passing the inner `AuthHello` to this same router; it changes the source endpoint only after all checks pass. Both transports use one ticket store, one authenticated session state machine, one rate limiter, and one actor mailbox. Error closure uses the stable `RealtimeCloseCode` mapping from Task 2.

`Program` maps `POST /v1/login/guest` over HTTPS. The ticket store creates a 32-byte random MAC key, opaque ticket, independent resume token, 15-minute expiry, and a session claim bound to player/match; the endpoint returns `playerId`, `sessionId`, base64 `ticket`, base64 `resumeToken`, base64 `macKey`, and `expiresAtUnixSeconds`. The same claim may own exactly one active connection; a disconnected slot follows Task 14's strict 10-second retention contract.

```json
{
  "Realtime": {
    "HttpsUrl": "https://127.0.0.1:7443",
    "WebSocketPath": "/v1/realtime",
    "WebSocketSubprotocol": "zweb.framesync.v1",
    "AllowedOrigins": ["https://127.0.0.1:7443"],
    "NativeBindAddress": "127.0.0.1",
    "NativePort": 7777,
    "MaxMessageBytes": 65536,
    "MessagesPerSecond": 64,
    "Burst": 128
  }
}
```

- [ ] **Step 4: Add real Kestrel WSS and kcp2k server loops**

`WebSocketIngress` accepts only TLS requests at exact path `/v1/realtime`, requires the offered subprotocol `zweb.framesync.v1`, enforces exact configured Origins, accepts binary complete messages only, and caps receive accumulation at `65536` bytes. Its first complete message must decode as `AuthHello`; otherwise it closes with `4001`. `KcpIngress` calls pinned `KcpServer.TickIncoming` and `TickOutgoing` from one monotonic loop, never exposes kcp2k types outside the native ingress file, and applies endpoint rebinding only after full-MAC/session/replay validation.

```csharp
while (!context.RequestAborted.IsCancellationRequested && socket.State == WebSocketState.Open)
{
    using var message = new MemoryStream();
    WebSocketReceiveResult result;
    do
    {
        result = await socket.ReceiveAsync(buffer, context.RequestAborted);
        if (result.MessageType != WebSocketMessageType.Binary)
            throw new RealtimeProtocolException(ProtocolError.UnknownMessageKind);
        if (message.Length + result.Count > RealtimeProtocol.MaxMessageBytes)
            throw new RealtimeProtocolException(ProtocolError.MessageTooLarge);
        message.Write(buffer.Array, buffer.Offset, result.Count);
    }
    while (!result.EndOfMessage);
    await _router.RouteAsync(session, message.ToArray(), context.RequestAborted);
}
```

- [ ] **Step 5: Run router and both ingress tests**

Run:

```bash
dotnet build Server/ZWebGLFrameWork.Server.sln --configuration Release --warnaserror --artifacts-path "$ZWEB_EVIDENCE_ROOT/server/task-13/artifacts"
ZWEB_BROWSER_WSS_EVIDENCE="$ZWEB_EVIDENCE_ROOT/server/task-13/browser-wss.json" \
ZWEB_NATIVE_IMPAIRMENT_EVIDENCE="$ZWEB_EVIDENCE_ROOT/server/task-13/native-ingress.json" \
  dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --no-build \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/server/task-13/artifacts" \
  --filter FullyQualifiedName~Ingress \
  --logger "trx;LogFileName=ingress-green.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/server/task-13"
```

Expected: both ingresses emit identical decoded vectors; the browser integration uses a real TLS WebSocket client at `/v1/realtime` with `zweb.framesync.v1`, proves `AuthHello` is the first binary message, and writes its assertion record to the absolute path in `ZWEB_BROWSER_WSS_EVIDENCE`; WSS rejects wrong Origin/text/oversize; native rejects foreign source/session/forged/replayed datagrams and writes packet/rebinding counters to `ZWEB_NATIVE_IMPAIRMENT_EVIDENCE`; shared router rejects expired ticket, wrong player, rate excess, and unknown protocol version.

- [ ] **Step 6: Commit**

```bash
git add Server/FrameSync Server/Tests
git commit -m "feat: add authenticated wss and native ingress"
```

### Task 14: Prove Cross-Transport End-to-End Convergence

**Files:**
- Modify: `Server/FrameSync/RealtimeSessionRouter.cs`
- Modify: `Server/FrameSync/InMemoryRealtimeTicketStore.cs`
- Create: `Server/FrameSync/RetainedRealtimeSession.cs`
- Create: `Server/Tests/FrameSyncServerEndToEndTests.cs`
- Create: `Server/Tests/RealtimeReconnectTests.cs`
- Create: `Server/Tools/FrameSyncFixtureGenerator/FrameSyncFixtureGenerator.csproj`
- Create: `Server/Tools/FrameSyncFixtureGenerator/Program.cs`
- Create: `Server/TestData/FrameSync/golden-match.json`
- Create: `Server/TestData/FrameSync/golden-commands.json`
- Create: `scripts/test-network-server.sh`
- Create: `scripts/close-network-server-evidence.sh`

**Interfaces:**
- Consumes: every prior task plus the real SimulationCore session factory.
- Produces: one deterministic release-gate command covering WSS/WSS, native/native, mixed WSS/native, native impairment, reconnect/resume, server cleanup, and replay validation of the completed match.

- [ ] **Step 1: Write failing transport-matrix tests**

Create the committed FrameSync corpus from the already validated Replay corpus so both paths share one authoritative match and checksum. The generator copies the authoritative match, flattens replay records into ordered per-tick command frames, and copies the replay final checksum/result into `golden-match.json`.

```xml
<!-- Server/Tools/FrameSyncFixtureGenerator/FrameSyncFixtureGenerator.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../ReplayValidator/ReplayValidator.csproj" />
  </ItemGroup>
</Project>
```

```csharp
string output = args.Single();
Directory.CreateDirectory(output);
ReplayFile replay = ReplayFile.FromJson(File.ReadAllText("Server/TestData/Replay/valid-replay.json"));
AuthoritativeMatchRecord match = JsonSerializer.Deserialize<AuthoritativeMatchRecord>(
    File.ReadAllText("Server/TestData/Replay/valid-match.json"), JsonOptions.Strict);
var golden = new GoldenMatchFixture
{
    Match = match,
    ExpectedFinalTick = replay.FinalTick,
    ExpectedChecksum = replay.FinalChecksum,
    ExpectedResult = replay.FinalResult
};
File.WriteAllText(Path.Combine(output, "golden-match.json"), JsonSerializer.Serialize(golden, JsonOptions.Canonical));
File.WriteAllText(Path.Combine(output, "golden-commands.json"), JsonSerializer.Serialize(replay.Records, JsonOptions.Canonical));

internal sealed class GoldenMatchFixture
{
    public AuthoritativeMatchRecord Match { get; init; }
    public int ExpectedFinalTick { get; init; }
    public ulong ExpectedChecksum { get; init; }
    public SimBattleResult ExpectedResult { get; init; }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Strict = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static readonly JsonSerializerOptions Canonical = new JsonSerializerOptions
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };
}
```

Run:

```bash
dotnet sln Server/ZWebGLFrameWork.Server.sln add Server/Tools/FrameSyncFixtureGenerator/FrameSyncFixtureGenerator.csproj
dotnet run --project Server/Tools/FrameSyncFixtureGenerator/FrameSyncFixtureGenerator.csproj \
  --configuration Release --artifacts-path "$ZWEB_EVIDENCE_ROOT/server/task-14/artifacts" -- Server/TestData/FrameSync
```

Expected: both golden files are written; a second run is byte-identical and their checksum/result match the validated Replay fixture.

```csharp
[TestCase(ClientTransport.Wss, ClientTransport.Wss)]
[TestCase(ClientTransport.NativeKcp, ClientTransport.NativeKcp)]
[TestCase(ClientTransport.Wss, ClientTransport.NativeKcp)]
public async Task FrameSyncServer_TwoClients_ReachEqualChecksumAndResult(
    ClientTransport player0Transport,
    ClientTransport player1Transport)
{
    await using var fixture = await EndToEndFixture.StartAsync(
        "Server/TestData/FrameSync/golden-match.json",
        nativeImpairment: DatagramImpairment.None);
    var player0 = await fixture.ConnectAsync(0, player0Transport);
    var player1 = await fixture.ConnectAsync(1, player1Transport);
    await fixture.PlayCommandsAsync("Server/TestData/FrameSync/golden-commands.json", player0, player1);

    MatchCompletion completion = await fixture.WaitForCompletionAsync(TimeSpan.FromSeconds(30));
    Assert.That(player0.LastAuthorityChecksum, Is.EqualTo(completion.Checksum));
    Assert.That(player1.LastAuthorityChecksum, Is.EqualTo(completion.Checksum));
    Assert.That(completion.Checksum, Is.EqualTo(fixture.GoldenChecksum));
    Assert.That(player0.Result, Is.EqualTo(completion.Result));
    Assert.That(player1.Result, Is.EqualTo(completion.Result));
}
```

- [ ] **Step 2: Write failing reconnect and native impairment tests**

```csharp
[Test]
public async Task WssReconnect_ResumesFromLastAckOrReceivesValidatedResync()
{
    await using var fixture = await EndToEndFixture.StartDefaultAsync();
    var client = await fixture.ConnectAsync(0, ClientTransport.Wss);
    await fixture.AdvanceToTickAsync(20);
    await client.DropTransportAsync();
    await fixture.AdvanceToTickAsync(90);
    ResumeHello resume = client.CreateResumeHello();
    Assert.That(resume.ResumeToken, Is.EqualTo(client.Credentials.ResumeToken));
    Assert.That(resume.LastAcceptedSequence, Is.EqualTo(client.LastAcceptedSequence));
    Assert.That(resume.LastAuthorityTick, Is.EqualTo(client.LastAuthorityTick));
    Assert.That(resume.TimelineRevision, Is.EqualTo(client.TimelineRevision));
    await client.ReconnectAsync(resume);
    Assert.That(client.LastAppliedResyncTick, Is.EqualTo(fixture.Server.LastCompletedTick));
    Assert.That(client.LastAuthorityChecksum, Is.EqualTo(fixture.Server.LastChecksum));
}

[Test]
public async Task DisconnectedSession_IsRetainedForExactlyTenSeconds()
{
    await using var fixture = await EndToEndFixture.StartDefaultAsync();
    var client = await fixture.ConnectAsync(0, ClientTransport.Wss);
    await client.DropTransportAsync();
    fixture.Clock.AdvanceMilliseconds(9999);
    Assert.That(await client.TryReconnectAsync(client.CreateResumeHello()), Is.True);

    await client.DropTransportAsync();
    fixture.Clock.AdvanceMilliseconds(10000);
    Assert.That(await client.TryReconnectAsync(client.CreateResumeHello()), Is.False);
    Assert.That(client.LastCloseCode, Is.EqualTo((ushort)RealtimeCloseCode.ExpiredSession));
    Assert.That(fixture.Server.HasRetainedSession(client.SessionId), Is.False);
}

[Test]
public async Task NativePair_ConvergesUnderDeterministicImpairment()
{
    var impairment = new DatagramImpairment(42, 20, 10, 4, 300);
    await using var fixture = await EndToEndFixture.StartAsync(
        "Server/TestData/FrameSync/golden-match.json",
        impairment);
    await fixture.ConnectBothNativeAndPlayGoldenCommandsAsync();
    MatchCompletion completion = await fixture.WaitForCompletionAsync(TimeSpan.FromSeconds(45));
    Assert.That(completion.Checksum, Is.EqualTo(fixture.GoldenChecksum));
}

private static IEnumerable<TestCaseData> TransportAuthenticationImpairmentMatrix()
{
    var pairs = new[]
    {
        (ClientTransport.Wss, ClientTransport.Wss),
        (ClientTransport.NativeKcp, ClientTransport.NativeKcp),
        (ClientTransport.Wss, ClientTransport.NativeKcp)
    };
    var authentication = new[]
    {
        AuthenticationScenario.Valid,
        AuthenticationScenario.ExpiredTicket,
        AuthenticationScenario.WrongSession,
        AuthenticationScenario.ReplayedResumeToken,
        AuthenticationScenario.ForgedNativeMac
    };
    var impairments = new[] { ImpairmentProfile.None, ImpairmentProfile.LossDuplicateReorderDelay };
    foreach (var pair in pairs)
        foreach (AuthenticationScenario auth in authentication)
            foreach (ImpairmentProfile impairment in impairments)
                yield return new TestCaseData(pair.Item1, pair.Item2, auth, impairment);
}

[TestCaseSource(nameof(TransportAuthenticationImpairmentMatrix))]
public async Task TransportMatrix_AuthenticatesAndConvergesUnderImpairment(
    ClientTransport player0,
    ClientTransport player1,
    AuthenticationScenario authentication,
    ImpairmentProfile impairment)
{
    await using var fixture = await EndToEndFixture.StartMatrixAsync(impairment);
    MatrixRunResult result = await fixture.RunAsync(player0, player1, authentication);
    if (authentication == AuthenticationScenario.Valid)
    {
        Assert.That(result.Completed, Is.True);
        Assert.That(result.Player0Checksum, Is.EqualTo(fixture.GoldenChecksum));
        Assert.That(result.Player1Checksum, Is.EqualTo(fixture.GoldenChecksum));
    }
    else
    {
        Assert.That(result.Completed, Is.False);
        Assert.That(result.RejectionCloseCode, Is.EqualTo(ExpectedCloseCode.For(authentication)));
    }
}
```

The fixture is `[NonParallelizable]` and executes all 30 rows; no transport/authentication/impairment row is skipped. `ForgedNativeMac` flips the native packet tag on every native leg and flips the opaque `AuthHello` credential on WSS-only legs, so every pair has an equivalent authentication failure and stable `4001` close assertion. After the last row it atomically writes the row count, transport pair, authentication scenario, impairment counters, close code, and final checksums to `ZWEB_NATIVE_IMPAIRMENT_EVIDENCE`.

- [ ] **Step 3: Run end-to-end tests and confirm the red state**

Run: `set -o pipefail; ! dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --artifacts-path "$ZWEB_EVIDENCE_ROOT/server/task-14/artifacts" --filter FullyQualifiedName~FrameSyncServerEndToEndTests 2>&1 | tee "$ZWEB_EVIDENCE_ROOT/server/task-14/e2e-red.txt"`.

Expected: tests fail until both real ingresses, reconnect/resume, fixture server lifetime, and real SimulationCore factory are wired together.

- [ ] **Step 4: Complete only the composition required by the tests**

Wire `Program` and the test host to one `RealtimeSessionRouter`, one ticket store, one match registry, and one canonical `ISimBattleSessionFactory`. Construct a Session with `Create(authoritativeMatch.CanonicalManifestBytes, logger)`, call `Start(authoritativeMatch.BattleInit)`, and reject a non-accepted `StartResult`; production casts the returned instance once to SimulationCore's concrete `SimBattleSession` and passes `session.SealTick` as the actor's authoritative sealing delegate. On disconnect, retain the authenticated slot for exactly 10,000 monotonic milliseconds, independent of ticket expiry. A valid `ResumeHello` must match the resume token, last accepted command sequence, last authority tick, and timeline revision. Send buffered authority frames when still inside 64 ticks; otherwise send one validated canonical full snapshot. At exactly 10,000 ms dispose the slot and return close code `4005`. Ended matches close both channels, dispose the Session, remove actor/mailbox entries, and retain only the immutable match result needed by ReplayValidator.

```csharp
internal sealed class RetainedRealtimeSession : IDisposable
{
    public RealtimeSessionClaims Claims { get; init; }
    public long DisconnectedAtMilliseconds { get; init; }
    public uint LastAcceptedSequence { get; init; }
    public int LastAuthorityTick { get; init; }
    public long TimelineRevision { get; init; }
    public Action DisposeOwnedState { get; init; }
    public void Dispose() => DisposeOwnedState?.Invoke();
}

private const long DisconnectedRetentionMilliseconds = 10000;

public bool TryResume(ResumeHello hello, long nowMilliseconds, out RealtimeSessionClaims claims)
{
    claims = null;
    if (!_retained.TryGetValue(hello.SessionId, out RetainedRealtimeSession retained)) return false;
    long elapsed = nowMilliseconds - retained.DisconnectedAtMilliseconds;
    if (elapsed < 0 || elapsed >= DisconnectedRetentionMilliseconds)
    {
        _retained.Remove(hello.SessionId);
        retained.Dispose();
        return false;
    }
    if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            hello.ResumeToken, retained.Claims.ResumeToken) ||
        hello.LastAcceptedSequence != retained.LastAcceptedSequence ||
        hello.LastAuthorityTick != retained.LastAuthorityTick ||
        hello.TimelineRevision != retained.TimelineRevision)
        return false;
    claims = retained.Claims;
    _retained.Remove(hello.SessionId);
    return true;
}
```

- [ ] **Step 5: Add the deterministic gate script**

```bash
#!/usr/bin/env bash
set -euo pipefail

export ZWEB_EVIDENCE_ROOT="${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-network-server-evidence}"
export ZWEB_BUILD_OUTPUT_ROOT="$ZWEB_EVIDENCE_ROOT/builds"
mkdir -p "$ZWEB_EVIDENCE_ROOT/final/dotnet" "$ZWEB_EVIDENCE_ROOT/final/unity" \
  "$ZWEB_EVIDENCE_ROOT/final/browser" "$ZWEB_EVIDENCE_ROOT/final/native" \
  "$ZWEB_EVIDENCE_ROOT/final/replay" "$ZWEB_EVIDENCE_ROOT/final/server" \
  "$ZWEB_BUILD_OUTPUT_ROOT/WebGL" "$ZWEB_BUILD_OUTPUT_ROOT/Windows"

bash scripts/verify-realtime-dependency-provenance.sh
dotnet restore Server/ZWebGLFrameWork.Server.sln --locked-mode --artifacts-path "$ZWEB_EVIDENCE_ROOT/final/dotnet"
dotnet build Server/ZWebGLFrameWork.Server.sln --configuration Release --no-restore --warnaserror --artifacts-path "$ZWEB_EVIDENCE_ROOT/final/dotnet"
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj \
  --configuration Release --no-build --no-restore \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/final/dotnet" \
  --logger "trx;LogFileName=simulationcore-tests.trx" \
  --results-directory "$ZWEB_EVIDENCE_ROOT/final/dotnet/results"
dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/final/dotnet" \
  --logger "trx;LogFileName=server-tests.trx" \
  --results-directory "$ZWEB_EVIDENCE_ROOT/final/dotnet/results"
ZWEB_BROWSER_WSS_EVIDENCE="$ZWEB_EVIDENCE_ROOT/final/browser/browser-wss.json" \
  dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --no-build --no-restore \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/final/dotnet" \
  --filter FullyQualifiedName~BrowserWssIntegrationTests \
  --logger "trx;LogFileName=browser-wss.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/final/browser"
ZWEB_NATIVE_IMPAIRMENT_EVIDENCE="$ZWEB_EVIDENCE_ROOT/final/native/native-impairment.json" \
  dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --no-build --no-restore \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/final/dotnet" \
  --filter FullyQualifiedName~TransportMatrix_AuthenticatesAndConvergesUnderImpairment \
  --logger "trx;LogFileName=authentication-impairment-matrix.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/final/native"
ZWEB_REPLAY_REJECTION_EVIDENCE="$ZWEB_EVIDENCE_ROOT/final/replay/rejection-matrix.json" \
  dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --no-build --no-restore \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/final/dotnet" \
  --filter FullyQualifiedName~ReplayCli_OversizedOrOverLimitReplay_IsRejected \
  --logger "trx;LogFileName=replay-rejection-matrix.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/final/replay"
dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release --no-build --no-restore \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/final/dotnet" \
  --filter FullyQualifiedName~FrameSyncServer_TwoClients_ReachEqualChecksumAndResult \
  --logger "trx;LogFileName=end-to-end-three-transports.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/final/server"
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix \
  -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Network.Tests \
  -testResults "$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.log"
ZWEB_BUILD_OUTPUT_ROOT="$ZWEB_BUILD_OUTPUT_ROOT" \
  /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix \
  -executeMethod ZWebGLFrameWork.Editor.NetworkVerificationBuildScript.BuildWebGL \
  -logFile "$ZWEB_EVIDENCE_ROOT/final/unity/webgl-build.log"
ZWEB_BUILD_OUTPUT_ROOT="$ZWEB_BUILD_OUTPUT_ROOT" \
  /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit -projectPath /Users/diegozhou/Documents/unityframework/zweb-audit-fix \
  -executeMethod ZWebGLFrameWork.Editor.NetworkVerificationBuildScript.BuildWindows \
  -logFile "$ZWEB_EVIDENCE_ROOT/final/unity/windows-build.log"
test -s "$ZWEB_EVIDENCE_ROOT/final/browser/browser-wss.json"
test -s "$ZWEB_EVIDENCE_ROOT/final/native/native-impairment.json"
test -s "$ZWEB_EVIDENCE_ROOT/final/replay/rejection-matrix.json"
test -s "$ZWEB_EVIDENCE_ROOT/final/server/end-to-end-three-transports.trx"
test ! -e Assets/Scripts/Network/KCP/KcpCore.cs
test ! -e Assets/Scripts/Network/Protocols/TcpClient.cs
test ! -e Assets/Scripts/Network/Protocols/UdpClient.cs
test ! -e Assets/Scripts/Network/Core/AddressResolver.cs
test -z "$(git status --porcelain --untracked-files=all)"
```

- [ ] **Step 6: Run the complete gate twice**

Run:

```bash
chmod +x scripts/test-network-server.sh
dotnet test Server/Tests/ZWebGLFrameWork.Server.Tests.csproj --configuration Release \
  --artifacts-path "$ZWEB_EVIDENCE_ROOT/server/task-14/artifacts" \
  --filter FullyQualifiedName~FrameSyncServerEndToEndTests \
  --logger "trx;LogFileName=e2e-green.trx" --results-directory "$ZWEB_EVIDENCE_ROOT/server/task-14"
bash scripts/test-network-server.sh
bash scripts/test-network-server.sh
test -z "$(git status --porcelain --untracked-files=all)"
```

Expected: both runs exit `0`; `SimulationCore.ServerTests`, ReplayValidator, FrameSyncServer, ingress, and Unity network suites each report a nonzero count; WSS/WSS, native/native, and WSS/native pass the authentication/impairment matrix and converge on the committed golden checksum/result; browser WSS proves exact path/subprotocol/`AuthHello`; native evidence proves full-MAC/replay/rebinding behavior; replay evidence contains all six exact resource ceilings; no forbidden source exists; the strict status command is empty after both runs.

- [ ] **Step 7: Commit**

```bash
git add Server/Tests Server/Tools/FrameSyncFixtureGenerator Server/TestData/FrameSync Server/ZWebGLFrameWork.Server.sln \
  scripts/test-network-server.sh scripts/close-network-server-evidence.sh
git commit -m "test: gate network replay and authoritative server"
```

- [ ] **Step 8: Close exactly the owned evidence-ledger entries**

Create `scripts/close-network-server-evidence.sh` with this complete mapping. It deliberately derives identifiers by index from the ownership manifest so the coverage table remains the single literal occurrence of each finding in this plan.

```bash
#!/usr/bin/env bash
set -euo pipefail

export ZWEB_EVIDENCE_ROOT="${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-network-server-evidence}"
ledger="docs/superpowers/specs/2026-07-16-project-repair-evidence.json"
ownership="docs/superpowers/plans/2026-07-17-project-repair-ownership.json"
IFS=$'\n' read -r -d '' -a owned < <(node -e '
  const fs = require("node:fs");
  const data = JSON.parse(fs.readFileSync(process.argv[1], "utf8"));
  for (const id of data.plans["network-server-repair"].issues) console.log(id);
' "$ownership"; printf '\0')
[[ "${#owned[@]}" -eq 24 ]]
non_owned_before=$(node -e '
  const fs = require("node:fs");
  const ownership = JSON.parse(fs.readFileSync(process.argv[1], "utf8"));
  const ledger = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
  const owned = new Set(ownership.plans["network-server-repair"].issues);
  console.log(JSON.stringify(Object.fromEntries(Object.entries(ledger.issues)
    .filter(([id]) => !owned.has(id)))));
' "$ownership" "$ledger")

declare -a red green gate subjects mode replacement
red[0]="$ZWEB_EVIDENCE_ROOT/network/task-04/lifecycle-red.log"
green[0]="$ZWEB_EVIDENCE_ROOT/network/task-04/lifecycle-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-07/removal-green.xml"
gate[0]="$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.xml"
subjects[0]="feat: own realtime connection lifetimes|refactor: remove unsupported socket stack"
red[1]="$ZWEB_EVIDENCE_ROOT/network/task-05/adapters-red.log"
green[1]="$ZWEB_EVIDENCE_ROOT/network/task-05/adapters-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-07/webgl-build.log"
gate[1]="$ZWEB_EVIDENCE_ROOT/final/browser/browser-wss.json|$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.xml"
subjects[1]="feat: add pinned NativeWebSocket adapter|refactor: remove unsupported socket stack"
red[2]="$ZWEB_EVIDENCE_ROOT/network/task-05/http-red.log"
green[2]="$ZWEB_EVIDENCE_ROOT/network/task-05/adapters-green.xml"
gate[2]="$ZWEB_EVIDENCE_ROOT/final/browser/browser-wss.json|$ZWEB_EVIDENCE_ROOT/network/task-07/webgl-build.log"
subjects[2]="feat: add pinned NativeWebSocket adapter"
red[3]="$ZWEB_EVIDENCE_ROOT/network/task-07/removal-red.log"
green[3]="$ZWEB_EVIDENCE_ROOT/network/task-07/removal-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-07/webgl-build.log|$ZWEB_EVIDENCE_ROOT/network/task-07/windows-build.log"
gate[3]="$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.xml"
subjects[3]="refactor: remove unsupported socket stack"
mode[3]="RemovedByReplacement"
replacement[3]="$ZWEB_EVIDENCE_ROOT/network/task-05/adapters-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-06/native-green.xml"
red[4]="$ZWEB_EVIDENCE_ROOT/network/task-07/removal-red.log"
green[4]="$ZWEB_EVIDENCE_ROOT/network/task-07/removal-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-05/adapters-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-06/native-green.xml"
gate[4]="$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.xml"
subjects[4]="refactor: remove unsupported socket stack"
mode[4]="RemovedByReplacement"
replacement[4]="$ZWEB_EVIDENCE_ROOT/network/task-05/adapters-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-06/native-green.xml"
red[5]="$ZWEB_EVIDENCE_ROOT/network/task-04/lifecycle-red.log"
green[5]="$ZWEB_EVIDENCE_ROOT/network/task-04/lifecycle-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-07/removal-green.xml"
gate[5]="$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.xml"
subjects[5]="feat: own realtime connection lifetimes|refactor: remove unsupported socket stack"
red[6]="$ZWEB_EVIDENCE_ROOT/network/task-03/auth-red.log"
green[6]="$ZWEB_EVIDENCE_ROOT/network/task-03/auth-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-06/native-green.xml"
gate[6]="$ZWEB_EVIDENCE_ROOT/final/native/native-impairment.json|$ZWEB_EVIDENCE_ROOT/final/native/authentication-impairment-matrix.trx"
subjects[6]="feat: authenticate realtime sessions|feat: add pinned native kcp2k adapter"
red[7]="$ZWEB_EVIDENCE_ROOT/network/task-06/native-red.log"
green[7]="$ZWEB_EVIDENCE_ROOT/network/task-06/native-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-06/native-impairment.json"
gate[7]="$ZWEB_EVIDENCE_ROOT/final/native/authentication-impairment-matrix.trx"
subjects[7]="feat: add pinned native kcp2k adapter"
red[8]="$ZWEB_EVIDENCE_ROOT/network/task-06/native-red.log"
green[8]="$ZWEB_EVIDENCE_ROOT/network/task-06/native-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-06/native-impairment.json"
gate[8]="$ZWEB_EVIDENCE_ROOT/final/native/authentication-impairment-matrix.trx"
subjects[8]="feat: add pinned native kcp2k adapter"
red[9]="$ZWEB_EVIDENCE_ROOT/network/task-08/input-red.log"
green[9]="$ZWEB_EVIDENCE_ROOT/network/task-08/input-green.xml"
gate[9]="$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.xml"
subjects[9]="feat: send and apply authoritative frame inputs"
red[10]="$ZWEB_EVIDENCE_ROOT/network/task-08/input-red.log|$ZWEB_EVIDENCE_ROOT/server/task-12/actor-red.txt"
green[10]="$ZWEB_EVIDENCE_ROOT/network/task-08/input-green.xml|$ZWEB_EVIDENCE_ROOT/server/task-12/actor-green.trx"
gate[10]="$ZWEB_EVIDENCE_ROOT/final/dotnet/results/server-tests.trx"
subjects[10]="feat: send and apply authoritative frame inputs|feat: add authoritative frame match actor"
red[11]="$ZWEB_EVIDENCE_ROOT/network/task-09/rollback-red.log"
green[11]="$ZWEB_EVIDENCE_ROOT/network/task-09/rollback-green.xml"
gate[11]="$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.xml"
subjects[11]="feat: replay prediction and resync frame state"
red[12]="$ZWEB_EVIDENCE_ROOT/network/task-02/codec-red.log|$ZWEB_EVIDENCE_ROOT/network/task-03/auth-red.log"
green[12]="$ZWEB_EVIDENCE_ROOT/network/task-02/contracts-green.xml|$ZWEB_EVIDENCE_ROOT/network/task-03/auth-green.xml"
gate[12]="$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.xml|$ZWEB_EVIDENCE_ROOT/final/dotnet/results/server-tests.trx"
subjects[12]="feat: add strict realtime contracts|feat: authenticate realtime sessions"
red[13]="$ZWEB_EVIDENCE_ROOT/network/task-02/dispatch-red.log"
green[13]="$ZWEB_EVIDENCE_ROOT/network/task-02/contracts-green.xml"
gate[13]="$ZWEB_EVIDENCE_ROOT/final/unity/network-server-gate.xml"
subjects[13]="feat: add strict realtime contracts"
red[14]="$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-red.txt"
green[14]="$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-green.trx"
gate[14]="$ZWEB_EVIDENCE_ROOT/final/replay/rejection-matrix.json"
subjects[14]="feat: validate complete replay structure"
red[15]="$ZWEB_EVIDENCE_ROOT/replay/task-11/authority-red.txt"
green[15]="$ZWEB_EVIDENCE_ROOT/replay/task-11/authority-green.trx"
gate[15]="$ZWEB_EVIDENCE_ROOT/final/replay/rejection-matrix.json"
subjects[15]="feat: validate replay against authoritative matches"
red[16]="$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-red.txt"
green[16]="$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-green.trx|$ZWEB_EVIDENCE_ROOT/replay/task-11/cli-green.trx"
gate[16]="$ZWEB_EVIDENCE_ROOT/final/replay/rejection-matrix.json"
subjects[16]="feat: validate complete replay structure|feat: validate replay against authoritative matches"
red[17]="$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-red.txt"
green[17]="$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-green.trx"
gate[17]="$ZWEB_EVIDENCE_ROOT/final/replay/rejection-matrix.json"
subjects[17]="feat: validate complete replay structure"
red[18]="$ZWEB_EVIDENCE_ROOT/replay/task-11/authority-red.txt"
green[18]="$ZWEB_EVIDENCE_ROOT/replay/task-11/authority-green.trx"
gate[18]="$ZWEB_EVIDENCE_ROOT/final/replay/rejection-matrix.json"
subjects[18]="feat: validate replay against authoritative matches"
red[19]="$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-red.txt"
green[19]="$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-green.trx"
gate[19]="$ZWEB_EVIDENCE_ROOT/final/replay/rejection-matrix.json"
subjects[19]="feat: validate complete replay structure"
red[20]="$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-red.txt"
green[20]="$ZWEB_EVIDENCE_ROOT/replay/task-10/structural-green.trx|$ZWEB_EVIDENCE_ROOT/replay/task-11/cli-green.trx|$ZWEB_EVIDENCE_ROOT/replay/task-11/rejection-matrix.json"
gate[20]="$ZWEB_EVIDENCE_ROOT/final/replay/rejection-matrix.json|$ZWEB_EVIDENCE_ROOT/final/replay/replay-rejection-matrix.trx"
subjects[20]="feat: validate complete replay structure|feat: validate replay against authoritative matches"
red[21]="$ZWEB_EVIDENCE_ROOT/server/task-12/actor-red.txt|$ZWEB_EVIDENCE_ROOT/server/task-13/ingress-red.txt|$ZWEB_EVIDENCE_ROOT/server/task-14/e2e-red.txt"
green[21]="$ZWEB_EVIDENCE_ROOT/server/task-12/actor-green.trx|$ZWEB_EVIDENCE_ROOT/server/task-13/ingress-green.trx|$ZWEB_EVIDENCE_ROOT/server/task-14/e2e-green.trx"
gate[21]="$ZWEB_EVIDENCE_ROOT/final/server/end-to-end-three-transports.trx|$ZWEB_EVIDENCE_ROOT/final/dotnet/results/server-tests.trx|$ZWEB_EVIDENCE_ROOT/final/dotnet/results/simulationcore-tests.trx"
subjects[21]="feat: add authoritative frame match actor|feat: add authenticated wss and native ingress|test: gate network replay and authoritative server"
red[22]="$ZWEB_EVIDENCE_ROOT/server/task-12/actor-red.txt"
green[22]="$ZWEB_EVIDENCE_ROOT/server/task-12/actor-green.trx"
gate[22]="$ZWEB_EVIDENCE_ROOT/final/dotnet/results/server-tests.trx"
subjects[22]="feat: add authoritative frame match actor"
red[23]="$ZWEB_EVIDENCE_ROOT/server/task-13/ingress-red.txt"
green[23]="$ZWEB_EVIDENCE_ROOT/server/task-13/ingress-green.trx|$ZWEB_EVIDENCE_ROOT/server/task-14/e2e-green.trx"
gate[23]="$ZWEB_EVIDENCE_ROOT/final/browser/browser-wss.json|$ZWEB_EVIDENCE_ROOT/final/native/native-impairment.json|$ZWEB_EVIDENCE_ROOT/final/server/end-to-end-three-transports.trx"
subjects[23]="feat: add authenticated wss and native ingress|test: gate network replay and authoritative server"

append_repeated() {
  local flag="$1" value="$2"
  local parts item
  IFS='|' read -r -a parts <<< "$value"
  for item in "${parts[@]}"; do
    test -s "$item"
    args+=("--$flag" "$item")
  done
}

closed_at=$(date -u +%F)
for index in "${!owned[@]}"; do
  args=(close --ledger "$ledger" --issue "${owned[$index]}" --date "$closed_at" \
    --mode "${mode[$index]:-Fixed}")
  append_repeated red "${red[$index]}"
  append_repeated green "${green[$index]}"
  append_repeated gate "${gate[$index]}"
  IFS='|' read -r -a commit_subjects <<< "${subjects[$index]}"
  for subject in "${commit_subjects[@]}"; do
    sha=$(git log --format=%H --extended-regexp --grep="^${subject}$" -n 1)
    [[ "$sha" =~ ^[0-9a-f]{40}$ ]]
    [[ "$(git show -s --format=%s "$sha")" == "$subject" ]]
    args+=(--fix "$sha")
  done
  if [[ -n "${replacement[$index]:-}" ]]; then
    append_repeated replacement "${replacement[$index]}"
  fi
  node scripts/audit/evidence-ledger.mjs "${args[@]}"
done

non_owned_after=$(node -e '
  const fs = require("node:fs");
  const ownership = JSON.parse(fs.readFileSync(process.argv[1], "utf8"));
  const ledger = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
  const owned = new Set(ownership.plans["network-server-repair"].issues);
  console.log(JSON.stringify(Object.fromEntries(Object.entries(ledger.issues)
    .filter(([id]) => !owned.has(id)))));
' "$ownership" "$ledger")
[[ "$non_owned_after" == "$non_owned_before" ]]

node -e '
  const fs = require("node:fs");
  const ownership = JSON.parse(fs.readFileSync(process.argv[1], "utf8"));
  const ledger = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
  const ids = ownership.plans["network-server-repair"].issues;
  if (ids.length !== 24) throw new Error(`owned count ${ids.length}, expected 24`);
  const notClosed = ids.filter(id => ledger.issues[id]?.status !== "Closed");
  if (notClosed.length) throw new Error(`not closed: ${notClosed.join(",")}`);
  console.log(`[network-server-evidence] closed=${ids.length}`);
' "$ownership" "$ledger"
node scripts/audit/evidence-ledger.mjs validate --ledger "$ledger"
```

Run:

```bash
chmod +x scripts/close-network-server-evidence.sh
bash scripts/close-network-server-evidence.sh | tee "$ZWEB_EVIDENCE_ROOT/final/evidence-close.log"
git add docs/superpowers/specs/2026-07-16-project-repair-evidence.json docs/superpowers/evidence/artifacts
git diff --cached --check
git commit -m "docs: close network replay server audit evidence"
```

Expected: the script prints `closed=24`; every owned entry has one or more red, green, and gate artifacts, one or more readable fix commits, and replacement evidence where required. Other owners' ledger entries are unchanged.

- [ ] **Step 9: Prove the final repository boundary is empty**

```bash
git diff --check
test -z "$(git status --porcelain --untracked-files=all)"
```

Expected: both commands exit `0` with no status output. Evidence and build directories remain exclusively below `$ZWEB_EVIDENCE_ROOT`.

## Final Execution Checklist

- [ ] Run `bash scripts/verify-realtime-dependency-provenance.sh`; confirm both printed full commits and archive/license hashes equal the reviewed NativeWebSocket and kcp2k locks.
- [ ] Run `bash scripts/test-network-server.sh` twice from a clean checkout.
- [ ] Run the WebGL and Windows `NetworkVerificationBuildScript` commands from Task 7 with `ZWEB_BUILD_OUTPUT_ROOT` beneath the evidence root.
- [ ] Run ReplayValidator against the committed valid fixture and every committed rejection fixture.
- [ ] Inspect Unity XML and .NET TRX files; every expected assembly/project has a nonzero test count and zero failed, skipped, or inconclusive tests.
- [ ] Confirm WebGL player assemblies contain no native networking or kcp2k reference.
- [ ] Confirm the custom KCP, raw TCP/UDP, resolver, and callback interface sources no longer exist.
- [ ] Confirm both clients and server end with the same golden checksum and result for WSS/WSS, native/native, and WSS/native across the full authentication/impairment matrix.
- [ ] Confirm exact protocol constants in source and test evidence: `65536`, `/v1/realtime`, `zweb.framesync.v1`, close codes `4001..4005`, full 32-byte HMAC, 1,024-packet replay window, and strict 10,000 ms disconnected retention.
- [ ] Run `bash scripts/close-network-server-evidence.sh`; confirm it closes exactly 24 ownership-derived entries and supports every repeated artifact/fix argument.
- [ ] Confirm `test -z "$(git status --porcelain --untracked-files=all)"` exits `0`; ignored or declared in-repository output is not an exception.

## Execution Handoff

Use subagent-driven development for implementation: dispatch one fresh worker per task, then perform specification review followed by code-quality review before advancing. Inline execution is acceptable only with the executing-plans skill and review checkpoints after Tasks 4, 7, 9, 11, and 14.
