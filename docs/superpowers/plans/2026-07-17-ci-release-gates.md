# CI, Evidence, and Release Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn every audited repair into a machine-enforced merge gate across Unity, .NET 8, WebGL, and Windows, with exact tool versions, nonzero tests, reproducible build summaries, smoke tests, and readable closure evidence.

**Architecture:** Small standard-library validators own JSON, NUnit XML, TRX, workflow YAML, build summaries, and evidence receipts. GitHub Actions composes those validators into independent audit, Unity test, server, build, WebGL smoke, and Windows smoke jobs; each job writes execution output outside tracked source and ends with a clean-tree assertion. Runtime smoke readiness is a quiet diagnostic component that reports a structured marker and only accepts a graceful-exit file when the explicit smoke command-line flag is present.

**Tech Stack:** GitHub Actions, GameCI v4, Unity `2022.3.62f3c1`, Node.js 20, Python 3 standard library, .NET SDK 8, NUnit XML, TRX, Playwright Chromium, HTTPS/OpenSSL, and PowerShell on Windows runners.

## Global Constraints

- Every GameCI action receives `unityVersion: 2022.3.62f3c1`; Unity 6 is forbidden.
- Unity build targets are WebGL and `StandaloneWindows64`; no Android or iOS release claim is added.
- Server restore, build, and test run on both `ubuntu-latest` and `windows-latest` using .NET 8 locked restore, Release, warnings as errors, and nonzero test validation.
- Push and pull-request filters cover `Assets/**`, `Packages/**`, `ProjectSettings/**`, `Server/**`, `.github/workflows/**`, `scripts/**`, `ci/**`, and the evidence ledger/artifacts.
- Test gates reject zero passed tests, count regression below the checked-in minimum, missing required names, failures, skipped tests, and inconclusive tests.
- WebGL smoke uses HTTPS with a locally trusted test CA, never `ignoreHTTPSErrors`; it waits at most 60 seconds for `app-ready`, clicks a real Unity UI button, and rejects mixed content or non-allowlisted console errors.
- Windows smoke waits at most 60 seconds for the same structured readiness marker, requests graceful exit through an explicit smoke-only file contract, and scans the player log with an explicit allowlist.
- All raw logs, XML, TRX, builds, certificates, and browser output live under `${RUNNER_TEMP}` or `${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-repair-evidence}`. Only hash-addressed audit receipts are checked in.
- Every gate ends with `git status --porcelain --untracked-files=all`; output must be empty.
- This plan owns exactly `CI-001`, `CI-002`, `CI-003`, `CI-004`, `CI-005`, and `TEST-009`.

## File Map

- Create `scripts/audit/evidence-ledger.mjs`: close and validate evidence entries using hash-addressed receipts.
- Create `scripts/audit/evidence-ledger.test.mjs`: Node tests for register parity, closed-entry requirements, hash verification, and issue closure.
- Create `docs/superpowers/evidence/artifacts/.gitkeep`: parent for deduplicated receipts; raw build artifacts remain external.
- Create `ci/required-tests.json`: per-mode/per-assembly minimum counts and named tests.
- Create `scripts/ci/validate-unity-results.py`: structured NUnit XML validator.
- Create `scripts/ci/test_validate_unity_results.py`: XML validator tests.
- Create `scripts/ci/validate-trx.py`: nonzero/pass-only .NET TRX validator.
- Create `scripts/ci/test_validate_trx.py`: TRX validator tests.
- Create `ci/package.json` and generated `ci/package-lock.json`: pinned YAML parser and Playwright test dependencies.
- Create `scripts/ci/workflow-contract.test.mjs`: structural tests for versions, triggers, jobs, BuildScript invocation, and clean-tree steps.
- Modify `.github/workflows/ci.yml`: install the complete G0-G5 workflow.
- Create `Assets/Editor/BuildReportJsonWriter.cs`: deterministic BuildReport summary and artifact hashing.
- Modify `Assets/Editor/BuildScript.cs`: call repository validation, enabled scenes, JSON writer, and fail by exception.
- Create `Assets/Tests/Editor/BuildReportJsonWriterTests.cs`: report artifact-hash regression tests.
- Create `scripts/ci/validate-build-summary.mjs`: post-build target/artifact/hash validator.
- Create `scripts/ci/validate-build-summary.test.mjs`: build-summary validator tests.
- Create `Assets/Scripts/UI/Diagnostics/SmokeReadinessReporter.cs`: structured readiness/click/exit marker.
- Create `scripts/ci/serve-https.mjs`: bounded HTTPS static server for WebGL artifacts.
- Create `ci/smoke/playwright.config.mjs`: trusted-HTTPS Playwright configuration.
- Create `ci/smoke/webgl.spec.mjs`: canvas-coordinate real-button smoke and console policy.
- Create `ci/smoke/console-allowlist.json`: exact WebGL console warning allowlist.
- Create `ci/smoke/windows-log-allowlist.txt`: anchored Windows log allowlist.
- Modify `.gitignore`: ignore CI dependency caches, browser output, and .NET build directories.

## Execution Order

Execute tasks strictly by number: `1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7`. Evidence tooling and result validators must exist before server, build, and smoke jobs consume them.

---

### Task 1: Make Issue Closure Evidence Machine-Auditable

**Issues closed after the final gate:** `TEST-009`

**Files:**
- Create: `scripts/audit/evidence-ledger.mjs`
- Create: `scripts/audit/evidence-ledger.test.mjs`
- Create: `docs/superpowers/evidence/artifacts/.gitkeep`
- Modify on later closures: `docs/superpowers/specs/2026-07-16-project-repair-evidence.json`

**Interfaces:**
- Consumes: issue register Markdown, ledger JSON, Git commit hashes, and executed red/green/gate files.
- Produces: `node scripts/audit/evidence-ledger.mjs validate --ledger <path> [--require-all-closed true]` and `close --issue <ID> --red <file> --fix <sha> --green <file> --gate <file> --mode <Fixed|RemovedByReplacement> --date <YYYY-MM-DD>`. `--red`, `--fix`, `--green`, `--gate`, and `--replacement` may be repeated.

- [ ] **Step 1: Write the failing ledger tests**

Create `scripts/audit/evidence-ledger.test.mjs`:

```javascript
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { closeIssue, validateLedger } from './evidence-ledger.mjs';

async function fixture() {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-ledger-'));
  await mkdir(path.join(root, 'docs/specs'), { recursive: true });
  const register = 'docs/specs/register.md';
  const ledger = 'docs/specs/evidence.json';
  await writeFile(path.join(root, register),
    '| ID | Severity |\n|---|---|\n| TEST-009 | P1 |\n', 'utf8');
  await writeFile(path.join(root, ledger), JSON.stringify({
    schemaVersion: 1,
    register,
    baselineCommit: '4a6738302ade966ae7a5eae1abc51487b29402ca',
    allowedStatuses: ['Open', 'InProgress', 'Closed'],
    closureModes: ['Fixed', 'RemovedByReplacement'],
    issues: {
      'TEST-009': {
        status: 'Open', redEvidence: [], fixCommits: [], greenEvidence: [],
        gateEvidence: [], closureMode: null, closedAt: null, replacementEvidence: [],
      },
    },
  }, null, 2) + '\n', 'utf8');
  return { root, ledger };
}

test('AuditRegister_ClosedIssuesHaveExecutedEvidence', async () => {
  const { root, ledger } = await fixture();
  const red = path.join(root, 'red.txt');
  const green = path.join(root, 'green.xml');
  const gate = path.join(root, 'gate.json');
  await writeFile(red, 'expected failure\n');
  await writeFile(green, '<test-run result="Passed"/>\n');
  await writeFile(gate, '{"result":"Passed"}\n');

  await closeIssue({
    root, ledgerPath: ledger, issue: 'TEST-009', red, green, gate,
    fix: '1111111111111111111111111111111111111111', mode: 'Fixed',
    date: '2026-07-17', replacement: [], commitExists: () => true,
  });

  const result = await validateLedger({
    root, ledgerPath: ledger, commitExists: () => true,
  });
  assert.deepEqual(result.errors, []);
  const parsed = JSON.parse(await readFile(path.join(root, ledger), 'utf8'));
  assert.equal(parsed.issues['TEST-009'].status, 'Closed');
  assert.equal(parsed.issues['TEST-009'].redEvidence.length, 1);
});

test('closed issue rejects unreadable or changed evidence', async () => {
  const { root, ledger } = await fixture();
  const parsed = JSON.parse(await readFile(path.join(root, ledger), 'utf8'));
  parsed.issues['TEST-009'] = {
    status: 'Closed',
    redEvidence: [{ path: 'docs/superpowers/evidence/artifacts/missing.txt', sha256: '0'.repeat(64) }],
    fixCommits: ['1'.repeat(40)],
    greenEvidence: [{ path: 'docs/superpowers/evidence/artifacts/missing.txt', sha256: '0'.repeat(64) }],
    gateEvidence: [{ path: 'docs/superpowers/evidence/artifacts/missing.txt', sha256: '0'.repeat(64) }],
    closureMode: 'Fixed', closedAt: '2026-07-17', replacementEvidence: [],
  };
  await writeFile(path.join(root, ledger), JSON.stringify(parsed, null, 2) + '\n');
  const result = await validateLedger({ root, ledgerPath: ledger, commitExists: () => true });
  assert.match(result.errors.join('\n'), /does not exist/);
});

test('register and ledger issue sets must be identical', async () => {
  const { root, ledger } = await fixture();
  const parsed = JSON.parse(await readFile(path.join(root, ledger), 'utf8'));
  parsed.issues['CI-001'] = structuredClone(parsed.issues['TEST-009']);
  await writeFile(path.join(root, ledger), JSON.stringify(parsed, null, 2) + '\n');
  const result = await validateLedger({ root, ledgerPath: ledger, commitExists: () => true });
  assert.match(result.errors.join('\n'), /issue sets differ/);
});
```

Run:

```bash
export ZWEB_EVIDENCE_ROOT="${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-repair-evidence}"
mkdir -p "$ZWEB_EVIDENCE_ROOT/ci/TEST-009"
set +e
node --test scripts/audit/evidence-ledger.test.mjs \
  > "$ZWEB_EVIDENCE_ROOT/ci/TEST-009/red.txt" 2>&1
status=$?
set -e
test "$status" -ne 0
rg -F 'ERR_MODULE_NOT_FOUND' "$ZWEB_EVIDENCE_ROOT/ci/TEST-009/red.txt"
```

Expected: nonzero because `evidence-ledger.mjs` does not exist.

- [ ] **Step 2: Implement the ledger command and validator**

Create `scripts/audit/evidence-ledger.mjs`:

