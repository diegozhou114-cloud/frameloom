# Project Repair Master Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute the five approved repair workstreams in dependency order, close all 101 audited issues with readable evidence, and produce clean WebGL, Windows, Linux server, and Windows server gates without opening the project in Unity 6.

**Architecture:** Five detailed plans own disjoint issue sets while this master plan owns ordering, shared-file serialization, toolchain checkpoints, and final G0-G5 acceptance. Early source audits and evidence tooling can proceed without Unity; authoritative compilation, import, asset generation, PlayMode, builds, and smoke tests wait for Unity `2022.3.62f3c1`, while server gates wait for .NET 8. Legacy Combat and custom KCP are removed only after their approved replacements pass and all production/serialized references are zero.

**Tech Stack:** Git, CodeGraph, Unity `2022.3.62f3c1`, NUnit, WebGL, Windows Standalone x86-64, .NET 8, ASP.NET Core/Kestrel, WSS, pinned kcp2k, Node.js 20, Python 3, GitHub Actions, and Playwright Chromium.

## Global Constraints

- The approved architecture is Architecture A: `SimulationCore` is the only production battle authority.
- Legacy Combat is migration-only and must be deleted after replacement/parity gates pass; no permanent feature toggle remains.
- Supported battle modes are exactly `village_attack`, `builder_base`, `clan_war`, and `campaign`.
- Supported Unity clients are WebGL and Windows Standalone x86-64.
- Server projects target .NET 8 and must pass Linux and Windows Release builds with warnings as errors.
- Unity stays pinned to `2022.3.62f3c1`; never open, import, serialize, test, or build this worktree with Unity `6000.3.19f1`.
- WebGL realtime is WSS. Native realtime uses immutable, provenance-locked kcp2k; custom KCP is deleted after replacement tests.
- Replay validation resolves authoritative match data and never trusts client seed, layout, result, or content hash.
- Raw execution output lives beneath `${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-repair-evidence}` or `${RUNNER_TEMP}`; the repository contains only source, configuration, generated Unity assets/metadata, and hash-addressed evidence artifacts.
- Every task follows red receipt, verified red, minimal implementation, focused green, broad gate, commit, and evidence closure.
- The evidence ledger is edited serially. No two workers may close issues or write `docs/superpowers/specs/2026-07-16-project-repair-evidence.json` concurrently.
- The worktree is `/Users/diegozhou/Documents/unityframework/zweb-audit-fix` on branch `codex/fix-audit-issues`; the original Desktop checkout remains untouched.

## Plan Index and Ownership

The canonical machine-readable allocation is `docs/superpowers/plans/2026-07-17-project-repair-ownership.json`.

| Plan | Count | Owned IDs |
|---|---:|---|
| `2026-07-17-compilation-asset-baseline.md` | 7 | `ASSET-001..002`, `BUILD-001..004`, `SCENE-001` |
| `2026-07-17-runtime-repair.md` | 26 | `CORE-001..002`, `BOOT-001..005`, `FSM-001..002`, `DATA-001..007`, `UI-001..004`, `GAME-001..005`, `SCENE-002` |
| `2026-07-17-simulationcore-migration.md` | 38 | `COMBAT-001..008`, `SIM-001..022`, `TEST-001..008` |
| `2026-07-17-network-server-repair.md` | 24 | `NET-001..014`, `REPLAY-001..007`, `SERVER-001..003` |
| `2026-07-17-ci-release-gates.md` | 6 | `CI-001..005`, `TEST-009` |
| **Total** | **101** | Every evidence-ledger ID exactly once |

## Shared-File Serialization

| Shared surface | Required order |
|---|---|
| `Assets/Editor/BuildScript.cs` | Baseline enabled-scene repair, then CI BuildReport writer integration |
| `Assets/Editor/BuildValidator.cs` | Baseline required-asset validation, then CI only consumes the public result contract |
| `Assets/Scripts/SimulationCore/**` | Simulation deterministic/session work, then Network transport extraction and shared protocol adapters |
| `Assets/Scripts/FSM/Game/States/BattleState.cs` | Runtime lifetime/pause contract, then Simulation session-only battle integration |
| `Assets/Scripts/Combat/**` | Baseline import-only fix, then Simulation migration and Legacy deletion |
| `Assets/Tests/Runtime/**` | Runtime creates and owns the PlayMode assembly; CI reads its results and manifest count |
| `Server/**` | Network/Server creates solution/projects/locks; CI adds no server behavior and only executes gates |
| `.github/workflows/ci.yml` | CI plan is the only writer |
| Required asset manifest/ViewConfig | Baseline creates, Simulation updates archetype consumers, final baseline validator reruns |
| Evidence ledger/artifacts | One closure batch at a time after the corresponding fix commit and broad gate |

