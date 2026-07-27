# Historical Evidence Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the impossible Task 17 subject/XML assumptions with a strict 38-row recovery manifest, validated historical RED receipts, real commit sets, exact current GREEN tests, and ledger closure.

**Architecture:** A versioned JSON manifest is the single source of 38-row ownership and provenance. A Node validator accepts exactly three RED classes, requires every commit to be reachable, requires every current GREEN to contain the exact method with `Passed`, and invokes the existing evidence-ledger API with multiple fix commits.

**Tech Stack:** Node.js 20, `node:test`, Git, Unity 2022.3.62f3c1, NUnit XML/TRX, existing evidence ledger

## Global Constraints

- No proxy/empty commit, history rewrite, renamed/copied/hand-edited XML, or synthetic historical regression.
- `ExecutedTestFailure` requires the exact method with `Failed` in structured XML/TRX.
- `ExpectedCompileFailure` requires a nonzero command receipt, exact diagnostics, source revision, and log hash.
- `HistoricalSourceRed` explicitly records that no test executed and is permitted only for already-shared historical work.
- Every row requires a fresh nonzero current GREEN with the exact acceptance method `Passed` plus its broad gate.
- Similar test names do not satisfy exact method requirements.
- New code follows TDD and cannot use `HistoricalSourceRed`.
- The ledger remains 101 entries with `Fixed` and `RemovedByReplacement`; multiple `fixCommits` use the existing array support.

---

### Task 1: Add the recovery manifest schema and validator

**Files:**
- Create: `scripts/audit/simulation-evidence-recovery.mjs`
- Create: `scripts/audit/simulation-evidence-recovery.test.mjs`
- Create: `scripts/audit/read-test-results.py`
- Create: `docs/superpowers/specs/2026-07-25-simulation-evidence-recovery.json`

**Interfaces:**
- Produces: `validateRecoveryManifest({ root, manifestPath, requireArtifacts })` and CLI `validate|close`.
- Consumes: ownership JSON, issue register, evidence ledger, Git commits, RED receipts, GREEN XML/TRX, and gate artifacts.

- [ ] **Step 1: Write validator RED tests**

Create temporary-repository fixtures and these tests:

```javascript
test('requires exactly the 38 simulation-owned rows', async () => {
  const fixture = await recoveryFixture();
  fixture.manifest.rows.pop();
  await fixture.writeManifest();
  const result = await validateRecoveryManifest(fixture.options());
  assert.match(result.errors.join('\n'), /exactly match simulation ownership/);
});

test('accepts multiple reachable fix commits and rejects an unreadable commit', async () => {
  const fixture = await recoveryFixture();
  fixture.row.fixCommits = [fixture.baseCommit, fixture.fixCommit];
  assert.deepEqual((await validateRecoveryManifest(fixture.options())).errors, []);
  fixture.row.fixCommits.push('f'.repeat(40));
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /unreadable fix commit/);
});

test('validates all three RED classes without treating source receipts as tests', async () => {
  const fixture = await recoveryFixture();
  await fixture.assertExecutedFailureAccepted();
  await fixture.assertCompileFailureAccepted();
  await fixture.assertHistoricalSourceRedAccepted();
});

test('always requires exact Passed GREEN and a gate artifact', async () => {
  const fixture = await recoveryFixture();
  fixture.writeGreen('DifferentMethod', 'Passed');
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /exact acceptance method.*Passed/);
});
```

The fixture creates a real temporary Git repository with two commits, a one-row ownership/register fixture, XML files, receipts, and hashes; inject expected issue count for unit tests while production always requires 38.

- [ ] **Step 2: Run tests and verify RED**

Run: `node --test scripts/audit/simulation-evidence-recovery.test.mjs`

Expected: module-not-found failure because the validator does not exist.

- [ ] **Step 3: Implement strict manifest parsing**

The production manifest shape is:

