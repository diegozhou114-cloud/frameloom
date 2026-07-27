import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { validateRecoveryManifest } from './simulation-evidence-recovery.mjs';

const hash = value => createHash('sha256').update(value).digest('hex');

function git(root, args) {
  return execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
}

async function recoveryFixture() {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-recovery-'));
  const artifacts = await mkdtemp(path.join(os.tmpdir(), 'zweb-recovery-artifacts-'));
  git(root, ['init', '--quiet']);
  git(root, ['config', 'user.email', 'audit@example.test']);
  git(root, ['config', 'user.name', 'Audit Test']);
  await mkdir(path.join(root, 'docs/plans'), { recursive: true });
  await mkdir(path.join(root, 'docs/specs'), { recursive: true });
  await mkdir(path.join(root, 'src'), { recursive: true });
  await mkdir(path.join(root, 'tests'), { recursive: true });
  await writeFile(path.join(root, 'docs/plans/simulation.md'), '# Simulation plan\n');
  await writeFile(path.join(root, 'docs/specs/register.md'),
    '| ID | Method |\n|---|---|\n| SIM-001 | AcceptanceMethod |\n');
  await writeFile(path.join(root, 'docs/specs/ownership.json'), JSON.stringify({
    schemaVersion: 1,
    plans: { simulationcore: { path: 'docs/plans/simulation.md', issues: ['SIM-001'] } },
  }, null, 2));
  await writeFile(path.join(root, 'docs/specs/ledger.json'), JSON.stringify({
    schemaVersion: 1,
    register: 'docs/specs/register.md',
    allowedStatuses: ['Open', 'Closed'],
    closureModes: ['Fixed'],
    issues: { 'SIM-001': {} },
  }, null, 2));
  await writeFile(path.join(root, 'src/feature.cs'), 'class Feature {}\n');
  git(root, ['add', '.']);
  git(root, ['commit', '--quiet', '-m', 'base']);
  const baseCommit = git(root, ['rev-parse', 'HEAD']);
  await writeFile(path.join(root, 'tests/Acceptance.cs'),
    'public void AcceptanceMethod() {}\n');
  git(root, ['add', '.']);
  git(root, ['commit', '--quiet', '-m', 'fix']);
  const fixCommit = git(root, ['rev-parse', 'HEAD']);
  await writeFile(path.join(root, 'src/future.cs'), 'class Future {}\n');
  git(root, ['add', '.']);
  git(root, ['commit', '--quiet', '-m', 'future']);
  const futureCommit = git(root, ['rev-parse', 'HEAD']);
  const headBranch = git(root, ['branch', '--show-current']);
  git(root, ['checkout', '--quiet', '-b', 'unreachable', baseCommit]);
  await writeFile(path.join(root, 'src/unreachable.cs'), 'class Unreachable {}\n');
  git(root, ['add', '.']);
  git(root, ['commit', '--quiet', '-m', 'unreachable']);
  const unreachableCommit = git(root, ['rev-parse', 'HEAD']);
  git(root, ['checkout', '--quiet', headBranch]);

  const redXml = path.join(artifacts, 'red.trx');
  const greenXml = path.join(artifacts, 'green.trx');
  const gate = path.join(artifacts, 'gate.json');
  await writeFile(redXml,
    '<TestRun><Results><UnitTestResult testName="Namespace.AcceptanceMethod" outcome="Failed" /></Results></TestRun>');
  await writeFile(greenXml,
    '<TestRun><Results><UnitTestResult testName="Namespace.AcceptanceMethod" outcome="Passed" /></Results></TestRun>');
  await writeFile(gate, '{"passed":true}\n');
  const manifestPath = 'docs/specs/recovery.json';
  const row = {
    issue: 'SIM-001', acceptanceMethod: 'AcceptanceMethod',
    red: { kind: 'ExecutedTestFailure', artifact: redXml }, green: greenXml,
    gates: [gate], fixCommits: [fixCommit], mode: 'Fixed',
  };
  const manifest = {
    schemaVersion: 2, ownership: 'docs/specs/ownership.json',
    register: 'docs/specs/register.md', ledger: 'docs/specs/ledger.json',
    historicalCutoff: fixCommit, rows: [row],
  };
  const writeManifest = async () => {
    await writeFile(path.join(root, manifestPath), JSON.stringify(manifest, null, 2) + '\n');
  };
  const options = () => ({
    root, manifestPath, requireArtifacts: true, expectedIssueCount: 1,
    trustedHistoricalCutoff: fixCommit,
  });
  const writeGreen = async (name, result) => {
    await writeFile(greenXml,
      `<TestRun><Results><UnitTestResult testName="Namespace.${name}" outcome="${result}" /></Results></TestRun>`);
  };
  const writeCompileReceipt = async () => {
    const logPath = path.join(artifacts, 'compile.log');
    const log = 'error CS0246: ISimBattleSession\n';
    await writeFile(logPath, log);
    const receipt = path.join(artifacts, 'compile.json');
    await writeFile(receipt, JSON.stringify({
      schemaVersion: 1, kind: 'ExpectedCompileFailure', sourceRevision: baseCommit,
      command: ['unity', '-batchmode'], exitCode: 1, logPath, logSha256: hash(log),
      requiredDiagnostics: ['error CS0246', 'ISimBattleSession'],
      planCitation: 'docs/plans/simulation.md:1',
    }, null, 2));
    row.red = { kind: 'ExpectedCompileFailure', artifact: receipt };
  };
  const writeSourceReceipt = async () => {
    const diff = execFileSync('git', ['diff', '--binary', `${baseCommit}..${fixCommit}`, '--', 'tests/Acceptance.cs'],
      { cwd: root });
    const receipt = path.join(artifacts, 'source.json');
    await writeFile(receipt, JSON.stringify({
      schemaVersion: 1, kind: 'HistoricalSourceRed', sourceRevision: baseCommit,
      fixCommits: [fixCommit], acceptanceMethod: 'AcceptanceMethod',
      testPresentBefore: false, testPresentAfter: true, paths: ['tests/Acceptance.cs'],
      gitDiffSha256: hash(diff), reason: 'The historical change added the acceptance test.',
    }, null, 2));
    row.red = { kind: 'HistoricalSourceRed', artifact: receipt };
  };
  await writeManifest();
  return {
    root, artifacts, manifest, row, baseCommit, fixCommit, futureCommit, unreachableCommit,
    writeManifest, options, writeGreen,
    assertExecutedFailureAccepted: async () => {
      row.red = { kind: 'ExecutedTestFailure', artifact: redXml };
      await writeManifest();
      assert.deepEqual((await validateRecoveryManifest(options())).errors, []);
    },
    assertCompileFailureAccepted: async () => {
      await writeCompileReceipt();
      await writeManifest();
      assert.deepEqual((await validateRecoveryManifest(options())).errors, []);
    },
    assertHistoricalSourceRedAccepted: async () => {
      await writeSourceReceipt();
      await writeManifest();
      assert.deepEqual((await validateRecoveryManifest(options())).errors, []);
    },
  };
}

