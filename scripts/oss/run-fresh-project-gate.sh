#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd -P)
DRY_RUN=0 NO_REMOTE=0 FIXTURE_RUN=0 REVISION= FIXTURE_OUTPUT= ACCESS= UNITY= EVIDENCE_ROOT=
PROJECT= OUTPUT= UNITY_PID= UNITY_PID_SAVED= UNITY_START= UNITY_COMMAND= UNITY_CWD= FINALIZED=0 SIGNAL_BLOCKED=0
TMP_ROOT=${TMPDIR:-/tmp}; TMP_ROOT=${TMP_ROOT%/}; TMP_ROOT=$(cd "$TMP_ROOT" && pwd -P)
TOTAL_TIMEOUT=${FRAMELOOM_FRESH_TOTAL_TIMEOUT_SECONDS:-1200}
EXIT_GRACE=${FRAMELOOM_FRESH_EXIT_GRACE_SECONDS:-600}
POLL_SECONDS=${FRAMELOOM_FRESH_POLL_SECONDS:-2}
TERM_GRACE=${FRAMELOOM_FRESH_TERM_GRACE_SECONDS:-30}
MARKER='Test run completed. Exiting with code 0 (Ok). Run completed.'
fail() { printf '%s\n' "$*" >&2; exit 2; }
canonical_output() {
  local value=$1 parent
  case "$value" in /*) ;; *) fail "Output path must be absolute: $value";; esac
  parent=$(cd "$(dirname "$value")" && pwd -P)
  printf '%s/%s\n' "$parent" "$(basename "$value")"
}
outside_repository() { case "$1" in "$ROOT"|"$ROOT"/*) fail "Output path must be outside the repository";; esac; }
new_output() {
  local output; output=$(canonical_output "$1"); outside_repository "$output"
  [ ! -e "$output" ] && [ ! -L "$output" ] || fail "Output path already exists: $output"
  mkdir "$output"; printf '%s\n' "$output"
}
cleanup_project() {
  [ -n "$PROJECT" ] || return 0
  case "$PROJECT" in "$TMP_ROOT"/frameloom-upm.*) ;; *) return 1;; esac
  if [ -e "$PROJECT" ] || [ -L "$PROJECT" ]; then rm -rf "$PROJECT"; fi
}
record_cleanup() {
  [ -n "$OUTPUT" ] || return 0
  node -e '
const fs=require("fs"),path=require("path"); const [file,project]=process.argv.slice(1), residual=[];
function walk(value){let info;try{info=fs.lstatSync(value)}catch(error){if(error.code==="ENOENT")return;throw error}
residual.push(value);if(info.isDirectory()&&!info.isSymbolicLink())for(const name of fs.readdirSync(value).sort((a,b)=>Buffer.compare(Buffer.from(a),Buffer.from(b))))walk(path.join(value,name))}
walk(project);fs.writeFileSync(file,JSON.stringify({projectPath:project,removed:residual.length===0,residualPaths:residual},null,2)+"\n");
' "$OUTPUT/cleanup.json" "$PROJECT"
}
finish_project() {
  local cleanup_exit=0; [ -z "$UNITY_PID" ] || return 1
  cleanup_project || cleanup_exit=$?; record_cleanup
  [ "$cleanup_exit" -eq 0 ] && [ ! -e "$PROJECT" ] && [ ! -L "$PROJECT" ] || return 1
  PROJECT=
}
process_cwd() {
  local value=
  if command -v lsof >/dev/null 2>&1; then
    value=$(lsof -a -p "$1" -d cwd -Fn 2>/dev/null | awk '/^n/{sub(/^n/,"");print;exit}' || true)
  elif [ -L "/proc/$1/cwd" ]; then value=$(readlink "/proc/$1/cwd" 2>/dev/null || true); fi
  [ -n "$value" ] && printf '%s\n' "$value"
}
identity_matches() {
  local live_start live_command live_ppid live_cwd
  live_start=$(ps -p "$UNITY_PID" -o lstart= 2>/dev/null | sed 's/^ *//;s/ *$//' || true)
  live_command=$(ps -ww -p "$UNITY_PID" -o command= 2>/dev/null || true)
  live_ppid=$(ps -p "$UNITY_PID" -o ppid= 2>/dev/null | tr -d ' ' || true)
  live_cwd=$(process_cwd "$UNITY_PID" || true)
  [ "$UNITY_PID" -gt 1 ] && [ "$live_ppid" = "$$" ] && [ -n "$UNITY_START" ] && \
    [ "$live_start" = "$UNITY_START" ] && [ "$live_command" = "$UNITY_COMMAND" ] && \
    [ "$live_cwd" = "$PROJECT" ] && [ "$UNITY_CWD" = "$PROJECT" ] && \
    [[ "$live_command" == *"$UNITY"* && "$live_command" == *"$PROJECT"* ]]
}
signal_unity() {
  local signal=$1 reason=$2
  if ! identity_matches; then
    printf 'SIGNAL_REJECTED pid=%s signal=%s reason=%s identity=unconfirmed\n' "$UNITY_PID" "$signal" "$reason" >> "$OUTPUT/signal-ledger.txt"
    SIGNAL_BLOCKED=1; return 1
  fi
  if kill -"$signal" "$UNITY_PID" 2>/dev/null; then
    SIGNAL_COUNT=$((SIGNAL_COUNT + 1)); printf 'SIG%s pid=%s reason=%s identity=confirmed\n' \
      "$signal" "$UNITY_PID" "$reason" >> "$OUTPUT/signal-ledger.txt"
  else kill -0 "$UNITY_PID" 2>/dev/null && return 1 || true; fi
}
terminate_unity() {
  local attempt
  signal_unity TERM "$1" || return 1
  for ((attempt=0; attempt<TERM_GRACE; attempt++)); do kill -0 "$UNITY_PID" 2>/dev/null || return 0; sleep 1; done
  kill -0 "$UNITY_PID" 2>/dev/null || return 0
  signal_unity KILL term-timeout
}
finalize() {
  local status=$?; trap - EXIT
  if [ -n "$UNITY_PID" ]; then
    if kill -0 "$UNITY_PID" 2>/dev/null; then
      [ "$SIGNAL_BLOCKED" -eq 0 ] && terminate_unity wrapper-exit || { record_cleanup || true; exit 2; }
    fi
    wait "$UNITY_PID" 2>/dev/null || true; UNITY_PID=
  fi
  if [ "$FINALIZED" -eq 0 ] && [ -n "$PROJECT" ]; then finish_project || status=2; fi
  exit "$status"
}
trap finalize EXIT
while [ "$#" -gt 0 ]; do
  case "$1" in
    --dry-run) DRY_RUN=1; shift;; --no-remote) NO_REMOTE=1; shift;; --fixture-run) FIXTURE_RUN=1; shift;;
    --framework-revision) [ "$#" -ge 2 ] || fail "Missing framework revision"; REVISION=$2; shift 2;;
    --fixture-output) [ "$#" -ge 2 ] || fail "Missing fixture output"; FIXTURE_OUTPUT=$2; shift 2;;
    --access) [ "$#" -ge 2 ] || fail "Missing access classification"; ACCESS=$2; shift 2;;
    --unity) [ "$#" -ge 2 ] || fail "Missing Unity path"; UNITY=$2; shift 2;;
    --evidence-root) [ "$#" -ge 2 ] || fail "Missing evidence root"; EVIDENCE_ROOT=$2; shift 2;;
    *) fail "Unknown argument: $1";;
  esac