```javascript
#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { existsSync } from 'node:fs';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const defaultLedger = 'docs/superpowers/specs/2026-07-16-project-repair-evidence.json';

function sha256(buffer) {
  return createHash('sha256').update(buffer).digest('hex');
}

function gitCommitExists(root, commit) {
  return spawnSync('git', ['cat-file', '-e', `${commit}^{commit}`], {
    cwd: root, stdio: 'ignore',
  }).status === 0;
}

function parseRegisterIds(markdown) {
  return [...markdown.matchAll(/^\|\s*([A-Z]+-\d{3})\s*\|/gm)]
    .map(match => match[1]).sort();
}

async function ingestArtifact(root, source) {
  const bytes = await readFile(source);
  const digest = sha256(bytes);
  const extension = path.extname(source).toLowerCase().replace(/[^.a-z0-9]/g, '') || '.bin';
  const relative = `docs/superpowers/evidence/artifacts/${digest}${extension}`;
  const destination = path.join(root, relative);
  await mkdir(path.dirname(destination), { recursive: true });
  if (!existsSync(destination)) await copyFile(source, destination);
  return { path: relative, sha256: digest, bytes: bytes.length };
}

async function validateEvidence(root, issue, label, values, errors) {
  if (!Array.isArray(values) || values.length === 0) {
    errors.push(`${issue} ${label} must contain at least one artifact`);
    return;
  }
  for (const value of values) {
    if (!value || typeof value.path !== 'string' || !/^[a-f0-9]{64}$/.test(value.sha256 || '')) {
      errors.push(`${issue} ${label} has an invalid receipt`);
      continue;
    }
    const artifact = path.join(root, value.path);
    if (!existsSync(artifact)) {
      errors.push(`${issue} ${label} artifact does not exist: ${value.path}`);
      continue;
    }
    const bytes = await readFile(artifact);
    if (sha256(bytes) !== value.sha256)
      errors.push(`${issue} ${label} artifact hash mismatch: ${value.path}`);
    if (value.bytes !== undefined && value.bytes !== bytes.length)
      errors.push(`${issue} ${label} artifact byte count mismatch: ${value.path}`);
  }
}

export async function validateLedger({
  root, ledgerPath, commitExists = gitCommitExists, requireAllClosed = false,
}) {
  const errors = [];
  const absoluteLedger = path.join(root, ledgerPath);
  const ledger = JSON.parse(await readFile(absoluteLedger, 'utf8'));
  const registerIds = parseRegisterIds(
    await readFile(path.join(root, ledger.register), 'utf8'));
  const ledgerIds = Object.keys(ledger.issues || {}).sort();
  if (JSON.stringify(registerIds) !== JSON.stringify(ledgerIds))
    errors.push('register and ledger issue sets differ');

  for (const issue of ledgerIds) {
    const entry = ledger.issues[issue];
    if (!ledger.allowedStatuses.includes(entry.status))
      errors.push(`${issue} has invalid status ${entry.status}`);
    if (requireAllClosed && entry.status !== 'Closed')
      errors.push(`${issue} is not Closed`);
    if (entry.status !== 'Closed') continue;
    if (!ledger.closureModes.includes(entry.closureMode))
      errors.push(`${issue} has invalid closure mode ${entry.closureMode}`);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(entry.closedAt || ''))
      errors.push(`${issue} has invalid closedAt`);
    if (!Array.isArray(entry.fixCommits) || entry.fixCommits.length === 0)
      errors.push(`${issue} has no fix commit`);
    for (const commit of entry.fixCommits || []) {
      if (!/^[a-f0-9]{40}$/.test(commit) || !commitExists(root, commit))
        errors.push(`${issue} fix commit is unreadable: ${commit}`);
    }
    await validateEvidence(root, issue, 'redEvidence', entry.redEvidence, errors);
    await validateEvidence(root, issue, 'greenEvidence', entry.greenEvidence, errors);
    await validateEvidence(root, issue, 'gateEvidence', entry.gateEvidence, errors);
    if (entry.closureMode === 'RemovedByReplacement')
      await validateEvidence(root, issue, 'replacementEvidence', entry.replacementEvidence, errors);
  }
  return { errors, issueCount: ledgerIds.length };
}

export async function closeIssue(options) {
  const { root, ledgerPath, issue, red, green, gate, fix, mode, date } = options;
  const commitExists = options.commitExists || gitCommitExists;
  const asArray = value => Array.isArray(value) ? value : [value];
  const fixes = asArray(fix);
  for (const commit of fixes) {
    if (!/^[a-f0-9]{40}$/.test(commit) || !commitExists(root, commit))
      throw new Error(`Fix commit is not readable: ${commit}`);
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date))
    throw new Error(`Closure date must be YYYY-MM-DD: ${date}`);
  const absoluteLedger = path.join(root, ledgerPath);
  const ledger = JSON.parse(await readFile(absoluteLedger, 'utf8'));
  const entry = ledger.issues?.[issue];
  if (!entry) throw new Error(`Unknown issue: ${issue}`);
  if (!ledger.closureModes.includes(mode)) throw new Error(`Invalid closure mode: ${mode}`);

  entry.status = 'Closed';
  entry.redEvidence = await Promise.all(asArray(red).map(value => ingestArtifact(root, value)));
  entry.fixCommits = fixes;
  entry.greenEvidence = await Promise.all(asArray(green).map(value => ingestArtifact(root, value)));
  entry.gateEvidence = await Promise.all(asArray(gate).map(value => ingestArtifact(root, value)));
  entry.closureMode = mode;
  entry.closedAt = date;
  entry.replacementEvidence = [];
  for (const replacement of options.replacement || [])
    entry.replacementEvidence.push(await ingestArtifact(root, replacement));
  if (mode === 'RemovedByReplacement' && entry.replacementEvidence.length === 0)
    throw new Error(`${issue} requires replacement evidence`);

  await writeFile(absoluteLedger, JSON.stringify(ledger, null, 2) + '\n', 'utf8');
}

function parseArgs(values) {
  const repeated = new Set(['red', 'fix', 'green', 'gate', 'replacement']);
  const parsed = { red: [], fix: [], green: [], gate: [], replacement: [] };
  for (let index = 0; index < values.length; index += 2) {
    const key = values[index];
    const value = values[index + 1];
    if (!key?.startsWith('--') || value === undefined) throw new Error(`Invalid argument: ${key}`);
    const name = key.slice(2);
    if (repeated.has(name)) parsed[name].push(value);
    else parsed[name] = value;
  }
  return parsed;
}

async function main() {
  const [command, ...values] = process.argv.slice(2);
  const args = parseArgs(values);
  const root = process.cwd();
  const ledgerPath = args.ledger || defaultLedger;
  if (command === 'validate') {
    const result = await validateLedger({
      root, ledgerPath, requireAllClosed: args['require-all-closed'] === 'true',
    });
    if (result.errors.length) {
      for (const error of result.errors) console.error(error);
      process.exitCode = 1;
    } else {
      console.log(`[AuditRegister_ClosedIssuesHaveExecutedEvidence] PASS issues=${result.issueCount}`);
    }
    return;
  }
  if (command === 'close') {
    await closeIssue({ root, ledgerPath, ...args });
    console.log(`[evidence-ledger] closed ${args.issue}`);
    return;
  }
  throw new Error('Usage: evidence-ledger.mjs <validate|close> [--key value]');
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
```

- [ ] **Step 3: Run green tests and the real 101-ID ledger validation**

Run:

```bash
chmod +x scripts/audit/evidence-ledger.mjs
mkdir -p docs/superpowers/evidence/artifacts
touch docs/superpowers/evidence/artifacts/.gitkeep
node --test scripts/audit/evidence-ledger.test.mjs \
  | tee "$ZWEB_EVIDENCE_ROOT/ci/TEST-009/green.txt"
node scripts/audit/evidence-ledger.mjs validate \
  --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json \
  | tee "$ZWEB_EVIDENCE_ROOT/ci/TEST-009/gate.txt"
```

Expected: three Node tests pass; real ledger validation prints `PASS issues=101` while Open entries remain valid.

- [ ] **Step 4: Commit the evidence contract**

Run:

```bash
git add scripts/audit/evidence-ledger.mjs scripts/audit/evidence-ledger.test.mjs \
  docs/superpowers/evidence/artifacts/.gitkeep
git diff --cached --check
git commit -m "test: enforce audited issue closure evidence"
```

Expected: commit succeeds; all other plans may now use the `close` interface without inventing ledger formats.

---

### Task 2: Reject Empty, Skipped, Missing, and Regressed Unity Tests

**Issues closed after the final gate:** `CI-003`

**Files:**
- Create: `ci/required-tests.json`
- Create: `scripts/ci/validate-unity-results.py`
- Create: `scripts/ci/test_validate_unity_results.py`

**Interfaces:**
- Consumes: one directory containing GameCI NUnit XML files and a test mode.
- Produces: `python3 scripts/ci/validate-unity-results.py --mode <EditMode|PlayMode> --manifest ci/required-tests.json --results <directory>`.

- [ ] **Step 1: Write failing structured-result tests**

Create `scripts/ci/test_validate_unity_results.py`:

```python
import json
import tempfile
import unittest
from pathlib import Path

from validate_unity_results import validate


class UnityResultValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.manifest = self.root / "required.json"
        self.manifest.write_text(json.dumps({
            "schemaVersion": 1,
            "modes": {
                "PlayMode": {
                    "assemblies": {
                        "ZWebGLFrameWork.Runtime.Tests": {
                            "minimumPassed": 1,
                            "requiredNames": ["LauncherPlayMode_EntersInteractiveVillage"]
                        }
                    }
                }
            }
        }), encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def write_result(self, case_result="Passed", case_name="LauncherPlayMode_EntersInteractiveVillage"):
        (self.root / "result.xml").write_text(f'''<?xml version="1.0"?>
<test-run result="{case_result}">
  <test-suite type="Assembly" name="ZWebGLFrameWork.Runtime.Tests" result="{case_result}">
    <test-case fullname="ZWebGLFrameWork.Runtime.Tests.Smoke.{case_name}" result="{case_result}" />
  </test-suite>
</test-run>''', encoding="utf-8")

    def test_accepts_nonzero_named_pass(self):
        self.write_result()
        self.assertEqual(validate("PlayMode", self.manifest, self.root), [])

    def test_PlayModeGate_RequiresExpectedNonZeroTestCount(self):
        (self.root / "result.xml").write_text('<test-run result="Passed"/>', encoding="utf-8")
        self.assertRegex("\n".join(validate("PlayMode", self.manifest, self.root)), "minimumPassed")

    def test_rejects_skipped_or_inconclusive(self):
        self.write_result(case_result="Skipped")
        self.assertRegex("\n".join(validate("PlayMode", self.manifest, self.root)), "not passed")

    def test_rejects_missing_required_name(self):
        self.write_result(case_name="DifferentTest")
        self.assertRegex("\n".join(validate("PlayMode", self.manifest, self.root)), "required test")


if __name__ == "__main__":
    unittest.main()
```

Run:

```bash
set +e
PYTHONPATH=scripts/ci python3 -m unittest scripts/ci/test_validate_unity_results.py \
  > "$ZWEB_EVIDENCE_ROOT/ci/CI-003-red.txt" 2>&1
status=$?
set -e
test "$status" -ne 0
rg -F 'No module named' "$ZWEB_EVIDENCE_ROOT/ci/CI-003-red.txt"
```

Expected: import failure because the validator is absent.

- [ ] **Step 2: Implement the NUnit XML validator**

Create `scripts/ci/validate-unity-results.py`:

```python
#!/usr/bin/env python3
import argparse
import json
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


PASS = {"Passed", "Success"}


def collect_cases(node, current_assembly, cases):
    if node.tag == "test-suite" and node.attrib.get("type") == "Assembly":
        current_assembly = Path(node.attrib.get("name", "")).stem
    if node.tag == "test-case":
        cases[current_assembly].append({
            "name": node.attrib.get("fullname") or node.attrib.get("name", ""),
            "result": node.attrib.get("result", ""),
        })
    for child in node:
        collect_cases(child, current_assembly, cases)


def validate(mode, manifest_path, results_path):
    errors = []
    manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
    mode_contract = manifest.get("modes", {}).get(mode)
    if mode_contract is None:
        return [f"manifest has no mode {mode}"]

    result_root = Path(results_path)
    xml_files = sorted(result_root.rglob("*.xml")) if result_root.is_dir() else [result_root]
    if not xml_files:
        return [f"no NUnit XML found beneath {result_root}"]

    cases = defaultdict(list)
    for xml_file in xml_files:
        try:
            collect_cases(ET.parse(xml_file).getroot(), "", cases)
        except ET.ParseError as error:
            errors.append(f"invalid XML {xml_file}: {error}")

    for assembly, contract in mode_contract.get("assemblies", {}).items():
        assembly_cases = cases.get(assembly, [])
        passed = [case for case in assembly_cases if case["result"] in PASS]
        if len(passed) < contract["minimumPassed"]:
            errors.append(
                f"{mode}/{assembly} minimumPassed={contract['minimumPassed']} actual={len(passed)}")
        non_passed = [case for case in assembly_cases if case["result"] not in PASS]
        for case in non_passed:
            errors.append(f"{mode}/{assembly} test not passed: {case['name']} result={case['result']}")
        names = {case["name"].split("(", 1)[0].rsplit(".", 1)[-1] for case in passed}
        for required in contract.get("requiredNames", []):
            if required not in names:
                errors.append(f"{mode}/{assembly} required test missing: {required}")

    all_cases = [case for values in cases.values() for case in values]
    for case in all_cases:
        if case["result"] not in PASS:
            errors.append(f"global test not passed: {case['name']} result={case['result']}")
    if not all_cases:
        errors.append(f"{mode} produced zero tests")
    return sorted(set(errors))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", required=True, choices=("EditMode", "PlayMode"))
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--results", required=True)
    args = parser.parse_args()
    errors = validate(args.mode, args.manifest, args.results)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print(f"[PlayModeGate_RequiresExpectedNonZeroTestCount] PASS mode={args.mode}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 3: Check in exact minimums and named closure tests**

Create `ci/required-tests.json` after the implementation workstreams have added their assemblies:

```json
{
  "schemaVersion": 1,
  "modes": {
    "EditMode": {
      "assemblies": {
        "ZWebGLFrameWork.Core.Tests": {
          "minimumPassed": 28,
          "requiredNames": ["SubscribeAndPublish_ZeroParams_HandlerIsCalled", "ValueChanged_TriggersOnChanged"]
        },
        "ZWebGLFrameWork.Editor.Tests": {
          "minimumPassed": 10,
          "requiredNames": [
            "EditorAssembly_AutoFixProcessor_Compiles",
            "SimulationSystems_RefDelegates_Compile",
            "GameplayAssembly_ResourceProducer_Compiles",
            "RepositoryMetaAudit_HasNoMissingOrOrphanedMeta",
            "RequiredRuntimeAssets_AllProductionKeysResolve",
            "BuildScenes_LauncherIsEnabledSceneZero",
            "BuildScript_EmitsMachineReadableBuildReport",
            "ProductionAssemblies_DoNotBypassSimulationSession",
            "LegacyCombat_SourceAndToggleAreAbsent"
          ]
        },
        "ZWebGLFrameWork.Network.Tests": {
          "minimumPassed": 10,
          "requiredNames": ["WebGLAssembly_HasNoSocketThreadOrNativeKcpReferences", "NativeKcp_FragmentedMessage_RoundTrips"]
        },
        "ZWebGLFrameWork.SimulationCore.Tests": {
          "minimumPassed": 60,
          "requiredNames": [
            "UnityAndValidator_SameBattleInitAndCommands_SameTickResults",
            "Rollback_AfterTickT_ResumesAtTPlusOneWithOriginalChecksumSequence",
            "ReplayExportParseValidate_RoundTripsExecutedCommands",
            "SupportedModes_MatchApprovedBehaviorMatrix"
          ]
        }
      }
    },
    "PlayMode": {
      "assemblies": {
        "ZWebGLFrameWork.Runtime.Tests": {
          "minimumPassed": 20,
          "requiredNames": [
            "LauncherPlayMode_EntersInteractiveVillage",
            "LauncherButton_PointerClick_InvokesAction",
            "SceneChange_AllGameplayServices_RebindAndReleaseOldScene"
          ]
        },
        "ZWebGLFrameWork.Combat.PlayModeTests": {
          "minimumPassed": 1,
          "requiredNames": [
            "BattleState_UsesSingleSimulationSession_EndToEnd"
          ]
        }
      }
    }
  }
}
```

If an assembly's executed count is higher when this task runs, set `minimumPassed` to that higher executed count before committing. Never reduce a committed minimum to make CI pass; remove a test only in the same reviewed commit that replaces its required behavior.

- [ ] **Step 4: Run green validator tests and commit**

Run:

```bash
chmod +x scripts/ci/validate-unity-results.py
PYTHONPATH=scripts/ci python3 -m unittest scripts/ci/test_validate_unity_results.py \
  | tee "$ZWEB_EVIDENCE_ROOT/ci/CI-003-green.txt"
git add ci/required-tests.json scripts/ci/validate-unity-results.py \
  scripts/ci/test_validate_unity_results.py
git diff --cached --check
git commit -m "test: reject empty or regressed Unity test runs"
```

Expected: four Python tests pass and commit succeeds.

---

### Task 3: Lock Unity Version and Complete Workflow Triggers

**Issues closed after the final gate:** `CI-001`, `CI-002`

**Files:**
- Create: `ci/package.json`
- Generate: `ci/package-lock.json`
- Create: `scripts/ci/workflow-contract.test.mjs`
- Modify: `.gitignore`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `.github/workflows/ci.yml` parsed as YAML 1.2.
- Produces: structural test `Workflow_AllUnityActionsUsePinnedProjectVersion` and `Workflow_TriggersCoverAllGateInputs`.

- [ ] **Step 1: Add pinned CI parser dependencies and failing workflow tests**

Create `ci/package.json`:

```json
{
  "name": "zweb-ci-contracts",
  "private": true,
  "type": "module",
  "engines": { "node": ">=20 <25" },
  "devDependencies": {
    "@playwright/test": "1.61.1",
    "yaml": "2.9.0"
  }
}
```

Generate the lock file:

```bash
npm install --prefix ci --package-lock-only --ignore-scripts
```

Append these execution-only directories to `.gitignore`:

```gitignore
# CI dependency and test output
ci/node_modules/
ci/test-results/
ci/playwright-report/
[Bb]in/
TestResults/
```

Create `scripts/ci/workflow-contract.test.mjs`:

```javascript
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { createRequire } from 'node:module';

const require = createRequire(new URL('../../ci/package.json', import.meta.url));
const YAML = require('yaml');

const source = await readFile('.github/workflows/ci.yml', 'utf8');
const workflow = YAML.parse(source);
const requiredPaths = [
  'Assets/**', 'Packages/**', 'ProjectSettings/**', 'Server/**',
  '.github/workflows/**', 'scripts/**', 'ci/**',
  'docs/superpowers/specs/2026-07-16-project-repair-evidence.json',
  'docs/superpowers/evidence/**',
];

test('Workflow_AllUnityActionsUsePinnedProjectVersion', () => {
  const actionSteps = Object.values(workflow.jobs).flatMap(job => job.steps || [])
    .filter(step => String(step.uses || '').startsWith('game-ci/'));
  assert.ok(actionSteps.length >= 2);
  for (const step of actionSteps)
    assert.equal(step.with?.unityVersion, '${{ env.UNITY_VERSION }}');
  assert.equal(workflow.env.UNITY_VERSION, '2022.3.62f3c1');
  assert.doesNotMatch(source, /2022\.3\.40f1|6000\./);
});

test('Workflow_TriggersCoverAllGateInputs', () => {
  for (const event of ['push', 'pull_request']) {
    const paths = workflow.on[event].paths;
    for (const required of requiredPaths)
      assert.ok(paths.includes(required), `${event} missing ${required}`);
  }
});

test('all mutating jobs end with an exact clean-tree gate', () => {
  for (const [name, job] of Object.entries(workflow.jobs)) {
    if (!job.steps) continue;
    const commands = job.steps.map(step => step.run || '').join('\n');
    assert.match(commands, /git status --porcelain --untracked-files=all/,
      `${name} has no clean-tree command`);
  }
});
```

Run:

```bash
npm ci --prefix ci --ignore-scripts
set +e
node --test scripts/ci/workflow-contract.test.mjs \
  > "$ZWEB_EVIDENCE_ROOT/ci/CI-001-CI-002-red.txt" 2>&1
status=$?
set -e
test "$status" -ne 0
```

Expected: version, trigger, and clean-tree tests fail against the current workflow.

- [ ] **Step 2: Replace workflow header and common audit job**

Use this exact workflow header and audit job; Tasks 4-6 add server/build/smoke jobs beneath it:

```yaml
name: ZWebGL G0-G5

on:
  push:
    branches: [master, develop]
    paths: &gate-paths
      - "Assets/**"
      - "Packages/**"
      - "ProjectSettings/**"
      - "Server/**"
      - ".github/workflows/**"
      - "scripts/**"
      - "ci/**"
      - "docs/superpowers/specs/2026-07-16-project-repair-evidence.json"
      - "docs/superpowers/evidence/**"
  pull_request:
    branches: [master]
    paths: *gate-paths

permissions:
  contents: read
  checks: write

concurrency:
  group: zweb-gates-${{ github.ref }}
  cancel-in-progress: true

env:
  UNITY_VERSION: 2022.3.62f3c1
  DOTNET_VERSION: 8.0.x

