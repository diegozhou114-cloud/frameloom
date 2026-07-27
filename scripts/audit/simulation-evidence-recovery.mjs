import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, realpathSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { closeIssue } from './evidence-ledger.mjs';

const recoveryScript = path.join(path.dirname(fileURLToPath(import.meta.url)), 'read-test-results.py');
const productionHistoricalCutoff = 'dffc4ce40e4000da93546181be50b9d4b4f5b5b8';
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const isSha = value => /^[a-f0-9]{40}$/.test(value || '');
const isObject = value => value && typeof value === 'object' && !Array.isArray(value);
const hasOnlyKeys = (value, keys) => isObject(value) && Object.keys(value).every(key => keys.includes(key));

function git(root, args, encoding = 'utf8') {
  return spawnSync('git', args, { cwd: root, encoding });
}

function commitIsReachable(root, commit) {
  return isSha(commit) && git(root, ['cat-file', '-e', `${commit}^{commit}`]).status === 0 &&
    git(root, ['merge-base', '--is-ancestor', commit, 'HEAD']).status === 0;
}

function commitPrecedesCutoff(root, commit, cutoff) {
  return isSha(commit) && isSha(cutoff) &&
    git(root, ['merge-base', '--is-ancestor', commit, cutoff]).status === 0;
}

function isExternalAbsolute(root, artifact) {
  if (typeof artifact !== 'string' || !path.isAbsolute(artifact)) return false;
  const resolvedRoot = existsSync(root) ? realpathSync(root) : path.resolve(root);
  const resolvedArtifact = existsSync(artifact) ? realpathSync(artifact) : path.resolve(artifact);
  return resolvedArtifact !== resolvedRoot && !resolvedArtifact.startsWith(`${resolvedRoot}${path.sep}`);
}

function normalizeCaseName(name) {
  return String(name).replace(/\([^)]*\)$/, '').split('.').pop();
}

function parseResults(root, artifact) {
  const parsed = spawnSync('python3', [recoveryScript, artifact], { cwd: root, encoding: 'utf8' });
  if (parsed.status !== 0) throw new Error(parsed.stderr.trim() || 'could not parse results');
  return JSON.parse(parsed.stdout);
}

function exactResult(root, artifact, method, result, label, errors) {
  try {
    const records = parseResults(root, artifact);
    if (!records.some(record => normalizeCaseName(record.name) === method && record.result === result))
      errors.push(`${label} must contain exact acceptance method ${method} with ${result}`);
  } catch (error) {
    errors.push(`${label} results are unreadable: ${error.message}`);
  }
}

async function readJson(file, errors, label) {
  try {
    return JSON.parse(await readFile(file, 'utf8'));
  } catch (error) {
    errors.push(`${label} is unreadable: ${error.message}`);
    return null;
  }
}

function registerIssues(markdown) {
  return new Set([...markdown.matchAll(/^\|\s*([A-Z]+-\d{3})\s*\|/gm)].map(match => match[1]));
}

function ownershipIssues(ownership, expectedIssueCount) {
  const plan = Object.values(ownership?.plans || {})
    .find(value => Array.isArray(value?.issues) && value.issues.length === expectedIssueCount);
  return plan?.issues || [];
}

function validateReceiptShape(receipt, errors, issue) {
  const common = ['schemaVersion', 'kind', 'sourceRevision'];
  const shapes = {
    ExecutedTestFailure: ['schemaVersion', 'kind', 'artifact'],
    ExpectedCompileFailure: [...common, 'command', 'exitCode', 'logPath', 'logSha256', 'requiredDiagnostics', 'planCitation'],
    HistoricalSourceRed: [...common, 'fixCommits', 'acceptanceMethod', 'testPresentBefore', 'testPresentAfter', 'paths', 'gitDiffSha256', 'reason', 'currentAcceptanceRequired'],
  };
  if (!hasOnlyKeys(receipt, shapes[receipt?.kind] || [])) errors.push(`${issue} RED receipt has unknown keys`);
  if (receipt?.schemaVersion !== 1 || !shapes[receipt?.kind]) errors.push(`${issue} RED receipt has invalid schema`);
}