---

### Task 1: Verify Ownership, Worktree, and Required Toolchains

**Files:**
- Verify: `docs/superpowers/plans/2026-07-17-project-repair-ownership.json`
- Verify: all five detailed plan files
- No production files change.

**Interfaces:**
- Consumes: current Git worktree, ownership JSON, evidence ledger, project version, executable paths.
- Produces: one preflight receipt and an exact pass/fail decision for Unity, .NET, Node, ownership, and clean-tree state.

**Verification cycle:** Preserve the current missing-tool output as red preflight evidence; green requires exact tool versions, 101 unique allocations, and the documentation-only plan commit followed by an empty worktree.

- [ ] **Step 1: Verify all 101 issue IDs are allocated exactly once**

Run:

```bash
cd /Users/diegozhou/Documents/unityframework/zweb-audit-fix
export ZWEB_EVIDENCE_ROOT="${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-repair-evidence}"
mkdir -p "$ZWEB_EVIDENCE_ROOT/master"
node -e '
const fs = require("fs");
const ownership = JSON.parse(fs.readFileSync("docs/superpowers/plans/2026-07-17-project-repair-ownership.json", "utf8"));
const ledger = JSON.parse(fs.readFileSync(ownership.evidenceLedger, "utf8"));
const allocated = Object.values(ownership.plans).flatMap(plan => plan.issues);
const counts = new Map();
for (const id of allocated) counts.set(id, (counts.get(id) || 0) + 1);
const duplicates = [...counts].filter(([, count]) => count !== 1).map(([id]) => id);
const expected = Object.keys(ledger.issues).sort();
const actual = [...counts.keys()].sort();
const missing = expected.filter(id => !counts.has(id));
const extra = actual.filter(id => !ledger.issues[id]);
if (allocated.length !== 101 || expected.length !== 101 || duplicates.length || missing.length || extra.length) {
  console.error(JSON.stringify({ allocated: allocated.length, expected: expected.length, duplicates, missing, extra }, null, 2));
  process.exit(1);
}
for (const plan of Object.values(ownership.plans)) {
  if (!fs.existsSync(plan.path)) throw new Error(`missing plan ${plan.path}`);
}
console.log("[plan-ownership] PASS issues=101 plans=5");
' | tee "$ZWEB_EVIDENCE_ROOT/master/ownership.txt"
```

Expected: `plan-ownership] PASS issues=101 plans=5`.

- [ ] **Step 2: Verify worktree isolation and base history**

Run:

```bash
test "$(git branch --show-current)" = 'codex/fix-audit-issues'
git merge-base --is-ancestor 4a6738302ade966ae7a5eae1abc51487b29402ca HEAD
test "$(git rev-parse --show-toplevel)" = '/Users/diegozhou/Documents/unityframework/zweb-audit-fix'
git status --porcelain --untracked-files=all | tee "$ZWEB_EVIDENCE_ROOT/master/preflight-status.txt"
```

Expected before plan execution: only the newly approved plan documents are untracked. After the plan commit, rerun and require no output.

- [ ] **Step 3: Verify exact executable versions without substitution**

Run:

```bash
export UNITY_EDITOR='/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity'
test -x "$UNITY_EDITOR"
"$UNITY_EDITOR" -version | rg -F '2022.3.62f3c1'
test "$(awk '/m_EditorVersion:/ {print $2}' ProjectSettings/ProjectVersion.txt)" = '2022.3.62f3c1'
dotnet --version | rg '^8\.'
node --version | rg '^v(20|21|22|23|24)\.'
codegraph status
```

Expected: every command exits `0`. Current evidence confirms both the editor binary and project version are `2022.3.62f3c1`; `dotnet` is still absent, while Unity `6000.3.19f1` is installed but forbidden. Unity work may use only the pinned editor; stop every .NET task until SDK 8 is provisioned.

