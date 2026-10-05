import assert from 'node:assert/strict';
import { chmod, lstat, mkdir, mkdtemp, readFile, realpath, rm, symlink, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { createServer } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

const repository = path.resolve(new URL('../..', import.meta.url).pathname);
const driver = path.join(repository, 'scripts/oss/run-webgl-smoke.sh');
const revision = '2'.repeat(40);
const approvedProfile = '(version 1)\n(allow default)\n(deny network-outbound (remote ip "*:*"))\n(allow network-outbound (remote ip "localhost:*"))\n';

async function freePort() {
  return new Promise((resolve, reject) => {
    const server = createServer(); server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const port = server.address().port; server.close(error => error ? reject(error) : resolve(port));
    });
  });
}

async function fixture(t) {
  const top = await realpath(await mkdtemp(path.join(os.tmpdir(), 'frameloom-webgl-smoke-')));
  t.after(() => rm(top, { recursive: true, force: true }));
  const bin = path.join(top, 'bin'); await mkdir(bin); const unity = path.join(bin, 'FakeUnity');
  await writeFile(unity, `#!/bin/sh
set -eu
printf '%s\n' "$*" > "$FRAMELOOM_UNITY_ARGS"
batch=0; graphics=0; quit=0; method=0; log=
while [ "$#" -gt 0 ]; do case "$1" in
  -batchmode) batch=1; shift;; -nographics) graphics=1; shift;; -quit) quit=1; shift;;
  -executeMethod) test "$2" = ZWebGLFrameWork.Editor.BuildScript.BuildWebGL || exit 91; method=1; shift 2;;
  -logFile) log=$2; shift 2;; -runTests) exit 92;; *) shift;; esac; done
test "$batch$graphics$quit$method" = 1111; test -n "$log"
test -n "\${ZWEB_EVIDENCE_ROOT:-}"; mkdir -p "$ZWEB_EVIDENCE_ROOT/builds/WebGL/Build" "$ZWEB_EVIDENCE_ROOT/builds/Reports"
printf '<html>FrameLoom</html>\n' > "$ZWEB_EVIDENCE_ROOT/builds/WebGL/index.html"
printf 'wasm\n' > "$ZWEB_EVIDENCE_ROOT/builds/WebGL/Build/app.wasm"
printf 'data\n' > "$ZWEB_EVIDENCE_ROOT/builds/WebGL/Build/app.data"
node -e 'const fs=require("fs"),c=require("crypto"),r=process.argv[1],names=["builds/WebGL/index.html","builds/WebGL/Build/app.wasm","builds/WebGL/Build/app.data"];const artifacts=names.map(p=>{const b=fs.readFileSync(r+"/"+p);return{path:p,bytes:b.length,sha256:c.createHash("sha256").update(b).digest("hex")}});fs.writeFileSync(r+"/builds/Reports/WebGL.json",JSON.stringify({schemaVersion:1,editorVersion:"2022.3.62f3c1",target:"WebGL",result:"Succeeded",errors:0,enabledScenes:["Assets/Scence/Launcher.unity"],artifacts})+"\\n")' "$ZWEB_EVIDENCE_ROOT"
printf 'Unity Editor Version: 2022.3.62f3c1\nBuild completed successfully\n' > "$log"
exit "\${FAKE_UNITY_EXIT:-0}"
`); await chmod(unity, 0o755);
  const browser = path.join(bin, 'fixture-browser');
  await writeFile(browser, `#!/bin/sh
set -eu
while [ "$#" -gt 0 ]; do case "$1" in --url) url=$2; shift 2;; --ca-cert) ca=$2; shift 2;; --spki) spki=$2; shift 2;; --report) report=$2; shift 2;; *) exit 93;; esac; done
test -s "$ca"; test -n "$spki"; /usr/bin/curl --fail --silent --cacert "$ca" "$url" >/dev/null
printf '%s\n' "$url" > "$FRAMELOOM_BROWSER_LOG"
if [ "\${FAKE_BROWSER_ALLOCATION:-0}" = 1 ]; then extra=',"webglManagedAllocation":"PASS"'; else extra=''; fi
printf '{"stats":{"expected":1,"unexpected":0,"skipped":0}%s}\n' "$extra" > "$report"
exit "\${FAKE_BROWSER_EXIT:-0}"
`); await chmod(browser, 0o755);
  return { top, unity, browser, port: await freePort(), unityArgs: path.join(top, 'unity.args'), browserLog: path.join(top, 'browser.log') };
}