test('requires exactly the 38 simulation-owned rows', async () => {
  const fixture = await recoveryFixture();
  fixture.manifest.rows.pop();
  await fixture.writeManifest();
  const result = await validateRecoveryManifest(fixture.options());
  assert.match(result.errors.join('\n'), /exactly match simulation ownership/);
});

test('accepts multiple reachable fix commits and rejects an unreadable commit', async () => {
  const fixture = await recoveryFixture();
  fixture.row.fixCommits = [fixture.baseCommit, fixture.fixCommit];
  await fixture.writeManifest();
  assert.deepEqual((await validateRecoveryManifest(fixture.options())).errors, []);
  fixture.row.fixCommits.push('f'.repeat(40));
  await fixture.writeManifest();
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /unreadable fix commit/);
});

test('validates all three RED classes without treating source receipts as tests', async () => {
  const fixture = await recoveryFixture();
  await fixture.assertExecutedFailureAccepted();
  await fixture.assertCompileFailureAccepted();
  await fixture.assertHistoricalSourceRedAccepted();
});

test('always requires exact Passed GREEN and a gate artifact', async () => {
  const fixture = await recoveryFixture();
  await fixture.writeGreen('DifferentMethod', 'Passed');
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /exact acceptance method.*Passed/);
});

test('rejects malformed and unknown test result formats', async () => {
  const fixture = await recoveryFixture();
  await writeFile(fixture.row.green, '<unknown />');
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'), /GREEN results/);
});

test('validates RED provenance when deferred GREEN artifacts are allowed', async () => {
  const fixture = await recoveryFixture();
  fixture.row.red = { kind: 'UnknownReceipt', artifact: fixture.row.green };
  await fixture.writeManifest();
  const result = await validateRecoveryManifest({ ...fixture.options(), requireArtifacts: false });
  assert.match(result.errors.join('\n'), /invalid RED kind/);
});

test('requires HistoricalSourceRed paths to cover the exact historical diff', async () => {
  const fixture = await recoveryFixture();
  await fixture.assertHistoricalSourceRedAccepted();
  const receiptPath = path.join(fixture.artifacts, 'source.json');
  const receipt = JSON.parse(await readFile(receiptPath, 'utf8'));
  receipt.paths = ['src/feature.cs'];
  await writeFile(receiptPath, JSON.stringify(receipt));
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /changed paths mismatch/);
});