- [ ] **Step 4: Commit the complete plan set before implementation**

Run only after all detailed-plan self-reviews pass:

```bash
git add docs/superpowers/plans/2026-07-17-*.md \
  docs/superpowers/plans/2026-07-17-project-repair-ownership.json
git diff --cached --check
git commit -m "docs: define audited project repair execution plans"
test -z "$(git status --porcelain --untracked-files=all)"
```

Expected: one documentation-only commit and a clean worktree.

---

### Task 2: Install Gate Infrastructure Before Closing Any Issue

**Files:**
- Execute Tasks 1-3 in `2026-07-17-ci-release-gates.md`.
- Execute source-audit creation portions of Tasks 1-3 in `2026-07-17-compilation-asset-baseline.md`.

**Interfaces:**
- Consumes: evidence ledger, test manifests, source tree, current workflow.
- Produces: evidence close/validate command, structured NUnit/TRX/build validators, ownership-safe closure format, source red receipts, and pinned workflow contract tests.

**Verification cycle:** Each referenced detailed task captures its named red receipt before implementation, runs the focused and broad green commands, and commits only its listed gate-infrastructure files.

- [ ] **Step 1: Implement and test evidence-ledger tooling first**

Follow CI Task 1 through its commit. Then run:

```bash
node --test scripts/audit/evidence-ledger.test.mjs
node scripts/audit/evidence-ledger.mjs validate \
  --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json
```

Expected: Node tests pass and the ledger reports `issues=101` with Open entries still allowed.

- [ ] **Step 2: Install structured test-result validators and workflow contract tests**

Follow CI Tasks 2-3 through their commits, but do not enable `--require-all-closed true` in a pushed branch until every workstream is ready for final closure. Run:

```bash
PYTHONPATH=scripts/ci python3 -m unittest discover -s scripts/ci -p 'test_*.py'
npm ci --prefix ci --ignore-scripts
node --test scripts/ci/workflow-contract.test.mjs
```

Expected: all validator unit tests pass. Workflow tests may remain red only for server/build/smoke jobs explicitly scheduled in later CI tasks; save that red receipt.

- [ ] **Step 3: Capture baseline red receipts before Unity import mutates metadata**

Follow Baseline Task 1 Step 1, Task 2 Step 1, and Task 3 Step 1. Verify:

```bash
test -s "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-001/red.txt"
test -s "$ZWEB_EVIDENCE_ROOT/baseline/BUILD/red.txt"
test -s "$ZWEB_EVIDENCE_ROOT/baseline/SCENE-001/red.txt"
```

Expected: all three red receipts exist before the first pinned-editor import.

---

### Task 3: Reach G0/G1 Import and Compilation Baseline

**Files:**
- Execute Baseline Tasks 1-4.
- Execute the host-neutral compilation task in the Simulation plan (`SIM-001`, `SIM-002`).
- Execute the initial callback/platform assembly tasks in the Network plan (`NET-001`, `NET-002`).
- Execute Runtime Task 0 to create the non-Editor PlayMode test assembly without claiming `CI-003` ownership.

**Interfaces:**
- Consumes: exact Unity editor, source audit receipts, platform asmdefs, host-neutral SimulationCore, required asset generator.
- Produces: stable metadata, repaired syntax/imports, no Unity references in SimulationCore, compilable Network callbacks/platform boundaries, Launcher scene zero, required assets, and nonzero discoverable test assemblies.

**Verification cycle:** Use the compiler/import/scene/asset red receipts captured in Task 2; green is the pinned-editor G1 compile plus repository audits, with every owner committing its compiler-baseline files before the next owner starts.

- [ ] **Step 1: Serialize compiler-blocker edits by ownership**

Apply in this order:

1. Baseline compiler contract task (`BUILD-001..004`).
2. Simulation host-neutral compiler task (`SIM-001`, `SIM-002`).
3. Network callback/platform split task (`NET-001`, `NET-002`).
4. Runtime PlayMode asmdef prerequisite.

After each commit, run its source audit. Do not run the pinned editor until all four groups are source-green.

- [ ] **Step 2: Import with the pinned editor and stabilize metadata**