```json
{
  "schemaVersion": 2,
  "ownership": "docs/superpowers/plans/2026-07-17-project-repair-ownership.json",
  "register": "docs/superpowers/specs/2026-07-16-project-repair-issue-register.md",
  "ledger": "docs/superpowers/specs/2026-07-16-project-repair-evidence.json",
  "historicalCutoff": "dffc4ce40e4000da93546181be50b9d4b4f5b5b8",
  "rows": [{
    "issue": "SIM-001",
    "acceptanceMethod": "SimulationCore_HasNoUnityEngineReferences",
    "red": { "kind": "ExecutedTestFailure", "artifact": "/tmp/example-red.trx" },
    "green": "/tmp/example-green.trx",
    "gates": ["/tmp/example-gate.json"],
    "fixCommits": ["74dec273736663c3eae75c940fcfeaf2fac018cd"],
    "mode": "Fixed"
  }]
}
```

`historicalCutoff` is a policy-pinned, reachable commit; the validator rejects a
manifest that changes it. Receipt schemas remain at version 1.

Reject unknown top-level/row/receipt keys, duplicate issues, duplicate methods, empty arrays, non-absolute external artifacts, issue-set mismatch, and modes outside the ledger schema.

- [ ] **Step 4: Implement the three RED validators**

`ExecutedTestFailure` invokes `scripts/audit/read-test-results.py`. The helper
uses Python `xml.etree.ElementTree`, walks namespace-local element names, and
prints a JSON array of `{ "name": string, "result": string }`: NUnit
`test-case` reads `fullname` then `name` and `result`; TRX `UnitTestResult`
reads `testName` and `outcome`. It exits nonzero on malformed XML, an unknown
root format, or zero test results. The Node validator normalizes a case name by
removing a trailing parameter list and taking the final dot-separated segment,
then requires the exact acceptance method with `Failed`.

`ExpectedCompileFailure` receipt JSON contains:

```json
{
  "schemaVersion": 1,
  "kind": "ExpectedCompileFailure",
  "sourceRevision": "26571ee9ce9dedd1fb91950eafa933cd8268c666",
  "command": ["/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity", "-batchmode", "-runTests"],
  "exitCode": 1,
  "logPath": "/tmp/zweb-sim-migration/task-01/red-reconstruction.log",
  "logSha256": "0000000000000000000000000000000000000000000000000000000000000000",
  "requiredDiagnostics": ["error CS0246", "ISimBattleSession"],
  "planCitation": "docs/superpowers/plans/2026-07-17-simulationcore-migration.md:233"
}
```

The all-zero `logSha256` above is fixture-only schema documentation; production
receipts must contain the computed SHA-256 of `logPath` and therefore cannot use
that fixture value.

Require readable revision, nonzero exit, exact log hash, every diagnostic substring, and a citation inside the repository plan.

`HistoricalSourceRed` receipt JSON contains readable `sourceRevision`, nonempty `fixCommits`, `acceptanceMethod`, `testPresentBefore=false`, `testPresentAfter=true` when the method exists at the fix, exact changed paths, `gitDiffSha256`, and `reason`. Verify the Git diff bytes/hash and method presence assertions. If the exact method never entered history, require `testPresentAfter=false` plus `currentAcceptanceRequired=true`.

- [ ] **Step 5: Implement unconditional GREEN/gate/provenance checks**

For every row: validate every 40-character fix SHA with `git cat-file -e`, parse the GREEN and require exact `Passed`, require nonempty gate files, hash all artifacts, and reject RED/GREEN paths that resolve inside the repository before ingestion.

- [ ] **Step 6: Implement CLI close using existing ledger API**

For `close`, validate the complete manifest first, then call `closeIssue` once per row with `red` set to the receipt or executed XML, `green`, `gate`, the full `fixCommits` array, `mode`, and one explicit `--date YYYY-MM-DD`. Stop before the first write if any row fails validation.

- [ ] **Step 7: Run tests GREEN and commit infrastructure**

```bash
node --test scripts/audit/simulation-evidence-recovery.test.mjs \
  scripts/audit/evidence-ledger.test.mjs
git add scripts/audit/simulation-evidence-recovery.mjs \
  scripts/audit/simulation-evidence-recovery.test.mjs \
  docs/superpowers/specs/2026-07-25-simulation-evidence-recovery.json
git commit -m "feat(audit): validate historical simulation evidence"
```