function run(value, evidence, env = {}, extra = []) {
  return spawnSync(driver, ['--fixture-run', '--revision', revision, '--unity', value.unity,
    '--evidence-root', evidence, '--browser-runner', value.browser, ...extra], { cwd: repository,
    encoding: 'utf8', env: { ...process.env, FRAMELOOM_WEBGL_PORT: String(value.port),
      FRAMELOOM_UNITY_ARGS: value.unityArgs, FRAMELOOM_BROWSER_LOG: value.browserLog, ...env } });
}

test('fixture proves build validation and pinned trusted HTTPS browser smoke', async t => {
  const value = await fixture(t); const evidence = path.join(value.top, 'evidence');
  const result = run(value, evidence); assert.equal(result.status, 0, `${result.stderr}\n${result.stdout}`);
  const args = await readFile(value.unityArgs, 'utf8');
  assert.match(args, /-projectPath .*zweb-glframe-oss-prep/); assert.doesNotMatch(args, /-runTests/);
  assert.equal(await readFile(value.browserLog, 'utf8'), `https://localhost:${value.port}/\n`);
  const digest = await readFile(path.join(evidence, 'webgl-smoke.digest'), 'utf8');
  assert.match(digest, /^schemaVersion=1$/m); assert.match(digest, /^classification=fixture-only$/m);
  assert.match(digest, /^formalReleaseEvidence=false$/m);
  assert.match(digest, /^gate=webgl-build-browser-smoke$/m); assert.match(digest, /^build=PASS$/m);
  assert.match(digest, /^browser=PASS$/m); assert.match(digest, /^webglManagedAllocation=NOT MEASURED$/m);
  assert.match(digest, /^unityGeneratedProjectLogCount=0$/m);
  assert.deepEqual(JSON.parse(await readFile(path.join(evidence, 'fixture-only.json'))), {
    schemaVersion: 1, classification: 'fixture-only', formalReleaseEvidence: false, revision,
  });
  const server = JSON.parse(await readFile(path.join(evidence, 'https-server-process.json')));
  assert.equal(server.reaped, true);
  assert.equal(spawnSync('ps', ['-p', String(server.pid), '-o', 'command='], { encoding: 'utf8' }).stdout.includes('serve-https.mjs'), false);
});

test('preserves only untracked regular Unity project logs as evidence', async t => {
  const source = await readFile(driver, 'utf8');
  const match = source.match(/preserve_unity_project_logs\(\) \{[\s\S]*?^\}\n/m);
  assert.ok(match, 'missing preserve_unity_project_logs function');
  const run = (root, output) => spawnSync('bash', ['-c', `${match[0]}
ROOT=$1 OUTPUT=$2 PROJECT_LOG_COUNT=0
preserve_unity_project_logs
rc=$?
printf '%s\n' "$PROJECT_LOG_COUNT"
exit "$rc"`, 'fixture', root, output], { encoding: 'utf8' });
  const top = await realpath(await mkdtemp(path.join(os.tmpdir(), 'frameloom-project-logs-')));
  t.after(() => rm(top, { recursive: true, force: true }));
  const root = path.join(top, 'repo'), output = path.join(top, 'evidence');
  await mkdir(path.join(root, 'Logs'), { recursive: true }); await mkdir(output);
  assert.equal(spawnSync('git', ['init', '-q'], { cwd: root }).status, 0);
  for (const name of ['AssetImportWorker0.log', 'AssetImportWorker1.log', 'ExportedTypes.log'])
    await writeFile(path.join(root, 'Logs', name), `${name}\n`);
  const result = run(root, output); assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout, '3\n');
  for (const name of ['AssetImportWorker0.log', 'AssetImportWorker1.log', 'ExportedTypes.log']) {
    await assert.rejects(lstat(path.join(root, 'Logs', name)), /ENOENT/);
    assert.equal(await readFile(path.join(output, 'unity-project-logs', name), 'utf8'), `${name}\n`);
  }
  const checked = spawnSync('shasum', ['-a', '256', '-c', 'manifest.sha256'], {
    cwd: path.join(output, 'unity-project-logs'), encoding: 'utf8',
  });
  assert.equal(checked.status, 0, checked.stderr);
});