done
case "$REVISION" in ''|*[!0-9a-f]*) fail "Framework revision must be a full lowercase SHA";; esac
[ "${#REVISION}" -eq 40 ] || fail "Framework revision must be a full lowercase SHA"
for limit in "$TOTAL_TIMEOUT" "$EXIT_GRACE" "$POLL_SECONDS" "$TERM_GRACE"; do case "$limit" in ''|*[!0-9]*) fail "Invalid watchdog limit";; esac; done
[ "$TOTAL_TIMEOUT" -gt 0 ] && [ "$TOTAL_TIMEOUT" -le 1200 ] || fail "Invalid total timeout"
[ "$EXIT_GRACE" -gt 0 ] && [ "$EXIT_GRACE" -le 600 ] || fail "Invalid exit grace"
[ "$POLL_SECONDS" -gt 0 ] && [ "$POLL_SECONDS" -le 2 ] || fail "Invalid poll interval"
[ "$TERM_GRACE" -gt 0 ] && [ "$TERM_GRACE" -le 30 ] || fail "Invalid termination grace"
PROJECT=$(mktemp -d "$TMP_ROOT/frameloom-upm.XXXXXX")
node "$ROOT/scripts/oss/generate-upm-consumer.mjs" --root "$ROOT" --output "$PROJECT" \
  --framework-revision "$REVISION" >/dev/null
