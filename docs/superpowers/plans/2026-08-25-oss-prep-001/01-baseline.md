# Stage 1: Baseline

This stage owns the immutable pre-move inventory and stop-on-baseline-failure gate. Shared execution
rules are in the [plan index](README.md#execution-contract).

## Task 1.1: Confirm Revision And Capture Tracked State

- **Paths:** repository root; external outputs
  `$OSS_EVIDENCE_ROOT/baseline/git.txt`, `tracked-files.txt`, and `status.txt`.
- **Behavior:** require branch `codex/oss-prep-001`, require `D` to be an ancestor of HEAD, record
  full HEAD/branch/remotes/tracked paths/staged and unstaged status, and record the Unity version.
- **Do not touch:** any tracked file, index, remote, Git config, `Library/`, Layout, or shader log.
- **Verify:** run:

```bash
REPO_ROOT=$(git rev-parse --show-toplevel)
test -n "$OSS_EVIDENCE_ROOT"
case "$OSS_EVIDENCE_ROOT/" in "$REPO_ROOT"/*) exit 1 ;; esac
mkdir -p "$OSS_EVIDENCE_ROOT/baseline"
test "$(git branch --show-current)" = codex/oss-prep-001
git merge-base --is-ancestor af1c2745062f262fc4b922f90b46f43158b537b7 HEAD
git rev-parse HEAD
test -z "$(git status --porcelain=v1 --untracked-files=all)"
git ls-files > "$OSS_EVIDENCE_ROOT/baseline/tracked-files.txt"
git remote -v > "$OSS_EVIDENCE_ROOT/baseline/remotes.txt"
```

  Expected: both assertions exit 0; implementation starts from an owner-approved `L` descendant;
  status is clean before capture and all outputs stay outside the repository.
- **Checkpoint:** read-only baseline identity record; no commit.
- **Stop:** wrong branch, missing `D`, unexpected worktree content, missing Unity binary, or evidence
  root inside the repository blocks all later stages.

## Task 1.2: Capture GUID, Assembly, Path, And Graph Inventories

- **Paths:** tracked `Assets/**/*.meta`, `Packages/**/*.meta`, all 25 current `*.asmdef` files,
  `Server/`, `.github/workflows/ci.yml`, `scripts/`, `Packages/manifest.json`,
  `Packages/packages-lock.json`; external `baseline/meta.tsv`, `asmdefs.ndjson`, and `paths.txt`.
- **Behavior:** record each tracked meta path/GUID and the complete parsed asmdef JSON plus asmdef
  meta GUID. Record every live consumer of `Assets/Scripts` and the two old transport package paths.
  Capture CodeGraph definitions/callers for both public-API targets.
- **Do not touch:** generated `.csproj`, historical plans/evidence, user Layout, or any `.meta`.
- **Verify:** run:

```bash
git ls-files '*.meta' | while IFS= read -r f; do
  printf '%s\t%s\n' "$f" "$(awk '/^guid:/{print $2}' "$f")"
done > "$OSS_EVIDENCE_ROOT/baseline/meta.tsv"
git ls-files '*.asmdef' | while IFS= read -r f; do
  jq -c --arg path "$f" --arg guid "$(awk '/^guid:/{print $2}' "$f.meta")" \
    '{path:$path,guid:$guid,asmdef:.}' "$f"
done > "$OSS_EVIDENCE_ROOT/baseline/asmdefs.ndjson"
rg -n 'Assets/Scripts|com\.zwebglframework\.(kcp2k|nativewebsocket)' \
  Assets/Editor Assets/Tests Server scripts .github AGENTS.md docs/DEVELOPMENT_GUIDE.md \
  .gitattributes Packages > "$OSS_EVIDENCE_ROOT/baseline/paths.txt"
codegraph explore "SessionPresentationBridge.Apply CommandRecorder.Record callers" \
  > "$OSS_EVIDENCE_ROOT/baseline/codegraph.txt"
```

  Expected: 523 nonempty GUID rows, 25 valid JSON rows, live path consumers captured, and CodeGraph
  reports `CombatManager` plus Replay/session test callers.
- **Checkpoint:** immutable move-comparison inputs; no repository change.
- **Stop:** duplicate/missing GUID, invalid asmdef JSON, unresolved assembly reference, or CodeGraph
  disagreement with source inspection requires owner review before moving files.

## Task 1.3: Run The Pre-Move Compile And Test Baseline

- **Paths:** current Unity host, `Server/ZWebGLFrameWork.Server.sln`, `scripts/audit/`, `scripts/ci/`;
  all logs/results under `$OSS_EVIDENCE_ROOT/baseline/`.
- **Behavior:** establish a passing host, Unity EditMode/PlayMode, Server, Node, and Python baseline.
- **Do not touch:** production/tests/configuration to make a baseline pass; do not classify a current
  failure as migration fallout.
- **Verify:** run:

```bash
UNITY_BIN=/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity
"$UNITY_BIN" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod ZWebGLFrameWork.Editor.BuildValidator.CompileAndValidate \
  -logFile "$OSS_EVIDENCE_ROOT/baseline/unity-validate.log"
"$UNITY_BIN" -batchmode -nographics -runTests -projectPath "$PWD" -testPlatform EditMode \
  -testResults "$OSS_EVIDENCE_ROOT/baseline/editmode.xml" \
  -logFile "$OSS_EVIDENCE_ROOT/baseline/editmode.log"
"$UNITY_BIN" -batchmode -nographics -runTests -projectPath "$PWD" -testPlatform PlayMode \
  -testResults "$OSS_EVIDENCE_ROOT/baseline/playmode.xml" \
  -logFile "$OSS_EVIDENCE_ROOT/baseline/playmode.log"
node --test scripts/audit/evidence-ledger.test.mjs scripts/ci/*.test.mjs
PYTHONPATH=scripts/ci python3 -m unittest discover -s scripts/ci -p 'test_*.py'
dotnet restore Server/ZWebGLFrameWork.Server.sln --locked-mode \
  --artifacts-path "$OSS_EVIDENCE_ROOT/baseline/dotnet"
dotnet build Server/ZWebGLFrameWork.Server.sln -c Release --no-restore --warnaserror \
  --artifacts-path "$OSS_EVIDENCE_ROOT/baseline/dotnet"
dotnet test Server/ZWebGLFrameWork.Server.sln -c Release --no-build --no-restore \
  --artifacts-path "$OSS_EVIDENCE_ROOT/baseline/dotnet" \
  --logger 'trx;LogFilePrefix=baseline' \
  --results-directory "$OSS_EVIDENCE_ROOT/baseline/server-tests"
```

  Expected: every process exits 0, Unity XML contains nonzero passing tests with no compile error,
  and Server TRX contains nonzero passing tests.
- **Checkpoint:** owner-reviewable baseline evidence and recorded clean Git status; no commit.
- **Stop:** any failure stops the move. Report it as pre-existing and do not edit around it.