test('project log preservation rejects symlinks and tracked files', async t => {
  const source = await readFile(driver, 'utf8');
  const match = source.match(/preserve_unity_project_logs\(\) \{[\s\S]*?^\}\n/m);
  assert.ok(match, 'missing preserve_unity_project_logs function');
  for (const mode of ['symlink', 'tracked']) {
    const top = await realpath(await mkdtemp(path.join(os.tmpdir(), `frameloom-project-log-${mode}-`)));
    t.after(() => rm(top, { recursive: true, force: true }));
    const root = path.join(top, 'repo'), output = path.join(top, 'evidence');
    await mkdir(path.join(root, 'Logs'), { recursive: true }); await mkdir(output);
    assert.equal(spawnSync('git', ['init', '-q'], { cwd: root }).status, 0);
    const log = path.join(root, 'Logs', 'ExportedTypes.log');
    if (mode === 'symlink') { const target = path.join(top, 'target'); await writeFile(target, 'log\n'); await symlink(target, log); }
    else { await writeFile(log, 'log\n'); assert.equal(spawnSync('git', ['add', 'Logs/ExportedTypes.log'], { cwd: root }).status, 0); }
    const result = spawnSync('bash', ['-c', `${match[0]}
ROOT=$1 OUTPUT=$2 PROJECT_LOG_COUNT=0
preserve_unity_project_logs`, 'fixture', root, output]);
    assert.notEqual(result.status, 0); assert.equal((await lstat(log)).isFile(), mode === 'tracked');
  }
});

test('build or browser failure cannot become a smoke PASS', async t => {
  for (const [env, diagnostic] of [[{ FAKE_UNITY_EXIT: '4' }, /Unity WebGL build failed/],
    [{ FAKE_BROWSER_EXIT: '5' }, /Trusted browser smoke failed/],
    [{ FAKE_BROWSER_ALLOCATION: '1' }, /managed-allocation claim/]]) {
    const value = await fixture(t); const result = run(value, path.join(value.top, 'evidence'), env);
    assert.notEqual(result.status, 0); assert.match(result.stderr, diagnostic);
  }
});