Expected: all tests pass; initial production manifest validation reports missing rows until Task 2 populates it.

---

### Task 2: Populate real 38-row provenance and RED classifications

**Files:**
- Modify: `docs/superpowers/specs/2026-07-25-simulation-evidence-recovery.json`
- Create: `scripts/audit/capture-historical-red.mjs`
- Create: `scripts/audit/capture-historical-red.test.mjs`

**Interfaces:**
- Consumes the three independently reviewed reports under `.superpowers/sdd/` and reachable Git objects.
- Produces reproducible RED receipt JSON beneath `${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-sim-migration}/recovery/red`.

- [ ] **Step 1: Write capture-tool RED tests**

Test that compile receipt capture rejects exit 0, missing diagnostics, and changed logs; test that source receipt capture hashes `git diff --binary <base>..<fix> -- <paths>` and records exact method presence before/after.

- [ ] **Step 2: Run tests RED, implement capture commands, run GREEN**

CLI forms are:

```bash
node scripts/audit/capture-historical-red.mjs compile \
  --revision <sha> --exit-code 1 --log <absolute-log> \
  --command <historical-executable> --command <historical-argument> \
  --diagnostic 'error CS0246' --diagnostic ISimBattleSession \
  --plan-citation docs/superpowers/plans/2026-07-17-simulationcore-migration.md:233 \
  --output /tmp/zweb-sim-migration/recovery/red/COMBAT-002.json

node scripts/audit/capture-historical-red.mjs source \
  --base <sha> --fix <sha> --method <exact-method> \
  --path Assets/Scripts/SimulationCore --path Assets/Tests/SimulationCore \
  --output /tmp/zweb-sim-migration/recovery/red/SIM-007.json
```

Write JSON atomically through a temporary sibling and rename; never overwrite a non-identical receipt.

- [ ] **Step 3: Use these exact real fix mappings**

Populate the 38 rows with these reviewed commit sets:

```text
COMBAT-001=e2c24e7d14184a2b3e9d4106e296204f77a62023
COMBAT-002=fb6d7622499aeab95d1fe828dfa349b17eedb332
COMBAT-003,COMBAT-004,TEST-005=592bba40a0c1618eb3bd14ae8e90a9ed4cbe51bf,f5e12b798e593ee6d9a8bfaa16df7a964a77f1af,4c8781f3740404d5a44df61677b0dc2659d95485
COMBAT-005,SIM-016=38106550af52ec627e85ff411852bb0759bb41e4,f5e12b798e593ee6d9a8bfaa16df7a964a77f1af,4c8781f3740404d5a44df61677b0dc2659d95485
COMBAT-006,SIM-012=b726fa5163ac4fc7b27368d9af62004557bf7188,4370805d9757e071345e8d0f7e6a942c7550603d
COMBAT-007=38106550af52ec627e85ff411852bb0759bb41e4
COMBAT-008=e2c24e7d14184a2b3e9d4106e296204f77a62023
SIM-001,SIM-002=74dec273736663c3eae75c940fcfeaf2fac018cd
SIM-003,SIM-014,TEST-002=9f68c91019855e63d1f8c80ffc6e381df7c0b48f
SIM-004,SIM-022=67b494cee535fa50cbb7dc30a37f33a9a875f4bd
SIM-005,SIM-006=cb016a380a9885a6978ff3097c4e180f27469a20
SIM-007,TEST-001=05779051471a55fa5acfe0e8786699a87b1e66e9,17bb8dc2f759032de3946d74d647823d712c27b5
SIM-008,SIM-021=9308bfacc9d6db8565f1764f90a3037c2f756eec,f5691ddb8c41516162bc6ae48781639b719ca88c
SIM-009,SIM-010,TEST-003=9e012e3a526aa851d6fecc46b353562498199905
SIM-011,TEST-007=ca2a09fa24f160d627135380631a4e67cfa11110,ebf6588a6a13a3d5df093ad6b790cf07a6c08bee
SIM-013,SIM-020=b726fa5163ac4fc7b27368d9af62004557bf7188
SIM-015=592bba40a0c1618eb3bd14ae8e90a9ed4cbe51bf,f5e12b798e593ee6d9a8bfaa16df7a964a77f1af,4c8781f3740404d5a44df61677b0dc2659d95485
SIM-017,SIM-019=bf4a95d3137399e81179fee0dd93071d398259ca
SIM-018=0700cabf80073aa5652774daf75a7af7f412957a
TEST-004,TEST-006=b5cfee513d6c6e9316e2a6176b2be4e58347eae2
TEST-008=38106550af52ec627e85ff411852bb0759bb41e4,9e012e3a526aa851d6fecc46b353562498199905
```

