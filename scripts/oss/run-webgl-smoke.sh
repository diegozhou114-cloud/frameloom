#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd -P)
REVISION= UNITY= EVIDENCE_ROOT= BROWSER_RUNNER= FIXTURE=0 OUTPUT= SERVER_PID= SERVER_REAPED=false
fail() { printf '%s\n' "$*" >&2; exit 2; }
while [ "$#" -gt 0 ]; do
  case "$1" in
    --fixture-run) FIXTURE=1; shift ;;
    --revision) REVISION=${2:-}; shift 2 ;;
    --unity) UNITY=${2:-}; shift 2 ;;
    --evidence-root) EVIDENCE_ROOT=${2:-}; shift 2 ;;
    --browser-runner) BROWSER_RUNNER=${2:-}; shift 2 ;;
    *) fail "Invalid argument: $1" ;;
  esac
done
[[ "$REVISION" =~ ^[a-f0-9]{40}$ ]] || fail "Revision must be a full lowercase 40-character SHA"
[ -x "$UNITY" ] && [[ "$UNITY" = /* ]] || fail "Unity must be an absolute executable"
if [ -n "$BROWSER_RUNNER" ]; then
  [ "$FIXTURE" -eq 1 ] || fail "--browser-runner requires --fixture-run"
  [ -x "$BROWSER_RUNNER" ] && [[ "$BROWSER_RUNNER" = /* ]] || fail "Browser runner must be an absolute executable"
elif [ "$FIXTURE" -eq 1 ]; then fail "--fixture-run requires --browser-runner"; fi
OUTPUT=$(node -e '
const fs=require("fs"),path=require("path"),[input,repo]=process.argv.slice(1);
function fail(value){console.error(value);process.exit(2)}
if(!path.isAbsolute(input))fail("Evidence root must be absolute");const target=path.resolve(input);
const parent=fs.realpathSync(path.dirname(target)),canonical=path.join(parent,path.basename(target)),root=fs.realpathSync(repo);
if(target!==canonical)fail("Evidence root must be a canonical directory");const relative=path.relative(root,target);
if(relative===""||(!relative.startsWith(".."+path.sep)&&relative!==".."))fail("Evidence root must be outside the repository");
let info=null;try{info=fs.lstatSync(target)}catch(error){if(error.code!=="ENOENT")throw error}
if(info&&(!info.isDirectory()||info.isSymbolicLink()))fail("Evidence root must be a canonical directory");
if(info&&fs.readdirSync(target).length)fail("Evidence root must be new or empty");if(!info)fs.mkdirSync(target);console.log(canonical);
' "$EVIDENCE_ROOT" "$ROOT")
if [ "$FIXTURE" -eq 0 ]; then
  [ "$(git -C "$ROOT" rev-parse HEAD)" = "$REVISION" ] || fail "Revision does not match HEAD"
  [ -z "$(git -C "$ROOT" status --porcelain=v1 --untracked-files=all)" ] || fail "Repository must be clean"
else
  node -e 'const fs=require("fs");fs.writeFileSync(process.argv[1],JSON.stringify({schemaVersion:1,classification:"fixture-only",formalReleaseEvidence:false,revision:process.argv[2]},null,2)+"\n")' "$OUTPUT/fixture-only.json" "$REVISION"
fi
set +e
ZWEB_EVIDENCE_ROOT="$OUTPUT" "$UNITY" -batchmode -nographics -quit -projectPath "$ROOT" \
  -executeMethod ZWebGLFrameWork.Editor.BuildScript.BuildWebGL -logFile "$OUTPUT/unity-webgl.log" \
  > "$OUTPUT/unity.stdout.txt" 2> "$OUTPUT/unity.stderr.txt"
UNITY_EXIT=$?
set -e
[ "$UNITY_EXIT" -eq 0 ] || fail "Unity WebGL build failed: exit $UNITY_EXIT"
SUMMARY="$OUTPUT/builds/Reports/WebGL.json"
node "$ROOT/scripts/ci/validate-build-summary.mjs" --summary "$SUMMARY" --target WebGL \
  --artifact-root "$OUTPUT" > "$OUTPUT/build-validator.txt" 2>&1 || fail "WebGL build summary validation failed"
CERTS="$OUTPUT/https-certs"; mkdir "$CERTS"
openssl genrsa -out "$CERTS/ca.key" 2048 >/dev/null 2>&1
openssl req -x509 -new -key "$CERTS/ca.key" -sha256 -days 1 -subj '/CN=FrameLoom Local Test CA' -out "$CERTS/ca.crt" >/dev/null 2>&1
openssl genrsa -out "$CERTS/server.key" 2048 >/dev/null 2>&1
openssl req -new -key "$CERTS/server.key" -subj '/CN=localhost' -out "$CERTS/server.csr" >/dev/null 2>&1
printf 'subjectAltName=DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\n' > "$CERTS/server.ext"
openssl x509 -req -in "$CERTS/server.csr" -CA "$CERTS/ca.crt" -CAkey "$CERTS/ca.key" \
  -CAcreateserial -out "$CERTS/server.crt" -days 1 -sha256 -extfile "$CERTS/server.ext" >/dev/null 2>&1
SPKI=$(openssl x509 -in "$CERTS/server.crt" -pubkey -noout | openssl pkey -pubin -outform DER 2>/dev/null | openssl dgst -sha256 -binary | openssl base64 -A)
PORT=${FRAMELOOM_WEBGL_PORT:-8443}; [[ "$PORT" =~ ^[0-9]+$ ]] && [ "$PORT" -ge 1024 ] && [ "$PORT" -le 65535 ] || fail "Invalid HTTPS port"
[ "$FIXTURE" -eq 1 ] || [ "$PORT" -eq 8443 ] || fail "Production WebGL smoke requires port 8443"
server_matches() {
  [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null || return 1
  [ "$(ps -o ppid= -p "$SERVER_PID" | tr -d ' ')" = "$$" ] || return 1
  case "$(ps -ww -o command= -p "$SERVER_PID")" in *"scripts/ci/serve-https.mjs"*"$OUTPUT/builds/WebGL"*) return 0;; *) return 1;; esac
}
stop_server() {
  [ -n "$SERVER_PID" ] || return 0
  if server_matches; then kill -TERM "$SERVER_PID"; fi
  for _ in 1 2 3 4 5; do kill -0 "$SERVER_PID" 2>/dev/null || break; sleep 1; done
  if kill -0 "$SERVER_PID" 2>/dev/null; then server_matches || return 1; kill -KILL "$SERVER_PID"; fi
  set +e; wait "$SERVER_PID" 2>/dev/null; set -e
  kill -0 "$SERVER_PID" 2>/dev/null && return 1; SERVER_REAPED=true; return 0
}
trap 'stop_server || true' EXIT
node "$ROOT/scripts/ci/serve-https.mjs" --root "$OUTPUT/builds/WebGL" --cert "$CERTS/server.crt" \
  --key "$CERTS/server.key" --port "$PORT" > "$OUTPUT/https-server.log" 2>&1 & SERVER_PID=$!
URL="https://localhost:$PORT/"; READY=0
for _ in $(seq 1 30); do
  if curl --fail --silent --cacert "$CERTS/ca.crt" "$URL" >/dev/null 2>&1; then READY=1; break; fi
  server_matches || break; sleep 1
done
[ "$READY" -eq 1 ] || { stop_server || true; fail "Trusted HTTPS server did not become ready"; }
REPORT="$OUTPUT/webgl-playwright.json"
set +e
if [ "$FIXTURE" -eq 1 ]; then
  "$BROWSER_RUNNER" --url "$URL" --ca-cert "$CERTS/ca.crt" --spki "$SPKI" --report "$REPORT" \
    > "$OUTPUT/browser.stdout.txt" 2> "$OUTPUT/browser.stderr.txt"
  BROWSER_EXIT=$?
else
  npm ci --prefix "$ROOT/ci" --ignore-scripts > "$OUTPUT/npm-ci.txt" 2>&1
  NPM_EXIT=$?
  if [ "$NPM_EXIT" -eq 0 ]; then
    node -e 'const fs=require("fs"),{pathToFileURL}=require("url"),[out,base,testDir,url,spki]=process.argv.slice(1);fs.writeFileSync(out,`import base from ${JSON.stringify(pathToFileURL(base).href)};\nexport default {...base,testDir:${JSON.stringify(testDir)},use:{...base.use,baseURL:${JSON.stringify(url)},launchOptions:{args:[${JSON.stringify(`--ignore-certificate-errors-spki-list=${spki}`)}]}}};\n`)' "$OUTPUT/playwright.config.mjs" "$ROOT/ci/smoke/playwright.config.mjs" "$ROOT/ci/smoke" "$URL" "$SPKI"
    (cd "$ROOT/ci" && npx --no-install playwright install chromium && ZWEB_PLAYWRIGHT_JSON="$REPORT" npx --no-install playwright test --config "$OUTPUT/playwright.config.mjs" webgl.spec.mjs) > "$OUTPUT/browser.stdout.txt" 2> "$OUTPUT/browser.stderr.txt"
    BROWSER_EXIT=$?
  else BROWSER_EXIT=$NPM_EXIT; fi
fi
set -e
stop_server || fail "HTTPS server identity changed or could not be reaped"
trap - EXIT
node -e 'const fs=require("fs"),[file,pid,reaped]=process.argv.slice(1);fs.writeFileSync(file,JSON.stringify({pid:+pid,reaped:reaped==="true"},null,2)+"\n")' "$OUTPUT/https-server-process.json" "$SERVER_PID" "$SERVER_REAPED"
[ "$BROWSER_EXIT" -eq 0 ] || fail "Trusted browser smoke failed: exit $BROWSER_EXIT"
node -e 'const fs=require("fs"),v=JSON.parse(fs.readFileSync(process.argv[1],"utf8")),s=v.stats||{};if(!(Number.isInteger(s.expected)&&s.expected>0&&s.unexpected===0&&s.skipped===0))throw Error("browser report is not a nonzero PASS");if(/managed.{0,20}alloc/i.test(JSON.stringify(v)))throw Error("managed-allocation claim is prohibited")' "$REPORT" 2> "$OUTPUT/browser-validator.txt" || { cat "$OUTPUT/browser-validator.txt" >&2; fail "Invalid trusted browser report or managed-allocation claim"; }
hash_file() { shasum -a 256 "$1" | cut -d' ' -f1; }
if [ "$FIXTURE" -eq 1 ]; then CLASSIFICATION=fixture-only; FORMAL=false
else CLASSIFICATION=formal-release; FORMAL=true; fi
{
  printf 'schemaVersion=1\nclassification=%s\nformalReleaseEvidence=%s\n' "$CLASSIFICATION" "$FORMAL"
  printf 'gate=webgl-build-browser-smoke\nrevision=%s\nbuild=PASS\nbrowser=PASS\n' "$REVISION"
  printf 'buildSummarySha256=%s\nbrowserReportSha256=%s\n' "$(hash_file "$SUMMARY")" "$(hash_file "$REPORT")"
  printf 'webglManagedAllocation=NOT MEASURED\n'
} > "$OUTPUT/webgl-smoke.digest"
if [ "$FIXTURE" -eq 0 ]; then
  [ -z "$(git -C "$ROOT" status --porcelain=v1 --untracked-files=all)" ] || fail "WebGL gate changed repository state"
fi
printf '[run-webgl-smoke] PASS\n'