test('rejects unsafe paths, revisions, and fixture-only overrides', async t => {
  const value = await fixture(t); const inside = path.join(repository, 'webgl-evidence-forbidden');
  const target = path.join(value.top, 'target'); await mkdir(target); const link = path.join(value.top, 'link'); await symlink(target, link);
  const profile = path.join(value.top, 'unity.sb'); await writeFile(profile, approvedProfile);
  for (const [args, diagnostic] of [
    [['--fixture-run', '--revision', 'main', '--unity', value.unity, '--evidence-root', path.join(value.top, 'a'), '--browser-runner', value.browser], /full lowercase 40-character SHA/],
    [['--fixture-run', '--revision', revision, '--unity', value.unity, '--evidence-root', inside, '--browser-runner', value.browser], /outside the repository/],
    [['--fixture-run', '--revision', revision, '--unity', value.unity, '--evidence-root', link, '--browser-runner', value.browser], /canonical directory/],
    [['--revision', revision, '--unity', value.unity, '--evidence-root', path.join(value.top, 'b'), '--browser-runner', value.browser], /fixture-run/],
    [['--fixture-run', '--revision', revision, '--unity', value.unity, '--evidence-root', path.join(value.top, 'c'), '--browser-runner', value.browser, '--sandbox-profile', profile], /not supported with --fixture-run/],
  ]) {
    const result = spawnSync(driver, args, { cwd: repository, encoding: 'utf8' });
    assert.notEqual(result.status, 0); assert.match(result.stderr, diagnostic);
  }
  await assert.rejects(lstat(inside), /ENOENT/);
});
test('sandbox profile validation is pre-evidence and the Unity-only launcher is structured', { skip: process.platform !== 'darwin' }, async t => {
  const value = await fixture(t), good = path.join(value.top, 'good.sb'), bad = path.join(value.top, 'bad.sb'), missing = path.join(value.top, 'missing.sb'); await writeFile(good, approvedProfile); await writeFile(bad, '(version 1)\n(allow default)\n');
  const profiles = path.join(value.top, 'profiles'); await mkdir(profiles); const nested = path.join(profiles, 'nested.sb'); await writeFile(nested, approvedProfile); const direct = path.join(value.top, 'direct.sb'), ancestor = path.join(value.top, 'ancestor'); await symlink(good, direct); await symlink(profiles, ancestor);
  const cases = [[['--sandbox-profile'], /Missing value/], [['--sandbox-profile', '--revision'], /Missing value/], [['--sandbox-profile', 'relative.sb'], /canonical and absolute/], [['--sandbox-profile', missing], /regular file/], [['--sandbox-profile', value.top], /regular file/], [['--sandbox-profile', direct], /must not be symlinks/], [['--sandbox-profile', path.join(ancestor, 'nested.sb')], /must not be symlinks/], [['--sandbox-profile', driver], /outside the repository/], [['--sandbox-profile', bad], /approved Unity sandbox profile/], [['--sandbox-profile', ''], /Missing value/], [['--sandbox-profile', good, '--sandbox-profile', ''], /Duplicate argument/]];
  for (const [tail, diagnostic] of cases) { const evidence = path.join(value.top, `formal-${Math.random()}`), result = spawnSync(driver, ['--revision', revision, '--unity', value.unity, '--evidence-root', evidence, ...tail], { cwd: repository, encoding: 'utf8', env: { ...process.env, FRAMELOOM_UNITY_ARGS: value.unityArgs } }); assert.notEqual(result.status, 0); assert.match(result.stderr, diagnostic); await assert.rejects(lstat(evidence), /ENOENT/); }
  const wrappedEvidence = path.join(value.top, 'wrapped'), wrapped = spawnSync('/usr/bin/sandbox-exec', ['-f', good, driver, '--revision', revision, '--unity', value.unity, '--evidence-root', wrappedEvidence, '--sandbox-profile', good], { cwd: repository, encoding: 'utf8', env: { ...process.env, FRAMELOOM_UNITY_ARGS: value.unityArgs } }); assert.notEqual(wrapped.status, 0); assert.match(wrapped.stderr, /Unity supervisor must run outside sandbox/); await assert.rejects(lstat(wrappedEvidence), /ENOENT/); await assert.rejects(lstat(value.unityArgs), /ENOENT/);
  const source = await readFile(driver, 'utf8'), parent = await readFile(path.join(repository, 'scripts/oss/run-required-gates.sh'), 'utf8'); assert.match(source, /unity-launcher-argv\.json/); assert.match(source, /UNITY_LAUNCH=\(\/usr\/bin\/sandbox-exec -f/); assert.match(source, /process\.platform!=="darwin"/); assert.match(parent, /webgl\+=\(--sandbox-profile "\$SANDBOX_PROFILE"\)/); assert.ok(source.trimEnd().split('\n').length <= 200);
});