test('requires HistoricalSourceRed to record testPresentBefore as false', async () => {
  const fixture = await recoveryFixture();
  await fixture.assertHistoricalSourceRedAccepted();
  const receiptPath = path.join(fixture.artifacts, 'source.json');
  const receipt = JSON.parse(await readFile(receiptPath, 'utf8'));
  receipt.testPresentBefore = true;
  await writeFile(receiptPath, JSON.stringify(receipt));
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /testPresentBefore must be false/);
});

test('requires HistoricalSourceRed receipt fix commits to match the closure row', async () => {
  const fixture = await recoveryFixture();
  await fixture.assertHistoricalSourceRedAccepted();
  fixture.row.fixCommits = [fixture.baseCommit, fixture.fixCommit];
  await fixture.writeManifest();
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /source RED fix commits must match row fix commits/);
});

test('rejects readable commits that are not reachable from HEAD', async () => {
  const fixture = await recoveryFixture();
  fixture.row.fixCommits = [fixture.unreachableCommit];
  await fixture.writeManifest();
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /unreachable fix commit/);
});

test('rejects HistoricalSourceRed commits introduced after the historical cutoff', async () => {
  const fixture = await recoveryFixture();
  await fixture.assertHistoricalSourceRedAccepted();
  fixture.manifest.historicalCutoff = fixture.baseCommit;
  await fixture.writeManifest();
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /historical cutoff/);
});

test('requires the historical cutoff to be reachable from HEAD', async () => {
  const fixture = await recoveryFixture();
  fixture.manifest.historicalCutoff = fixture.unreachableCommit;
  await fixture.writeManifest();
  const result = await validateRecoveryManifest({
    ...fixture.options(), trustedHistoricalCutoff: fixture.unreachableCommit,
  });
  assert.match(result.errors.join('\n'), /trusted historical cutoff.*unreadable/);
});

test('rejects a manifest that advances the trusted historical cutoff', async () => {
  const fixture = await recoveryFixture();
  await fixture.assertHistoricalSourceRedAccepted();
  fixture.manifest.historicalCutoff = fixture.futureCommit;
  await fixture.writeManifest();
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /historical cutoff.*trusted/);
});

test('requires recovery manifests declaring a historical cutoff to use schema version 2', async () => {
  const fixture = await recoveryFixture();
  fixture.manifest.schemaVersion = 1;
  await fixture.writeManifest();
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /schemaVersion must equal 2/);
});

test('rejects compile plan citations that escape the repository docs directory', async () => {
  const fixture = await recoveryFixture();
  await fixture.assertCompileFailureAccepted();
  const receiptPath = path.join(fixture.artifacts, 'compile.json');
  const receipt = JSON.parse(await readFile(receiptPath, 'utf8'));
  const outside = path.join(path.dirname(fixture.root), 'outside-plan.md');
  await writeFile(outside, '# Outside plan\n');
  receipt.planCitation = `docs/../../${path.basename(outside)}:1`;
  await writeFile(receiptPath, JSON.stringify(receipt));
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /compile plan citation is invalid/);
});

test('requires HistoricalSourceRed revisions to precede every fix commit', async () => {
  const fixture = await recoveryFixture();
  await fixture.assertHistoricalSourceRedAccepted();
  const receiptPath = path.join(fixture.artifacts, 'source.json');
  const receipt = JSON.parse(await readFile(receiptPath, 'utf8'));
  receipt.sourceRevision = fixture.fixCommit;
  receipt.fixCommits = [fixture.baseCommit];
  receipt.testPresentBefore = false;
  receipt.testPresentAfter = true;
  const reversed = execFileSync('git', [
    'diff', '--binary', `${fixture.fixCommit}..${fixture.baseCommit}`, '--', 'tests/Acceptance.cs',
  ], { cwd: fixture.root });
  receipt.gitDiffSha256 = hash(reversed);
  await writeFile(receiptPath, JSON.stringify(receipt));
  assert.match((await validateRecoveryManifest(fixture.options())).errors.join('\n'),
    /source RED revision must precede every fix commit/);
});

test('reports a malformed HistoricalSourceRed revision without throwing', async () => {
  const fixture = await recoveryFixture();
  await fixture.assertHistoricalSourceRedAccepted();
  const receiptPath = path.join(fixture.artifacts, 'source.json');
  const receipt = JSON.parse(await readFile(receiptPath, 'utf8'));
  delete receipt.sourceRevision;
  await writeFile(receiptPath, JSON.stringify(receipt));
  const result = await validateRecoveryManifest(fixture.options());
  assert.match(result.errors.join('\n'), /source RED revision is unreadable/);
});
