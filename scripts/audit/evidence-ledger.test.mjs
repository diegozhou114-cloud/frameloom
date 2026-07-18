import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { closeIssue, validateLedger } from './evidence-ledger.mjs';

async function fixture() {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-ledger-'));
  await mkdir(path.join(root, 'docs/specs'), { recursive: true });
  const register = 'docs/specs/register.md';
  const ledger = 'docs/specs/evidence.json';
  await writeFile(path.join(root, register),
    '| ID | Severity |\n|---|---|\n| TEST-009 | P1 |\n', 'utf8');
  await writeFile(path.join(root, ledger), JSON.stringify({
    schemaVersion: 1,
    register,
    baselineCommit: '4a6738302ade966ae7a5eae1abc51487b29402ca',
    allowedStatuses: ['Open', 'InProgress', 'Closed'],
    closureModes: ['Fixed', 'RemovedByReplacement'],
    issues: {
      'TEST-009': {
        status: 'Open', redEvidence: [], fixCommits: [], greenEvidence: [],
        gateEvidence: [], closureMode: null, closedAt: null, replacementEvidence: [],
      },
    },
  }, null, 2) + '\n', 'utf8');
  return { root, ledger };
}

test('AuditRegister_ClosedIssuesHaveExecutedEvidence', async () => {
  const { root, ledger } = await fixture();
  const red = path.join(root, 'red.txt');
  const green = path.join(root, 'green.xml');
  const gate = path.join(root, 'gate.json');
  await writeFile(red, 'expected failure\n');
  await writeFile(green, '<test-run result="Passed"/>\n');
  await writeFile(gate, '{"result":"Passed"}\n');

  await closeIssue({
    root, ledgerPath: ledger, issue: 'TEST-009', red, green, gate,
    fix: '1111111111111111111111111111111111111111', mode: 'Fixed',
    date: '2026-07-17', replacement: [], commitExists: () => true,
  });

  const result = await validateLedger({
    root, ledgerPath: ledger, commitExists: () => true,
  });
  assert.deepEqual(result.errors, []);
  const parsed = JSON.parse(await readFile(path.join(root, ledger), 'utf8'));
  assert.equal(parsed.issues['TEST-009'].status, 'Closed');
  assert.equal(parsed.issues['TEST-009'].redEvidence.length, 1);
});

test('closed issue rejects unreadable or changed evidence', async () => {
  const { root, ledger } = await fixture();
  const parsed = JSON.parse(await readFile(path.join(root, ledger), 'utf8'));
  parsed.issues['TEST-009'] = {
    status: 'Closed',
    redEvidence: [{ path: 'docs/superpowers/evidence/artifacts/missing.txt', sha256: '0'.repeat(64) }],
    fixCommits: ['1'.repeat(40)],
    greenEvidence: [{ path: 'docs/superpowers/evidence/artifacts/missing.txt', sha256: '0'.repeat(64) }],
    gateEvidence: [{ path: 'docs/superpowers/evidence/artifacts/missing.txt', sha256: '0'.repeat(64) }],
    closureMode: 'Fixed', closedAt: '2026-07-17', replacementEvidence: [],
  };
  await writeFile(path.join(root, ledger), JSON.stringify(parsed, null, 2) + '\n');
  const result = await validateLedger({ root, ledgerPath: ledger, commitExists: () => true });
  assert.match(result.errors.join('\n'), /does not exist/);
});

test('register and ledger issue sets must be identical', async () => {
  const { root, ledger } = await fixture();
  const parsed = JSON.parse(await readFile(path.join(root, ledger), 'utf8'));
  parsed.issues['CI-001'] = structuredClone(parsed.issues['TEST-009']);
  await writeFile(path.join(root, ledger), JSON.stringify(parsed, null, 2) + '\n');
  const result = await validateLedger({ root, ledgerPath: ledger, commitExists: () => true });
  assert.match(result.errors.join('\n'), /issue sets differ/);
});
