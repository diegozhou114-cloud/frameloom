# Stage 3: Public API TDD

This stage owns only the two approved API contracts. It starts after the move checkpoint and follows
strict RED-to-GREEN order; no failing assembly is committed.

## Known Callers And Coverage

- Buffered presentation production caller:
  `Packages/com.zwebglframework.runtime/Runtime/Combat/CombatManager.cs`.
- Existing bridge coverage: `Assets/Tests/Combat/BufferedSessionPresentationBridgeTests.cs`; its
  reflection helper becomes a direct call only after visibility GREEN.
- Existing Replay callers/fixtures: `Assets/Tests/SimulationCore/ReplayFixture.cs`,
  `ReplaySessionRoundTripTests.cs`, `SessionTerminalTests.cs`, and
  `Server/SimulationCore.Tests/ReplayRoundTripTests.cs`.
- Public definitions after the move:
  `Packages/com.zwebglframework.runtime/Runtime/Combat/Presentation/SessionPresentationBridge.cs`
  and `Packages/com.zwebglframework.runtime/Runtime/SimulationCore/Command/CommandRecorder.cs`.

## Task 3.1: Create The External Bridge Contract RED

- **Paths:** add `Assets/Tests/PublicApiContract.meta`,
  `Assets/Tests/PublicApiContract/ZWebGLFrameWork.PublicApiContract.Tests.asmdef`,
  `SessionPresentationBridgePublicContractTests.cs`, `PublicApiContractTestSupport.cs`, and each
  file's same-path `.meta`.
- **Behavior:** the asmdef has ordinary `references` exactly `ZWebGLFrameWork.Combat` and
  `ZWebGLFrameWork.SimulationCore`; `includePlatforms: [Editor]`; `overrideReferences: true`;
  `precompiledReferences: [nunit.framework.dll]`; `autoReferenced: false`; and
  `defineConstraints: [UNITY_INCLUDE_TESTS]`. Never put `nunit.framework.dll` in ordinary
  `references`. It has no matching `InternalsVisibleTo`. Use real public session APIs to publish
  presentation, call the buffered overload directly, then clear/reuse/dispose the parent and prove
  later `Sample` uses retained value copies/bridge-owned views. With the current overload set, the
  canonical inaccessible-member RED is CS1501: Roslyn excludes the internal two-argument overload
  from the external assembly's accessible candidates and sees only the public one-argument overload.
- **Do not touch:** production visibility/behavior, friend assembly lists, reflection, extra
  production assembly references, duplicated production code, repository-path loading, Demo content,
  or allocation measurement.
- **Verify:** run focused EditMode compilation with `-testFilter
  ZWebGLFrameWork.PublicApiContract.Tests`; expected RED is the single unique CS1501 diagnostic on
  the direct two-argument `SessionPresentationBridge.Apply` call, with no other compiler diagnostic
  or root cause. Prove the external assembly has no friend, reflection, or dynamic access. Do not
  alter the call to manufacture CS0122; task 3.2 makes this same call compile by changing only the
  buffered overload's visibility to public. Save log/results outside the repository.
- **Checkpoint:** RED evidence plus uncommitted external test sources; do not commit broken compile.
- **Stop:** the test compiles through internals/reflection, cannot publish data through public
  session APIs, or fails for any reason other than overload visibility.

## Task 3.2: Make Only Buffered Apply Public And Reach GREEN

- **Paths:** modify
  `Packages/com.zwebglframework.runtime/Runtime/Combat/Presentation/SessionPresentationBridge.cs`,
  `Assets/Tests/Combat/BufferedSessionPresentationBridgeTests.cs`, and the external bridge test.
- **Behavior:** change only `Apply(SimPresentationOutputBuffer frame, long revision)` from internal
  to public. Add English XML docs: `frame` is borrowed from the parent output; `revision` must match
  the parent; consumption is synchronous; no buffer/list view is retained; after return the parent
  may be cleared, reused, or disposed while interpolation/value copies and views remain valid.
  Replace the existing internal-overload reflection delegate with direct public calls.
- **Do not touch:** the owning `PresentationFrame` overload, interpolation algorithm, view pooling,
  signatures, namespace, asmdefs/IVT, or steady-state allocation behavior.
- **Verify:** rerun the same focused filter; expected exit 0 and direct-call lifecycle cases pass.
  Also run `ZWebGLFrameWork.Combat.Tests.BufferedSessionPresentationBridgeTests`; expected all pass
  including the existing zero-allocation probe.