if [ "$DRY_RUN" -eq 1 ]; then
  [ "$NO_REMOTE" -eq 1 ] || fail "Dry-run requires --no-remote"
  [ -n "$FIXTURE_OUTPUT" ] || fail "Dry-run requires --fixture-output"
  OUTPUT=$(new_output "$FIXTURE_OUTPUT")
  cp "$PROJECT/Packages/manifest.json" "$OUTPUT/manifest.json"
  cp "$PROJECT/Assets/PublicApiContract/ZWebGLFrameWork.PublicApiContract.Tests.asmdef" "$OUTPUT/contract-asmdef.json"
  (cd "$PROJECT/Assets/PublicApiContract" && shasum -a 256 *.cs) > "$OUTPUT/copied-sources.sha256"
  printf '{"schemaVersion":1,"classification":"fixture-only","formalReleaseEvidence":false,"networkInvoked":false,"unityInvoked":false,"frameworkRevision":"%s"}\n' \
    "$REVISION" > "$OUTPUT/fixture-only.json"
  finish_project || fail "Consumer cleanup failed"; FINALIZED=1
  printf '[fresh-project-dry-run] PASS fixture-only output=%s\n' "$OUTPUT"; exit 0
fi

[ "$FIXTURE_RUN" -eq 0 ] || { [ "$NO_REMOTE" -eq 1 ] && [ -z "$ACCESS" ] || fail "Fixture run requires --no-remote and no --access"; }
if [ "$FIXTURE_RUN" -eq 0 ]; then
  [ "$NO_REMOTE" -eq 0 ] || fail "--no-remote requires --dry-run or --fixture-run"
  case "$ACCESS" in private-authenticated|public-anonymous) ;; *) fail "Real gate requires an approved --access value";; esac
else ACCESS=fixture-only; fi
[ -x "$UNITY" ] || fail "Unity path is not executable: $UNITY"
[ -n "$EVIDENCE_ROOT" ] || fail "Real gate requires --evidence-root"
command -v python3 >/dev/null || fail "python3 is required"
OUTPUT=$(new_output "$EVIDENCE_ROOT")
printf '{"schemaVersion":1,"frameworkRevision":"%s","access":"%s","fixtureOnly":%s,"formalReleaseEvidence":%s,"initialLibraryPresent":false,"cacheIsolation":"global-cache-observed"}\n' \
  "$REVISION" "$ACCESS" "$([ "$FIXTURE_RUN" -eq 1 ] && printf true || printf false)" \
  "$([ "$FIXTURE_RUN" -eq 1 ] && printf false || printf true)" > "$OUTPUT/input.json"
cp "$PROJECT/Packages/manifest.json" "$OUTPUT/manifest.json"
cp "$PROJECT/Assets/PublicApiContract/ZWebGLFrameWork.PublicApiContract.Tests.asmdef" "$OUTPUT/contract-asmdef.json"
mkdir "$OUTPUT/contract-sources"; cp "$PROJECT/Assets/PublicApiContract/"*.cs "$OUTPUT/contract-sources/"
: > "$OUTPUT/signal-ledger.txt"; : > "$OUTPUT/unity-timeout.sample.txt"
START_EPOCH=$(date +%s); COMPLETE_EPOCH=0; COMPLETE_SOURCE=none; POST_EXIT=0; WATCHDOG=0; WATCHDOG_REASON=none; SIGNAL_COUNT=0
[ ! -e "$PROJECT/Library" ] && [ ! -L "$PROJECT/Library" ] || fail "Generated consumer Library must start absent"
(cd "$PROJECT" && exec "$UNITY" -batchmode -runTests -projectPath "$PROJECT" -testPlatform EditMode \
  -testResults "$OUTPUT/results.xml" -logFile "$OUTPUT/unity.log" \
  -testFilter ZWebGLFrameWork.PublicApiContract.Tests) > "$OUTPUT/unity.stdout.txt" 2> "$OUTPUT/unity.stderr.txt" &