Before writing the manifest, verify every listed 40-character SHA is reachable
with `git cat-file -e <sha>^{commit}`. The validator rejects abbreviations; no
subject substitution is used.

- [ ] **Step 4: Assign RED classes without overstating execution**

Use `ExecutedTestFailure` for SIM-001, SIM-002, SIM-005, SIM-006, and COMBAT-008. Use `ExpectedCompileFailure` for COMBAT-002, SIM-004, and SIM-022 from the retained Task 1/2 compile logs. Use `HistoricalSourceRed` for the remaining 30 rows. Each row gets its own receipt even when rows share a diff.

- [ ] **Step 5: Validate provenance-only mode and commit**

```bash
node --test scripts/audit/capture-historical-red.test.mjs \
  scripts/audit/simulation-evidence-recovery.test.mjs
node scripts/audit/simulation-evidence-recovery.mjs validate \
  --manifest docs/superpowers/specs/2026-07-25-simulation-evidence-recovery.json \
  --require-artifacts false
git add docs/superpowers/specs/2026-07-25-simulation-evidence-recovery.json \
  scripts/audit/capture-historical-red.mjs scripts/audit/capture-historical-red.test.mjs
git commit -m "docs(audit): map simulation fixes to real history"
```

Expected: issue ownership and every commit/RED receipt validate; GREEN/gate paths may be declared but are not accepted for closure until Task 4.

---

### Task 3: Add the 16 missing exact current acceptance tests

**Files:**
- Modify: `Assets/Tests/SimulationCore/SessionDeterminismTests.cs`
- Modify: `Assets/Tests/SimulationCore/SimReplayRoundTripTests.cs`
- Modify: `Assets/Tests/SimulationCore/SessionContractTests.cs`
- Modify: `Assets/Tests/SimulationCore/ModeBehaviorTests.cs`
- Modify: `Assets/Tests/SimulationCore/CoreTargetingTests.cs`
- Modify: `Assets/Tests/SimulationCore/SessionTerminalTests.cs`

**Interfaces:**
- Produces all exact acceptance methods currently absent from reachable test source.
- Reuses assertion helpers, never calls one `[Test]` method from another.

- [ ] **Step 1: Add exact snapshot/checksum RED tests**

In `SessionDeterminismTests`, add real aggregate tests named:

```text
Checksum_ChangesForPathPhaseAllocatorAndPendingEventDifferences
Snapshot_PathMutationAndSystemClock_AreIsolatedAndRestored
SnapshotRestore_ForeignSchemaCorruptOrStale_IsRejectedAtomically
```

Extract private assertion helpers from the existing path, checksum, capture/restore, and hostile-envelope tests. Each new test creates fresh sessions and invokes those helpers; it must contain direct assertions for every clause in its name.

- [ ] **Step 2: Run exact filters and verify RED before helper/production fixes**

Run each exact method filter. If a new aggregate test passes immediately, add an assertion for the uncovered plan clause rather than accepting it as TDD evidence. Any uncovered behavior receives the smallest production fix and a separate commit.

- [ ] **Step 3: Add exact rollback and command admission tests**

In `SimReplayRoundTripTests` and `SessionContractTests`, add:

```text
SessionRestart_MatchesFreshSessionState
RollbackReplay_MatchesOriginalChecksumSequence
Commands_SameTickSamePlayer_AreOrderedBySequence
CommandSubmit_LateFutureDuplicate_HasDeterministicDisposition
DeployCommand_RejectsOutOfPhaseDisallowedOrOverLimit
DeployMoveSkill_ValidCommandsMutateState_InvalidCommandsDoNot
```

