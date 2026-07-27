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

  const logPath = path.join(artifacts, 'compile.log');
  await writeFile(logPath, 'error CS0246: ISimBattleSession\n');
  return { root, artifacts, base, fix, logPath };
}

test('compile receipts reject exit zero, missing diagnostics, and changed logs', async () => {
  const value = await fixture();
  const output = path.join(value.artifacts, 'compile.json');
  const options = {
    root: value.root, revision: value.base, exitCode: 1, logPath: value.logPath,
    diagnostics: ['error CS0246', 'ISimBattleSession'],
    planCitation: 'docs/plans/simulation.md:1', command: ['Unity', '-batchmode'], output,
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
    paths: ['tests/Acceptance.cs'], output,
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
