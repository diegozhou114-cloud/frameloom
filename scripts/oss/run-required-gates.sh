#!/usr/bin/env bash
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/../.." && pwd -P)
REGISTRY="$ROOT/ci/oss/gates.json"
PHASE= REVISION= UNITY= EVIDENCE_ROOT= EXECUTOR= OUTPUT= RESULTS= FAILED_GATE=
FIXTURE=0 FAILED=0
TOTAL_TIMEOUT=${FRAMELOOM_GATE_TOTAL_TIMEOUT_SECONDS:-1200}; EXIT_GRACE=${FRAMELOOM_GATE_EXIT_GRACE_SECONDS:-600}; POLL_SECONDS=${FRAMELOOM_GATE_POLL_SECONDS:-2}; TERM_GRACE=${FRAMELOOM_GATE_TERM_GRACE_SECONDS:-30}; U_PID= U_START= U_COMMAND= U_PARENT= U_CWD= WRAPPER_START= SIGNALS=0
MIXED_CLASSES='ContentManifestGoldenTests DeterministicRandomCompatibilityTests LogicIntCompatibilityTests ModeBehaviorGoldenTests SessionCommandTests SessionDeterminismTests ModeBehaviorTests CoreTargetingTests SimSystemGroupTests SessionTerminalTests'
P_FOUNDATION=61add622c04ad72494b5da785e542b984ec18df4
P_FOUNDATION_PATHS='.github/workflows/ci.yml ci/oss/gates.json scripts/ci/workflow-contract.test.mjs scripts/oss/audit-history.mjs scripts/oss/audit-history.test.mjs scripts/oss/compare-package-content.mjs scripts/oss/compare-package-content.test.mjs scripts/oss/release-evidence.mjs scripts/oss/release-evidence.test.mjs scripts/oss/run-required-gates.sh scripts/oss/run-required-gates.test.mjs scripts/oss/run-webgl-smoke.sh scripts/oss/run-webgl-smoke.test.mjs'
P_PREFIX=ef4abbee5afe6891ccc570eefcd1842d35ef95dd
P_PATHS='scripts/oss/run-required-gates.sh scripts/oss/run-required-gates.test.mjs scripts/vendor-nativewebsocket.test.sh'
fail() { printf '%s\n' "$*" >&2; exit 2; }
need_value() { [ "$#" -ge 2 ] && [ -n "$2" ] || fail "Missing value for $1"; }
set_once() { [ -z "$1" ] || fail "Duplicate argument: $2"; printf '%s' "$3"; }

while [ "$#" -gt 0 ]; do
  case "$1" in
    --fixture-run) [ "$FIXTURE" -eq 0 ] || fail "Duplicate argument: $1"; FIXTURE=1; shift ;;
    --phase) need_value "$@"; PHASE=$(set_once "$PHASE" "$1" "$2"); shift 2 ;;
    --revision) need_value "$@"; REVISION=$(set_once "$REVISION" "$1" "$2"); shift 2 ;;
    --unity) need_value "$@"; UNITY=$(set_once "$UNITY" "$1" "$2"); shift 2 ;;
    --evidence-root) need_value "$@"; EVIDENCE_ROOT=$(set_once "$EVIDENCE_ROOT" "$1" "$2"); shift 2 ;;
    --executor) need_value "$@"; EXECUTOR=$(set_once "$EXECUTOR" "$1" "$2"); shift 2 ;;
    *) fail "Invalid argument: $1" ;;
  esac