UNITY_PID=$!; UNITY_PID_SAVED=$UNITY_PID; sleep 0.1 || true
UNITY_START=$(ps -p "$UNITY_PID" -o lstart= 2>/dev/null | sed 's/^ *//;s/ *$//' || true)
UNITY_COMMAND=$(ps -ww -p "$UNITY_PID" -o command= 2>/dev/null || true)
UNITY_CWD=$(process_cwd "$UNITY_PID" || true)
while kill -0 "$UNITY_PID" 2>/dev/null; do
  NOW=$(date +%s)
  if [ "$COMPLETE_EPOCH" -eq 0 ] && [ -f "$OUTPUT/unity.log" ] && [ -f "$OUTPUT/results.xml" ] && \
      rg -Fq "$MARKER" "$OUTPUT/unity.log" && python3 "$ROOT/scripts/audit/read-test-results.py" "$OUTPUT/results.xml" >/dev/null 2>&1; then
    COMPLETE_EPOCH=$NOW; COMPLETE_SOURCE=poll
  fi
  if [ "$NOW" -ge $((START_EPOCH + TOTAL_TIMEOUT)) ]; then WATCHDOG=1; WATCHDOG_REASON=total-timeout; fi
  if [ "$COMPLETE_EPOCH" -gt 0 ] && [ "$NOW" -ge $((COMPLETE_EPOCH + EXIT_GRACE)) ]; then WATCHDOG=1; WATCHDOG_REASON=post-completion-timeout; fi
  [ "$WATCHDOG" -eq 0 ] || break; sleep "$POLL_SECONDS"
done
if [ "$WATCHDOG" -eq 1 ]; then
  command -v sample >/dev/null && sample "$UNITY_PID" 10 1 -file "$OUTPUT/unity-timeout.sample.txt" >/dev/null 2>&1 || true
  terminate_unity "$WATCHDOG_REASON" || fail "Unity process identity could not be confirmed; consumer retained"
fi
set +e; wait "$UNITY_PID"; UNITY_EXIT=$?; set -e; UNITY_PID=
END_EPOCH=$(date +%s)
if [ "$COMPLETE_EPOCH" -eq 0 ] && [ -f "$OUTPUT/unity.log" ] && [ -f "$OUTPUT/results.xml" ] && \
    rg -Fq "$MARKER" "$OUTPUT/unity.log" && python3 "$ROOT/scripts/audit/read-test-results.py" "$OUTPUT/results.xml" >/dev/null 2>&1; then
  COMPLETE_EPOCH=$(node -e 'const fs=require("fs");console.log(Math.floor(fs.statSync(process.argv[1]).mtimeMs/1000))' \
    "$OUTPUT/results.xml"); COMPLETE_SOURCE=xml-mtime-post-exit; POST_EXIT=1
fi
COMPLETION_TO_EXIT=-1; [ "$COMPLETE_EPOCH" -eq 0 ] || COMPLETION_TO_EXIT=$((END_EPOCH - COMPLETE_EPOCH))
[ -f "$PROJECT/Packages/packages-lock.json" ] && cp "$PROJECT/Packages/packages-lock.json" "$OUTPUT/packages-lock.json" || printf '{}\n' > "$OUTPUT/packages-lock.json"
set +e
node "$ROOT/scripts/oss/generate-upm-consumer.mjs" --capture-project "$PROJECT" --evidence-root "$OUTPUT" \
  --framework-revision "$REVISION" > "$OUTPUT/resolved-capture.txt" 2>&1
