import assert from 'node:assert/strict'; import { chmod, copyFile, lstat, mkdir, mkdtemp, readFile, realpath, rm, symlink, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process'; import os from 'node:os'; import path from 'node:path'; import test from 'node:test';
import { validateGates } from './release-evidence.mjs'; const repository = path.resolve(new URL('../..', import.meta.url).pathname), driver = path.join(repository, 'scripts/oss/run-required-gates.sh');
const registryPath = path.join(repository, 'ci/oss/gates.json'), revision = '1'.repeat(40), approvedProfile = '(version 1)\n(allow default)\n(deny network-outbound (remote ip "*:*"))\n(allow network-outbound (remote ip "localhost:*"))\n', childProxyProfile = '(version 1)\n(allow default)\n(deny network-outbound (remote ip "localhost:1082"))\n';
async function fixture(t) {
  const top = await realpath(await mkdtemp(path.join(os.tmpdir(), 'frameloom-required-gates-')));
  t.after(() => rm(top, { recursive: true, force: true }));
  const bin = path.join(top, 'bin'); await mkdir(bin);
  const unity = path.join(bin, 'FakeUnity');
  await writeFile(unity, '#!/bin/sh\n[ -z "${FRAMELOOM_UNITY_MARKER:-}" ] || printf invoked\\n >> "$FRAMELOOM_UNITY_MARKER"\nexit 99\n'); await chmod(unity, 0o755);
  const executor = path.join(bin, 'fixture-gate');
  await writeFile(executor, `#!/bin/sh
set -eu
id=$1; output=$2; revision=$3; unity=$4
test "\${#revision}" -eq 40
test -x "$unity"
printf '%s\n' "$id" >> "$FRAMELOOM_EXECUTOR_LOG"
printf 'gate=%s\nrevision=%s\n' "$id" "$revision" > "$output"
if [ "\${FRAMELOOM_FAIL_GATE:-}" = "$id" ]; then
  printf 'result=FAIL\n' >> "$output"
  exit 7
fi
printf 'result=PASS\n' >> "$output"
`);
  await chmod(executor, 0o755);
  return { top, unity, executor, log: path.join(top, 'executor.log') };
}
function run(value, evidence, extra = [], env = {}) {
  return spawnSync(driver, ['--fixture-run', '--phase', 'package-freeze', '--revision', revision,
    '--unity', value.unity, '--evidence-root', evidence, '--executor', value.executor, ...extra], {
    cwd: repository, encoding: 'utf8', env: { ...process.env, FRAMELOOM_EXECUTOR_LOG: value.log, ...env },
  });
}
async function rows(evidence) { return (await readFile(path.join(evidence, 'fixture-results.ndjson'), 'utf8')).trim().split('\n').map(JSON.parse); }
function semanticShell(source) { return source.replace(/\\\r?\n/g, ' ').replace(/\s+/g, ' ').trim(); }
async function watchdogFixture(t, ignoreTerm, profile) { const top = await realpath(await mkdtemp(path.join(os.tmpdir(), 'frameloom-watchdog-')));
  t.after(() => rm(top, { recursive: true, force: true }));
  const repo = path.join(top, 'repo'); const scripts = path.join(repo, 'scripts/oss');
  const ci = path.join(repo, 'ci/oss'); const bin = path.join(repo, 'bin');
  await mkdir(scripts, { recursive: true }); await mkdir(ci, { recursive: true }); await mkdir(bin);
  const runner = path.join(scripts, 'run-required-gates.sh');
  await copyFile(driver, runner); await chmod(runner, 0o755);
  await copyFile(path.join(repository, 'scripts/oss/release-evidence.mjs'), path.join(scripts, 'release-evidence.mjs'));
  await copyFile(registryPath, path.join(ci, 'gates.json'));
  const unity = path.join(repo, 'FakeUnity');
  const source = path.join(repo, 'fake-unity.c');
  await writeFile(source, `#include <signal.h>\n#include <unistd.h>\nstatic void stop(int value){(void)value;_exit(143);}\nint main(void){signal(SIGTERM,${ignoreTerm ? 'SIG_IGN' : 'stop'});for(;;)pause();}\n`);
  assert.equal(spawnSync('/usr/bin/clang', ['-arch', 'arm64', source, '-o', unity]).status, 0); await rm(source);
  for (const tool of ['dotnet', 'gitleaks']) { const stub = path.join(bin, tool); await writeFile(stub, '#!/bin/sh\nexit 0\n'); await chmod(stub, 0o755); }
  assert.equal(spawnSync('git', ['init', '-q'], { cwd: repo }).status, 0);
  assert.equal(spawnSync('git', ['add', '.'], { cwd: repo }).status, 0);
  assert.equal(spawnSync('git', ['-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.test', 'commit', '-qm', 'fixture'], { cwd: repo }).status, 0);
  const head = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: repo, encoding: 'utf8' }).stdout.trim();
  const evidence = path.join(top, 'evidence'), profilePath = path.join(top, 'unity.sb'), args = ['--phase', 'package-freeze', '--revision', head, '--unity', unity, '--evidence-root', evidence]; if (profile) { await writeFile(profilePath, profile); args.push('--sandbox-profile', profilePath); }
  const result = spawnSync(runner, args, {
    cwd: top, encoding: 'utf8', timeout: 15_000,
    env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, FRAMELOOM_GATE_TOTAL_TIMEOUT_SECONDS: '2', FRAMELOOM_GATE_EXIT_GRACE_SECONDS: '1', FRAMELOOM_GATE_POLL_SECONDS: '1', FRAMELOOM_GATE_TERM_GRACE_SECONDS: '1' },
  });
  return { evidence, result, profile, unity, repo };
}
test('formal Unity commands use the bounded lifecycle and macOS TestRunner contract', async () => {
  const source = await readFile(driver, 'utf8'); const shell = semanticShell(source);
  const branch = shell.match(/if \[ "\$kind" = test \]; then args=\((.*?)\); \[ -z "\$filter" \]/);
  assert.ok(branch, 'missing TestRunner argument branch'); assert.match(branch[1], /-batchmode -runTests/); assert.doesNotMatch(branch[1], /-nographics|-quit/);
  for (const contract of [
    /& U_PID=\$!/, /started\+TOTAL_TIMEOUT/, /complete\+EXIT_GRACE/,
    /\[ "\$start" = "\$U_START" \]/, /\[ "\$command" = "\$U_COMMAND" \]/,
    /\[ "\$parent" = "\$U_PARENT" \]/, /\[ "\$cwd" = "\$U_CWD" \]/,
    /kill -TERM "\$U_PID"/, /kill -KILL "\$U_PID"/, /wait "\$U_PID"/,
    /\[ "\$watchdog" -eq 0 \].*\[ "\$SIGNALS" -eq 0 \].*\[ "\$rc" -eq 0 \] \|\| return 1/,
    /postExitAdjudicated/, /Metal devices available:/, /JobSystem::ShutdownJobThreads/,
  ]) assert.match(shell, contract);
  assert.ok(source.trimEnd().split('\n').length <= 200, 'runner exceeds 200 lines'); assert.notEqual((await lstat(driver)).mode & 0o111, 0, 'runner is not executable');
});
test('formal Apple Silicon preflight accepts only a native thin arm64 Unity editor', async () => {
  const source = await readFile(driver, 'utf8'), shell = semanticShell(source);
  const predicate = source.match(/native_unity_ok\(\) \{[\s\S]*?\n\}/); assert.ok(predicate, 'missing architecture predicate');
  const cases = [
    [['Darwin', '1', 'arm64', 'Mach-O 64-bit executable arm64'], true],
    [['Darwin', '1', 'x86_64', 'Mach-O 64-bit executable x86_64'], false],
    [['Darwin', '1', 'x86_64 arm64', 'Mach-O universal binary with 2 architectures'], false],
    [['Darwin', '2', 'arm64', 'Mach-O 64-bit executable arm64'], false],
    [['Linux', 'not-applicable', 'not-applicable', 'ELF 64-bit executable'], true],
  ];
  for (const [args, accepted] of cases) { const result = spawnSync('bash', ['-c', `${predicate[0]}\nnative_unity_ok "$@"`, 'gate', ...args]); assert.equal(result.status === 0, accepted, args.join('|')); }
  for (const contract of [/\/usr\/bin\/uname -s/, /\/usr\/sbin\/sysctl -in hw\.optional\.arm64/, /\/usr\/sbin\/sysctl -n hw\.machine/, /\/usr\/bin\/lipo -archs "\$UNITY"/, /\/usr\/bin\/file -b "\$UNITY"/, /unity-architecture\.txt/, /unity-\*\|external-public-contracts\|editor-mono-allocation\|simulation-soak\|webgl-build-browser-smoke/, /webgl-build-browser-smoke\).*--evidence-root "\$dir\/smoke"/]) assert.match(shell, contract);
});
test('watchdog sends TERM and escalates to KILL only for an identity-confirmed live process', { skip: process.platform !== 'darwin' }, async t => {
  for (const profile of [null, approvedProfile, childProxyProfile]) for (const [ignoreTerm, expected] of [[false, ['TERM']], [true, ['TERM', 'KILL']]]) {
    const value = await watchdogFixture(t, ignoreTerm, profile); assert.equal(value.result.status, 1, value.result.stderr);
    const gate = path.join(value.evidence, 'unity-compile');
    if (profile) { const snapshot = path.join(gate, 'sandbox-profile.sb'); assert.equal(await readFile(snapshot, 'utf8'), profile); assert.equal((await lstat(snapshot)).mode & 0o777, 0o444); assert.equal(spawnSync('shasum', ['-a', '256', '-c', 'sandbox-profile.sha256'], { cwd: gate }).status, 0); const argv = JSON.parse(await readFile(path.join(gate, 'unity-launcher-argv.json'), 'utf8')); assert.deepEqual(argv.slice(0, 3), ['/usr/bin/sandbox-exec', '-f', snapshot]); assert.equal(argv[3], value.unity); assert.ok(argv.includes(value.repo)); }
    assert.match(await readFile(path.join(gate, 'process-samples.txt'), 'utf8'), /^epoch=.* pid=.* ppid=.* cwd=/m);
    assert.match(await readFile(path.join(gate, 'watchdog.txt'), 'utf8'), /^watchdog=total-timeout$/m);
    const signals = (await readFile(path.join(gate, 'signal-ledger.txt'), 'utf8')).trim().split('\n');
    assert.deepEqual(signals.map(line => line.match(/^signal=(TERM|KILL).*identity=confirmed$/)?.[1]), expected);
    const lifecycle = await readFile(path.join(gate, 'lifecycle.txt'), 'utf8');
    assert.match(lifecycle, /watchdog=1\nreason=total-timeout/);
    assert.match(lifecycle, new RegExp(`signals=${expected.length}$`, 'm'));
  }
});
test('mixed SimulationCore TRX must contain the exact approved class set', async () => {
  const source = await readFile(driver, 'utf8'); const shell = semanticShell(source);
  const assignment = source.match(/^MIXED_CLASSES='([^']+)'$/m); assert.ok(assignment);
  assert.deepEqual(assignment[1].split(' '), ['ContentManifestGoldenTests', 'DeterministicRandomCompatibilityTests',
    'LogicIntCompatibilityTests', 'ModeBehaviorGoldenTests', 'SessionCommandTests', 'SessionDeterminismTests', 'ModeBehaviorTests', 'CoreTargetingTests', 'SimSystemGroupTests', 'SessionTerminalTests']);
  assert.match(shell, /xml\.etree\.ElementTree/);
  assert.match(shell, /expected=set\(sys\.argv\[2:\]\)/);
  assert.match(shell, /attrib\["className"\]/);
  assert.match(shell, /missing=.*extra=/);
  assert.match(shell, /validate-trx\.py.*--minimum 132.*simulation\.trx.*\$MIXED_CLASSES/);
});
test('package structure rejects filesystem and Git-index runtime symlinks', async () => {
  const shell = semanticShell(await readFile(driver, 'utf8'));
  assert.match(shell, /find "\$runtime" -type l -print -quit/);
  assert.match(shell, /git -C "\$ROOT" ls-files -s -- Packages\/com\.zwebglframework\.runtime\/Runtime/);
  assert.match(shell, /\$1=="120000"/);
  for (const name of ['README.md.meta', 'CHANGELOG.md.meta', 'LICENSE.meta', 'THIRD_PARTY_NOTICES.md.meta']) assert.ok(shell.includes(`Packages/com.zwebglframework.runtime/${name}`));
});
test('vendor contract accepts the wrapped governance classification', () => { const result = spawnSync('bash', [path.join(repository, 'scripts/vendor-nativewebsocket.test.sh')], { cwd: repository, encoding: 'utf8' }); assert.equal(result.status, 0, result.stdout + result.stderr); });
test('P diff gate pins the append-only corrections and exact non-merge candidate', async t => {
  const source = await readFile(driver, 'utf8'), assignments = [...source.matchAll(/\bP_([A-Z0-9_]+)=(?:'([^']*)'|([a-f0-9]{40}))/g)], values = Object.fromEntries(assignments.map(match => [match[1], match[2] ?? match[3]]));
  const paths = values.PATHS.split(' '), expectedPaths = ['docs/superpowers/plans/2026-08-25-oss-prep-001/05-package-freeze.md', 'scripts/oss/run-required-gates.sh', 'scripts/oss/run-required-gates.test.mjs', 'scripts/oss/run-webgl-smoke.sh', 'scripts/oss/run-webgl-smoke.test.mjs'], setup = assignments.map(match => match[0]).join('\n');
  assert.deepEqual([values.FOUNDATION, values.STAGE4, values.REPAIR, values.DESIGN, values.PLAN, values.PREFIX, values.SMOKE, values.LINK_DESIGN, values.LINK_PLAN, values.LINKER, values.CANDIDATE3, values.CANDIDATE4], ['61add622c04ad72494b5da785e542b984ec18df4', 'ef4abbee5afe6891ccc570eefcd1842d35ef95dd', '76f009686196a459a9bbea6dec8f192fa2d4a09a', 'f98478a960e7f68b6294dd8c8af1bc48877542f4', '7a0d7f4f096fc21ed4fef4e1efa638adc987659e', '27cbe4486bb53adc99a8a7f7753824e1b519a474', 'f9b0641f2f7347ec20ff248931b13cc8ff00f5c8', '67eef1c27c7f3e6beb577eb47062ba12276cb212', '5a22c3d0d89972e69736071e488d0b9576a4236b', '0297b6a66e306968aea5d2c2a42e0750b5cf256f', '3ab06b8c5220c047fa56db7da7264f2596a558b9', '66db9eb90df30095ca2d669e158f0e097f7629ff']);
  assert.deepEqual(values.STAGE4_PATHS.split(' '), ['.github/workflows/ci.yml', 'ci/oss/gates.json', 'scripts/ci/workflow-contract.test.mjs', 'scripts/oss/audit-history.mjs', 'scripts/oss/audit-history.test.mjs', 'scripts/oss/compare-package-content.mjs', 'scripts/oss/compare-package-content.test.mjs', 'scripts/oss/release-evidence.mjs', 'scripts/oss/release-evidence.test.mjs', 'scripts/oss/run-required-gates.sh', 'scripts/oss/run-required-gates.test.mjs', 'scripts/oss/run-webgl-smoke.sh', 'scripts/oss/run-webgl-smoke.test.mjs']);
  assert.deepEqual([values.REPAIR_PATHS, values.DESIGN_PATHS, values.PLAN_PATHS, values.PREFIX_PATHS, values.SMOKE_PATHS, values.LINK_DESIGN_PATHS, values.LINK_PLAN_PATHS, values.LINKER_PATHS], ['scripts/oss/run-required-gates.sh scripts/oss/run-required-gates.test.mjs scripts/vendor-nativewebsocket.test.sh', 'docs/superpowers/specs/2026-08-14-oss-prep-001-design/05-release-evidence.md', 'docs/superpowers/plans/2026-08-25-oss-prep-001/01-baseline.md', 'scripts/oss/run-required-gates.sh scripts/oss/run-required-gates.test.mjs', 'scripts/oss/run-required-gates.sh scripts/oss/run-required-gates.test.mjs', 'docs/superpowers/specs/2026-08-14-oss-prep-001-design/05-release-evidence.md', 'docs/superpowers/plans/2026-08-25-oss-prep-001/04-governance-validation.md', 'Assets/Tests/Editor/RepositoryIntegrityTests.cs Packages/com.zwebglframework.runtime/Runtime/FSM/Game/GameLauncher.cs Packages/com.zwebglframework.runtime/Runtime/Network/Native/NativeRealtimeChannelFactory.cs Packages/com.zwebglframework.runtime/Runtime/Network/WebGL/WebGlRealtimeChannelFactory.cs Packages/com.zwebglframework.runtime/Runtime/UI/Diagnostics/SmokeReadinessReporter.cs']);
  assert.deepEqual(paths, expectedPaths);
  assert.deepEqual([values.CANDIDATE3_PATHS.split(' '), values.CANDIDATE4_PATHS.split(' ')], [['scripts/oss/run-required-gates.sh', 'scripts/oss/run-required-gates.test.mjs', 'scripts/oss/run-webgl-smoke.sh', 'scripts/oss/run-webgl-smoke.test.mjs'], expectedPaths]);
  const gate = source.match(/checkpoint_gate\(\) \{[\s\S]*?\n\}\np_diff_gate\(\) \{[\s\S]*?\n\}/)[0]; const git = (root, args) => spawnSync('git', args, { cwd: root, encoding: 'utf8' }); const prefix = values.CANDIDATE4;
  for (const mode of ['valid', 'extra', 'missing', 'non-direct', 'merge']) {
    const top = await realpath(await mkdtemp(path.join(os.tmpdir(), `frameloom-diff-${mode}-`))); t.after(() => rm(top, { recursive: true, force: true })); const repo = path.join(top, 'repo');
    assert.equal(spawnSync('git', ['clone', '-q', '--no-hardlinks', repository, repo]).status, 0); assert.equal(git(repo, ['config', 'user.name', 'Fixture']).status, 0); assert.equal(git(repo, ['config', 'user.email', 'fixture@example.test']).status, 0);
    assert.equal(git(repo, ['checkout', '-q', '--detach', prefix]).status, 0); if (mode === 'non-direct') assert.equal(git(repo, ['commit', '--allow-empty', '-qm', 'intermediate']).status, 0);
    const selected = mode === 'missing' ? paths.slice(1) : paths; for (const name of selected) { const file = path.join(repo, name); await mkdir(path.dirname(file), { recursive: true }); await writeFile(file, `${await readFile(file, 'utf8').catch(() => '')}\nfixture\n`); }
    if (mode === 'extra') await writeFile(path.join(repo, 'extra.txt'), 'extra\n'); assert.equal(git(repo, ['add', '-A']).status, 0); assert.equal(git(repo, ['commit', '-qm', 'candidate']).status, 0);
    const side = git(repo, ['rev-parse', 'HEAD']).stdout.trim(); if (mode === 'merge') { assert.equal(git(repo, ['checkout', '-q', '--detach', prefix]).status, 0); assert.equal(git(repo, ['merge', '-q', '--no-ff', '-m', 'merge', side]).status, 0); }
    const revision = git(repo, ['rev-parse', 'HEAD']).stdout.trim(); const evidence = path.join(top, 'evidence'); await mkdir(evidence);
    const result = spawnSync('bash', ['-c', `${setup}\n${gate}\nROOT=$1 REVISION=$2; p_diff_gate "$3"`, 'gate', repo, revision, evidence], { encoding: 'utf8' });
    assert.equal(result.status === 0, mode === 'valid', `${mode}: ${result.stdout}${result.stderr}`);
    if (mode !== 'valid') assert.match(result.stdout, ['extra', 'missing'].includes(mode) ? /candidate path mismatch/ : /candidate must be the non-merge direct child/);
  }
});
test('fixture runner executes every applicable Required gate and cannot pose as formal evidence', async t => {
  const value = await fixture(t); const evidence = path.join(value.top, 'evidence');
  const result = run(value, evidence); assert.equal(result.status, 0, result.stderr);
  const registry = JSON.parse(await readFile(registryPath, 'utf8')); const actual = await rows(evidence);
  assert.match(validateGates(registry, actual, 'package-freeze').join('\n'), /Invalid terminal evidence/);
  const expected = registry.gates.filter(gate => gate.milestone === 'package-freeze' && gate.required)
    .map(gate => gate.id);
  assert.deepEqual((await readFile(value.log, 'utf8')).trim().split('\n'), expected);
  assert.equal(actual.length, registry.gates.length);
  assert.ok(actual.filter(row => expected.includes(row.id)).every(row =>
    row.status === 'PASS' && row.evidenceScope === 'fixture' && /^[a-f0-9]{64}$/.test(row.digest) && row.evidenceFile.endsWith('.digest')));
  assert.match(await readFile(path.join(evidence, actual.find(row => row.status === 'PASS').evidenceFile), 'utf8'), /classification=fixture-only\nformalReleaseEvidence=false/);
  assert.deepEqual(JSON.parse(await readFile(path.join(evidence, 'fixture-only.json'))), {
    schemaVersion: 1, classification: 'fixture-only', formalReleaseEvidence: false,
    phase: 'package-freeze', revision,
  });
  await assert.rejects(lstat(path.join(evidence, 'results.ndjson')), /ENOENT/);
});
test('first Required failure is recorded and stops later applicable gates', async t => {
  const value = await fixture(t); const evidence = path.join(value.top, 'failed');
  const result = run(value, evidence, [], { FRAMELOOM_FAIL_GATE: 'unity-editmode' });
  assert.notEqual(result.status, 0); const actual = await rows(evidence);
  assert.equal(actual.find(row => row.id === 'unity-editmode').status, 'FAIL');
  assert.equal(actual.find(row => row.id === 'unity-playmode').status, 'NOT RUN');
  assert.deepEqual((await readFile(value.log, 'utf8')).trim().split('\n'),
    ['unity-compile', 'unity-editmode']);
  assert.match(result.stderr, /Required gate failed: unity-editmode/);
});
test('rejects unsafe fixture and production arguments before execution', async t => {
  const value = await fixture(t); const inside = path.join(repository, 'gate-evidence-forbidden');
  const existing = path.join(value.top, 'existing'); await mkdir(existing); await writeFile(path.join(existing, 'stale'), 'x');
  const target = path.join(value.top, 'target'); await mkdir(target); const link = path.join(value.top, 'link'); await symlink(target, link);
  for (const [args, diagnostic] of [
    [['--fixture-run', '--phase', 'unknown', '--revision', revision, '--unity', value.unity, '--evidence-root', path.join(value.top, 'a'), '--executor', value.executor], /Unsupported phase/],
    [['--fixture-run', '--phase', 'package-freeze', '--revision', 'main', '--unity', value.unity, '--evidence-root', path.join(value.top, 'b'), '--executor', value.executor], /full lowercase 40-character SHA/],
    [['--fixture-run', '--phase', 'package-freeze', '--revision', revision, '--unity', value.unity, '--evidence-root', inside, '--executor', value.executor], /outside the repository/],
    [['--fixture-run', '--phase', 'package-freeze', '--revision', revision, '--unity', value.unity, '--evidence-root', existing, '--executor', value.executor], /new or empty/],
    [['--fixture-run', '--phase', 'package-freeze', '--revision', revision, '--unity', value.unity, '--evidence-root', link, '--executor', value.executor], /canonical directory/],
    [['--phase', 'package-freeze', '--revision', revision, '--unity', value.unity, '--evidence-root', path.join(value.top, 'c'), '--executor', value.executor], /fixture-run/],
  ]) {
    const result = spawnSync(driver, args, { cwd: repository, encoding: 'utf8' });
    assert.notEqual(result.status, 0, args.join(' '));
    assert.match(result.stderr, diagnostic);
  }
  await assert.rejects(lstat(inside), /ENOENT/);
});
test('sandbox profile validation precedes evidence and rejects fixture use', { skip: process.platform !== 'darwin' }, async t => {
  const value = await fixture(t), bad = path.join(value.top, 'bad.sb'), good = path.join(value.top, 'good.sb'), missing = path.join(value.top, 'missing.sb'), marker = path.join(value.top, 'unity-invoked');
  const target = path.join(value.top, 'profiles'); await mkdir(target); const nested = path.join(target, 'nested.sb'); await writeFile(nested, approvedProfile);
  const direct = path.join(value.top, 'direct.sb'), ancestor = path.join(value.top, 'ancestor'); await symlink(good, direct); await symlink(target, ancestor); await writeFile(bad, '(version 1)\n(allow default)\n'); await writeFile(good, approvedProfile);
  const cases = [[['--sandbox-profile'], /Missing value/], [['--sandbox-profile', '--revision'], /Missing value/], [['--sandbox-profile', 'relative.sb'], /canonical and absolute/], [['--sandbox-profile', missing], /regular file/], [['--sandbox-profile', value.top], /regular file/], [['--sandbox-profile', direct], /must not be symlinks/], [['--sandbox-profile', path.join(ancestor, 'nested.sb')], /must not be symlinks/], [['--sandbox-profile', driver], /outside the repository/], [['--sandbox-profile', bad], /approved Unity sandbox profile/], [['--sandbox-profile', ''], /Missing value/], [['--sandbox-profile', good, '--sandbox-profile', ''], /Duplicate argument/]];
  for (const [tail, diagnostic] of cases) { const evidence = path.join(value.top, `sandbox-${Math.random()}`), args = ['--phase', 'package-freeze', '--revision', revision, '--unity', value.unity, '--evidence-root', evidence, ...tail]; const result = spawnSync(driver, args, { cwd: repository, encoding: 'utf8', env: { ...process.env, FRAMELOOM_UNITY_MARKER: marker } }); assert.notEqual(result.status, 0); assert.match(result.stderr, diagnostic); await assert.rejects(lstat(evidence), /ENOENT/); }
  const fixtureEvidence = path.join(value.top, 'fixture-sandbox'), fixtureResult = run(value, fixtureEvidence, ['--sandbox-profile', good], { FRAMELOOM_UNITY_MARKER: marker }); assert.match(fixtureResult.stderr, /not supported with --fixture-run/); await assert.rejects(lstat(fixtureEvidence), /ENOENT/);
  const wrappedEvidence = path.join(value.top, 'wrapped'), wrapped = spawnSync('/usr/bin/sandbox-exec', ['-f', good, driver, '--phase', 'package-freeze', '--revision', revision, '--unity', value.unity, '--evidence-root', wrappedEvidence, '--sandbox-profile', good], { cwd: repository, encoding: 'utf8', env: { ...process.env, FRAMELOOM_UNITY_MARKER: marker } }); assert.notEqual(wrapped.status, 0); assert.match(wrapped.stderr, /Unity supervisor must run outside sandbox/); await assert.rejects(lstat(wrappedEvidence), /ENOENT/); await assert.rejects(lstat(marker), /ENOENT/);
  const childProxy = path.join(value.top, 'child-proxy.sb'), childProxyEvidence = path.join(value.top, 'child-proxy-evidence'); await writeFile(childProxy, childProxyProfile); const childProxyResult = spawnSync(driver, ['--phase', 'package-freeze', '--revision', revision, '--unity', value.unity, '--evidence-root', childProxyEvidence, '--sandbox-profile', childProxy], { cwd: repository, encoding: 'utf8' }); assert.notEqual(childProxyResult.status, 0); assert.match(childProxyResult.stderr, /Formal run requires revision at HEAD and a clean repository/);
  assert.match(await readFile(driver, 'utf8'), /process\.platform!=="darwin"/);
});