done
[ "$PHASE" = package-freeze ] || fail "Unsupported phase: ${PHASE:-missing}"
[[ "$REVISION" =~ ^[a-f0-9]{40}$ ]] || fail "Revision must be a full lowercase 40-character SHA"
[ "$FIXTURE" -eq 1 ] || [ -z "$EXECUTOR" ] || fail "--executor requires --fixture-run"
[ "$FIXTURE" -eq 0 ] || [ -n "$EXECUTOR" ] || fail "--fixture-run requires --executor"
canonical_executable() {
  local value=$1 label=$2 canonical
  [[ "$value" = /* ]] && [ -f "$value" ] && [ -x "$value" ] && [ ! -L "$value" ] ||
    fail "$label must be an absolute executable"
  canonical=$(cd "$(dirname "$value")" && pwd -P)/$(basename "$value")
  [ "$value" = "$canonical" ] || fail "$label must be canonical"
}
canonical_executable "$UNITY" Unity
[ -z "$EXECUTOR" ] || canonical_executable "$EXECUTOR" Executor
OUTPUT=$(node -e '
const fs=require("fs"),path=require("path"),[input,repo]=process.argv.slice(1);
const die=m=>{console.error(m);process.exit(2)};if(!path.isAbsolute(input))die("Evidence root must be absolute");
const target=path.resolve(input),parent=fs.realpathSync(path.dirname(target));const canonical=path.join(parent,path.basename(target)),root=fs.realpathSync(repo);
if(input!==target||target!==canonical)die("Evidence root must be a canonical directory");const relative=path.relative(root,target);
if(relative===""||(!relative.startsWith(".."+path.sep)&&relative!==".."))die("Evidence root must be outside the repository");
let info;try{info=fs.lstatSync(target)}catch(e){if(e.code!=="ENOENT")throw e}if(info&&(!info.isDirectory()||info.isSymbolicLink()))die("Evidence root must be a canonical directory");
if(info&&fs.readdirSync(target).length)die("Evidence root must be new or empty");if(!info)fs.mkdirSync(target);console.log(canonical);
' "$EVIDENCE_ROOT" "$ROOT")
(cd "$ROOT" && node --input-type=module -e 'import {readFile} from "node:fs/promises";import {validateRegistry} from "./scripts/oss/release-evidence.mjs";const e=validateRegistry(JSON.parse(await readFile("ci/oss/gates.json")));if(e.length)throw Error(e.join("\n"));') >/dev/null

candidate_ok() {
  [ "$(git -C "$ROOT" rev-parse --verify HEAD^{commit})" = "$REVISION" ] &&
    [ "$(git -C "$ROOT" rev-parse --verify "$REVISION^{commit}")" = "$REVISION" ] &&
    [ -z "$(git -C "$ROOT" status --porcelain=v1 --untracked-files=all)" ]
}
if [ "$FIXTURE" -eq 1 ]; then
  RESULTS="$OUTPUT/fixture-results.ndjson"
  node -e 'require("fs").writeFileSync(process.argv[1],JSON.stringify({schemaVersion:1,classification:"fixture-only",formalReleaseEvidence:false,phase:process.argv[2],revision:process.argv[3]},null,2)+"\n")' \
    "$OUTPUT/fixture-only.json" "$PHASE" "$REVISION"
else
  candidate_ok || fail "Formal run requires revision at HEAD and a clean repository"
  for limit in "$TOTAL_TIMEOUT" "$EXIT_GRACE" "$POLL_SECONDS" "$TERM_GRACE"; do [[ "$limit" =~ ^[0-9]+$ ]] || fail "Invalid Unity watchdog limit"; done
  [ "$TOTAL_TIMEOUT" -gt 0 ] && [ "$TOTAL_TIMEOUT" -le 3600 ] && [ "$EXIT_GRACE" -gt 0 ] && [ "$EXIT_GRACE" -le 600 ] && [ "$POLL_SECONDS" -gt 0 ] && [ "$POLL_SECONDS" -le 5 ] && [ "$TERM_GRACE" -gt 0 ] && [ "$TERM_GRACE" -le 30 ] || fail "Invalid Unity watchdog limit"
  for tool in git node npm python3 dotnet rg shasum openssl curl gitleaks lsof; do command -v "$tool" >/dev/null || fail "Required tool unavailable: $tool"; done
  RESULTS="$OUTPUT/results.ndjson"
fi
: > "$RESULTS"

focused_xml() {
  python3 "$ROOT/scripts/audit/read-test-results.py" "$1" | node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>{const r=JSON.parse(s);if(!r.length||r.some(x=>!["Passed","Success"].includes(x.result)))process.exit(1);console.log(`focused tests passed=${r.length}`)})'
}
xml_ok() {
  if [ -z "$2" ]; then python3 "$ROOT/scripts/ci/validate-unity-results.py" --mode "$1" --manifest "$ROOT/ci/required-tests.json" --results "$3"; else focused_xml "$3"; fi
}
process_cwd() { lsof -a -p "$1" -d cwd -Fn 2>/dev/null | awk '/^n/{sub(/^n/,"");print;exit}'; }
identity_ok() {
  local start command parent cwd
  kill -0 "$U_PID" 2>/dev/null || return 1; start=$(ps -p "$U_PID" -o lstart= | sed 's/^ *//;s/ *$//'); command=$(ps -ww -p "$U_PID" -o command=); parent=$(ps -p "$U_PID" -o ppid= | tr -d ' '); cwd=$(process_cwd "$U_PID")
  [ "$U_PID" -gt 1 ] && [ "$start" = "$U_START" ] && [ "$command" = "$U_COMMAND" ] && [ "$parent" = "$U_PARENT" ] && [ "$parent" = "$$" ] && [ "$cwd" = "$U_CWD" ] && [ "$cwd" = "$ROOT" ] && [ "$(ps -p "$$" -o lstart= | sed 's/^ *//;s/ *$//')" = "$WRAPPER_START" ] && [[ "$command" == *"$UNITY"* && "$command" == *"$ROOT"* ]]
}
stop_unity() {
  local second; identity_ok || { printf 'signal=REJECTED identity=unconfirmed\n' >> "$1/signal-ledger.txt"; return 1; }
  kill -TERM "$U_PID"; SIGNALS=$((SIGNALS+1)); printf 'signal=TERM pid=%s identity=confirmed\n' "$U_PID" >> "$1/signal-ledger.txt"
  for ((second=0; second<TERM_GRACE; second++)); do kill -0 "$U_PID" 2>/dev/null || return 0; sleep 1; done
  kill -0 "$U_PID" 2>/dev/null || return 0; identity_ok || return 1; kill -KILL "$U_PID"; SIGNALS=$((SIGNALS+1)); printf 'signal=KILL pid=%s identity=confirmed\n' "$U_PID" >> "$1/signal-ledger.txt"
}
unity_run() {
  local kind=$1 mode=$2 filter=$3 dir=$4 xml="$4/results.xml" log="$4/unity.log" marker started ended now complete=0 watchdog=0 reason=none post=0 rc
  marker='Test run completed. Exiting with code 0 (Ok). Run completed.'; local args
  if [ "$kind" = test ]; then args=(-batchmode -runTests -projectPath "$ROOT" -testPlatform "$mode" -testResults "$xml" -logFile "$log"); [ -z "$filter" ] || args+=(-testFilter "$filter")
  elif [ "$kind" = compile ]; then args=(-batchmode -nographics -quit -projectPath "$ROOT" -executeMethod ZWebGLFrameWork.Editor.BuildValidator.CompileAndValidate -logFile "$log")
  else args=(-batchmode -nographics -quit -projectPath "$ROOT" -executeMethod ZWebGLFrameWork.Editor.SimulationPerformanceBatch.Run -logFile "$log"); fi
  : > "$dir/signal-ledger.txt"; started=$(date +%s); WRAPPER_START=$(ps -p "$$" -o lstart= | sed 's/^ *//;s/ *$//'); U_PID= U_START= U_COMMAND= U_PARENT= U_CWD= SIGNALS=0
  (cd "$ROOT" && exec env ZWEB_EVIDENCE_ROOT="$dir" "$UNITY" "${args[@]}") > "$dir/unity.stdout.txt" 2> "$dir/unity.stderr.txt" & U_PID=$!; sleep 0.2
  if kill -0 "$U_PID" 2>/dev/null; then U_START=$(ps -p "$U_PID" -o lstart= | sed 's/^ *//;s/ *$//'); U_COMMAND=$(ps -ww -p "$U_PID" -o command=); U_PARENT=$(ps -p "$U_PID" -o ppid= | tr -d ' '); U_CWD=$(process_cwd "$U_PID"); identity_ok || return 1; fi
  printf 'pid=%s\nppid=%s\ncwd=%s\ncommand=%s\n' "$U_PID" "${U_PARENT:-exited}" "${U_CWD:-exited}" "${U_COMMAND:-exited}" > "$dir/process-identity.txt"
  while kill -0 "$U_PID" 2>/dev/null; do now=$(date +%s); printf 'epoch=%s pid=%s ppid=%s cwd=%s\n' "$now" "$U_PID" "$(ps -p "$U_PID" -o ppid= | tr -d ' ')" "$(process_cwd "$U_PID")" >> "$dir/process-samples.txt"
    if [ "$kind" = test ] && [ "$complete" -eq 0 ] && [ -f "$log" ] && [ -f "$xml" ] && rg -Fq "$marker" "$log" && xml_ok "$mode" "$filter" "$xml" >/dev/null 2>&1; then complete=$now; fi
    if [ "$now" -ge $((started+TOTAL_TIMEOUT)) ]; then watchdog=1; reason=total-timeout; break; fi
    if [ "$complete" -gt 0 ] && [ "$now" -ge $((complete+EXIT_GRACE)) ]; then watchdog=1; reason=post-completion-timeout; break; fi; sleep "$POLL_SECONDS"
  done
  if [ "$watchdog" -eq 1 ]; then printf 'watchdog=%s\n' "$reason" > "$dir/watchdog.txt"; stop_unity "$dir" || return 1; fi
  if wait "$U_PID"; then rc=0; else rc=$?; fi; ended=$(date +%s)
  if [ "$kind" = test ] && [ "$complete" -eq 0 ] && [ -f "$log" ] && [ "$(rg -Fc "$marker" "$log" 2>/dev/null || true)" -eq 1 ] && xml_ok "$mode" "$filter" "$xml" >/dev/null 2>&1; then complete=$(node -e 'console.log(Math.floor(require("fs").statSync(process.argv[1]).mtimeMs/1000))' "$xml"); post=1; fi
  printf 'start=%s\nend=%s\nexit=%s\ncompletion=%s\ncompletionToExit=%s\npostExitAdjudicated=%s\nwatchdog=%s\nreason=%s\nsignals=%s\n' "$started" "$ended" "$rc" "$complete" "$([ "$complete" -gt 0 ] && printf '%s' "$((ended-complete))" || printf '%s' -1)" "$post" "$watchdog" "$reason" "$SIGNALS" > "$dir/lifecycle.txt"
  [ -f "$log" ] && [ "$watchdog" -eq 0 ] && [ "$SIGNALS" -eq 0 ] && [ "$rc" -eq 0 ] || return 1
  if [ "$kind" = test ]; then [ "$complete" -ge "$started" ] && [ "$complete" -le "$ended" ] && [ "$((ended-complete))" -le "$EXIT_GRACE" ] && [ "$(rg -Fc "$marker" "$log" || true)" -eq 1 ] || return 1; xml_ok "$mode" "$filter" "$xml" && rg -Fq 'Metal devices available:' "$log" && rg -Fq 'Using device ' "$log" && rg -m3 'GfxDevice: creating device client|Metal devices available:|Using device ' "$log" || return 1; fi
  ! rg -qi 'Application::Terminate|Application::CoreShutdown|JobSystem::ShutdownJobThreads|buffered presentation failed|PublishUnobservedTestException|PublishUnobservedTaskException|ExceptionHolder.*Finalize' "$log"
}
evidence_inventory() {
  local dir=$1; [ -z "$(find "$dir" -type l -print -quit)" ] || return 1
  (cd "$dir" && find . -type f -print | LC_ALL=C sort | while IFS= read -r file; do printf '%s  %s\n' "$(shasum -a 256 "$file" | cut -d' ' -f1)" "${file#./}"; done)
}
p_diff_gate() {
  local dir=$1 row actual expected; row=$(git -C "$ROOT" rev-list --parents -n 1 "$REVISION") || return 1; set -- $row
  [ "$#" -eq 2 ] && [ "$1" = "$REVISION" ] && [ "$2" = "$P_PREFIX" ] || { printf 'candidate must be the non-merge direct child of %s\n' "$P_PREFIX"; return 1; }
  row=$(git -C "$ROOT" rev-list --parents -n 1 "$P_PREFIX") || return 1; set -- $row; [ "$#" -eq 2 ] && [ "$1" = "$P_PREFIX" ] && [ "$2" = "$P_FOUNDATION" ] || { printf 'Stage 4 checkpoint must be the non-merge direct child of %s\n' "$P_FOUNDATION"; return 1; }
  actual=$(git -C "$ROOT" diff-tree --no-commit-id --name-only -r "$P_PREFIX" | LC_ALL=C sort) || return 1; expected=$(printf '%s\n' $P_FOUNDATION_PATHS | LC_ALL=C sort); printf '%s\n' "$actual" > "$dir/stage4-checkpoint-paths.txt"; [ "$actual" = "$expected" ] || { printf 'Stage 4 checkpoint path mismatch\nexpected:\n%s\nactual:\n%s\n' "$expected" "$actual"; return 1; }
  actual=$(git -C "$ROOT" diff-tree --no-commit-id --name-only -r "$REVISION" | LC_ALL=C sort) || return 1; expected=$(printf '%s\n' $P_PATHS | LC_ALL=C sort); printf '%s\n' "$actual" > "$dir/checkpoint-paths.txt"
  [ "$actual" = "$expected" ] || { printf 'checkpoint path mismatch\nexpected:\n%s\nactual:\n%s\n' "$expected" "$actual"; return 1; }
  git -C "$ROOT" diff --check af1c2745062f262fc4b922f90b46f43158b537b7.."$REVISION" && git -C "$ROOT" diff --quiet af1c2745062f262fc4b922f90b46f43158b537b7.."$REVISION" -- README.md UserSettings/Layouts/default-2022.dwlt
}
framework_dotnet() {
  local dir=$1 artifacts="$1/artifacts" results="$1/results" project filter
  mkdir -p "$artifacts" "$results"
  for project in Server/ECS.Tests/ZWebGLFrameWork.ECS.ServerTests.csproj Server/Network.Tests/ZWebGLFrameWork.Network.ServerTests.csproj Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj; do dotnet restore "$ROOT/$project" --locked-mode --artifacts-path "$artifacts"; done
  dotnet build "$ROOT/Server/SimulationCore/ZWebGLFrameWork.SimulationCore.csproj" -c Release --warnaserror --artifacts-path "$artifacts"
  dotnet build "$ROOT/Server/NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj" -c Release --warnaserror --artifacts-path "$artifacts"
  dotnet test "$ROOT/Server/ECS.Tests/ZWebGLFrameWork.ECS.ServerTests.csproj" -c Release --no-restore --warnaserror --artifacts-path "$artifacts" --logger 'trx;LogFileName=ecs.trx' --results-directory "$results"
  dotnet test "$ROOT/Server/Network.Tests/ZWebGLFrameWork.Network.ServerTests.csproj" -c Release --no-restore --warnaserror --artifacts-path "$artifacts" --logger 'trx;LogFileName=network.trx' --results-directory "$results"
  filter='FullyQualifiedName~ContentManifestGoldenTests|FullyQualifiedName~DeterministicRandomCompatibilityTests|FullyQualifiedName~LogicIntCompatibilityTests|FullyQualifiedName~ModeBehaviorGoldenTests|FullyQualifiedName~SessionCommandTests|FullyQualifiedName~SessionDeterminismTests|FullyQualifiedName~ModeBehaviorTests|FullyQualifiedName~CoreTargetingTests|FullyQualifiedName~SimSystemGroupTests|FullyQualifiedName~SessionTerminalTests'
  dotnet test "$ROOT/Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj" -c Release --no-restore --warnaserror --artifacts-path "$artifacts" --filter "$filter" --logger 'trx;LogFileName=simulation.trx' --results-directory "$results"
  python3 "$ROOT/scripts/ci/validate-trx.py" --results "$results/ecs.trx" --minimum 6
  python3 "$ROOT/scripts/ci/validate-trx.py" --results "$results/network.trx" --minimum 35
  python3 "$ROOT/scripts/ci/validate-trx.py" --results "$results/simulation.trx" --minimum 132
  python3 -c 'import sys,xml.etree.ElementTree as E;expected=set(sys.argv[2:]);actual={n.attrib["className"].rsplit(".",1)[-1] for n in E.parse(sys.argv[1]).iter() if n.tag.rsplit("}",1)[-1]=="TestMethod" and n.attrib.get("className")};missing=sorted(expected-actual);extra=sorted(actual-expected);print(f"mixed classes missing={missing} extra={extra}");raise SystemExit(bool(missing or extra))' "$results/simulation.trx" $MIXED_CLASSES
}
structure_gate() {
  local dir=$1 baseline="$1/baseline" runtime="$ROOT/Packages/com.zwebglframework.runtime/Runtime"; mkdir "$baseline"
  [ -z "$(find "$runtime" -type l -print -quit)" ] && [ -z "$(git -C "$ROOT" ls-files -s -- Packages/com.zwebglframework.runtime/Runtime | awk '$1=="120000"{print;exit}')" ] || return 1
  node - "$ROOT" "$baseline" <<'NODE'
const {execFileSync}=require('child_process'),fs=require('fs'),path=require('path'),r=process.argv[2],out=process.argv[3],rev='da608fdfb4a377eaf068f973f7e0fbb9a825418e';
const git=(...a)=>execFileSync('git',['-C',r,...a]);const files=git('ls-tree','-r','--name-only','-z',rev).toString().split('\0').filter(Boolean);fs.writeFileSync(path.join(out,'tracked-files.txt'),files.join('\n')+'\n');
const show=f=>git('show',`${rev}:${f}`).toString();const guid=f=>(show(f).match(/^guid:\s*([a-f0-9]{32})/m)||[])[1];const metas=files.filter(f=>f.endsWith('.meta')).map(f=>`${f}\t${guid(f)}`);fs.writeFileSync(path.join(out,'meta.tsv'),metas.join('\n')+'\n');
const asm=files.filter(f=>f.endsWith('.asmdef')).map(f=>JSON.stringify({path:f,guid:guid(f+'.meta'),asmdef:JSON.parse(show(f))}));fs.writeFileSync(path.join(out,'asmdefs.ndjson'),asm.join('\n')+'\n');
NODE
  npm ci --prefix "$ROOT/ci" --ignore-scripts
  local args=(--baseline "$baseline" --root "$ROOT") meta allowed=(Packages/com.zwebglframework.runtime/package.json.meta Packages/com.zwebglframework.runtime/ThirdParty.meta Packages/com.zwebglframework.runtime/ThirdParty/Kcp2k.meta Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket.meta Packages/com.zwebglframework.runtime/README.md.meta Packages/com.zwebglframework.runtime/CHANGELOG.md.meta Packages/com.zwebglframework.runtime/LICENSE.meta Packages/com.zwebglframework.runtime/THIRD_PARTY_NOTICES.md.meta Assets/Tests/PublicApiContract.meta Assets/Tests/PublicApiContract/ZWebGLFrameWork.PublicApiContract.Tests.asmdef.meta Assets/Tests/PublicApiContract/SessionPresentationBridgePublicContractTests.cs.meta Assets/Tests/PublicApiContract/PublicApiContractTestSupport.cs.meta Assets/Tests/PublicApiContract/CommandRecorderPublicContractTests.cs.meta Assets/Tests/SimulationCore/CommandRecorderBufferedOutputTests.cs.meta)
  for meta in "${allowed[@]}"; do args+=(--allow-new-meta "$meta"); done
  node "$ROOT/scripts/oss/validate-package-move.mjs" "${args[@]}"
  node "$ROOT/scripts/oss/validate-package-governance.mjs" --root "$ROOT"
  "$ROOT/scripts/repair/audit-meta.sh" "$ROOT"; "$ROOT/scripts/repair/audit-compiler-contracts.sh" "$ROOT"
  (cd "$ROOT" && scripts/vendor-nativewebsocket.test.sh && scripts/vendor-nativewebsocket.metadata.test.sh)
}
run_formal_gate() {
  local id=$1 dir=$2; printf 'gate=%s\nrevision=%s\n' "$id" "$REVISION"
  case "$id" in
    unity-compile) unity_run compile '' '' "$dir" ;;
    unity-editmode) unity_run test EditMode '' "$dir" ;;
    unity-playmode) unity_run test PlayMode '' "$dir" ;;
    external-public-contracts) unity_run test EditMode ZWebGLFrameWork.PublicApiContract.Tests "$dir" ;;
    editor-mono-allocation) unity_run test EditMode 'ZWebGLFrameWork.Editor.Tests.SimulationPerformanceAllocationHotspotTests;ZWebGLFrameWork.Combat.Tests.BufferedSessionPresentationBridgeTests' "$dir" ;;
    simulation-soak) unity_run performance '' '' "$dir"; node "$ROOT/scripts/ci/validate-simulation-performance-report.mjs" --report "$dir/performance/simulation-baseline.json" ;;
    framework-dotnet) framework_dotnet "$dir" ;;
    node-python-workflow) npm ci --prefix "$ROOT/ci" --ignore-scripts; (cd "$ROOT" && node --test scripts/audit/*.test.mjs scripts/ci/*.test.mjs scripts/oss/*.test.mjs); (cd "$ROOT" && PYTHONPATH=scripts/ci python3 -m unittest discover -s scripts/ci -p 'test_*.py') ;;
    webgl-build-browser-smoke) "$ROOT/scripts/oss/run-webgl-smoke.sh" --revision "$REVISION" --unity "$UNITY" --evidence-root "$dir" ;;
    package-structure-governance) structure_gate "$dir" ;;
    p-history-provenance) (cd "$ROOT" && node scripts/oss/audit-history.mjs --revision "$REVISION" --evidence-root "$dir") ;;
    p-diff-whitespace-whitelist) p_diff_gate "$dir" ;;
    *) return 2 ;;
  esac
}

while IFS=$'\t' read -r id policy required milestone environment registry_notes; do
  status='NOT RUN'; digest='-'; evidence_scope=none; notes='Not run before its milestone.'; evidence_file=
  if [ "$required" = true ] && [ "$milestone" = "$PHASE" ]; then
    if [ "$FAILED" -eq 0 ]; then
      evidence_file="$id.digest"; target="$OUTPUT/$evidence_file"; gate_dir="$OUTPUT/$id"; mkdir "$gate_dir"
      set +e
      if [ "$FIXTURE" -eq 1 ]; then "$EXECUTOR" "$id" "$target" "$REVISION" "$UNITY"; gate_exit=$?
      else { candidate_ok && run_formal_gate "$id" "$gate_dir" && evidence_inventory "$gate_dir" && candidate_ok; } > "$target" 2>&1; gate_exit=$?; fi
      set -e
      [ -s "$target" ] || printf 'gate=%s\nrevision=%s\n' "$id" "$REVISION" > "$target"
      [ "$FIXTURE" -eq 0 ] || printf 'classification=fixture-only\nformalReleaseEvidence=false\n' >> "$target"
      printf 'environment=%s\nexit=%s\n' "$environment" "$gate_exit" >> "$target"
      status=PASS; notes='Gate command passed.'; [ "$gate_exit" -eq 0 ] || { status=FAIL; notes='Gate command failed.'; FAILED=1; FAILED_GATE=$id; }
      digest=$(shasum -a 256 "$target" | cut -d' ' -f1); [ "$FIXTURE" -eq 0 ] && evidence_scope=private || evidence_scope=fixture
    else notes='Not run after an earlier Required failure.'; fi
  elif [ "$required" = false ]; then notes=$registry_notes; fi
  node -e 'const fs=require("fs"),[file,id,revision,status,digest,notes,scope,evidence]=process.argv.slice(1);const row={id,revision,status,digest,notes,evidenceScope:scope};if(evidence)row.evidenceFile=evidence;fs.appendFileSync(file,JSON.stringify(row)+"\n")' \
    "$RESULTS" "$id" "$policy" "$status" "$digest" "$notes" "$evidence_scope" "$evidence_file"
done < <(node -e 'const r=require(process.argv[1]);for(const g of r.gates)console.log([g.id,g.revision,g.required,g.milestone,g.environment,g.notes||"-"].join("\t"))' "$REGISTRY")

if [ "$FIXTURE" -eq 0 ] && [ "$FAILED" -eq 0 ]; then
  node "$ROOT/scripts/oss/release-evidence.mjs" validate-gates --registry "$REGISTRY" --results "$RESULTS" --through "$PHASE" || { FAILED=1; FAILED_GATE=gate-results-validation; }
fi
if [ "$FAILED" -ne 0 ]; then printf 'Required gate failed: %s\n' "$FAILED_GATE" >&2; exit 1; fi
printf '[run-required-gates] PASS phase=%s mode=%s\n' "$PHASE" "$([ "$FIXTURE" -eq 1 ] && printf fixture || printf formal)"
