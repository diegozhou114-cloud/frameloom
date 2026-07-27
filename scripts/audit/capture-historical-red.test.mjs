import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import {
  captureCompileReceipt,
  captureSourceReceipt,
} from './capture-historical-red.mjs';

const sha256 = value => createHash('sha256').update(value).digest('hex');

function git(root, args, encoding = 'utf8') {
  return execFileSync('git', args, { cwd: root, encoding }).trim();
}

async function fixture() {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-capture-red-'));
  const artifacts = await mkdtemp(path.join(os.tmpdir(), 'zweb-capture-red-artifacts-'));
  git(root, ['init', '--quiet']);
  git(root, ['config', 'user.email', 'audit@example.test']);
  git(root, ['config', 'user.name', 'Audit Test']);
  await mkdir(path.join(root, 'docs/plans'), { recursive: true });
  await mkdir(path.join(root, 'tests'), { recursive: true });
  await writeFile(path.join(root, 'docs/plans/simulation.md'), '# Simulation plan\n');
  await writeFile(path.join(root, 'tests/Existing.cs'), 'class Existing {}\n');
  git(root, ['add', '.']);
  git(root, ['commit', '--quiet', '-m', 'base']);
  const base = git(root, ['rev-parse', 'HEAD']);

  await writeFile(path.join(root, 'tests/Acceptance.cs'),
    'public class Acceptance { public void AcceptanceMethod() {} }\n');
  git(root, ['add', '.']);
  git(root, ['commit', '--quiet', '-m', 'fix']);
  const fix = git(root, ['rev-parse', 'HEAD']);
  await writeFile(path.join(root, 'tests/Acceptance.cs'),
    'public class Acceptance { public void AcceptanceMethod() {} } // revised\n');
  await writeFile(path.join(root, 'tests/Second.cs'), 'class Second {}\n');
  git(root, ['add', '.']);
  git(root, ['commit', '--quiet', '-m', 'second fix']);
  const secondFix = git(root, ['rev-parse', 'HEAD']);
  const headBranch = git(root, ['branch', '--show-current']);
  git(root, ['checkout', '--quiet', '-b', 'unreachable', base]);
  await writeFile(path.join(root, 'tests/Unreachable.cs'), 'class Unreachable {}\n');
  git(root, ['add', '.']);
  git(root, ['commit', '--quiet', '-m', 'unreachable']);
  const unreachable = git(root, ['rev-parse', 'HEAD']);
  git(root, ['checkout', '--quiet', headBranch]);

  const logPath = path.join(artifacts, 'compile.log');
  await writeFile(logPath, 'error CS0246: ISimBattleSession\n');
  return { root, artifacts, base, fix, secondFix, unreachable, logPath };
}

test('compile receipts reject exit zero, missing diagnostics, and changed logs', async () => {
  const value = await fixture();
  const output = path.join(value.artifacts, 'compile.json');
  const options = {
    root: value.root, revision: value.base, exitCode: 1, logPath: value.logPath,
    diagnostics: ['error CS0246', 'ISimBattleSession'],
    planCitation: 'docs/plans/simulation.md:1', command: ['Unity', '-batchmode'], output,
    evidenceRoot: value.artifacts,
  };

  const receipt = await captureCompileReceipt(options);
  assert.equal(receipt.logSha256, sha256(await readFile(value.logPath)));
  const concurrentOutput = path.join(value.artifacts, 'concurrent.json');
  const concurrent = await Promise.allSettled([
    captureCompileReceipt({ ...options, output: concurrentOutput }),
    captureCompileReceipt({ ...options, output: concurrentOutput }),
  ]);
  assert.deepEqual(concurrent.map(result => result.status), ['fulfilled', 'fulfilled']);
  assert.equal((await readdir(value.artifacts)).some(name => name.endsWith('.tmp')), false);
  const conflictingOutput = path.join(value.artifacts, 'conflicting.json');
  const conflicting = await Promise.allSettled([
    captureCompileReceipt({ ...options, output: conflictingOutput }),
    captureCompileReceipt({ ...options, output: conflictingOutput, command: ['Unity', '-nographics'] }),
  ]);
  assert.equal(conflicting.filter(result => result.status === 'fulfilled').length, 1);
  assert.equal(conflicting.filter(result => result.status === 'rejected').length, 1);
  assert.equal((await readdir(value.artifacts)).some(name => name.endsWith('.tmp')), false);
  await assert.rejects(captureCompileReceipt({ ...options, output: path.join(value.artifacts, 'zero.json'), exitCode: 0 }),
    /nonzero exit/);
  await assert.rejects(captureCompileReceipt({ ...options, output: path.join(value.artifacts, 'missing.json'), diagnostics: [] }),
    /diagnostic/);

  await writeFile(value.logPath, 'error CS0246: ISimBattleSession changed\n');
  await assert.rejects(captureCompileReceipt(options), /non-identical receipt/);
  assert.equal((await readdir(value.artifacts)).some(name => name.endsWith('.tmp')), false);
});