Follow Baseline Task 1 using `UNITY_EDITOR`. Then run:

```bash
scripts/repair/audit-meta.sh "$PWD"
git status --short
```

Expected: metadata audit passes and only intentional importer metadata is present before its commit.

- [ ] **Step 3: Run the first authoritative Unity compile/EditMode gate**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/master/G1"
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults "$ZWEB_EVIDENCE_ROOT/master/G1/editmode.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/master/G1/editmode.log"
rg -F 'test-result="Passed"' "$ZWEB_EVIDENCE_ROOT/master/G1/editmode.xml"
rg -n 'error CS[0-9]+|Scripts have compiler errors|AssemblyBuilderException|MissingReferenceException|The referenced script .* is missing' \
  "$ZWEB_EVIDENCE_ROOT/master/G1/editmode.log" && exit 1 || true
```

Expected: editor exits `0`, result is Passed, and forbidden compiler/import scan emits nothing.

- [ ] **Step 4: Generate/validate required assets and scene order**

Complete Baseline Tasks 3-4. Then run the entire Editor test assembly and metadata/build-scene source audits exactly as Baseline Task 5 specifies.

Expected: `BUILD-001..004`, `ASSET-001..002`, and `SCENE-001` have green focused evidence, but close them only after the broad G0/G1 receipt is saved.

---

### Task 4: Build Runtime and Deterministic Simulation Foundations

**Files:**
- Execute Runtime plan tasks excluding final battle-session wiring until the Simulation session contract is green.
- Execute Simulation plan deterministic contracts/world/commands/modes/snapshot tasks.

**Interfaces:**
- Consumes: G1 baseline, Runtime PlayMode assembly, approved session contracts and behavior matrix.
- Produces: reliable boot/persistence/village/UI plus a host-neutral deterministic session with immutable results/presentation frames.

**Verification cycle:** Every Runtime and Simulation detailed task must first reproduce its assigned red test, then pass its focused and broad green assemblies and create the task's scoped commit before this master task advances.

- [ ] **Step 1: Run independent runtime and simulation tasks in parallel with file locks**

Safe parallel groups:

- Runtime: `CORE-*`, initial FSM/boot, save/local store, occupancy/resource production, UI lifecycle, scene rebinding.
- Simulation: schema/entity validity, `LogicInt`, command ordering/dispositions, checksums, deep snapshots, rollback, deterministic modes, AI/targeting, presentation contracts.

Do not parallelize edits to `BattleState.cs`, `CombatManager.cs`, required asset manifest, or evidence ledger.

- [ ] **Step 2: Require each focused task to run its broad assembly gate**

After every runtime task:

```bash
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform PlayMode \
  -testFilter ZWebGLFrameWork.Runtime.Tests \
  -testResults "$ZWEB_EVIDENCE_ROOT/master/runtime-latest.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/master/runtime-latest.log"
```

After every simulation task:

```bash
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -assemblyNames ZWebGLFrameWork.SimulationCore.Tests \
  -testResults "$ZWEB_EVIDENCE_ROOT/master/simulation-latest.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/master/simulation-latest.log"
