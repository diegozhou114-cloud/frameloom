# Unity Compile Error Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the nine current Unity C# compiler errors while preserving simulation, resource-production, and connection-generation behavior.

**Architecture:** Correct each call site to match its existing API contract. Store the three generation-guarded network delegates so the exact subscribed instances can be removed during disconnect, without changing public interfaces.

**Tech Stack:** Unity 2022.3.62f3c1, C# 9.0, .NET Standard 2.1, Unity Editor compilation pipeline.

## Global Constraints

- Keep the original repair limited to the four production files named in the approved design.
- For compiler errors revealed only after that repair, modify only the serialization DTOs and their existing round-trip test imports; do not add packages or UnityEngine dependencies.
- Do not change public APIs, gameplay behavior, or protocol behavior.
- Do not refactor adjacent code or clean unrelated warnings.
- Treat the running Unity Editor's fresh compilation cycle as authoritative; generated `.csproj` files currently contain stale references.

---

### Task 1: Repair The Nine Compile Errors

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Systems/SimAttackSystem.cs:13`
- Modify: `Assets/Scripts/SimulationCore/Systems/SimMovementSystem.cs:11`
- Modify: `Assets/Scripts/Gameplay/Economy/ResourceProducer.cs:1`
- Modify: `Assets/Scripts/Network/NetworkManager.cs:29`
- Test: `/Users/diegozhou/Library/Logs/Unity/Editor.log`

**Interfaces:**
- Consumes: `SimWorld.RefAction<TA, TB, TC>`, `Action`, `Action<string>`, and `INetworkClient` events.
- Produces: The same existing systems and `NetworkManager` public API, with compiler-valid callbacks and exact event unsubscription.

- [x] **Step 1: Verify the RED baseline**

Run:

```bash
rg -n "error CS(1001|0103|1593)" /Users/diegozhou/Library/Logs/Unity/Editor.log | tail -9
wc -l /Users/diegozhou/Library/Logs/Unity/Editor.log
```

Expected: nine latest errors across the four target files. Record the line count so final verification examines only log lines appended after this point.

- [x] **Step 2: Make both writable component lambdas explicit**

In `SimAttackSystem.Update`, use:

```csharp
World.ForEachEntities<SimAttackComponent, SimTransformComponent>(
    (SimEntity entity, ref SimAttackComponent attack, ref SimTransformComponent transform) =>
{
```

In `SimMovementSystem.Update`, use:

```csharp
World.ForEachEntities<SimTransformComponent, SimMovementComponent>(
    (SimEntity entity, ref SimTransformComponent transform, ref SimMovementComponent movement) =>
{
```

- [x] **Step 3: Resolve `System.Math` in resource production**

Add the import before `using UnityEngine;`:

```csharp
using System;
using UnityEngine;
```

Keep all three existing `Math.Min` expressions unchanged.

- [x] **Step 4: Store and forward network event callbacks**

Add fields beside `_connectGeneration`:

```csharp
private Action _connectedHandler;
private Action<string> _disconnectedHandler;
private Action<string> _connectFailedHandler;
```

Replace the three subscriptions in `Connect` with:

```csharp
_connectedHandler = () =>
{
    if (_connectGeneration != myGeneration) return;
    HandleConnected();
};
_disconnectedHandler = reason =>
{
    if (_connectGeneration != myGeneration) return;
    HandleDisconnected(reason);
};
_connectFailedHandler = error =>
{
    if (_connectGeneration != myGeneration) return;
    HandleConnectFailed(error);
};

client.OnConnected += _connectedHandler;
client.OnDisconnected += _disconnectedHandler;
client.OnConnectFailed += _connectFailedHandler;
client.OnDataReceived += HandleDataReceived;
```

Replace the three corresponding removals in `Disconnect` with:

```csharp
_currentClient.OnConnected -= _connectedHandler;
_currentClient.OnDisconnected -= _disconnectedHandler;
_currentClient.OnConnectFailed -= _connectFailedHandler;
```

After `_currentClient = null;`, clear the stored handlers:

```csharp
_connectedHandler = null;
_disconnectedHandler = null;
_connectFailedHandler = null;
```

- [x] **Step 5: Verify the GREEN compilation cycle**

Wait for Unity to detect the edits, then inspect only Editor log lines appended after the Step 1 baseline:

```bash
tail -n +2772 /Users/diegozhou/Library/Logs/Unity/Editor.log
```

Expected: the original nine `CS1001`, `CS0103`, and `CS1593` errors are absent. The first completed cycle reveals the separate `System.Text.Json` compatibility errors handled by Task 2.

- [x] **Step 6: Check the final change boundary**

Run:

```bash
git diff --check
git diff -- Assets/Scripts/SimulationCore/Systems/SimAttackSystem.cs Assets/Scripts/SimulationCore/Systems/SimMovementSystem.cs Assets/Scripts/Gameplay/Economy/ResourceProducer.cs Assets/Scripts/Network/NetworkManager.cs
```

Expected: no whitespace errors; every production-code hunk maps directly to Steps 2-4. Do not include pre-existing Unity metadata or layout changes.

---

### Task 2: Replace The Unity-Incompatible Replay JSON Backend

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Command/ReplayFile.cs:1`
- Modify: `Assets/Scripts/SimulationCore/Interfaces/ISimInitializer.cs:1`
- Modify: `Assets/Tests/SimulationCore/SimReplayRoundTripTests.cs:1`
- Test: `Assets/Tests/SimulationCore/SimReplayRoundTripTests.cs:184`

**Interfaces:**
- Consumes: `DataContractJsonSerializer`, `DataContract`, and `DataMember` from the BCL available to both Unity 2022 and .NET 8.
- Produces: unchanged `ReplayFile.ToJson()`, `ReplayFile.FromJson(string)`, and read-only `SimEntitySpawnInfo` accessors.

- [x] **Step 1: Confirm the compatibility RED baseline**

The Unity compile cycle after Task 1 must contain `CS0234` for `System.Text.Json` and `CS0246` for `JsonInclude` or `JsonConstructor`. The existing `ReplayFile_JsonRoundTrip_PreservesData` test is unable to compile, which is the failing regression test.

- [x] **Step 2: Replace JSON annotations and serializer**

In `ReplayFile.cs`, replace `System.Text.Json` imports with:

```csharp
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
```

Annotate each replay DTO with `[DataContract]` and each serialized member with an explicitly ordered `[DataMember(Order = N)]`. Keep names, types, defaults, and public APIs unchanged.

Implement serialization with a local `DataContractJsonSerializer(typeof(ReplayFile))`, `MemoryStream`, and UTF-8 encoding. Use `JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true)` for indented output. Deserialize by reading the UTF-8 bytes through the same serializer.

- [x] **Step 3: Keep spawn data externally immutable**

In `ISimInitializer.cs`, replace `System.Text.Json.Serialization` with `System.Runtime.Serialization`, add `[DataContract]` to `SimEntitySpawnInfo`, and replace its four JSON attributes with ordered `[DataMember]` attributes. Keep the readonly struct, readonly fields, and constructor unchanged.

- [x] **Step 4: Remove test-only unavailable imports**

Delete the unused `System.Text.Json` and `System.Text.Json.Serialization` imports from `SimReplayRoundTripTests.cs`. Do not change test behavior.

- [ ] **Step 5: Verify Unity and server compatibility**

Trigger `Assets > Refresh`, then inspect Editor log lines from 3421 onward. Expected: no `error CS` entries and a completed script reload. Run the existing Unity SimulationCore tests, including `ReplayFile_JsonRoundTrip_PreservesData`, and require zero failures.

Run:

```bash
dotnet build Server/ReplayValidator/ReplayValidator.csproj --no-restore
```

Expected: build succeeds with zero errors.

Observed: Unity compilation succeeded, the JSON round-trip test passed, and ReplayValidator built
with zero warnings/errors. The full SimulationCore suite remains at 60/67 passing because of seven
pre-existing battle, replay/rollback, and archetype behavior failures listed under Task 3.

---

### Task 3: Restore SimulationCore Runtime Compatibility

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Network/FrameInput.cs:28`
- Modify: `Assets/Scripts/SimulationCore/Network/KcpTransport.cs:51`
- Modify: `Assets/Scripts/SimulationCore/Orchestrator/SimOrchestrator.cs:1`
- Modify: `Assets/Scripts/SimulationCore/Math/LogicInt.cs:53`
- Modify: `Assets/Tests/SimulationCore/LogicIntTests.cs:194`

**Interfaces:**
- Consumes: BCL `System.Array` and `System.Math` plus the existing `LogicInt` operators.
- Produces: the same no-Unity SimulationCore API and exact scaled multiplication when the final value fits in `long`.

- [x] **Step 1: Replace the unsupported multiplication test**

Replace the test that directly calls the unavailable three-argument `Math.BigMul` overload with `Multiply_LargeRawValues_DoesNotOverflowIntermediate`. Construct raw operands whose direct product exceeds `long.MaxValue`, calculate the expected scaled result with `decimal`, and assert the `LogicInt` result matches.

- [x] **Step 2: Implement portable scaled multiplication**

Replace `Math.BigMul` with quotient/remainder decomposition around `LogicInt.Scale`. Multiply and add only non-negative terms, clamp if a term would exceed `long.MaxValue`, then restore the result sign. This avoids both unsupported APIs and intermediate overflow without allocations.

- [x] **Step 3: Remove Unity-only diagnostics from the pure core**

Keep `FrameInput` clamping but remove its three `UnityEngine.Debug` calls. Keep `KcpTransport` dropping malformed packets but remove the Unity warning. Remove placeholder command logs and their one-time warning state from `SimOrchestrator`; do not add an engine reference.

- [x] **Step 4: Restore BCL name resolution**

Add `using System;` to `SimOrchestrator.cs` so its existing `Array` and `Math` calls resolve.

- [ ] **Step 5: Re-run the full verification contract**

Refresh Unity, require no new `error CS` entries, run SimulationCore tests with zero failures, and build `Server/ReplayValidator/ReplayValidator.csproj` with zero errors.

Observed: Unity compilation and ReplayValidator build pass. `Multiply_LargeRawValues_DoesNotOverflowIntermediate`,
`ReplayFile_JsonRoundTrip_PreservesData`, and `SimWorldDeterminismTests.FullSnapshot_Restore_ChecksumIdentical`
pass. The complete SimulationCore run has 60 passes and 7 failures:

- `SimBattleDeterminismTests`: 2 failures
- `SimReplayRoundTripTests`: 4 failures
- `SimWorldTests`: 1 failure

---

### Task 4: Repair Compiler Layers Revealed After SimulationCore

**Files:**
- Modify: `Assets/Scripts/Combat/CombatManager.cs`
- Modify: `Assets/Scripts/FSM/Game/States/BattleState.cs`
- Modify: `Assets/Scripts/Combat/Core/CombatBootstrap.cs`
- Modify: `Assets/Scripts/Combat/Tests/ZWebGLFrameWork.Combat.Tests.asmdef`
- Modify: `Assets/Editor/AutoFixProcessor.cs`
- Modify: `Assets/Tests/SimulationCore/LogicIntTests.cs`
- Modify: `Assets/Tests/SimulationCore/SimWorldDeterminismTests.cs`
- Modify: `Server/ReplayValidator/ReplayValidator.cs`

- [x] Remove the invalid Combat-to-FSM dependency and select SimulationCore from `BattleState`.
- [x] Use test-local scale data and public entity handles instead of inaccessible internals.
- [x] Restore the missing `FindUsingInsertPosition` declaration and Combat test Data reference.
- [x] Adapt ReplayValidator to the current `SimWorld` constructor.
- [x] Verify a successful Unity Tundra compile and ReplayValidator build.