- **Checkpoint:** bridge API/test GREEN may be reviewed independently; no recorder change included.
- **Stop:** XML docs imply retained borrowed data, parent reuse changes later samples, allocations
  regress, or any signature beyond accessibility changes.

## Task 3.3: Add Recorder Contract And Atomicity RED

- **Paths:** add `Assets/Tests/PublicApiContract/CommandRecorderPublicContractTests.cs` and
  `CommandRecorderPublicContractTests.cs.meta`; add
  `Assets/Tests/SimulationCore/CommandRecorderBufferedOutputTests.cs` and its same-path `.meta`.
- **Behavior:** external direct-call tests must fail compilation because
  `Record(SimTickOutputBuffer)` is absent. Specify owning command copies after clear/reuse/dispose;
  exact final tick/checksum/terminal result parity with `Record(TickResult)`; null, missing
  `ExecutedCommands`, new/unpublished, cleared, and disposed rejection; and deep Replay immutability
  on every rejection. Internal tests may use existing SimulationCore friendship solely to publish a
  command whose property throws during serialization and prove no partial mutation.
- **Do not touch:** recorder production code before RED, external asmdef friendship/reflection,
  zero-allocation gates, unrelated Replay format/deserialization, or command execution semantics.
- **Verify:** focused compilation must RED only on the missing overload. Both external and internal
  sources are present before the production edit; their behavior assertions become executable only
  after the signature exists. Preserve the RED compiler log in external evidence.
- **Checkpoint:** recorder RED evidence; do not commit a broken assembly or weaken assertions.
- **Stop:** tests cannot snapshot Records content plus FinalTick/FinalChecksum/FinalResult, or require
  modifying `SimTickOutputBuffer` internals/public surface to create valid public outputs.

## Task 3.4: Implement Recorder Validation And Atomic Commit GREEN

- **Paths:** modify
  `Packages/com.zwebglframework.runtime/Runtime/SimulationCore/Command/CommandRecorder.cs` and the
  two recorder test files from task 3.3.
- **Behavior:** add English-documented `public void Record(SimTickOutputBuffer output)` with this
  exact order: null check; `ExecutedCommands` field check; `Tick < 0` check; serialize all borrowed
  commands into a new owning `ReplayTickRecord` and new `SerializableCommand` objects; only then add
  the record and update final tick/checksum, plus final result when non-null. Null throws
  `ArgumentNullException(nameof(output))`; the two state errors throw `ArgumentException` with
  parameter `output`. Reuse a private serializer only if all existing overload behavior remains.
- **Do not touch:** `SimTickOutputBuffer`, Replay wire/schema/version, `Record(TickResult)` observable
  behavior, existing command mappings, or performance gates. Replay capture remains allocating.
- **Verify:** focused external/internal filters exit 0. For every rejected or throwing case compare
  record count and every record/command field plus all three final fields before/after. Successful
  capture must remain unchanged after buffer clear, reuse, and dispose.
- **Checkpoint:** recorder API/test GREEN separate from move and governance.
- **Stop:** mutation precedes complete serialization, exception parameter differs, unpublished state
  is accepted, owning-copy proof fails, or parity with `Record(TickResult)` changes.

## Task 3.5: Focused Then Complete Regression

- **Paths:** `Assets/Tests/PublicApiContract/`, `Assets/Tests/Combat/`,
  `Assets/Tests/SimulationCore/`, all host PlayMode tests, and Server tests; external evidence under
  `$OSS_EVIDENCE_ROOT/public-api/`.
- **Behavior:** run filters in order: external contracts, Combat bridge, SimulationCore Replay, full
  EditMode, full PlayMode, then Server. Audit the external asmdef and production IVT declarations.
- **Do not touch:** tests to mask failures, allocation thresholds, or non-API production behavior.
- **Verify:** use the Unity commands from [task 1.3](01-baseline.md#task-13-run-the-pre-move-compile-and-test-baseline)
  with focused `-testFilter` values before unfiltered runs, then the Server commands. Run
  `rg -n 'InternalsVisibleTo|Reflection|BindingFlags' Assets/Tests/PublicApiContract` and expect no
  hit; parsed asmdef references must be the approved set. All test counts are nonzero and pass.
- **Checkpoint:** one reviewable Public API TDD checkpoint containing RED evidence references, both
  minimal APIs, direct external contracts, and GREEN results.
- **Stop:** any focused/full regression, external compile boundary, borrowed/owning lifecycle,
  atomicity, or existing allocation gate fails.
