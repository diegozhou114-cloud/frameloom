import assert from 'node:assert/strict';
import { chmod, lstat, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

const repository = path.resolve(new URL('../..', import.meta.url).pathname);
const driver = path.join(repository, 'scripts/oss/run-fresh-project-gate.sh');
const revision = '1'.repeat(40);

async function fixture(t) {
  const top = await mkdtemp(path.join(os.tmpdir(), 'zweb-fresh-driver-'));
  t.after(() => rm(top, { recursive: true, force: true }));
  const bin = path.join(top, 'bin'); await mkdir(bin);
  for (const name of ['git', 'curl']) {
    const target = path.join(bin, name);
    await writeFile(target, '#!/bin/sh\necho forbidden-network-command >&2\nexit 99\n'); await chmod(target, 0o755);
  }
  const sample = path.join(bin, 'sample');
  await writeFile(sample, '#!/bin/sh\nwhile [ "$#" -gt 0 ]; do [ "$1" = -file ] && { : > "$2"; exit 0; }; shift; done\nexit 0\n');
  await chmod(sample, 0o755);
  return { top, bin, env: { ...process.env, PATH: `${bin}:${process.env.PATH}` } };
}

function run(args, env = process.env) {
  return spawnSync(driver, args, { cwd: repository, env, encoding: 'utf8' });
}
const fixtureArgs = (unity, evidence) => ['--fixture-run', '--no-remote', '--framework-revision', revision,
  '--unity', unity, '--evidence-root', evidence];
async function assertCleaned(evidence, unity) {
  const cleanup = JSON.parse(await readFile(path.join(evidence, 'cleanup.json')));
  assert.equal(cleanup.removed, true); assert.deepEqual(cleanup.residualPaths, []);
  await assert.rejects(lstat(cleanup.projectPath), /ENOENT/);
  const result = JSON.parse(await readFile(path.join(evidence, 'unity-result.json')));
  assert.equal(spawnSync('ps', ['-p', `${result.process.pid}`, '-o', 'command='], { encoding: 'utf8' }).stdout.includes(unity), false);
  return { cleanup, result };
}

async function writeFakeUnity(value) {
  const unity = path.join(value.bin, 'FakeUnity');
  const script = `#!/bin/sh
while [ "$#" -gt 0 ]; do
  case "$1" in
    -projectPath) project=$2; shift 2;; -testResults) results=$2; shift 2;;
    -logFile) log=$2; shift 2;; -nographics|-quit) exit 88;; *) shift;;
  esac
done
sleep 0.2
if [ "\${FAKE_IGNORE_TERM:-0}" = 1 ]; then trap '' TERM; fi
if [ "\${FAKE_CACHE_ROOT_SYMLINK:-0}" = 1 ]; then mkdir -p "$project/Library" "$FAKE_CACHE_TARGET"; ln -s "$FAKE_CACHE_TARGET" "$project/Library/PackageCache"; fi
mkdir -p "$project/Library/PackageCache/com.zwebglframework.runtime@1111111111/Runtime"
if [ "\${FAKE_SKIP_UNITASK:-0}" != 1 ]; then mkdir -p "$project/Library/PackageCache/com.cysharp.unitask@e5acc106ee/Runtime"; fi
cat > "$project/Packages/packages-lock.json" <<'JSON'
{"dependencies":{"com.cysharp.unitask":{"version":"https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#e5acc106ee196bc5a32fb14cdf2987b0f96d11e0","depth":0,"source":"git","dependencies":{},"hash":"e5acc106ee196bc5a32fb14cdf2987b0f96d11e0"},"com.zwebglframework.runtime":{"version":"https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.runtime#${revision}","depth":0,"source":"git","dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.unitywebrequest":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.physics":"1.0.0"},"hash":"${revision}"}}}
JSON
printf '{"name":"com.zwebglframework.runtime","version":"0.1.0"}\n' > "$project/Library/PackageCache/com.zwebglframework.runtime@1111111111/package.json"
printf 'public class Runtime {}\n' > "$project/Library/PackageCache/com.zwebglframework.runtime@1111111111/Runtime/Runtime.cs"
if [ "\${FAKE_SKIP_UNITASK:-0}" != 1 ]; then
  printf '{"name":"com.cysharp.unitask","version":"2.5.11"}\n' > "$project/Library/PackageCache/com.cysharp.unitask@e5acc106ee/package.json"
  printf 'public class UniTask {}\n' > "$project/Library/PackageCache/com.cysharp.unitask@e5acc106ee/Runtime/UniTask.cs"
fi
if [ "\${FAKE_IVT:-0}" = 1 ]; then printf '[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ZWebGLFrameWork.PublicApiContract.Tests")]\n' > "$project/Library/PackageCache/com.zwebglframework.runtime@1111111111/Runtime/Friend.cs"; fi
if [ "\${FAKE_SYMLINK:-0}" = 1 ]; then ln -s Runtime.cs "$project/Library/PackageCache/com.zwebglframework.runtime@1111111111/Runtime/Linked.cs"; fi
if [ "\${FAKE_DUPLICATE:-0}" = 1 ]; then mkdir -p "$project/Library/PackageCache/com.zwebglframework.runtime@duplicate"; fi
cat > "$results" <<'XML'
<test-run result="Passed" total="7" passed="7" failed="0" inconclusive="0" skipped="0">
<test-case fullname="ZWebGLFrameWork.PublicApiContract.Tests.CommandRecorderPublicContractTests.BufferedRecord_MatchesTickResultCommandsAndTerminalFields" result="Passed" />
<test-case fullname="ZWebGLFrameWork.PublicApiContract.Tests.CommandRecorderPublicContractTests.BufferedRecord_OwnsCommandsAcrossParentClearReuseAndDispose" result="Passed" />
<test-case fullname="ZWebGLFrameWork.PublicApiContract.Tests.CommandRecorderPublicContractTests.BufferedRecord_RejectsInvalidOutputsWithoutReplayMutation" result="Passed" />
<test-case fullname="ZWebGLFrameWork.PublicApiContract.Tests.SessionPresentationBridgePublicContractTests.BufferedApply_BufferReuseUpdatesOnlyAfterExplicitApply" result="Passed" />
<test-case fullname="ZWebGLFrameWork.PublicApiContract.Tests.SessionPresentationBridgePublicContractTests.BufferedApply_OutputClearDoesNotChangeLaterSample" result="Passed" />
<test-case fullname="ZWebGLFrameWork.PublicApiContract.Tests.SessionPresentationBridgePublicContractTests.BufferedApply_OutputDisposePreservesConsumedValuesAndViews" result="Passed" />
<test-case fullname="ZWebGLFrameWork.PublicApiContract.Tests.SessionPresentationBridgePublicContractTests.BufferedApply_PublishedOutputCreatesAndSamplesView" result="Passed" />
</test-run>
XML
printf 'Unity Editor Version: 2022.3.62f3c1\nGfxDevice: creating device client\nMetal devices available: 1\nUsing device Apple M3 Pro (high power)\nInitializing Metal device caps: Apple M3 Pro\nTest run completed. Exiting with code 0 (Ok). Run completed.\n' > "$log"
if [ "\${FAKE_HANG:-0}" = 1 ]; then while :; do :; done; fi
exit 0
`;
  await writeFile(unity, script); await chmod(unity, 0o755);
  const syntax = spawnSync('/bin/sh', ['-n', unity], { encoding: 'utf8' });
  assert.equal(syntax.status, 0, `${syntax.stderr}\n${script.split('\n').map((line, index) => `${index + 1}: ${line}`).join('\n')}`);
  return unity;
}

test('dry-run is fixture-only, deterministic, cleaned, and no-remote', async t => {
  const value = await fixture(t); const first = path.join(value.top, 'first');
  const second = path.join(value.top, 'second');
  for (const output of [first, second]) {
    const result = run(['--dry-run', '--no-remote', '--framework-revision', revision,
      '--fixture-output', output], value.env);
    assert.equal(result.status, 0, result.stderr);
    const marker = JSON.parse(await readFile(path.join(output, 'fixture-only.json')));
    assert.deepEqual(marker, { schemaVersion: 1, classification: 'fixture-only',
      formalReleaseEvidence: false, networkInvoked: false, unityInvoked: false,
      frameworkRevision: revision });
    const cleanup = JSON.parse(await readFile(path.join(output, 'cleanup.json')));
    assert.equal(cleanup.removed, true); assert.deepEqual(cleanup.residualPaths, []);
    await assert.rejects(readFile(cleanup.projectPath), /ENOENT/);
    const manifest = JSON.parse(await readFile(path.join(output, 'manifest.json')));
    assert.deepEqual(manifest.dependencies, {
      'com.cysharp.unitask': 'https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#e5acc106ee196bc5a32fb14cdf2987b0f96d11e0',
      'com.unity.test-framework': '1.4.6',
      'com.zwebglframework.runtime': `https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.runtime#${revision}`,
    });
    assert.deepEqual((await readdir(output)).sort(), ['cleanup.json', 'contract-asmdef.json',
      'copied-sources.sha256', 'fixture-only.json', 'manifest.json']);
  }
  assert.equal(await readFile(path.join(first, 'manifest.json'), 'utf8'),
    await readFile(path.join(second, 'manifest.json'), 'utf8'));
  assert.equal(await readFile(path.join(first, 'contract-asmdef.json'), 'utf8'),
    await readFile(path.join(second, 'contract-asmdef.json'), 'utf8'));
});

test('dry-run requires no-remote, full SHA, and fixture output', async t => {
  const value = await fixture(t);
  for (const args of [
    ['--dry-run', '--framework-revision', revision, '--fixture-output', path.join(value.top, 'a')],
    ['--dry-run', '--no-remote', '--framework-revision', 'main', '--fixture-output', path.join(value.top, 'b')],
    ['--dry-run', '--no-remote', '--framework-revision', revision],
  ]) assert.notEqual(run(args, value.env).status, 0);
});

test('fixture Unity flow captures inputs and removes its consumer', async t => {
  const value = await fixture(t); const unity = await writeFakeUnity(value);
  const evidence = path.join(value.top, 'evidence');
  const result = run(fixtureArgs(unity, evidence), value.env);
  const unityStderr = await readFile(path.join(evidence, 'unity.stderr.txt'), 'utf8').catch(() => '');
  assert.equal(result.status, 0, `${result.stderr}\n${result.stdout}\n${unityStderr}`);
  assert.match(await readFile(path.join(evidence, 'validator.txt'), 'utf8'), /PASS fixture-only/);
  const { cleanup, result: unityResult } = await assertCleaned(evidence, unity);
  assert.equal(await readFile(path.join(evidence,
    'resolved-package.json'), 'utf8'), '{"name":"com.zwebglframework.runtime","version":"0.1.0"}\n');
  assert.deepEqual(JSON.parse(await readFile(path.join(evidence, 'input.json'))), { schemaVersion: 1,
    frameworkRevision: revision, access: 'fixture-only', fixtureOnly: true, formalReleaseEvidence: false,
    initialLibraryPresent: false, cacheIsolation: 'global-cache-observed' });
  const resolved = JSON.parse(await readFile(path.join(evidence, 'resolved-source.json')));
  assert.deepEqual(Object.keys(resolved.packages).sort(),
    ['com.cysharp.unitask', 'com.zwebglframework.runtime']);
  for (const value of Object.values(resolved.packages)) {
    assert.equal(value.cacheMatches, 1); assert.equal(value.symlink, false);
    assert.equal(value.cacheIsolation, 'global-cache-observed');
    assert.deepEqual(value.friendGrants, []); assert.match(value.inventorySha256, /^[a-f0-9]{64}$/);
    assert.ok(value.fileCount >= 2); await readFile(path.join(evidence, value.inventoryFile));
  }
  assert.equal(unityResult.lifecycle.watchdogCount, 0);
  assert.equal(unityResult.lifecycle.signalCount, 0);
  assert.equal(unityResult.lifecycle.completionMarkerCount, 1);
  assert.equal(unityResult.lifecycle.completionSource, 'xml-mtime-post-exit');
  assert.equal(unityResult.process.observedCwd, cleanup.projectPath);
  assert.deepEqual(unityResult.graphics, { backend: 'Metal', device: 'Apple M3 Pro (high power)' });
});

test('fixture Unity flow rejects a real public-contract friend grant', async t => {
  const value = await fixture(t); const unity = await writeFakeUnity(value);
  const evidence = path.join(value.top, 'evidence');
  const result = run(fixtureArgs(unity, evidence), { ...value.env, FAKE_IVT: '1' });
  assert.notEqual(result.status, 0);
  assert.match(await readFile(path.join(evidence, 'validator.txt'), 'utf8'),
    /Public contract friend access detected/);
  await assertCleaned(evidence, unity);
});

test('fixture Unity flow rejects invalid cache trees and cleans without following links', async t => {
  for (const [flag, diagnostic] of [['FAKE_SKIP_UNITASK', /Expected one resolved cache for com\.cysharp\.unitask/],
    ['FAKE_DUPLICATE', /Expected one resolved cache for com\.zwebglframework\.runtime; found 2/],
    ['FAKE_SYMLINK', /Resolved package symlink/],
    ['FAKE_CACHE_ROOT_SYMLINK', /Library\/PackageCache must be a project-local directory/]]) {
    const value = await fixture(t); const unity = await writeFakeUnity(value);
    const evidence = path.join(value.top, 'evidence'); const external = path.join(value.top, 'external-cache');
    const result = run(fixtureArgs(unity, evidence), { ...value.env, [flag]: '1', FAKE_CACHE_TARGET: external });
    assert.notEqual(result.status, 0);
    assert.match(await readFile(path.join(evidence, 'resolved-capture.txt'), 'utf8'), diagnostic);
    await assertCleaned(evidence, unity);
    if (flag === 'FAKE_CACHE_ROOT_SYMLINK') assert.match(await readFile(path.join(external,
      'com.zwebglframework.runtime@1111111111/package.json'), 'utf8'), /com\.zwebglframework\.runtime/);
  }
});

test('fixture Unity lifecycle watchdog safely reaps TERM and KILL cases', async t => {
  for (const [ignoreTerm, signals] of [[false, 1], [true, 2]]) {
    const value = await fixture(t); const unity = await writeFakeUnity(value); const evidence = path.join(value.top, 'evidence');
    const result = run(fixtureArgs(unity, evidence), { ...value.env, FAKE_HANG: '1',
      FAKE_IGNORE_TERM: ignoreTerm ? '1' : '0', FRAMELOOM_FRESH_EXIT_GRACE_SECONDS: '1',
      FRAMELOOM_FRESH_TERM_GRACE_SECONDS: '1', FRAMELOOM_FRESH_POLL_SECONDS: '1' });
    assert.notEqual(result.status, 0); const checked = await assertCleaned(evidence, unity);
    assert.equal(checked.result.lifecycle.watchdogCount, 1);
    assert.equal(checked.result.lifecycle.signalCount, signals);
    assert.equal(checked.result.lifecycle.watchdogReason, 'post-completion-timeout');
  }
});