async function validateCompileReceipt(root, receipt, errors, issue) {
  if (!commitIsReachable(root, receipt.sourceRevision)) errors.push(`${issue} RED source revision is unreadable`);
  if (!Array.isArray(receipt.command) || receipt.command.length === 0 || receipt.command.some(value => typeof value !== 'string'))
    errors.push(`${issue} compile command is invalid`);
  if (!Number.isInteger(receipt.exitCode) || receipt.exitCode === 0) errors.push(`${issue} compile receipt requires nonzero exit`);
  if (!isExternalAbsolute(root, receipt.logPath) || !existsSync(receipt.logPath)) {
    errors.push(`${issue} compile log is unreadable`);
  } else {
    const bytes = await readFile(receipt.logPath);
    if (!/^[a-f0-9]{64}$/.test(receipt.logSha256 || '') || sha256(bytes) !== receipt.logSha256)
      errors.push(`${issue} compile log hash mismatch`);
    const text = bytes.toString('utf8');
    if (!Array.isArray(receipt.requiredDiagnostics) || receipt.requiredDiagnostics.length === 0 ||
      receipt.requiredDiagnostics.some(value => typeof value !== 'string' || !text.includes(value)))
      errors.push(`${issue} compile diagnostics are incomplete`);
  }
  if (typeof receipt.planCitation !== 'string' || !/^[^:]+:\d+$/.test(receipt.planCitation) ||
    !receipt.planCitation.startsWith('docs/') || !existsSync(path.join(root, receipt.planCitation.replace(/:\d+$/, ''))))
    errors.push(`${issue} compile plan citation is invalid`);
}

function methodPresent(root, commit, method, paths) {
  return git(root, ['grep', '-F', method, commit, '--', ...paths]).status === 0;
}

function sameCommitSet(left, right) {
  if (!Array.isArray(left) || !Array.isArray(right)) return false;
  return JSON.stringify([...new Set(left)].sort()) === JSON.stringify([...new Set(right)].sort());
}

function validateSourceReceipt(root, receipt, row, errors, issue, historicalCutoff) {
  if (!commitIsReachable(root, receipt.sourceRevision)) errors.push(`${issue} source RED revision is unreadable`);
  if (!commitPrecedesCutoff(root, receipt.sourceRevision, historicalCutoff))
    errors.push(`${issue} source RED revision is after the historical cutoff`);
  if (!Array.isArray(receipt.fixCommits) || receipt.fixCommits.length === 0 ||
    receipt.fixCommits.some(commit => !commitIsReachable(root, commit))) errors.push(`${issue} source RED fix commits are unreadable`);
  if (!Array.isArray(receipt.fixCommits) ||
    receipt.fixCommits.some(commit => !commitPrecedesCutoff(root, commit, historicalCutoff)))
  {
    errors.push(`${issue} source RED fix commits are after the historical cutoff`);
  }
  if (!sameCommitSet(receipt.fixCommits, row.fixCommits))
    errors.push(`${issue} source RED fix commits must match row fix commits`);
  if (receipt.testPresentBefore !== false) errors.push(`${issue} source RED testPresentBefore must be false`);
  if (receipt.acceptanceMethod !== row.acceptanceMethod) errors.push(`${issue} source RED acceptance method differs`);
  if (!Array.isArray(receipt.paths) || receipt.paths.length === 0 || receipt.paths.some(value => typeof value !== 'string' || path.isAbsolute(value)))
    errors.push(`${issue} source RED paths are invalid`);
  if (typeof receipt.reason !== 'string' || receipt.reason.length === 0) errors.push(`${issue} source RED reason is required`);
  const fix = receipt.fixCommits?.[receipt.fixCommits.length - 1];
  if (!fix || !Array.isArray(receipt.paths) || receipt.paths.length === 0) return;
  const changedPaths = git(root, ['diff', '--name-only', `${receipt.sourceRevision}..${fix}`]);
  const exactPaths = changedPaths.status === 0 ? changedPaths.stdout.trim().split('\n').filter(Boolean).sort() : [];
  if (JSON.stringify([...receipt.paths].sort()) !== JSON.stringify(exactPaths))
    errors.push(`${issue} source RED changed paths mismatch`);
  const diff = git(root, ['diff', '--binary', `${receipt.sourceRevision}..${fix}`, '--', ...receipt.paths], null);
  if (!/^[a-f0-9]{64}$/.test(receipt.gitDiffSha256 || '') || diff.status !== 0 || sha256(diff.stdout) !== receipt.gitDiffSha256)
    errors.push(`${issue} source RED diff hash mismatch`);
  const before = methodPresent(root, receipt.sourceRevision, row.acceptanceMethod, receipt.paths);
  const after = methodPresent(root, fix, row.acceptanceMethod, receipt.paths);
  if (receipt.testPresentBefore !== before || receipt.testPresentAfter !== after)
    errors.push(`${issue} source RED method presence assertion is false`);
  if (!after && receipt.currentAcceptanceRequired !== true)
    errors.push(`${issue} source RED must require current acceptance when no historical method exists`);
}

