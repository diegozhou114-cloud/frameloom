# Stage 1: Baseline

This stage owns the immutable pre-move inventory and stop-on-baseline-failure gate. Shared execution
rules are in the [plan index](README.md#execution-contract).

## Pre-Baseline Repair Gate

Before Task 1.1, land the one-time Unity 2022.3 metadata canonicalization as a separately reviewed
repair. The only accepted tracked metadata scope is the 71 legacy `.meta` blobs that are 59 bytes,
lack a terminal LF, and are reproducibly rewritten by a clean-clone import. Copy Unity's output
byte-for-byte; preserve all GUIDs, asset paths, and pre-existing nonempty semantic values.

Do not treat a missing importer as independently invalid. Keep the remaining 16 importerless
`.meta` files byte-for-byte unchanged. In particular,
`Assets/Tests/SimulationCore/SessionTerminalTests.cs.meta` must retain
`timeCreated: 1784620800`. Unity-generated shader logs are permitted only at the ignored pattern
`/Logs/shadercompiler-UnityShaderCompiler*.log`; never edit, delete, stage, or use them as test
input. Any other tracked change or unapproved untracked path blocks Task 1.1.

## Task 1.1: Confirm Revision And Capture Tracked State

- **Paths:** repository root; external outputs
  `$OSS_EVIDENCE_ROOT/baseline/git.txt`, `tracked-files.txt`, and `status.txt`.
- **Behavior:** require branch `codex/oss-prep-001`, require `D3` and the owner-approved full `L2` SHA
  to be ancestors of HEAD, record full HEAD/branch/remotes/tracked paths/staged and unstaged status,
  approved ignored shader-log paths, and the Unity version.
- **Do not touch:** any tracked file, index, remote, Git config, `Library/`, Layout, or shader log.
- **Verify:** run:

```bash
REPO_ROOT=$(git rev-parse --show-toplevel)
test -n "$OSS_EVIDENCE_ROOT"
test -n "$APPROVED_L2_SHA"
case "$OSS_EVIDENCE_ROOT/" in "$REPO_ROOT"/*) exit 1 ;; esac
mkdir -p "$OSS_EVIDENCE_ROOT/baseline"
test "$(git branch --show-current)" = codex/oss-prep-001
git merge-base --is-ancestor c164d8a99d8a76dc08c5a7bc92798999f2af4980 HEAD
git merge-base --is-ancestor "$APPROVED_L2_SHA" HEAD
git rev-parse HEAD
test -z "$(git status --porcelain=v1 --untracked-files=all)"
git ls-files > "$OSS_EVIDENCE_ROOT/baseline/tracked-files.txt"
git remote -v > "$OSS_EVIDENCE_ROOT/baseline/remotes.txt"
```

  Expected: both assertions exit 0; implementation starts from an owner-approved `L2` descendant;
  status is clean before capture and all outputs stay outside the repository.
- **Checkpoint:** read-only baseline identity record; no commit.
- **Stop:** wrong branch, missing `D3`/approved `L2`, unexpected worktree content, missing Unity binary, or evidence
  root inside the repository blocks all later stages.

## Task 1.2: Capture GUID, Assembly, Path, And Graph Inventories

- **Paths:** tracked `Assets/**/*.meta`, `Packages/**/*.meta`, all 25 current `*.asmdef` files,
  every `Server/**/*.csproj`, `Server/ZWebGLFrameWork.Server.sln`, `.github/workflows/ci.yml`, `scripts/`,
  `Packages/manifest.json`,
  `Packages/packages-lock.json`; external `baseline/meta.tsv`, `asmdefs.ndjson`, and `paths.txt`.
- **Behavior:** record each tracked meta path/GUID and the complete parsed asmdef JSON plus asmdef
  meta GUID. Record every live consumer of `Assets/Scripts` and the two old transport package paths.
  Capture CodeGraph definitions/callers for both public-API targets. Classify every .NET project as
  Framework Required, Reference Non-Required, or Mixed from parsed compile/project references and
  source evidence; record the exact mixed class filter defined in Task 1.3.
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

  Expected: 523 nonempty GUID rows, 25 valid JSON rows, live path consumers and all 12 .NET projects
  plus the solution aggregate classified, and CodeGraph reports `CombatManager` plus Replay/session
  test callers.
- **Checkpoint:** immutable move-comparison inputs; no repository change.
- **Stop:** duplicate/missing GUID, invalid asmdef JSON, unresolved assembly reference, or CodeGraph
  disagreement with source inspection requires owner review before moving files.

## Task 1.3: Run The Pre-Move Compile And Test Baseline

- **Paths:** current Unity host; package-linked SimulationCore, NetworkContracts, ECS.Tests,
  Network.Tests, and SimulationCore.Tests projects; `scripts/audit/`, `scripts/ci/`; all logs/results
  under `$OSS_EVIDENCE_ROOT/baseline/`.
- **Behavior:** establish a passing Unity, Node, Python, and package-owned engine-free .NET baseline.
  Run the mixed SimulationCore test assembly only through its exact framework class filter. The
  complete Server solution and reference hosts/tests/tools are non-Required diagnostics.
- **Do not touch:** production/tests/configuration to make a baseline pass; do not classify a current
  failure as migration fallout or add a test project to separate the mixed assembly.
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
npm ci --prefix ci --ignore-scripts
node --test scripts/audit/evidence-ledger.test.mjs scripts/ci/*.test.mjs
PYTHONPATH=scripts/ci python3 -m unittest discover -s scripts/ci -p 'test_*.py'
for project in \
  Server/ECS.Tests/ZWebGLFrameWork.ECS.ServerTests.csproj \
  Server/Network.Tests/ZWebGLFrameWork.Network.ServerTests.csproj \
  Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj; do
  dotnet restore "$project" --locked-mode \
    --artifacts-path "$OSS_EVIDENCE_ROOT/baseline/dotnet"
done
dotnet build Server/SimulationCore/ZWebGLFrameWork.SimulationCore.csproj -c Release \
  --warnaserror --artifacts-path "$OSS_EVIDENCE_ROOT/baseline/dotnet"
dotnet build Server/NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj -c Release \
  --warnaserror --artifacts-path "$OSS_EVIDENCE_ROOT/baseline/dotnet"
dotnet test Server/ECS.Tests/ZWebGLFrameWork.ECS.ServerTests.csproj -c Release \
  --no-restore --warnaserror --artifacts-path "$OSS_EVIDENCE_ROOT/baseline/dotnet" \
  --logger 'trx;LogFileName=ecs-framework.trx' \
  --results-directory "$OSS_EVIDENCE_ROOT/baseline/framework-tests"
dotnet test Server/Network.Tests/ZWebGLFrameWork.Network.ServerTests.csproj -c Release \
  --no-restore --warnaserror --artifacts-path "$OSS_EVIDENCE_ROOT/baseline/dotnet" \
  --logger 'trx;LogFileName=network-framework.trx' \
  --results-directory "$OSS_EVIDENCE_ROOT/baseline/framework-tests"
FRAMELOOM_SIM_FILTER='FullyQualifiedName~ZWebGLFrameWork.SimulationCore.ServerTests.ContentManifestGoldenTests'
FRAMELOOM_SIM_FILTER+='|FullyQualifiedName~ZWebGLFrameWork.SimulationCore.ServerTests.DeterministicRandomCompatibilityTests'
FRAMELOOM_SIM_FILTER+='|FullyQualifiedName~ZWebGLFrameWork.SimulationCore.ServerTests.LogicIntCompatibilityTests'
FRAMELOOM_SIM_FILTER+='|FullyQualifiedName~ZWebGLFrameWork.SimulationCore.ServerTests.ModeBehaviorGoldenTests'
FRAMELOOM_SIM_FILTER+='|FullyQualifiedName~ZWebGLFrameWork.SimulationCore.ServerTests.SessionCommandTests'
FRAMELOOM_SIM_FILTER+='|FullyQualifiedName~ZWebGLFrameWork.SimulationCore.Tests.SessionDeterminismTests'
FRAMELOOM_SIM_FILTER+='|FullyQualifiedName~ZWebGLFrameWork.SimulationCore.Tests.ModeBehaviorTests'
FRAMELOOM_SIM_FILTER+='|FullyQualifiedName~ZWebGLFrameWork.SimulationCore.Tests.CoreTargetingTests'
FRAMELOOM_SIM_FILTER+='|FullyQualifiedName~ZWebGLFrameWork.SimulationCore.Tests.SimSystemGroupTests'
FRAMELOOM_SIM_FILTER+='|FullyQualifiedName~ZWebGLFrameWork.SimulationCore.Tests.SessionTerminalTests'
dotnet test Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj \
  -c Release --no-restore --warnaserror \
  --artifacts-path "$OSS_EVIDENCE_ROOT/baseline/dotnet" --filter "$FRAMELOOM_SIM_FILTER" \
  --logger 'trx;LogFileName=simulationcore-framework.trx' \
  --results-directory "$OSS_EVIDENCE_ROOT/baseline/framework-tests"
```

  Expected: every Required process exits 0; Unity XML and each framework TRX contain nonzero passing
  tests with zero failures; the mixed TRX contains only the ten named classes. Reference
  ReplayValidator/FrameSync/RealtimeHost/Kcp2kHost, `Server/Tests`, fixture generators, and the full
  solution are `Required = No`. The existing 336/340 aggregate result remains historical diagnostic
  evidence, not a complete Server PASS.
- **Checkpoint:** owner-reviewable Required baseline evidence and separately labeled optional
  reference diagnostics; Node/Python results exist regardless of reference status; no commit.
- **Stop:** any Required gate failure or mixed-filter drift stops the move. A reference Server failure
  or `NOT RUN` does not stop Node/Python or the framework gate and must not be called framework CI PASS.