jobs:
  audit:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
          lfs: true
      - uses: actions/setup-node@v4
        with:
          node-version: 20
          cache: npm
          cache-dependency-path: ci/package-lock.json
      - run: npm ci --prefix ci --ignore-scripts
      - run: node --test scripts/audit/evidence-ledger.test.mjs scripts/ci/*.test.mjs
      - run: PYTHONPATH=scripts/ci python3 -m unittest discover -s scripts/ci -p 'test_*.py'
      - run: node scripts/audit/evidence-ledger.mjs validate --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json
      - run: scripts/repair/audit-meta.sh "$PWD"
      - name: Clean tree
        run: test -z "$(git status --porcelain --untracked-files=all)"

  unity-tests:
    needs: audit
    runs-on: ubuntu-latest
    strategy:
      fail-fast: false
      matrix:
        testMode: [EditMode, PlayMode]
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
          lfs: true
      - uses: actions/cache@v4
        with:
          path: Library
          key: Library-${{ env.UNITY_VERSION }}-${{ matrix.testMode }}-${{ hashFiles('Assets/**', 'Packages/**', 'ProjectSettings/**') }}
          restore-keys: Library-${{ env.UNITY_VERSION }}-${{ matrix.testMode }}-
      - name: Unity ${{ matrix.testMode }}
        uses: game-ci/unity-test-runner@v4
        env:
          UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}
        with:
          githubToken: ${{ secrets.GITHUB_TOKEN }}
          unityVersion: ${{ env.UNITY_VERSION }}
          testMode: ${{ matrix.testMode }}
          artifactsPath: ${{ runner.temp }}/unity-${{ matrix.testMode }}
          checkName: Unity ${{ matrix.testMode }} ${{ env.UNITY_VERSION }}
          customParameters: -nographics -batchmode
      - name: Validate nonzero required tests
        run: python3 scripts/ci/validate-unity-results.py --mode "${{ matrix.testMode }}" --manifest ci/required-tests.json --results "${{ runner.temp }}/unity-${{ matrix.testMode }}"
      - if: always()
        uses: actions/upload-artifact@v4
        with:
          name: unity-${{ matrix.testMode }}
          path: ${{ runner.temp }}/unity-${{ matrix.testMode }}
      - name: Clean tree
        run: test -z "$(git status --porcelain --untracked-files=all)"
```

- [ ] **Step 3: Run the green structural tests and commit the workflow foundation**

Run after Tasks 4-6 have completed the same workflow file:

```bash
npm ci --prefix ci --ignore-scripts
node --test scripts/ci/workflow-contract.test.mjs \
  | tee "$ZWEB_EVIDENCE_ROOT/ci/CI-001-CI-002-green.txt"
git add .gitignore ci/package.json ci/package-lock.json scripts/ci/workflow-contract.test.mjs \
  .github/workflows/ci.yml
git diff --cached --check
git commit -m "ci: pin Unity and cover all gate inputs"
```

Expected: all structural tests pass and no `2022.3.40f1` remains.

---

### Task 4: Build and Test the .NET 8 Server on Linux and Windows

**Issues closed after the final gate:** `CI-004`

**Files:**
- Create: `scripts/ci/validate-trx.py`
- Create: `scripts/ci/test_validate_trx.py`
- Modify: `scripts/ci/workflow-contract.test.mjs`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `Server/ZWebGLFrameWork.Server.sln`, project lock files created by the Network/Server plan, and one or more TRX files.
- Produces: `python3 scripts/ci/validate-trx.py --results <directory> --minimum 1` and a two-OS server matrix.

- [ ] **Step 1: Write failing TRX validation tests**

Create `scripts/ci/test_validate_trx.py`:

```python
import tempfile
import unittest
from pathlib import Path

from validate_trx import validate


class TrxValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def write(self, outcomes):
        rows = "".join(
            f'<UnitTestResult testName="test{index}" outcome="{outcome}" />'
            for index, outcome in enumerate(outcomes)
        )
        (self.root / "results.trx").write_text(
            f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{rows}</Results></TestRun>',
            encoding="utf-8")

    def test_accepts_nonzero_passed_tests(self):
        self.write(["Passed", "Passed"])
        self.assertEqual(validate(self.root, 1), [])

    def test_rejects_zero_tests(self):
        self.write([])
        self.assertRegex("\n".join(validate(self.root, 1)), "minimum")

    def test_rejects_failed_or_skipped_results(self):
        self.write(["Passed", "Failed", "NotExecuted"])
        self.assertRegex("\n".join(validate(self.root, 1)), "not Passed")


if __name__ == "__main__":
    unittest.main()
```

Run:

```bash
set +e
PYTHONPATH=scripts/ci python3 -m unittest scripts/ci/test_validate_trx.py \
  > "$ZWEB_EVIDENCE_ROOT/ci/CI-004-red.txt" 2>&1
status=$?
set -e
test "$status" -ne 0
rg -F 'No module named' "$ZWEB_EVIDENCE_ROOT/ci/CI-004-red.txt"
```

Expected: import failure because `validate_trx.py` is absent.

- [ ] **Step 2: Implement structured TRX validation**

Create `scripts/ci/validate-trx.py`:

```python
#!/usr/bin/env python3
import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def local_name(tag):
    return tag.rsplit("}", 1)[-1]


def validate(results_path, minimum):
    root = Path(results_path)
    files = sorted(root.rglob("*.trx")) if root.is_dir() else [root]
    if not files:
        return [f"no TRX files found beneath {root}"]
    results = []
    errors = []
    for trx in files:
        try:
            document = ET.parse(trx).getroot()
        except ET.ParseError as error:
            errors.append(f"invalid TRX {trx}: {error}")
            continue
        results.extend(
            node.attrib for node in document.iter()
            if local_name(node.tag) == "UnitTestResult"
        )
    passed = [result for result in results if result.get("outcome") == "Passed"]
    if len(passed) < minimum:
        errors.append(f"server tests minimum={minimum} actualPassed={len(passed)}")
    for result in results:
        if result.get("outcome") != "Passed":
            errors.append(
                f"server test not Passed: {result.get('testName', '')} outcome={result.get('outcome', '')}")
    return sorted(set(errors))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", required=True)
    parser.add_argument("--minimum", type=int, required=True)
    args = parser.parse_args()
    errors = validate(args.results, args.minimum)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print("[CI_ServerProjects_BuildAndTestReleaseWarnAsError] PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 3: Extend workflow contract tests for the server matrix**

Append to `scripts/ci/workflow-contract.test.mjs`:

```javascript
test('CI_ServerProjects_BuildAndTestReleaseWarnAsError', () => {
  const server = workflow.jobs.server;
  assert.deepEqual(server.strategy.matrix.os, ['ubuntu-latest', 'windows-latest']);
  const commands = server.steps.map(step => step.run || '').join('\n');
  assert.match(commands, /dotnet restore Server\/ZWebGLFrameWork\.Server\.sln --locked-mode/);
  assert.match(commands, /dotnet build Server\/ZWebGLFrameWork\.Server\.sln -c Release --no-restore --warnaserror/);
  assert.match(commands, /dotnet test Server\/ZWebGLFrameWork\.Server\.sln -c Release --no-build --no-restore/);
  assert.match(commands, /--artifacts-path "\$DOTNET_ARTIFACTS"/);
  assert.match(commands, /validate-trx\.py --results/);
});
```

- [ ] **Step 4: Add the two-OS server job**

Append this job under `.github/workflows/ci.yml` `jobs`:

```yaml
  server:
    needs: audit
    runs-on: ${{ matrix.os }}
    env:
      DOTNET_ARTIFACTS: ${{ runner.temp }}/dotnet-artifacts
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest]
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
          cache: true
          cache-dependency-path: Server/**/packages.lock.json
      - name: Locked restore
        shell: bash
        run: dotnet restore Server/ZWebGLFrameWork.Server.sln --locked-mode --artifacts-path "$DOTNET_ARTIFACTS"
      - name: Release build, warnings as errors
        shell: bash
        run: dotnet build Server/ZWebGLFrameWork.Server.sln -c Release --no-restore --warnaserror --artifacts-path "$DOTNET_ARTIFACTS"
      - name: Release tests
        shell: bash
        run: dotnet test Server/ZWebGLFrameWork.Server.sln -c Release --no-build --no-restore --artifacts-path "$DOTNET_ARTIFACTS" --logger "trx;LogFileName=server.trx" --results-directory "${{ runner.temp }}/server-tests"
      - name: Require nonzero passing tests
        shell: bash
        run: python3 scripts/ci/validate-trx.py --results "${{ runner.temp }}/server-tests" --minimum 1
      - if: always()
        uses: actions/upload-artifact@v4
        with:
          name: server-tests-${{ matrix.os }}
          path: ${{ runner.temp }}/server-tests
      - name: Clean tree
        shell: bash
        run: test -z "$(git status --porcelain --untracked-files=all)"
```

- [ ] **Step 5: Run green tests and commit**

Run after the Network/Server plan provides the solution and lock files:

```bash
chmod +x scripts/ci/validate-trx.py
PYTHONPATH=scripts/ci python3 -m unittest scripts/ci/test_validate_trx.py \
  | tee "$ZWEB_EVIDENCE_ROOT/ci/CI-004-green.txt"
DOTNET_ARTIFACTS="$ZWEB_EVIDENCE_ROOT/ci/CI-004-dotnet"
dotnet restore Server/ZWebGLFrameWork.Server.sln --locked-mode \
  --artifacts-path "$DOTNET_ARTIFACTS"
dotnet build Server/ZWebGLFrameWork.Server.sln -c Release --no-restore --warnaserror \
  --artifacts-path "$DOTNET_ARTIFACTS"
dotnet test Server/ZWebGLFrameWork.Server.sln -c Release --no-build --no-restore \
  --artifacts-path "$DOTNET_ARTIFACTS" \
  --logger "trx;LogFileName=server.trx" \
  --results-directory "$ZWEB_EVIDENCE_ROOT/ci/CI-004-server-tests"
python3 scripts/ci/validate-trx.py \
  --results "$ZWEB_EVIDENCE_ROOT/ci/CI-004-server-tests" --minimum 1
git add scripts/ci/validate-trx.py scripts/ci/test_validate_trx.py \
  scripts/ci/workflow-contract.test.mjs .github/workflows/ci.yml
git diff --cached --check
git commit -m "ci: gate .NET 8 server on Linux and Windows"
```

Expected: local .NET commands require a provisioned .NET 8 SDK; the current host has no `dotnet`, so stop before claiming green until SDK 8 is installed. CI must pass on both matrix runners.

---

### Task 5: Emit and Validate Deterministic BuildReport JSON

**Issues closed after the final gate:** contributes to `CI-005`

**Files:**
- Create: `Assets/Editor/BuildReportJsonWriter.cs`
- Modify: `Assets/Editor/BuildScript.cs`
- Create: `Assets/Tests/Editor/BuildReportJsonWriterTests.cs`
- Create: `scripts/ci/validate-build-summary.mjs`
- Create: `scripts/ci/validate-build-summary.test.mjs`
- Modify: `scripts/ci/workflow-contract.test.mjs`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: Unity `BuildReport`, enabled scene paths, target output path, and `ZWEB_EVIDENCE_ROOT`.
- Produces: `${ZWEB_EVIDENCE_ROOT}/builds/Reports/<BuildTarget>.json` with editor version, target, scenes, hashes, result, warning count, and error count; validator accepts `--summary`, `--target`, and `--artifact-root`.

- [ ] **Step 1: Write failing artifact and summary tests**

Create `Assets/Tests/Editor/BuildReportJsonWriterTests.cs`:

```csharp
using System;
using System.IO;
using NUnit.Framework;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class BuildReportJsonWriterTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "zweb-build-report-tests", TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(Path.Combine(_root, "builds", "WebGL"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void BuildScript_EmitsMachineReadableBuildReport()
        {
            var wasm = Path.Combine(_root, "builds", "WebGL", "game.wasm");
            File.WriteAllBytes(wasm, new byte[] { 1, 2, 3, 4 });

            var first = BuildReportJsonWriter.CollectArtifacts(
                Path.Combine(_root, "builds", "WebGL"), _root);
            var second = BuildReportJsonWriter.CollectArtifacts(
                Path.Combine(_root, "builds", "WebGL"), _root);

            Assert.That(first, Has.Length.EqualTo(1));
            Assert.That(first[0].path, Is.EqualTo("builds/WebGL/game.wasm"));
            Assert.That(first[0].bytes, Is.EqualTo(4));
            Assert.That(first[0].sha256, Has.Length.EqualTo(64));
            Assert.That(second[0].sha256, Is.EqualTo(first[0].sha256));
        }

        [Test, NonParallelizable]
        public void BuildScript_UsesConfiguredExternalEvidenceRoot()
        {
            string previous = Environment.GetEnvironmentVariable("ZWEB_EVIDENCE_ROOT");
            try
            {
                Environment.SetEnvironmentVariable("ZWEB_EVIDENCE_ROOT", _root);
                Assert.That(BuildScript.GetEvidenceRoot(), Is.EqualTo(Path.GetFullPath(_root)));
                Assert.That(BuildScript.GetEvidenceRoot(),
                    Does.Not.StartWith(Path.GetFullPath(Directory.GetCurrentDirectory()) + Path.DirectorySeparatorChar));

                string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
                Environment.SetEnvironmentVariable("ZWEB_EVIDENCE_ROOT", projectRoot);
                Assert.Throws<UnityEditor.Build.BuildFailedException>(
                    () => BuildScript.GetEvidenceRoot());
            }
            finally
            {
                Environment.SetEnvironmentVariable("ZWEB_EVIDENCE_ROOT", previous);
            }
        }
    }
}
```

Create `scripts/ci/validate-build-summary.test.mjs`:

```javascript
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { validateSummary } from './validate-build-summary.mjs';

