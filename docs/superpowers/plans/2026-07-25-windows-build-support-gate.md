# Windows Build Support Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Install Windows Mono Build Support for Unity 2022.3.62f3c1 and prove a real `StandaloneWindows64` build succeeds.

**Architecture:** A small repository verifier pins the editor and module path. Unity Hub performs the external installation through its supported headless CLI; the existing BuildScript writes the executable and machine-readable report beneath an external evidence root.

**Tech Stack:** Unity Hub CLI, Unity 2022.3.62f3c1, Windows Mono Build Support, Node.js build-summary validator

## Global Constraints

- The only accepted editor is `/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app`; module verification reads its `Contents` directory.
- The only requested module is `windows-mono` for version `2022.3.62f3c1`.
- Installation requires the host approval UI and may download a large module.
- An unsupported-target message is RED; only a succeeded `StandaloneWindows64` report is GREEN.
- Build output and logs remain outside the repository under `/tmp/zweb-repair-evidence/windows`.

---

### Task 1: Install and verify the pinned Windows module

**Files:**
- Create: `scripts/ci/verify-unity-windows-module.sh`
- Create: `scripts/ci/verify-unity-windows-module.test.mjs`

**Interfaces:**
- Produces: an executable verifier accepting an optional Unity `Contents` directory and exiting 0 only for the expected Windows support layout.

- [ ] **Step 1: Write the verifier contract test**

Create `scripts/ci/verify-unity-windows-module.test.mjs`:

```javascript
import assert from 'node:assert/strict';
import { chmod, mkdtemp, mkdir, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const script = path.resolve('scripts/ci/verify-unity-windows-module.sh');

test('rejects Unity contents without WindowsStandaloneSupport', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-unity-module-'));
  const result = spawnSync(script, [root], { encoding: 'utf8' });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /missing WindowsStandaloneSupport/);
});

test('accepts the pinned module layout', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-unity-module-'));
  const support = path.join(root, 'PlaybackEngines/WindowsStandaloneSupport');
  await mkdir(support, { recursive: true });
  await writeFile(path.join(support, 'ivy.xml'), '<ivy-module/>\n');
  const result = spawnSync(script, [root], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /WindowsStandaloneSupport verified/);
});
```

- [ ] **Step 2: Run the test and verify RED**

Run: `node --test scripts/ci/verify-unity-windows-module.test.mjs`

Expected: tests fail because the verifier script is absent.

- [ ] **Step 3: Implement the module verifier**

Create executable `scripts/ci/verify-unity-windows-module.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

unity_contents=${1:-/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents}
support="$unity_contents/PlaybackEngines/WindowsStandaloneSupport"
if [[ ! -d "$support" || ! -s "$support/ivy.xml" ]]; then
  echo "missing WindowsStandaloneSupport for Unity 2022.3.62f3c1: $support" >&2
  exit 1
fi
echo "WindowsStandaloneSupport verified: $support"
```

- [ ] **Step 4: Run verifier unit tests GREEN and real preflight RED**

```bash
chmod +x scripts/ci/verify-unity-windows-module.sh
node --test scripts/ci/verify-unity-windows-module.test.mjs
scripts/ci/verify-unity-windows-module.sh
```

Expected: Node tests pass; real preflight exits 1 because the module is not yet installed.

- [ ] **Step 5: Install the approved module**

Run through the host approval UI:

```bash
'/Applications/Unity Hub.app/Contents/MacOS/Unity Hub' -- --headless \
  install-modules --version 2022.3.62f3c1 -m windows-mono
```

Expected: Hub reports a successful module installation for the already installed editor.

- [ ] **Step 6: Verify installed module GREEN**

Run: `scripts/ci/verify-unity-windows-module.sh`

Expected: exit 0 and exact support path printed.

- [ ] **Step 7: Run a real Windows build**

```bash
mkdir -p /tmp/zweb-repair-evidence/windows
ZWEB_EVIDENCE_ROOT=/tmp/zweb-repair-evidence/windows \
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "$PWD" \
  -executeMethod ZWebGLFrameWork.Editor.BuildScript.BuildWindows \
  -logFile /tmp/zweb-repair-evidence/windows/unity.log -quit
```

Expected: Unity exits 0, produces `/tmp/zweb-repair-evidence/windows/builds/Windows/ZWebGLFrameWork.exe`, and writes `/tmp/zweb-repair-evidence/windows/builds/Reports/StandaloneWindows64.json`.

- [ ] **Step 8: Validate the machine-readable report**

```bash
node scripts/ci/validate-build-summary.mjs \
  --summary /tmp/zweb-repair-evidence/windows/builds/Reports/StandaloneWindows64.json \
  --target StandaloneWindows64 \
  --artifact-root /tmp/zweb-repair-evidence/windows
test -s /tmp/zweb-repair-evidence/windows/builds/Windows/ZWebGLFrameWork.exe
rg -n "error CS|Build failed|Unsupported build target" \
  /tmp/zweb-repair-evidence/windows/unity.log && exit 1 || test "$?" -eq 1
```

Expected: validator passes, executable is nonempty, and guarded search finds no compiler/build/unsupported-target error.

- [ ] **Step 9: Commit the repeatable verifier**

```bash
git add scripts/ci/verify-unity-windows-module.sh \
  scripts/ci/verify-unity-windows-module.test.mjs
git commit -m "build: verify pinned Unity Windows support"
```