CAPTURE_EXIT=$?
python3 "$ROOT/scripts/audit/read-test-results.py" "$OUTPUT/results.xml" > "$OUTPUT/test-cases.json" 2>/dev/null
[ "$?" -eq 0 ] || printf '[]\n' > "$OUTPUT/test-cases.json"
set -e
node -e '
const fs=require("fs"),[xmlFile,logFile,sampleFile,outFile,...v]=process.argv.slice(1),read=p=>fs.existsSync(p)?fs.readFileSync(p,"utf8"):"";
const xml=read(xmlFile),log=read(logFile),sample=read(sampleFile),attrs=Object.fromEntries([...(xml.match(/<test-run\b([^>]*)>/)||[," "])[1].matchAll(/([\w-]+)="([^"]*)"/g)].map(x=>[x[1],x[2]]));
const count=r=>(log.match(r)||[]).length+(sample.match(r)||[]).length,device=log.match(/^Using device (.+)$/m)?.[1]||"unknown",metal=/^GfxDevice: creating device client$/m.test(log)&&/^Using device Apple M3 Pro \(high power\)$/m.test(log)&&/^Initializing Metal device caps: Apple M3 Pro$/m.test(log);
const result={schemaVersion:1,editorVersion:log.match(/(?:Version|Editor Version):?\s*(2022\.3\.62f3c1)/i)?.[1]||"unknown",exitCode:+v[0],compileErrors:(log.match(/error CS\d+/gi)||[]).length,
testRun:Object.fromEntries(["total","passed","failed","inconclusive","skipped"].map(k=>[k,+(attrs[k]??-1)])),lifecycle:{startEpoch:+v[1],completionEpoch:+v[2],exitEpoch:+v[3],completionToExitSeconds:+v[4],completionMarkerCount:(log.split(v[10]).length-1),postExitAdjudicated:v[5]==="1",completionSource:v[6],watchdogCount:+v[7],watchdogReason:v[8],signalCount:+v[9],shutdownSpinMatches:count(/Application::Terminate|Application::CoreShutdown|JobSystem::ShutdownJobThreads/g),unobservedExceptionMatches:count(/buffered presentation failed|UniTaskScheduler[.:]PublishUnobservedTaskException|ExceptionHolder[.:]Finalize/gi)},graphics:{backend:metal?"Metal":"unknown",device},process:{pid:+v[11],observedCwd:v[12],projectPath:v[13]}};
fs.writeFileSync(outFile,JSON.stringify(result,null,2)+"\n");
' "$OUTPUT/results.xml" "$OUTPUT/unity.log" "$OUTPUT/unity-timeout.sample.txt" "$OUTPUT/unity-result.json" \
  "$UNITY_EXIT" "$START_EPOCH" "$COMPLETE_EPOCH" "$END_EPOCH" "$COMPLETION_TO_EXIT" "$POST_EXIT" "$COMPLETE_SOURCE" \
  "$WATCHDOG" "$WATCHDOG_REASON" "$SIGNAL_COUNT" "$MARKER" "$UNITY_PID_SAVED" "$UNITY_CWD" "$PROJECT"
finish_project || fail "Consumer cleanup failed"; FINALIZED=1
set +e
VALIDATOR_ARGS=(--evidence-root "$OUTPUT" --repository-root "$ROOT" --framework-revision "$REVISION")
[ "$FIXTURE_RUN" -eq 0 ] || VALIDATOR_ARGS+=(--allow-fixture true)
node "$ROOT/scripts/oss/validate-fresh-project.mjs" "${VALIDATOR_ARGS[@]}" > "$OUTPUT/validator.txt" 2>&1
VALIDATOR_EXIT=$?
set -e; cat "$OUTPUT/validator.txt"
[ "$UNITY_EXIT" -eq 0 ] && [ "$WATCHDOG" -eq 0 ] && [ "$SIGNAL_COUNT" -eq 0 ] && \
  [ "$CAPTURE_EXIT" -eq 0 ] && [ "$VALIDATOR_EXIT" -eq 0 ]