test('accepts a hashed successful WebGL summary', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-summary-'));
  await mkdir(path.join(root, 'builds/WebGL'), { recursive: true });
  const artifacts = {
    'builds/WebGL/index.html': '<canvas></canvas>',
    'builds/WebGL/game.wasm': 'wasm',
    'builds/WebGL/game.data': 'data',
  };
  const entries = [];
  for (const [relative, content] of Object.entries(artifacts)) {
    await writeFile(path.join(root, relative), content);
    entries.push({
      path: relative, bytes: Buffer.byteLength(content),
      sha256: createHash('sha256').update(content).digest('hex'),
    });
  }
  const summary = path.join(root, 'summary.json');
  await writeFile(summary, JSON.stringify({
    schemaVersion: 1, editorVersion: '2022.3.62f3c1', target: 'WebGL',
    enabledScenes: ['Assets/Scence/Launcher.unity'], artifacts: entries,
    result: 'Succeeded', warnings: 0, errors: 0,
  }));
  assert.deepEqual(await validateSummary(summary, root, 'WebGL'), []);
});
```

Run both tests before their implementations:

```bash
export ZWEB_EVIDENCE_ROOT="${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-repair-evidence}"
mkdir -p "$ZWEB_EVIDENCE_ROOT/ci/CI-005"
set +e
node --test scripts/ci/validate-build-summary.test.mjs \
  > "$ZWEB_EVIDENCE_ROOT/ci/CI-005/build-summary-red.txt" 2>&1
node_status=$?
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Editor.Tests.BuildReportJsonWriterTests \
  -testResults "$ZWEB_EVIDENCE_ROOT/ci/CI-005/build-writer-red.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/ci/CI-005/build-writer-red.log"
unity_status=$?
set -e
test "$node_status" -ne 0
test "$unity_status" -ne 0
rg -F 'ERR_MODULE_NOT_FOUND' \
  "$ZWEB_EVIDENCE_ROOT/ci/CI-005/build-summary-red.txt"
```

Expected: Node exits nonzero because `validate-build-summary.mjs` does not exist, and Unity exits nonzero because `BuildReportJsonWriter` does not exist. The exact red receipts are nonempty under `$ZWEB_EVIDENCE_ROOT/ci/CI-005/`.

- [ ] **Step 2: Implement BuildReport JSON generation**

Create `Assets/Editor/BuildReportJsonWriter.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZWebGLFrameWork.Editor
{
    [Serializable]
    public sealed class BuildArtifactRecord
    {
        public string path;
        public long bytes;
        public string sha256;
    }

    [Serializable]
    public sealed class BuildReportDocument
    {
        public int schemaVersion = 1;
        public string editorVersion;
        public string target;
        public string[] enabledScenes;
        public BuildArtifactRecord[] artifacts;
        public string result;
        public int warnings;
        public int errors;
    }

    public static class BuildReportJsonWriter
    {
        public static string Write(BuildReport report, string outputPath, string[] enabledScenes,
            string evidenceRoot)
        {
            var target = report.summary.platform.ToString();
            var document = new BuildReportDocument
            {
                editorVersion = Application.unityVersion,
                target = target,
                enabledScenes = enabledScenes,
                artifacts = CollectArtifacts(outputPath, evidenceRoot),
                result = report.summary.result.ToString(),
                warnings = report.summary.totalWarnings,
                errors = report.summary.totalErrors,
            };
            var reportDirectory = Path.Combine(evidenceRoot, "builds", "Reports");
            Directory.CreateDirectory(reportDirectory);
            var reportPath = Path.Combine(reportDirectory, target + ".json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(document, true) + "\n");
            return reportPath;
        }

        public static BuildArtifactRecord[] CollectArtifacts(string outputPath, string evidenceRoot)
        {
            var files = Directory.Exists(outputPath)
                ? Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories)
                : File.Exists(outputPath)
                    ? Directory.GetFiles(Path.GetDirectoryName(outputPath), "*", SearchOption.AllDirectories)
                    : Array.Empty<string>();
            return files.OrderBy(file => file, StringComparer.Ordinal)
                .Select(file => new BuildArtifactRecord
                {
                    path = ToEvidenceRelative(file, evidenceRoot),
                    bytes = new FileInfo(file).Length,
                    sha256 = ComputeSha256(file),
                })
                .ToArray();
        }

        private static string ToEvidenceRelative(string file, string evidenceRoot)
        {
            var root = Path.GetFullPath(evidenceRoot).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidOperationException($"Build artifact escaped evidence root: {full}");
            return full.Substring(root.Length).Replace('\\', '/');
        }

        private static string ComputeSha256(string file)
        {
            using var stream = File.OpenRead(file);
            using var hash = SHA256.Create();
            return string.Concat(hash.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
```

Replace all four public path wrappers plus `BuildScript.Build` with the baseline plan's enabled-scene and required-asset validation plus this exact external-output flow:

```csharp
public static string GetEvidenceRoot()
{
    var configured = System.Environment.GetEnvironmentVariable("ZWEB_EVIDENCE_ROOT");
    var root = string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(Path.GetTempPath(), "zweb-repair-evidence")
        : configured;
    var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
    var projectRoot = Path.GetFullPath(
        Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar);
    if (string.Equals(fullRoot, projectRoot, System.StringComparison.OrdinalIgnoreCase) ||
        fullRoot.StartsWith(projectRoot + Path.DirectorySeparatorChar,
            System.StringComparison.OrdinalIgnoreCase))
    {
        throw new BuildFailedException(
            $"ZWEB_EVIDENCE_ROOT must be outside the repository: {fullRoot}");
    }
    return fullRoot;
}

public static void BuildWebGL() =>
    Build(Path.Combine(GetEvidenceRoot(), "builds", "WebGL"), BuildTarget.WebGL);

public static void BuildWindows() =>
    Build(Path.Combine(GetEvidenceRoot(), "builds", "Windows", "ZWebGLFrameWork.exe"),
        BuildTarget.StandaloneWindows64);

public static void BuildAndroid() =>
    Build(Path.Combine(GetEvidenceRoot(), "builds", "Android", "ZWebGLFrameWork.apk"),
        BuildTarget.Android);

public static void BuildIOS() =>
    Build(Path.Combine(GetEvidenceRoot(), "builds", "iOS"), BuildTarget.iOS);

private static void Build(string outputPath, BuildTarget target)
{
    Debug.Log($"[BuildScript] Starting build: {target}");
    AssetDatabase.Refresh();
    if (!BuildValidator.ValidateBootPipeline())
        throw new BuildFailedException("Required runtime asset validation failed.");

    var scenes = GetEnabledScenePaths();
    if (scenes.Length == 0)
        throw new BuildFailedException("Build Settings has no enabled scenes.");

    var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
    {
        scenes = scenes,
        locationPathName = outputPath,
        target = target,
        options = BuildOptions.None,
    });
    var summaryPath = BuildReportJsonWriter.Write(
        report, outputPath, scenes, GetEvidenceRoot());
    Debug.Log($"[BuildScript] Build report: {summaryPath}");
    if (report.summary.result != BuildResult.Succeeded)
        throw new BuildFailedException(
            $"Build failed: errors={report.summary.totalErrors}, warnings={report.summary.totalWarnings}");
}
```

Delete both `EditorApplication.Exit` calls so GameCI owns process exit. CI invokes only `BuildWebGL` and `BuildWindows`; dormant Android/iOS wrappers also use the external root and create no release claim.

- [ ] **Step 3: Implement build-summary validation**

Create `scripts/ci/validate-build-summary.mjs`:

```javascript
#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export async function validateSummary(summaryPath, artifactRoot, expectedTarget) {
  const errors = [];
  const document = JSON.parse(await readFile(summaryPath, 'utf8'));
  if (document.schemaVersion !== 1) errors.push('schemaVersion must equal 1');
  if (document.editorVersion !== '2022.3.62f3c1') errors.push(`wrong editor ${document.editorVersion}`);
  if (document.target !== expectedTarget) errors.push(`wrong target ${document.target}`);
  if (document.result !== 'Succeeded') errors.push(`build result ${document.result}`);
  if (document.errors !== 0) errors.push(`build errors ${document.errors}`);
  if (document.enabledScenes?.[0] !== 'Assets/Scence/Launcher.unity')
    errors.push('Launcher is not enabled scene zero');

  const paths = [];
  for (const artifact of document.artifacts || []) {
    const absolute = path.resolve(artifactRoot, artifact.path);
    if (!absolute.startsWith(path.resolve(artifactRoot) + path.sep)) {
      errors.push(`artifact escaped artifact root: ${artifact.path}`);
      continue;
    }
    let bytes;
    try { bytes = await readFile(absolute); }
    catch { errors.push(`artifact is unreadable: ${artifact.path}`); continue; }
    const digest = createHash('sha256').update(bytes).digest('hex');
    if (digest !== artifact.sha256) errors.push(`artifact hash mismatch: ${artifact.path}`);
    if (bytes.length !== artifact.bytes) errors.push(`artifact size mismatch: ${artifact.path}`);
    paths.push(artifact.path);
  }

  if (expectedTarget === 'WebGL') {
    if (!paths.some(value => /\.html$/i.test(value))) errors.push('WebGL HTML missing');
    if (!paths.some(value => /\.wasm(?:\.(?:br|gz))?$/i.test(value))) errors.push('WebGL WASM missing');
    if (!paths.some(value => /\.data(?:\.(?:br|gz))?$/i.test(value))) errors.push('WebGL data missing');
  } else if (expectedTarget === 'StandaloneWindows64') {
    if (!paths.some(value => /\.exe$/i.test(value))) errors.push('Windows executable missing');
    if (!paths.some(value => /_Data\//.test(value))) errors.push('Windows Data directory missing');
  }
  return errors;
}

async function main() {
  const values = Object.fromEntries(
    process.argv.slice(2).reduce((pairs, value, index, all) => {
      if (index % 2 === 0) pairs.push([value.slice(2), all[index + 1]]);
      return pairs;
    }, []));
  const errors = await validateSummary(values.summary, values['artifact-root'], values.target);
  if (errors.length) { console.error(errors.join('\n')); process.exitCode = 1; }
  else console.log(`[CI_BuildJobs_InvokeRepositoryBuildScriptAndSmokeArtifacts] PASS target=${values.target}`);
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error.stack); process.exitCode = 1; });
```

- [ ] **Step 4: Add build workflow contracts and job**

Append this test to `workflow-contract.test.mjs`:

```javascript
test('CI_BuildJobs_InvokeRepositoryBuildScriptAndSmokeArtifacts', () => {
  const build = workflow.jobs.build;
  const action = build.steps.find(step => step.uses === 'game-ci/unity-builder@v4');
  assert.equal(action.with.unityVersion, '${{ env.UNITY_VERSION }}');
  assert.match(String(action.with.buildMethod), /ZWebGLFrameWork\.Editor\.BuildScript/);
  assert.match(String(build.env.ZWEB_EVIDENCE_ROOT), /runner\.temp/);
  assert.match(String(action.with.buildPath), /runner\.temp/);
  const commands = build.steps.map(step => step.run || '').join('\n');
  assert.match(commands, /validate-build-summary\.mjs/);
  assert.match(commands, /--artifact-root/);
});
```

Append this job to `.github/workflows/ci.yml`:

```yaml
  build:
    needs: [unity-tests, server]
    runs-on: ubuntu-latest
    env:
      ZWEB_EVIDENCE_ROOT: ${{ runner.temp }}/zweb-${{ matrix.targetPlatform }}
    strategy:
      fail-fast: false
      matrix:
        include:
          - targetPlatform: WebGL
            buildMethod: ZWebGLFrameWork.Editor.BuildScript.BuildWebGL
            artifactDir: WebGL
            summary: WebGL
          - targetPlatform: StandaloneWindows64
            buildMethod: ZWebGLFrameWork.Editor.BuildScript.BuildWindows
            artifactDir: Windows
            summary: StandaloneWindows64
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
          lfs: true
      - uses: game-ci/unity-builder@v4
        env:
          UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}
        with:
          unityVersion: ${{ env.UNITY_VERSION }}
          targetPlatform: ${{ matrix.targetPlatform }}
          buildMethod: ${{ matrix.buildMethod }}
          buildPath: ${{ runner.temp }}/zweb-${{ matrix.targetPlatform }}/builds/${{ matrix.artifactDir }}
          allowDirtyBuild: false
      - name: Validate build summary and hashes
        run: node scripts/ci/validate-build-summary.mjs --summary "$ZWEB_EVIDENCE_ROOT/builds/Reports/${{ matrix.summary }}.json" --target "${{ matrix.summary }}" --artifact-root "$ZWEB_EVIDENCE_ROOT"
      - uses: actions/upload-artifact@v4
        with:
          name: build-${{ matrix.targetPlatform }}
          path: |
            ${{ runner.temp }}/zweb-${{ matrix.targetPlatform }}/builds/${{ matrix.artifactDir }}
            ${{ runner.temp }}/zweb-${{ matrix.targetPlatform }}/builds/Reports/${{ matrix.summary }}.json
      - name: Clean tree
        run: test -z "$(git status --porcelain --untracked-files=all)"
```

- [ ] **Step 5: Run green focused tests and commit**

Run:

```bash
node --test scripts/ci/validate-build-summary.test.mjs \
  | tee "$ZWEB_EVIDENCE_ROOT/ci/CI-005/build-summary-green.txt"
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Editor.Tests.BuildReportJsonWriterTests \
  -testResults "$ZWEB_EVIDENCE_ROOT/ci/CI-005/build-writer-green.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/ci/CI-005/build-writer-green.log"
git add Assets/Editor/BuildReportJsonWriter.cs Assets/Editor/BuildScript.cs \
  Assets/Tests/Editor/BuildReportJsonWriterTests.cs \
  scripts/ci/validate-build-summary.mjs scripts/ci/validate-build-summary.test.mjs \
  scripts/ci/workflow-contract.test.mjs .github/workflows/ci.yml
git diff --cached --check
git commit -m "ci: emit and verify Unity build reports"
```

Expected: Node and EditMode tests pass. Formal WebGL/Windows BuildReport evidence is produced in Task 6, not inferred from unit tests.

---

### Task 6: Run Trusted-HTTPS WebGL and Structured Windows Smoke Tests

**Issues closed after the final gate:** completes `CI-005`

**Files:**
- Create: `Assets/Scripts/UI/Diagnostics/SmokeReadinessReporter.cs`
- Create: `scripts/ci/serve-https.mjs`
- Create: `scripts/ci/validate-player-log.mjs`
- Create: `scripts/ci/validate-player-log.test.mjs`
- Create: `ci/smoke/playwright.config.mjs`
- Create: `ci/smoke/webgl.spec.mjs`
- Create: `ci/smoke/console-allowlist.json`
- Create: `ci/smoke/windows-log-allowlist.txt`
- Modify: `scripts/ci/workflow-contract.test.mjs`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: WebGL query `zweb-smoke=1` or native arguments `-zwebSmokeExitFile <absolute-path>`.
- Produces: `[ZWEB_SMOKE]` JSON markers `app-ready`, `button-clicked`, and `app-exit`; HTTPS server arguments `--root`, `--cert`, `--key`, `--port`; player log validator arguments `--log`, `--allowlist`.

- [ ] **Step 1: Add a real UI readiness and graceful-exit reporter**

Create `Assets/Scripts/UI/Diagnostics/SmokeReadinessReporter.cs`:

```csharp
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ZWebGLFrameWork.UI
{
    public sealed class SmokeReadinessReporter : MonoBehaviour
    {
        [Serializable]
        private sealed class Marker
        {
            public string @event;
            public string state;
            public float buttonX;
            public float buttonY;
            public int screenWidth;
            public int screenHeight;
        }

        private string _exitFile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallWhenRequested()
        {
            if (!TryGetSmokeRequest(out var exitFile)) return;
            var instance = new GameObject("[SmokeReadinessReporter]")
                .AddComponent<SmokeReadinessReporter>();
            instance._exitFile = exitFile;
            DontDestroyOnLoad(instance.gameObject);
        }

        private static bool TryGetSmokeRequest(out string exitFile)
        {
            exitFile = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            return Application.absoluteURL.IndexOf("zweb-smoke=1", StringComparison.Ordinal) >= 0;
#else
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
            {
                if (arguments[index] != "-zwebSmokeExitFile") continue;
                exitFile = Path.GetFullPath(arguments[index + 1]);
                return true;
            }
            return false;
#endif
        }

        private IEnumerator Start()
        {
            Button button = null;
            while (button == null)
            {
                button = FindObjectsOfType<Button>(true)
                    .Where(candidate => candidate.isActiveAndEnabled &&
                        candidate.interactable && candidate.gameObject.activeInHierarchy)
                    .OrderBy(candidate => GetHierarchyPath(candidate.transform), StringComparer.Ordinal)
                    .FirstOrDefault();
                if (button == null) yield return null;
            }

            yield return null;
            var rect = (RectTransform)button.transform;
            var canvas = button.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var worldCenter = (corners[0] + corners[2]) * 0.5f;
            var screenCenter = RectTransformUtility.WorldToScreenPoint(camera, worldCenter);

            button.onClick.AddListener(ReportButtonClicked);
            Emit(new Marker
            {
                @event = "app-ready",
                state = SceneManager.GetActiveScene().name,
                buttonX = screenCenter.x,
                buttonY = screenCenter.y,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
            });
        }

        private void Update()
        {
            if (string.IsNullOrEmpty(_exitFile) || !File.Exists(_exitFile)) return;
            File.Delete(_exitFile);
            Emit(new Marker { @event = "app-exit", state = SceneManager.GetActiveScene().name });
            Application.Quit(0);
            _exitFile = null;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static void ReportButtonClicked()
        {
            Emit(new Marker { @event = "button-clicked", state = SceneManager.GetActiveScene().name });
        }

        private static void Emit(Marker marker)
        {
            Debug.Log("[ZWEB_SMOKE] " + JsonUtility.ToJson(marker));
        }
    }
}
```

The reporter creates no visible UI, does not run without the query/argument contract, and attaches only an observational listener to an existing real `Button`.

- [ ] **Step 2: Add the bounded HTTPS static server**

Create `scripts/ci/serve-https.mjs`:

```javascript
#!/usr/bin/env node
import { createServer } from 'node:https';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';

const args = Object.fromEntries(process.argv.slice(2).reduce((pairs, value, index, all) => {
  if (index % 2 === 0) pairs.push([value.slice(2), all[index + 1]]);
  return pairs;
}, []));
const root = path.resolve(args.root);
const port = Number(args.port || 8443);
const mime = new Map([
  ['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'], ['.json', 'application/json'],
  ['.wasm', 'application/wasm'], ['.data', 'application/octet-stream'],
  ['.br', 'application/octet-stream'], ['.gz', 'application/gzip'],
]);

const options = { key: await readFile(args.key), cert: await readFile(args.cert) };
const server = createServer(options, async (request, response) => {
  try {
    const requestPath = decodeURIComponent(new URL(request.url, `https://${request.headers.host}`).pathname);
    const relative = requestPath === '/' ? 'index.html' : requestPath.replace(/^\/+/, '');
    const file = path.resolve(root, relative);
    if (!file.startsWith(root + path.sep)) {
      response.writeHead(403).end('forbidden');
      return;
    }
    const info = await stat(file);
    if (!info.isFile()) throw new Error('not a file');
    const bytes = await readFile(file);
    const compression = /\.(?:br|gz)$/i.test(file) ? path.extname(file).toLowerCase() : '';
    const logicalFile = compression ? file.slice(0, -compression.length) : file;
    const extension = path.extname(logicalFile).toLowerCase();
    const headers = {
      'content-type': mime.get(extension) || 'application/octet-stream',
      'content-length': bytes.length,
      'cache-control': 'no-store',
      'cross-origin-opener-policy': 'same-origin',
      'cross-origin-embedder-policy': 'require-corp',
    };
    if (compression === '.br') headers['content-encoding'] = 'br';
    if (compression === '.gz') headers['content-encoding'] = 'gzip';
    response.writeHead(200, headers);
    response.end(bytes);
  } catch {
    response.writeHead(404).end('not found');
  }
});
server.listen(port, '127.0.0.1', () =>
  console.log(`[serve-https] https://localhost:${port} root=${root}`));
```

- [ ] **Step 3: Add Playwright's real-canvas click test**

Create `ci/smoke/playwright.config.mjs`:

```javascript
import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  timeout: 70_000,
  expect: { timeout: 60_000 },
  retries: 0,
  workers: 1,
  reporter: [['line'], ['json', { outputFile: process.env.ZWEB_PLAYWRIGHT_JSON }]],
  use: {
    browserName: 'chromium',
    baseURL: 'https://localhost:8443',
    headless: true,
    ignoreHTTPSErrors: false,
    viewport: { width: 1280, height: 720 },
  },
});
```

Create `ci/smoke/console-allowlist.json`:

```json
{ "patterns": [] }
```

Create `ci/smoke/webgl.spec.mjs`:

```javascript
import { expect, test } from '@playwright/test';
import { readFile } from 'node:fs/promises';