Each test constructs independent sessions, records pre/post checksums, and directly asserts every disposition or mutation. Share only private fixture builders and assertion helpers.

- [ ] **Step 4: Add exact mode/combat tests**

In `ModeBehaviorTests`, `CoreTargetingTests`, and `SessionTerminalTests`, add:

```text
CampaignAndBuilderBase_StartupDoesNotPrematurelyEnd
CombatResult_UnityAndDotNet_AreIdentical
SimulationModes_ImplementApprovedStartupWaveAndTerminalMatrix
BattlePathfinding_DiagonalBesideSingleWall_IsRejected
DefenseBuilding_AcquiresAndDamagesAttacker
DefenseTargetingAndAI_UnityAndDotNet_ProduceSameChecksums
BattleSession_AttackDamageDeathResult_EndToEnd
```

Use the existing canonical mode vectors, core target/path fixtures, and one-hit terminal session builders. Each exact test must execute the public session/core APIs and make direct assertions; no alias test may call another test method.

- [ ] **Step 5: Run the 16 exact methods, then full SimulationCore GREEN**

Write results to `/tmp/zweb-sim-migration/recovery/green`. Run each exact filter once, then the full `ZWebGLFrameWork.SimulationCore.Tests` fixture set. Expected: all exact methods appear with `Passed`; full suite is nonzero and pass-only.

- [ ] **Step 6: Commit tests and any behavior fixes in TDD-sized commits**

Stage only the fixture and production files covered by each observed RED. Commit messages name the behavior, not evidence bookkeeping. After all 16 are GREEN, `git diff --check` must pass.

---

### Task 4: Generate current GREEN/gates and close 38 rows

**Files:**
- Modify: `docs/superpowers/specs/2026-07-25-simulation-evidence-recovery.json`
- Modify at closure: `docs/superpowers/specs/2026-07-16-project-repair-evidence.json`
- Create at closure: hash-addressed files beneath `docs/superpowers/evidence/artifacts/`

**Interfaces:**
- Produces 38 Closed simulation-owned ledger entries with validated RED, GREEN, gate, and real fix commits.

- [ ] **Step 1: Run fresh exact GREEN collection**

Run Unity 2022.3.62f3c1 EditMode/PlayMode filters named by all 38 rows and .NET architecture tests where applicable. Write every result and log beneath `/tmp/zweb-sim-migration/recovery/green`; do not reuse stale artifacts.

- [ ] **Step 2: Run broad gates**

Run full EditMode, PlayMode, SimulationCore server tests, WebGL build, and the B1 Windows build. Validate with existing Unity/TRX/build-summary validators. Put all gate paths into the manifest rows according to affected platform.

- [ ] **Step 3: Validate without writes**

```bash
node scripts/audit/simulation-evidence-recovery.mjs validate \
  --manifest docs/superpowers/specs/2026-07-25-simulation-evidence-recovery.json \
  --require-artifacts true
```

Expected: `PASS rows=38` and zero warnings/errors.

- [ ] **Step 4: Close atomically and validate all 101 issues**

```bash
node scripts/audit/simulation-evidence-recovery.mjs close \
  --manifest docs/superpowers/specs/2026-07-25-simulation-evidence-recovery.json \
  --date 2026-07-25
node scripts/audit/evidence-ledger.mjs validate \
  --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json
```

The close command writes a backup in `/tmp`, closes all 38 rows only after complete validation, and restores the ledger if any ingestion/write fails.

- [ ] **Step 5: Commit evidence closure**

```bash
git add docs/superpowers/specs/2026-07-25-simulation-evidence-recovery.json \
  docs/superpowers/specs/2026-07-16-project-repair-evidence.json \
  docs/superpowers/evidence/artifacts
git commit -m "test(sim): close audited simulation evidence"
```

Expected: commit contains only the manifest, ledger, and hash-addressed evidence; working tree is clean after the commit.