async function validateRed(root, row, errors, requireArtifacts, historicalCutoff) {
  const red = row.red;
  if (!hasOnlyKeys(red, ['kind', 'artifact']) || !isExternalAbsolute(root, red?.artifact)) {
    errors.push(`${row.issue} RED artifact must be an external absolute path`);
    return;
  }
  if (!['ExecutedTestFailure', 'ExpectedCompileFailure', 'HistoricalSourceRed'].includes(red.kind)) {
    errors.push(`${row.issue} has invalid RED kind`);
    return;
  }
  if (!existsSync(red.artifact)) {
    errors.push(`${row.issue} RED artifact is unreadable`);
    return;
  }
  if (red.kind === 'ExecutedTestFailure') {
    exactResult(root, red.artifact, row.acceptanceMethod, 'Failed', `${row.issue} RED`, errors);
    return;
  }
  const receipt = await readJson(red.artifact, errors, `${row.issue} RED receipt`);
  if (!receipt) return;
  validateReceiptShape(receipt, errors, row.issue);
  if (receipt.kind !== red.kind) errors.push(`${row.issue} RED kind does not match receipt`);
  if (red.kind === 'ExpectedCompileFailure') await validateCompileReceipt(root, receipt, errors, row.issue);
  if (red.kind === 'HistoricalSourceRed')
    validateSourceReceipt(root, receipt, row, errors, row.issue, historicalCutoff);
}

