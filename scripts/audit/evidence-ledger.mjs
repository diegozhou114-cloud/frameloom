#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { existsSync } from 'node:fs';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const defaultLedger = 'docs/superpowers/specs/2026-07-16-project-repair-evidence.json';

function sha256(buffer) {
  return createHash('sha256').update(buffer).digest('hex');
}

function gitCommitExists(root, commit) {
  return spawnSync('git', ['cat-file', '-e', `${commit}^{commit}`], {
    cwd: root, stdio: 'ignore',
  }).status === 0;
}

function parseRegisterIds(markdown) {
  return [...markdown.matchAll(/^\|\s*([A-Z]+-\d{3})\s*\|/gm)]
    .map(match => match[1]).sort();
}

async function ingestArtifact(root, source) {
  const bytes = await readFile(source);
  const digest = sha256(bytes);
  const extension = path.extname(source).toLowerCase().replace(/[^.a-z0-9]/g, '') || '.bin';
  const relative = `docs/superpowers/evidence/artifacts/${digest}${extension}`;
  const destination = path.join(root, relative);
  await mkdir(path.dirname(destination), { recursive: true });
  if (!existsSync(destination)) await copyFile(source, destination);
  return { path: relative, sha256: digest, bytes: bytes.length };
}

async function validateEvidence(root, issue, label, values, errors) {
  if (!Array.isArray(values) || values.length === 0) {
    errors.push(`${issue} ${label} must contain at least one artifact`);
    return;
  }
  for (const value of values) {
    if (!value || typeof value.path !== 'string' || !/^[a-f0-9]{64}$/.test(value.sha256 || '')) {
      errors.push(`${issue} ${label} has an invalid receipt`);
      continue;
    }
    const artifact = path.join(root, value.path);
    if (!existsSync(artifact)) {
      errors.push(`${issue} ${label} artifact does not exist: ${value.path}`);
      continue;
    }
    const bytes = await readFile(artifact);
    if (sha256(bytes) !== value.sha256)
      errors.push(`${issue} ${label} artifact hash mismatch: ${value.path}`);
    if (value.bytes !== undefined && value.bytes !== bytes.length)
      errors.push(`${issue} ${label} artifact byte count mismatch: ${value.path}`);
  }
}

export async function validateLedger({
  root, ledgerPath, commitExists = gitCommitExists, requireAllClosed = false,
}) {
  const errors = [];
  const absoluteLedger = path.join(root, ledgerPath);
  const ledger = JSON.parse(await readFile(absoluteLedger, 'utf8'));
  const registerIds = parseRegisterIds(
    await readFile(path.join(root, ledger.register), 'utf8'));
  const ledgerIds = Object.keys(ledger.issues || {}).sort();
  if (JSON.stringify(registerIds) !== JSON.stringify(ledgerIds))
    errors.push('register and ledger issue sets differ');

  for (const issue of ledgerIds) {
    const entry = ledger.issues[issue];
    if (!ledger.allowedStatuses.includes(entry.status))
      errors.push(`${issue} has invalid status ${entry.status}`);
    if (requireAllClosed && entry.status !== 'Closed')
      errors.push(`${issue} is not Closed`);
    if (entry.status !== 'Closed') continue;
    if (!ledger.closureModes.includes(entry.closureMode))
      errors.push(`${issue} has invalid closure mode ${entry.closureMode}`);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(entry.closedAt || ''))
      errors.push(`${issue} has invalid closedAt`);
    if (!Array.isArray(entry.fixCommits) || entry.fixCommits.length === 0)
      errors.push(`${issue} has no fix commit`);
    for (const commit of entry.fixCommits || []) {
      if (!/^[a-f0-9]{40}$/.test(commit) || !commitExists(root, commit))
        errors.push(`${issue} fix commit is unreadable: ${commit}`);
    }
    await validateEvidence(root, issue, 'redEvidence', entry.redEvidence, errors);
    await validateEvidence(root, issue, 'greenEvidence', entry.greenEvidence, errors);
    await validateEvidence(root, issue, 'gateEvidence', entry.gateEvidence, errors);
    if (entry.closureMode === 'RemovedByReplacement')
      await validateEvidence(root, issue, 'replacementEvidence', entry.replacementEvidence, errors);
  }
  return { errors, issueCount: ledgerIds.length };
}

export async function closeIssue(options) {
  const { root, ledgerPath, issue, red, green, gate, fix, mode, date } = options;
  const commitExists = options.commitExists || gitCommitExists;
  const asArray = value => Array.isArray(value) ? value : [value];
  const fixes = asArray(fix);
  for (const commit of fixes) {
    if (!/^[a-f0-9]{40}$/.test(commit) || !commitExists(root, commit))
      throw new Error(`Fix commit is not readable: ${commit}`);
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date))
    throw new Error(`Closure date must be YYYY-MM-DD: ${date}`);
  const absoluteLedger = path.join(root, ledgerPath);
  const ledger = JSON.parse(await readFile(absoluteLedger, 'utf8'));
  const entry = ledger.issues?.[issue];
  if (!entry) throw new Error(`Unknown issue: ${issue}`);
  if (!ledger.closureModes.includes(mode)) throw new Error(`Invalid closure mode: ${mode}`);

  entry.status = 'Closed';
  entry.redEvidence = await Promise.all(asArray(red).map(value => ingestArtifact(root, value)));
  entry.fixCommits = fixes;
  entry.greenEvidence = await Promise.all(asArray(green).map(value => ingestArtifact(root, value)));
  entry.gateEvidence = await Promise.all(asArray(gate).map(value => ingestArtifact(root, value)));
  entry.closureMode = mode;
  entry.closedAt = date;
  entry.replacementEvidence = [];
  for (const replacement of options.replacement || [])
    entry.replacementEvidence.push(await ingestArtifact(root, replacement));
  if (mode === 'RemovedByReplacement' && entry.replacementEvidence.length === 0)
    throw new Error(`${issue} requires replacement evidence`);

  await writeFile(absoluteLedger, JSON.stringify(ledger, null, 2) + '\n', 'utf8');
}

function parseArgs(values) {
  const repeated = new Set(['red', 'fix', 'green', 'gate', 'replacement']);
  const parsed = { red: [], fix: [], green: [], gate: [], replacement: [] };
  for (let index = 0; index < values.length; index += 2) {
    const key = values[index];
    const value = values[index + 1];
    if (!key?.startsWith('--') || value === undefined) throw new Error(`Invalid argument: ${key}`);
    const name = key.slice(2);
    if (repeated.has(name)) parsed[name].push(value);
    else parsed[name] = value;
  }
  return parsed;
}

async function main() {
  const [command, ...values] = process.argv.slice(2);
  const args = parseArgs(values);
  const root = process.cwd();
  const ledgerPath = args.ledger || defaultLedger;
  if (command === 'validate') {
    const result = await validateLedger({
      root, ledgerPath, requireAllClosed: args['require-all-closed'] === 'true',
    });
    if (result.errors.length) {
      for (const error of result.errors) console.error(error);
      process.exitCode = 1;
    } else {
      console.log(`[AuditRegister_ClosedIssuesHaveExecutedEvidence] PASS issues=${result.issueCount}`);
    }
    return;
  }
  if (command === 'close') {
    await closeIssue({ root, ledgerPath, ...args });
    console.log(`[evidence-ledger] closed ${args.issue}`);
    return;
  }
  throw new Error('Usage: evidence-ledger.mjs <validate|close> [--key value]');
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