const allowlist = JSON.parse(
  await readFile(new URL('./console-allowlist.json', import.meta.url), 'utf8'))
  .patterns.map(pattern => new RegExp(pattern));

function deferred() {
  let resolve;
  const promise = new Promise(value => { resolve = value; });
  return { promise, resolve };
}

test('WebGL HTTPS app-ready and real button click', async ({ page }) => {
  const ready = deferred();
  const clicked = deferred();
  const failures = [];

  page.on('console', message => {
    const text = message.text();
    const markerIndex = text.indexOf('[ZWEB_SMOKE] ');
    if (markerIndex >= 0) {
      const marker = JSON.parse(text.slice(markerIndex + '[ZWEB_SMOKE] '.length));
      if (marker.event === 'app-ready') ready.resolve(marker);
      if (marker.event === 'button-clicked') clicked.resolve(marker);
    }
    if ((message.type() === 'error' || /mixed content/i.test(text)) &&
        !allowlist.some(pattern => pattern.test(text))) failures.push(text);
  });
  page.on('pageerror', error => failures.push(error.stack || error.message));
  page.on('request', request => {
    if (request.url().startsWith('http://')) failures.push(`mixed content request: ${request.url()}`);
  });

  await page.goto('/?zweb-smoke=1', { waitUntil: 'domcontentloaded' });
  const marker = await Promise.race([
    ready.promise,
    new Promise((_, reject) => setTimeout(() => reject(new Error('app-ready timeout')), 60_000)),
  ]);
  expect(marker.screenWidth).toBeGreaterThan(0);
  expect(marker.screenHeight).toBeGreaterThan(0);

  const canvas = page.locator('canvas').first();
  await expect(canvas).toBeVisible();
  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();
  const x = box.x + (marker.buttonX / marker.screenWidth) * box.width;
  const y = box.y + (1 - marker.buttonY / marker.screenHeight) * box.height;
  await page.mouse.click(x, y);
  await Promise.race([
    clicked.promise,
    new Promise((_, reject) => setTimeout(() => reject(new Error('button-clicked timeout')), 10_000)),
  ]);

  expect(failures).toEqual([]);
  expect(page.url()).toMatch(/^https:\/\/localhost:8443\//);
});
```

- [ ] **Step 4: Add failing structural smoke-gate checks**

Append to `workflow-contract.test.mjs`:

```javascript
test('build smoke gates require trusted HTTPS and structured Windows exit', () => {
  const buildCommands = workflow.jobs.build.steps.map(step => step.run || '').join('\n');
  assert.match(buildCommands, /update-ca-certificates/);
  assert.match(source + buildCommands, /webgl\.spec\.mjs/);
  assert.match(source, /name:\s*webgl-smoke-report/);
  assert.match(source, /runner\.temp.*webgl-playwright\.json/);
  assert.ok(workflow.jobs['windows-smoke'], 'windows-smoke job missing');
  const windowsCommands = workflow.jobs['windows-smoke'].steps.map(step => step.run || '').join('\n');
  assert.ok(workflow.jobs.build.steps.some(step => step.uses === 'actions/setup-node@v4'));
  assert.match(buildCommands, /npm ci --prefix ci --ignore-scripts/);
  assert.match(buildCommands, /npx --no-install playwright install/);
  assert.match(buildCommands, /npx --no-install playwright test/);
  assert.match(windowsCommands, /zwebSmokeExitFile/);
  assert.match(windowsCommands, /app-ready/);
  assert.match(windowsCommands, /-Filter ZWebGLFrameWork\.exe/);
  assert.doesNotMatch(windowsCommands, /Select-Object -First 1/);
  assert.match(windowsCommands, /validate-player-log\.mjs/);
  assert.doesNotMatch(playwrightConfig, /ignoreHTTPSErrors:\s*true/);
});
```

Because Node test callbacks cannot use an undefined helper, add this top-level read beside the workflow read:

```javascript
const playwrightConfig = await readFile('ci/smoke/playwright.config.mjs', 'utf8');
```

Run before adding either smoke job:

```bash
set +e
node --test scripts/ci/workflow-contract.test.mjs \
  > "$ZWEB_EVIDENCE_ROOT/ci/CI-005/workflow-red.txt" 2>&1
status=$?
set -e
test "$status" -ne 0
rg 'update-ca-certificates|windows-smoke job missing' \
  "$ZWEB_EVIDENCE_ROOT/ci/CI-005/workflow-red.txt"
```

Expected: nonzero because the Task 5 workflow has neither trusted-HTTPS steps nor a `windows-smoke` job; `workflow-red.txt` is nonempty and records the failed contract.

- [ ] **Step 5: Add strict native player-log validation**

Create `ci/smoke/windows-log-allowlist.txt` as an intentionally empty file. Add an anchored pattern only after a reviewed, reproducible benign engine line is observed.

Create `scripts/ci/validate-player-log.mjs`:

```javascript
#!/usr/bin/env node
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

export async function validatePlayerLog(logPath, allowlistPath) {
  const lines = (await readFile(logPath, 'utf8')).split(/\r?\n/);
  const patterns = (await readFile(allowlistPath, 'utf8')).split(/\r?\n/)
    .filter(Boolean).map(value => new RegExp(value));
  const forbidden = /\b(?:Exception|Error|Assert|Crash|MissingReference)\b/i;
  const errors = lines.filter(line => forbidden.test(line) &&
    !patterns.some(pattern => pattern.test(line)));
  if (!lines.some(line => line.includes('"event":"app-ready"')))
    errors.push('missing app-ready marker');
  if (!lines.some(line => line.includes('"event":"app-exit"')))
    errors.push('missing app-exit marker');
  return errors;
}

async function main() {
  const args = Object.fromEntries(process.argv.slice(2).reduce((pairs, value, index, all) => {
    if (index % 2 === 0) pairs.push([value.slice(2), all[index + 1]]);
    return pairs;
  }, []));
  const errors = await validatePlayerLog(args.log, args.allowlist);
  if (errors.length) { console.error(errors.join('\n')); process.exitCode = 1; }
  else console.log('[windows-smoke] PASS');
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error.stack); process.exitCode = 1; });
```

Create `scripts/ci/validate-player-log.test.mjs`:

```javascript
import assert from 'node:assert/strict';
import { mkdtemp, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { validatePlayerLog } from './validate-player-log.mjs';

test('requires readiness, exit, and no unallowlisted errors', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-player-log-'));
  const log = path.join(root, 'player.log');
  const allowlist = path.join(root, 'allowlist.txt');
  await writeFile(allowlist, '');
  await writeFile(log,
    '[ZWEB_SMOKE] {"event":"app-ready"}\n[ZWEB_SMOKE] {"event":"app-exit"}\n');
  assert.deepEqual(await validatePlayerLog(log, allowlist), []);
  await writeFile(log, 'Error: broken\n');
  assert.match((await validatePlayerLog(log, allowlist)).join('\n'), /Error: broken/);
});
```

- [ ] **Step 6: Add WebGL and Windows smoke jobs to the workflow**

Insert these WebGL-only steps in the `build` job after summary validation and before artifact upload:

```yaml
      - if: matrix.targetPlatform == 'WebGL'
        uses: actions/setup-node@v4
        with:
          node-version: 20
          cache: npm
          cache-dependency-path: ci/package-lock.json
      - if: matrix.targetPlatform == 'WebGL'
        name: Install locked smoke dependencies
        run: npm ci --prefix ci --ignore-scripts
      - if: matrix.targetPlatform == 'WebGL'
        name: Install Chromium and local trust tools
        run: |
          cd ci
          npx --no-install playwright install --with-deps chromium
          sudo apt-get update
          sudo apt-get install -y libnss3-tools
      - if: matrix.targetPlatform == 'WebGL'
        name: Create and trust local HTTPS CA
        run: |
          certs="${{ runner.temp }}/zweb-certs"
          mkdir -p "$certs"
          openssl genrsa -out "$certs/ca.key" 2048
          openssl req -x509 -new -nodes -key "$certs/ca.key" -sha256 -days 1 \
            -subj '/CN=ZWeb CI Local CA' -out "$certs/ca.crt"
          openssl genrsa -out "$certs/server.key" 2048
          openssl req -new -key "$certs/server.key" -subj '/CN=localhost' -out "$certs/server.csr"
          printf 'subjectAltName=DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\n' > "$certs/server.ext"
          openssl x509 -req -in "$certs/server.csr" -CA "$certs/ca.crt" -CAkey "$certs/ca.key" \
            -CAcreateserial -out "$certs/server.crt" -days 1 -sha256 -extfile "$certs/server.ext"
          sudo cp "$certs/ca.crt" /usr/local/share/ca-certificates/zweb-ci-local.crt
          sudo update-ca-certificates
          mkdir -p "$HOME/.pki/nssdb"
          certutil -d "sql:$HOME/.pki/nssdb" -N --empty-password 2>/dev/null || true
          certutil -d "sql:$HOME/.pki/nssdb" -A -t 'C,,' -n 'ZWeb CI Local CA' -i "$certs/ca.crt"
      - if: matrix.targetPlatform == 'WebGL'
        name: Trusted HTTPS WebGL smoke
        env:
          ZWEB_PLAYWRIGHT_JSON: ${{ runner.temp }}/webgl-playwright.json
        run: |
          certs="${{ runner.temp }}/zweb-certs"
          node scripts/ci/serve-https.mjs --root "$ZWEB_EVIDENCE_ROOT/builds/WebGL" --cert "$certs/server.crt" \
            --key "$certs/server.key" --port 8443 > "${{ runner.temp }}/https-server.log" 2>&1 &
          server_pid=$!
          trap 'kill "$server_pid" 2>/dev/null || true' EXIT
          for attempt in $(seq 1 30); do
            curl --fail --silent https://localhost:8443/ >/dev/null && break
            sleep 1
          done
          curl --fail --silent https://localhost:8443/ >/dev/null
          cd ci
          npx --no-install playwright test --config smoke/playwright.config.mjs smoke/webgl.spec.mjs
      - if: matrix.targetPlatform == 'WebGL'
        name: Upload WebGL smoke report
        uses: actions/upload-artifact@v4
        with:
          name: webgl-smoke-report
          path: ${{ runner.temp }}/webgl-playwright.json
          if-no-files-found: error