export async function validateRecoveryManifest({
  root,
  manifestPath,
  requireArtifacts = true,
  expectedIssueCount = 38,
  trustedHistoricalCutoff = productionHistoricalCutoff,
}) {
  const errors = [];
  const manifest = await readJson(path.join(root, manifestPath), errors, 'recovery manifest');
  if (!manifest) return { errors, rowCount: 0 };
  if (!hasOnlyKeys(manifest, ['schemaVersion', 'ownership', 'register', 'ledger', 'historicalCutoff', 'rows']) || manifest.schemaVersion !== 2)
    errors.push('recovery manifest has unknown keys or schemaVersion must equal 2');
  for (const key of ['ownership', 'register', 'ledger']) {
    if (typeof manifest[key] !== 'string' || path.isAbsolute(manifest[key])) errors.push(`recovery manifest ${key} is invalid`);
  }
  if (manifest.historicalCutoff !== trustedHistoricalCutoff)
    errors.push('recovery manifest historical cutoff is not trusted');
  if (!commitIsReachable(root, trustedHistoricalCutoff))
    errors.push('trusted historical cutoff is unreadable or unreachable from HEAD');
  if (!Array.isArray(manifest.rows)) {
    errors.push('recovery manifest rows must be an array');
    return { errors, rowCount: 0 };
  }
  const ownership = await readJson(path.join(root, manifest.ownership), errors, 'ownership');
  const ledger = await readJson(path.join(root, manifest.ledger), errors, 'ledger');
  let registered = new Set();
  try { registered = registerIssues(await readFile(path.join(root, manifest.register), 'utf8')); }
  catch (error) { errors.push(`register is unreadable: ${error.message}`); }
  const expectedIssues = ownershipIssues(ownership, expectedIssueCount).sort();
  const rowIssues = manifest.rows.map(row => row?.issue).sort();
  if (expectedIssues.length !== expectedIssueCount || JSON.stringify(rowIssues) !== JSON.stringify(expectedIssues))
    errors.push('rows must exactly match simulation ownership');
  const seenIssues = new Set();
  const seenMethods = new Set();
  for (const row of manifest.rows) {
    const keys = ['issue', 'acceptanceMethod', 'red', 'green', 'gates', 'fixCommits', 'mode'];
    if (!hasOnlyKeys(row, keys)) { errors.push('recovery row has unknown keys'); continue; }
    if (typeof row.issue !== 'string' || seenIssues.has(row.issue)) errors.push('recovery rows contain duplicate issue');
    seenIssues.add(row.issue);
    if (!registered.has(row.issue) || !ledger?.issues?.[row.issue]) errors.push(`${row.issue} is not in register and ledger`);
    if (typeof row.acceptanceMethod !== 'string' || row.acceptanceMethod.length === 0 || seenMethods.has(row.acceptanceMethod))
      errors.push('recovery rows contain duplicate or invalid acceptance method');
    seenMethods.add(row.acceptanceMethod);
    if (!Array.isArray(row.fixCommits) || row.fixCommits.length === 0) errors.push(`${row.issue} must contain fix commits`);
    for (const commit of row.fixCommits || []) if (!commitIsReachable(root, commit))
      errors.push(`${row.issue} unreadable fix commit; unreachable fix commit: ${commit}`);
    if (!ledger?.closureModes?.includes(row.mode)) errors.push(`${row.issue} has invalid closure mode`);
    await validateRed(root, row, errors, requireArtifacts, trustedHistoricalCutoff);
    if (!isExternalAbsolute(root, row.green)) errors.push(`${row.issue} GREEN artifact must be an external absolute path`);
    else if (requireArtifacts) {
      if (!existsSync(row.green)) errors.push(`${row.issue} GREEN artifact is unreadable`);
      else exactResult(root, row.green, row.acceptanceMethod, 'Passed', `${row.issue} GREEN`, errors);
    }
    if (!Array.isArray(row.gates) || row.gates.length === 0) errors.push(`${row.issue} must contain a gate artifact`);
    for (const gate of row.gates || []) {
      if (!isExternalAbsolute(root, gate)) errors.push(`${row.issue} gate artifact must be an external absolute path`);
      else if (requireArtifacts && !existsSync(gate)) errors.push(`${row.issue} gate artifact is unreadable`);
    }
  }
  return { errors, rowCount: manifest.rows.length };
}

function parseArgs(values) {
  const parsed = {};
  for (let index = 0; index < values.length; index += 2) {
    const key = values[index];
    const value = values[index + 1];
    if (!key?.startsWith('--') || value === undefined) throw new Error(`Invalid argument: ${key}`);
    parsed[key.slice(2)] = value;
  }
  return parsed;
}

async function main() {
  const [command, ...values] = process.argv.slice(2);
  const args = parseArgs(values);
  const root = process.cwd();
  const manifestPath = args.manifest || 'docs/superpowers/specs/2026-07-25-simulation-evidence-recovery.json';
  const result = await validateRecoveryManifest({ root, manifestPath, requireArtifacts: args['require-artifacts'] !== 'false' });
  if (result.errors.length) {
    for (const error of result.errors) console.error(error);
    process.exitCode = 1;
    return;
  }
  if (command === 'validate') {
    console.log(`[simulation-evidence-recovery] PASS rows=${result.rowCount}`);
    return;
  }
  if (command !== 'close' || !/^\d{4}-\d{2}-\d{2}$/.test(args.date || ''))
    throw new Error('Usage: simulation-evidence-recovery.mjs <validate|close> --manifest <path> [--date YYYY-MM-DD]');
  const manifest = JSON.parse(await readFile(path.join(root, manifestPath), 'utf8'));
  for (const row of manifest.rows) {
    await closeIssue({ root, ledgerPath: manifest.ledger, issue: row.issue, red: row.red.artifact,
      green: row.green, gate: row.gates, fix: row.fixCommits, mode: row.mode, date: args.date });
  }
  console.log(`[simulation-evidence-recovery] closed rows=${manifest.rows.length}`);
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