test('source receipts hash the exact diff and record method presence before and after', async () => {
  const value = await fixture();
  const output = path.join(value.artifacts, 'source.json');
  const receipt = await captureSourceReceipt({
    root: value.root, base: value.base, fix: value.fix, method: 'AcceptanceMethod',
    paths: ['tests/Acceptance.cs'], output, evidenceRoot: value.artifacts,
  });
  const diff = execFileSync('git', [
    'diff', '--binary', `${value.base}..${value.fix}`, '--', 'tests/Acceptance.cs',
  ], { cwd: value.root });

  assert.deepEqual(receipt.fixCommits, [value.fix]);
  assert.equal(receipt.testPresentBefore, false);
  assert.equal(receipt.testPresentAfter, true);
  assert.equal(receipt.gitDiffSha256, sha256(diff));
  assert.deepEqual(receipt.paths, ['tests/Acceptance.cs']);
});

test('source receipts preserve an ordered multi-commit fix set', async () => {
  const value = await fixture();
  const receipt = await captureSourceReceipt({
    root: value.root, base: value.base, fixCommits: [value.fix, value.secondFix],
    method: 'AcceptanceMethod', paths: ['tests/Acceptance.cs', 'tests/Second.cs'],
    output: path.join(value.artifacts, 'multi-source.json'), evidenceRoot: value.artifacts,
  });

  assert.deepEqual(receipt.fixCommits, [value.fix, value.secondFix]);
  assert.deepEqual(receipt.paths, ['tests/Acceptance.cs', 'tests/Second.cs']);
});

test('source receipts capture historical diffs larger than the child-process default buffer', async () => {
  const value = await fixture();
  await writeFile(path.join(value.root, 'tests/Large.txt'), 'x'.repeat(1_200_000));
  git(value.root, ['add', '.']);
  git(value.root, ['commit', '--quiet', '-m', 'large historical diff']);
  const largeFix = git(value.root, ['rev-parse', 'HEAD']);

  const receipt = await captureSourceReceipt({
    root: value.root, base: value.base, fixCommits: [value.fix, value.secondFix, largeFix],
    method: 'AcceptanceMethod', paths: ['tests/Acceptance.cs', 'tests/Second.cs', 'tests/Large.txt'],
    output: path.join(value.artifacts, 'large-source.json'), evidenceRoot: value.artifacts,
  });

  assert.deepEqual(receipt.fixCommits, [value.fix, value.secondFix, largeFix]);
  assert.deepEqual(receipt.paths, ['tests/Acceptance.cs', 'tests/Large.txt', 'tests/Second.cs']);
});

test('capture rejects unreachable commits, escaped plan citations, and repository outputs', async () => {
  const value = await fixture();
  const options = {
    root: value.root, revision: value.base, exitCode: 1, logPath: value.logPath,
    diagnostics: ['error CS0246'], planCitation: 'docs/plans/simulation.md:1',
    command: ['Unity', '-batchmode'], output: path.join(value.artifacts, 'compile.json'),
  };
  await assert.rejects(captureCompileReceipt({ ...options, revision: value.unreachable }), /reachable/);
  const outside = path.join(path.dirname(value.root), 'outside-plan.md');
  await writeFile(outside, '# Outside plan\n');
  await assert.rejects(captureCompileReceipt({ ...options, planCitation: `docs/../../${path.basename(outside)}:1` }),
    /plan citation/);
  await assert.rejects(captureCompileReceipt({
    ...options, evidenceRoot: value.artifacts, output: path.join(value.root, 'inside.json'),
  }), /evidence root/);
});

test('source capture rejects methods already present before the historical fix', async () => {
  const value = await fixture();
  await assert.rejects(captureSourceReceipt({
    root: value.root, base: value.fix, fix: value.secondFix, method: 'AcceptanceMethod',
    paths: ['tests/Acceptance.cs', 'tests/Second.cs'], output: path.join(value.artifacts, 'invalid.json'),
    evidenceRoot: value.artifacts,
  }), /present before/);
});

test('CLI rejects unknown and repeated single-value options', async () => {
  const value = await fixture();
  const script = path.resolve('scripts/audit/capture-historical-red.mjs');
  const args = [
    script, 'compile', '--revision', value.base, '--exit-code', '1', '--log', value.logPath,
    '--command', 'Unity', '--diagnostic', 'error CS0246',
    '--plan-citation', 'docs/plans/simulation.md:1',
    '--output', path.join(value.artifacts, 'one.json'),
  ];
  const environment = { ...process.env, ZWEB_EVIDENCE_ROOT: value.artifacts };
  assert.throws(() => execFileSync(process.execPath, [...args, '--typo', 'value'], {
    cwd: value.root, env: environment, stdio: 'pipe',
  }), /Unknown argument/);
  assert.throws(() => execFileSync(process.execPath, [
    ...args, '--output', path.join(value.artifacts, 'two.json'),
  ], { cwd: value.root, env: environment, stdio: 'pipe' }), /Duplicate argument/);
});