```

Append the Windows smoke job:

```yaml
  windows-smoke:
    needs: build
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: actions/setup-node@v4
        with:
          node-version: 20
      - uses: actions/download-artifact@v4
        with:
          name: build-StandaloneWindows64
          path: ${{ runner.temp }}/windows-build
      - name: Start, await readiness, and request graceful exit
        shell: pwsh
        run: |
          $executables = @(Get-ChildItem "${{ runner.temp }}/windows-build" -Recurse -Filter ZWebGLFrameWork.exe)
          if ($executables.Count -ne 1) {
            throw "Expected exactly one ZWebGLFrameWork.exe, found $($executables.Count)"
          }
          $exe = $executables[0]
          $log = "${{ runner.temp }}/windows-player.log"
          $exitFile = "${{ runner.temp }}/zweb-smoke-exit"
          $process = Start-Process -FilePath $exe.FullName -PassThru -ArgumentList @(
            "-logFile", $log, "-zwebSmokeExitFile", $exitFile,
            "-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0")
          $deadline = (Get-Date).AddSeconds(60)
          while ((Get-Date) -lt $deadline) {
            if ((Test-Path $log) -and (Select-String -Path $log -SimpleMatch '"event":"app-ready"' -Quiet)) { break }
            if ($process.HasExited) { throw "Player exited before readiness with $($process.ExitCode)" }
            Start-Sleep -Milliseconds 250
          }
          if (-not (Select-String -Path $log -SimpleMatch '"event":"app-ready"' -Quiet)) { throw "app-ready timeout" }
          New-Item -ItemType File -Path $exitFile -Force | Out-Null
          if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "graceful exit timeout" }
          if ($process.ExitCode -ne 0) { throw "Player exit code $($process.ExitCode)" }
          node scripts/ci/validate-player-log.mjs --log $log --allowlist ci/smoke/windows-log-allowlist.txt
      - if: always()
        uses: actions/upload-artifact@v4
        with:
          name: windows-smoke-log
          path: ${{ runner.temp }}/windows-player.log
      - name: Clean tree
        shell: bash
        run: test -z "$(git status --porcelain --untracked-files=all)"
```

- [ ] **Step 7: Run focused tests and commit smoke infrastructure**

Run:

```bash
node --test scripts/ci/validate-player-log.test.mjs \
  | tee "$ZWEB_EVIDENCE_ROOT/ci/CI-005/player-log-green.txt"
npm ci --prefix ci --ignore-scripts
node --test scripts/ci/workflow-contract.test.mjs \
  | tee "$ZWEB_EVIDENCE_ROOT/ci/CI-005/workflow-green.txt"
git add Assets/Scripts/UI/Diagnostics/SmokeReadinessReporter.cs \
  scripts/ci/serve-https.mjs scripts/ci/validate-player-log.mjs \
  scripts/ci/validate-player-log.test.mjs ci/smoke \
  scripts/ci/workflow-contract.test.mjs .github/workflows/ci.yml
git diff --cached --check
git commit -m "ci: smoke WebGL and Windows player builds"
```

Expected: local Node tests pass. Formal smoke closure requires successful GitHub Actions artifacts from both WebGL and `windows-smoke`; this host cannot substitute macOS execution for Windows.

---

### Task 7: Close CI Evidence and Run the Final Release Contract

**Issues closed:** `CI-001`, `CI-002`, `CI-003`, `CI-004`, `CI-005`, `TEST-009`

**Files:**
- Modify: `docs/superpowers/specs/2026-07-16-project-repair-evidence.json`
- Modify: `scripts/ci/workflow-contract.test.mjs`
- Modify: `.github/workflows/ci.yml`
- Create through the ledger command: hash-addressed files beneath `docs/superpowers/evidence/artifacts/`

**Interfaces:**
- Consumes: successful GitHub Actions downloads for Unity XML, Linux/Windows TRX, WebGL/Windows summaries, Playwright JSON, and Windows player log.
- Produces: six Closed ledger entries and a final `--require-all-closed true` validation after all other workstream closures are recorded.

- [ ] **Step 1: Download final CI artifacts outside the repository**

Using the successful workflow run for the final repair commit, place artifacts under this exact external layout:

```text
$ZWEB_EVIDENCE_ROOT/ci/final/
  unity-EditMode/results.xml
  unity-PlayMode/results.xml
  server-ubuntu/server.trx
  server-windows/server.trx
  build-WebGL/builds/Reports/WebGL.json
  build-WebGL/builds/WebGL/...
  build-Windows/builds/Reports/StandaloneWindows64.json
  build-Windows/builds/Windows/...
  webgl-playwright.json
  windows-player.log