```

Expected: each command exits `0`; no task proceeds on a newly failing broad assembly.

- [ ] **Step 3: Checkpoint cross-runtime golden behavior before transport work**

Require the Simulation plan's canonical content bytes, ordered commands, presentation frames, and checksum corpus to pass under Unity and .NET 8 before Network/Server consumes those contracts.

Expected: identical per-tick checksums and serialized bytes for the quick corpus; record G3 checkpoint artifacts outside the repo.

---

### Task 5: Build Network, Replay, and Authoritative Server on Stable Contracts

**Files:**
- Execute `2026-07-17-network-server-repair.md` after Simulation session/envelope/snapshot contracts are committed.
- Execute CI Task 4 only after `Server/ZWebGLFrameWork.Server.sln` and lock files exist.
- Execute CI Task 5 after the server job exists and before Simulation's final build/deletion gate.

**Interfaces:**
- Consumes: versioned Simulation session/command/authority/snapshot contracts and pinned dependency provenance.
- Produces: HTTPS/WSS WebGL adapters, authenticated kcp2k native adapter, bounded dispatcher, replay validator/CLI, match actor, and Linux/Windows server test gates.

**Verification cycle:** Network/Replay/Server tasks retain their issue-specific red receipts; green requires focused protocol tests, the impairment matrix, and Linux/Windows .NET 8 gates, with the exact commits named by the detailed plan.

- [ ] **Step 1: Record immutable dependency provenance before import**

Execute the Network plan's NativeWebSocket and kcp2k provenance task before adding dependency content. Reject mutable branch/tag URLs. Verify recorded upstream URL, commit, SHA-256, and license against fetched bytes.

- [ ] **Step 2: Implement adapters and authority path without changing Simulation contracts ad hoc**

Any required contract change returns to the Simulation plan owner, updates the golden corpus, and passes Unity/.NET compatibility before Network continues. Transport tasks may not expose socket, thread, or native KCP symbols to WebGL assemblies.

- [ ] **Step 3: Run transport impairment/authentication matrix**

Use the exact G4 matrix from the approved design: 0/5/20% loss, 0/5% duplication, 0/25% reordering, 512/1200-byte MTU, 50/150/300 ms RTT, fixed seeds, and 30-second case timeout. Run WSS-WSS, native-native, and WSS-native through one match actor.

Expected: ordered delivery, equal final checksum/result, stable auth rejection codes, explicit NoOp deadline advancement, reconnect/resume, and atomic resync.

- [ ] **Step 4: Run .NET 8 locked gates on both operating systems**

Execute CI Task 4 and require both `ubuntu-latest` and `windows-latest` artifacts. A local macOS .NET run is useful but cannot replace either required matrix result.

- [ ] **Step 5: Install external Unity build and BuildReport contracts**

Execute CI Task 5 through its commit. Require `BuildScript` to resolve every target beneath `ZWEB_EVIDENCE_ROOT`, verify artifact hashes relative to that external root, and keep the repository clean. Simulation Task 17 consumes this contract unchanged.

---

### Task 6: Switch Production to Session Authority and Remove Legacy Paths

**Files:**
- Execute the Simulation plan's production integration, presentation, result, parity, and deletion tasks.
- Execute Runtime `DATA-005`, `SCENE-002`, and final Battle/UI/cleanup integration after session results are stable.
- Execute Network custom KCP and old native-client deletion only after replacement gates.

**Interfaces:**
- Consumes: stable runtime, session, presentation, result, replay, and transport replacements.
- Produces: Launcher-to-village-to-battle-to-reward loop using only `ISimBattleSession`, zero Legacy/custom-KCP production references, and transactional exactly-once rewards.

**Verification cycle:** Preserve failing integration and zero-reference audits as red evidence; green requires replacement, parity, full PlayMode, and deletion gates before the corresponding Runtime/Simulation/Network commits remove Legacy or custom KCP code.

- [ ] **Step 1: Complete production session integration and approved mode matrix**

Require all four modes to pass deployment/wave/zero-attacker/timeout/draw/result/reward behavior. Known Legacy bugs are intentional divergences and must not be copied into the oracle.

- [ ] **Step 2: Complete presentation, Launcher, pause/resume, result, and cleanup PlayMode flows**

Run Launcher PlayMode coverage for real pointer click, main village interaction, save/load, battle entry, pause/resume without restart, rollback presentation revision, result save-before-return, scene changes, and zero view/subscription leaks.

- [ ] **Step 3: Prove removal prerequisites before deleting anything**

Run CodeGraph plus source/serialized audits for production references. Required zero-reference sets:

- Legacy `World`, `CombatOrchestrator`, systems, toggle, and direct `SimWorld`/`SimOrchestrator` host access.
- Custom `KcpCore`, old KCP transport/client, obsolete `INetworkClient`/TCP paths covered by replacement rules.
- Serialized scene/prefab GUID references to deleted scripts.

Expected: replacements pass first; deletion commit then passes G1/G2/G3/G4/G5 with no missing scripts.

- [ ] **Step 4: Delete Legacy Combat and custom KCP in separate reviewed commits**

Do not combine these two removals. Each commit includes its zero-reference evidence, replacement tests, metadata deletion, and post-delete compilation/build gates.

---

### Task 7: Execute G0-G5, Close 101 Issues, and Hand Off a Clean Branch

**Files:**
- Execute CI Tasks 6-7 and rerun the complete build workflow using the CI Task 5 infrastructure.
- Update required-test minimums to actual non-decreasing executed counts.
- Close each plan's owned evidence entries serially.

**Interfaces:**
- Consumes: all workstream green artifacts and fix commits.
- Produces: successful G0-G5 jobs, all-Closed ledger, readable hash-addressed evidence, and clean branch.

**Verification cycle:** Any open ledger entry or failed G0-G5 job is red; green requires all 101 closures and all platform gates, followed by the serial evidence commit and an empty worktree.

- [ ] **Step 1: Run full local gates available on the host**

Run:

```bash
scripts/repair/audit-meta.sh "$PWD"
scripts/repair/audit-compiler-contracts.sh "$PWD"
scripts/repair/audit-build-scenes.sh "$PWD"
PYTHONPATH=scripts/ci python3 -m unittest discover -s scripts/ci -p 'test_*.py'
npm ci --prefix ci --ignore-scripts
node --test scripts/audit/evidence-ledger.test.mjs scripts/ci/*.test.mjs
DOTNET_ARTIFACTS="$ZWEB_EVIDENCE_ROOT/master/dotnet-artifacts"
dotnet restore Server/ZWebGLFrameWork.Server.sln --locked-mode \
  --artifacts-path "$DOTNET_ARTIFACTS"
dotnet build Server/ZWebGLFrameWork.Server.sln -c Release --no-restore --warnaserror \
  --artifacts-path "$DOTNET_ARTIFACTS"
dotnet test Server/ZWebGLFrameWork.Server.sln -c Release --no-build --no-restore \
  --artifacts-path "$DOTNET_ARTIFACTS"
```

Then run full EditMode and PlayMode with pinned Unity and validate both XML directories against `ci/required-tests.json`.

Expected: all available commands exit `0`; no skipped/inconclusive/zero-count results.

- [ ] **Step 2: Require successful final CI matrix and smoke artifacts**

Required successful jobs:

- `audit`
- `unity-tests (EditMode)`
- `unity-tests (PlayMode)`
- `server (ubuntu-latest)`
- `server (windows-latest)`
- `build (WebGL)` including trusted HTTPS Playwright click
- `build (StandaloneWindows64)`
- `windows-smoke`

Download the eight final artifacts under the layout defined by CI Task 7 and rerun their validators locally.

- [ ] **Step 3: Close evidence in serialized plan batches**

Close in this order so dependencies and removal modes remain auditable:

1. Baseline 7.
2. Runtime issues except cross-plan battle/result closure.
3. Simulation replacement/fixed issues, then Runtime `DATA-005`/`SCENE-002` integration.
4. Network/Replay/Server issues, using `RemovedByReplacement` only for approved removed symbols with replacement evidence.
5. CI 6.

After each batch, run ledger validation without `--require-all-closed`; after the fifth batch, require all Closed.

- [ ] **Step 4: Run final all-closed and clean-tree contract**

Run:

```bash
node scripts/audit/evidence-ledger.mjs validate \
  --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json \
  --require-all-closed true
test -z "$(git status --porcelain --untracked-files=all)"
git log --oneline --decorate 4a6738302ade966ae7a5eae1abc51487b29402ca..HEAD
```

Expected: ledger reports `PASS issues=101`, clean-tree emits nothing, and history shows small issue-focused commits plus explicit removal/evidence commits.

## Self-Review Record

- Spec coverage: Tasks 1-7 coordinate G0-G5, every approved target/mode, authoritative replay, transport replacement, Legacy removal, evidence closure, and clean-tree delivery.
- Ownership: machine-readable allocation contains 101 unique IDs and exactly matches the evidence ledger; detailed plans own implementation, while this plan owns only orchestration and final gates.
- Shared-file consistency: BuildScript, BattleState, SimulationCore/Network boundaries, Server, workflow, asset manifest, and evidence ledger each have an explicit single-writer order.
- Simplicity boundary: no Unity migration, unrelated gameplay, mobile release, second battle engine, or alternate evidence system is introduced.
- Environment limitation: source planning is complete without the missing tools, but no Unity/.NET/Windows success claim is allowed until exact external prerequisites and final CI results exist.