```

The two Unity directories come from `unity-EditMode` and `unity-PlayMode`; server TRX files come from the two `server-tests-*` matrix artifacts; each complete `build-*` artifact retains both its `builds/Reports` summary and the exact `builds/WebGL` or `builds/Windows` binary tree; `webgl-playwright.json` comes from `webgl-smoke-report`; and `windows-player.log` comes from `windows-smoke-log`. Do not flatten either build artifact because summary paths and hashes are relative to its artifact root.

Verify every path is nonempty:

```bash
set -euo pipefail
for path in \
  unity-EditMode/results.xml unity-PlayMode/results.xml \
  server-ubuntu/server.trx server-windows/server.trx \
  build-WebGL/builds/Reports/WebGL.json \
  build-Windows/builds/Reports/StandaloneWindows64.json \
  webgl-playwright.json windows-player.log; do
  test -s "$ZWEB_EVIDENCE_ROOT/ci/final/$path"
done
test "$(find "$ZWEB_EVIDENCE_ROOT/ci/final/build-WebGL/builds/WebGL" -type f -size +0 | wc -l | tr -d ' ')" -gt 0
test "$(find "$ZWEB_EVIDENCE_ROOT/ci/final/build-Windows/builds/Windows" -type f -size +0 | wc -l | tr -d ' ')" -gt 0
find "$ZWEB_EVIDENCE_ROOT/ci/final" -type f -size +0 -print | LC_ALL=C sort
```

Expected: all eight fixed evidence files and both nonempty binary trees exist outside the repository. Do not copy build binaries or raw Unity logs into the repository.

- [ ] **Step 2: Re-run validators against downloaded evidence**

Run:

```bash
python3 scripts/ci/validate-unity-results.py --mode EditMode \
  --manifest ci/required-tests.json --results "$ZWEB_EVIDENCE_ROOT/ci/final/unity-EditMode"
python3 scripts/ci/validate-unity-results.py --mode PlayMode \
  --manifest ci/required-tests.json --results "$ZWEB_EVIDENCE_ROOT/ci/final/unity-PlayMode"
python3 scripts/ci/validate-trx.py \
  --results "$ZWEB_EVIDENCE_ROOT/ci/final/server-ubuntu" --minimum 1
python3 scripts/ci/validate-trx.py \
  --results "$ZWEB_EVIDENCE_ROOT/ci/final/server-windows" --minimum 1
BUILD_VALIDATION="$ZWEB_EVIDENCE_ROOT/ci/final/build-artifacts-validated.txt"
: > "$BUILD_VALIDATION"
node scripts/ci/validate-build-summary.mjs \
  --summary "$ZWEB_EVIDENCE_ROOT/ci/final/build-WebGL/builds/Reports/WebGL.json" \
  --target WebGL --artifact-root "$ZWEB_EVIDENCE_ROOT/ci/final/build-WebGL" \
  | tee -a "$BUILD_VALIDATION"
node scripts/ci/validate-build-summary.mjs \
  --summary "$ZWEB_EVIDENCE_ROOT/ci/final/build-Windows/builds/Reports/StandaloneWindows64.json" \
  --target StandaloneWindows64 --artifact-root "$ZWEB_EVIDENCE_ROOT/ci/final/build-Windows" \
  | tee -a "$BUILD_VALIDATION"
test -s "$BUILD_VALIDATION"
node scripts/ci/validate-player-log.mjs \
  --log "$ZWEB_EVIDENCE_ROOT/ci/final/windows-player.log" \
  --allowlist ci/smoke/windows-log-allowlist.txt
node -e '
const fs = require("fs");
const report = JSON.parse(fs.readFileSync(process.argv[1], "utf8"));
const stats = report.stats || {};
if ((stats.expected || 0) < 1 || (stats.unexpected || 0) !== 0 ||
    (stats.flaky || 0) !== 0 || (stats.skipped || 0) !== 0) {
  console.error(JSON.stringify(stats));
  process.exit(1);
}
console.log(`[webgl-playwright] PASS expected=${stats.expected}`);
' "$ZWEB_EVIDENCE_ROOT/ci/final/webgl-playwright.json"
```

Expected: all eight validation commands exit `0`; both downloaded binary trees reproduce every summary path, size, and SHA-256, and the Playwright report has at least one expected test and zero unexpected, flaky, or skipped tests.

- [ ] **Step 3: Close the six owned entries with exact artifact sets**

Resolve committed fixes:

```bash
evidence_fix="$(git log -1 --format=%H --grep='enforce audited issue closure evidence')"
test_fix="$(git log -1 --format=%H --grep='reject empty or regressed Unity test runs')"
workflow_fix="$(git log -1 --format=%H --grep='pin Unity and cover all gate inputs')"
server_fix="$(git log -1 --format=%H --grep='gate .NET 8 server on Linux and Windows')"
report_fix="$(git log -1 --format=%H --grep='emit and verify Unity build reports')"
smoke_fix="$(git log -1 --format=%H --grep='smoke WebGL and Windows player builds')"
for commit in "$evidence_fix" "$test_fix" "$workflow_fix" "$server_fix" "$report_fix" "$smoke_fix"; do
  test "${#commit}" -eq 40
done
closed_at="$(date -u +%F)"
```

Close each entry:

```bash
node scripts/audit/evidence-ledger.mjs close \
  --issue TEST-009 \
  --red "$ZWEB_EVIDENCE_ROOT/ci/TEST-009/red.txt" \
  --fix "$evidence_fix" \
  --green "$ZWEB_EVIDENCE_ROOT/ci/TEST-009/green.txt" \
  --gate "$ZWEB_EVIDENCE_ROOT/ci/TEST-009/gate.txt" \
  --mode Fixed --date "$closed_at"

for issue in CI-001 CI-002; do
  node scripts/audit/evidence-ledger.mjs close \
    --issue "$issue" \
    --red "$ZWEB_EVIDENCE_ROOT/ci/CI-001-CI-002-red.txt" \
    --fix "$workflow_fix" \
    --green "$ZWEB_EVIDENCE_ROOT/ci/CI-001-CI-002-green.txt" \
    --gate "$ZWEB_EVIDENCE_ROOT/ci/CI-005/workflow-green.txt" \
    --mode Fixed --date "$closed_at"
done

node scripts/audit/evidence-ledger.mjs close \
  --issue CI-003 \
  --red "$ZWEB_EVIDENCE_ROOT/ci/CI-003-red.txt" \
  --fix "$test_fix" \
  --green "$ZWEB_EVIDENCE_ROOT/ci/CI-003-green.txt" \
  --gate "$ZWEB_EVIDENCE_ROOT/ci/final/unity-EditMode/results.xml" \
  --gate "$ZWEB_EVIDENCE_ROOT/ci/final/unity-PlayMode/results.xml" \
  --mode Fixed --date "$closed_at"

node scripts/audit/evidence-ledger.mjs close \
  --issue CI-004 \
  --red "$ZWEB_EVIDENCE_ROOT/ci/CI-004-red.txt" \
  --fix "$server_fix" \
  --green "$ZWEB_EVIDENCE_ROOT/ci/CI-004-green.txt" \
  --gate "$ZWEB_EVIDENCE_ROOT/ci/final/server-ubuntu/server.trx" \
  --gate "$ZWEB_EVIDENCE_ROOT/ci/final/server-windows/server.trx" \
  --mode Fixed --date "$closed_at"

node scripts/audit/evidence-ledger.mjs close \
  --issue CI-005 \
  --red "$ZWEB_EVIDENCE_ROOT/ci/CI-005/build-summary-red.txt" \
  --red "$ZWEB_EVIDENCE_ROOT/ci/CI-005/workflow-red.txt" \
  --fix "$report_fix" --fix "$smoke_fix" \
  --green "$ZWEB_EVIDENCE_ROOT/ci/CI-005/build-summary-green.txt" \
  --green "$ZWEB_EVIDENCE_ROOT/ci/CI-005/player-log-green.txt" \
  --green "$ZWEB_EVIDENCE_ROOT/ci/CI-005/workflow-green.txt" \
  --gate "$ZWEB_EVIDENCE_ROOT/ci/final/build-WebGL/builds/Reports/WebGL.json" \
  --gate "$ZWEB_EVIDENCE_ROOT/ci/final/build-Windows/builds/Reports/StandaloneWindows64.json" \
  --gate "$ZWEB_EVIDENCE_ROOT/ci/final/build-artifacts-validated.txt" \
  --gate "$ZWEB_EVIDENCE_ROOT/ci/final/webgl-playwright.json" \
  --gate "$ZWEB_EVIDENCE_ROOT/ci/final/windows-player.log" \
  --mode Fixed --date "$closed_at"
```

Expected: six commands/loop entries exit `0`, preserve existing issue evidence, and deduplicate identical artifacts by SHA-256.

- [ ] **Step 4: Require every one of the 101 issues to be Closed**

Run only after the baseline, runtime, simulation, and network/server plans have recorded their closures:

```bash
node scripts/audit/evidence-ledger.mjs validate \
  --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json \
  --require-all-closed true \
  | tee "$ZWEB_EVIDENCE_ROOT/ci/final/all-closed.txt"
```

Expected: `AuditRegister_ClosedIssuesHaveExecutedEvidence] PASS issues=101`. Any Open/InProgress ID, missing commit, absent artifact, or hash mismatch fails.

After that command is green, change the `audit` job's ledger invocation from the staged-work form:

```yaml
      - run: node scripts/audit/evidence-ledger.mjs validate --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json
```

to the final merge gate:

```yaml
      - run: node scripts/audit/evidence-ledger.mjs validate --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json --require-all-closed true
```

Append this contract to `scripts/ci/workflow-contract.test.mjs`:

```javascript
test('final audit job requires every registered issue to be closed', () => {
  const auditCommands = workflow.jobs.audit.steps.map(step => step.run || '').join('\n');
  assert.match(auditCommands,
    /evidence-ledger\.mjs validate .*--require-all-closed true/);
});
```

Run `node --test scripts/ci/workflow-contract.test.mjs`; expected: all workflow contracts pass only after the final merge gate is present.

- [ ] **Step 5: Commit evidence and prove a clean repository**

Run:

```bash
git add docs/superpowers/specs/2026-07-16-project-repair-evidence.json \
  docs/superpowers/evidence/artifacts scripts/ci/workflow-contract.test.mjs \
  .github/workflows/ci.yml
git diff --cached --check
git commit -m "test: close audited repair evidence ledger"
node scripts/audit/evidence-ledger.mjs validate \
  --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json \
  --require-all-closed true
test -z "$(git status --porcelain --untracked-files=all)"
```

Expected: evidence commit succeeds, all 101 entries validate, and the clean-tree command emits no output.

## Self-Review Record

- Spec coverage: exact Unity version, complete triggers, nonzero Unity/.NET tests, two-OS server gates, BuildScript JSON, WebGL trusted HTTPS, Windows readiness/exit, evidence validation, and clean-tree enforcement are each assigned to a numbered task.
- Ownership: only `CI-001..005` and `TEST-009` are closed here; runtime test assembly creation belongs to the runtime plan even though Task 2 validates it.
- Placeholder scan: every source file, command, expected result, fixed version, port, timeout, test minimum, and artifact path is explicit.
- Type consistency: ledger close accepts repeated evidence/fix flags; baseline and all other plans use the same command. NUnit and TRX validators receive directories, while build/player validators receive explicit files.
- Environment limitation: this host has Unity `2022.3.62f3c1` but currently lacks .NET 8 and cannot perform Windows execution. Local Unity evidence cannot replace the required Linux/Windows CI artifacts for Tasks 4-7.
