#!/usr/bin/env node
import { spawnSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { existsSync, realpathSync } from 'node:fs';
import { link, mkdir, readFile, unlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const sha256 = value => createHash('sha256').update(value).digest('hex');
const isSha = value => /^[a-f0-9]{40}$/.test(value || '');

function git(root, args, encoding = 'utf8') {
  return spawnSync('git', args, { cwd: root, encoding });
}

function requireCommit(root, commit, label) {
  if (!isSha(commit) || git(root, ['cat-file', '-e', `${commit}^{commit}`]).status !== 0 ||
    git(root, ['merge-base', '--is-ancestor', commit, 'HEAD']).status !== 0)
  {
    throw new Error(`${label} is not a readable commit reachable from HEAD`);
  }
}

function isWithin(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative === '' || (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative));
}

async function requireEvidenceOutput(root, evidenceRoot, output) {
  if (typeof output !== 'string' || !path.isAbsolute(output))
    throw new Error('output must be an absolute path');
  if (typeof evidenceRoot !== 'string' || !path.isAbsolute(evidenceRoot))
    throw new Error('evidence root must be an absolute path');
  await mkdir(evidenceRoot, { recursive: true });
  const projectRoot = realpathSync(root);
  const canonicalEvidenceRoot = realpathSync(evidenceRoot);
  if (isWithin(projectRoot, canonicalEvidenceRoot))
    throw new Error('evidence root must be outside the repository');
  const destination = path.resolve(output);
  await mkdir(path.dirname(destination), { recursive: true });
  const canonicalDestination = path.join(
    realpathSync(path.dirname(destination)), path.basename(destination));
  if (!isWithin(canonicalEvidenceRoot, canonicalDestination))
    throw new Error('output must be within the configured external evidence root');
  if (existsSync(destination) && !isWithin(canonicalEvidenceRoot, realpathSync(destination)))
    throw new Error('output must be within the configured external evidence root');
  return canonicalDestination;
}

async function writeReceipt(root, evidenceRoot, output, receipt) {
  const destination = await requireEvidenceOutput(root, evidenceRoot, output);
  const content = `${JSON.stringify(receipt, null, 2)}\n`;
  if (existsSync(destination)) {
    if (await readFile(destination, 'utf8') !== content)
      throw new Error(`refusing to overwrite non-identical receipt: ${destination}`);
    return receipt;
  }
  const temporary = `${destination}.${randomUUID()}.tmp`;
  try {
    await writeFile(temporary, content, 'utf8');
    try {
      await link(temporary, destination);
    } catch (error) {
      if (error.code !== 'EEXIST') throw error;
      if (await readFile(destination, 'utf8') === content) return receipt;
      throw new Error(`refusing to overwrite non-identical receipt: ${destination}`);
    }
  } finally {
    if (existsSync(temporary)) await unlink(temporary);
  }
  return receipt;
}

function requirePlanCitation(root, citation) {
  if (typeof citation !== 'string' || !/^[^:]+:\d+$/.test(citation))
    throw new Error('plan citation must be a repository path and line number');
  const relative = citation.replace(/:\d+$/, '');
  const docsRoot = path.join(root, 'docs');
  const document = path.resolve(root, relative);
  if (!relative.startsWith('docs/') || !isWithin(path.resolve(docsRoot), document) || !existsSync(document) ||
    !isWithin(realpathSync(docsRoot), realpathSync(document)))
  {
    throw new Error('plan citation must reference a readable repository plan');
  }
}

export async function captureCompileReceipt({
  root, revision, exitCode, logPath, diagnostics, planCitation, command, output,
  evidenceRoot = process.env.ZWEB_EVIDENCE_ROOT || '/tmp/zweb-sim-migration',
}) {
  requireCommit(root, revision, 'source revision');
  if (!Number.isInteger(exitCode) || exitCode === 0) throw new Error('compile receipt requires nonzero exit');
  if (!Array.isArray(command) || command.length === 0 || command.some(value => typeof value !== 'string' || value.length === 0))
    throw new Error('compile command is required');
  if (!Array.isArray(diagnostics) || diagnostics.length === 0 || diagnostics.some(value => typeof value !== 'string' || value.length === 0))
    throw new Error('at least one diagnostic is required');
  if (typeof logPath !== 'string' || !path.isAbsolute(logPath) || !existsSync(logPath))
    throw new Error('compile log is unreadable');
  requirePlanCitation(root, planCitation);
  const bytes = await readFile(logPath);
  const text = bytes.toString('utf8');
  if (diagnostics.some(diagnostic => !text.includes(diagnostic)))
    throw new Error('compile log is missing a required diagnostic');
  return writeReceipt(root, evidenceRoot, output, {
    schemaVersion: 1, kind: 'ExpectedCompileFailure', sourceRevision: revision,
    command, exitCode, logPath, logSha256: sha256(bytes), requiredDiagnostics: diagnostics,
    planCitation,
  });
}

function methodPresent(root, commit, method, paths) {
  const result = git(root, ['grep', '-F', method, commit, '--', ...paths]);
  if (result.status === 0) return true;
  if (result.status === 1) return false;
  throw new Error(`could not inspect ${commit} for ${method}`);
}

export async function captureSourceReceipt({
  root, base, fix, fixCommits, method, paths, output,
  evidenceRoot = process.env.ZWEB_EVIDENCE_ROOT || '/tmp/zweb-sim-migration',
}) {
  requireCommit(root, base, 'source revision');
  const fixes = fixCommits || [fix];
  if (!Array.isArray(fixes) || fixes.length === 0) throw new Error('at least one fix commit is required');
  let previous = base;
  for (const commit of fixes) {
    requireCommit(root, commit, 'fix commit');
    if (git(root, ['merge-base', '--is-ancestor', previous, commit]).status !== 0)
      throw new Error('fix commits must be ordered descendants of the source revision');
    previous = commit;
  }
  const finalFix = fixes[fixes.length - 1];
  if (typeof method !== 'string' || method.length === 0) throw new Error('acceptance method is required');
  if (!Array.isArray(paths) || paths.length === 0 || paths.some(value => typeof value !== 'string' || value.length === 0 || path.isAbsolute(value)))
    throw new Error('at least one relative source path is required');
  const changed = git(root, ['diff', '--name-only', `${base}..${finalFix}`]);
  if (changed.status !== 0) throw new Error('could not inspect changed paths');
  const changedPaths = changed.stdout.trim().split('\n').filter(Boolean).sort();
  const requestedPaths = [...new Set(paths)].sort();
  if (JSON.stringify(requestedPaths) !== JSON.stringify(changedPaths))
    throw new Error('source paths must exactly match the historical diff');
  const diff = git(root, ['diff', '--binary', `${base}..${finalFix}`, '--', ...requestedPaths], null);
  if (diff.status !== 0) throw new Error('could not read historical diff');
  const testPresentBefore = methodPresent(root, base, method, requestedPaths);
  const testPresentAfter = methodPresent(root, finalFix, method, requestedPaths);
  if (testPresentBefore) throw new Error('historical source method must not be present before the fix');
  return writeReceipt(root, evidenceRoot, output, {
    schemaVersion: 1, kind: 'HistoricalSourceRed', sourceRevision: base, fixCommits: fixes,
    acceptanceMethod: method, testPresentBefore, testPresentAfter, paths: requestedPaths,
    gitDiffSha256: sha256(diff.stdout),
    reason: `Historical source inspection for ${base}..${finalFix}.`,
    currentAcceptanceRequired: !testPresentAfter,
  });
}

function parseArgs(values, allowed, repeated) {
  const parsed = Object.fromEntries([...repeated].map(name => [name, []]));
  for (let index = 0; index < values.length; index += 2) {
    const key = values[index];
    const value = values[index + 1];
    if (!key?.startsWith('--') || value === undefined) throw new Error(`Invalid argument: ${key}`);
    const name = key.slice(2);
    if (!allowed.has(name)) throw new Error(`Unknown argument: ${key}`);
    if (repeated.has(name)) parsed[name].push(value);
    else if (Object.hasOwn(parsed, name)) throw new Error(`Duplicate argument: ${key}`);
    else parsed[name] = value;
  }
  return parsed;
}

async function main() {
  const [command, ...values] = process.argv.slice(2);
  const contracts = {
    compile: {
      allowed: new Set(['revision', 'exit-code', 'log', 'command', 'diagnostic', 'plan-citation', 'output']),
      repeated: new Set(['command', 'diagnostic']),
    },
    source: {
      allowed: new Set(['base', 'fix', 'method', 'path', 'output']),
      repeated: new Set(['fix', 'path']),
    },
  };
  if (!contracts[command])
    throw new Error('Usage: capture-historical-red.mjs <compile|source> [--key value]');
  const args = parseArgs(values, contracts[command].allowed, contracts[command].repeated);
  const root = process.cwd();
  if (command === 'compile') {
    await captureCompileReceipt({
      root, revision: args.revision, exitCode: Number(args['exit-code']), logPath: args.log,
      diagnostics: args.diagnostic, planCitation: args['plan-citation'], command: args.command,
      output: args.output, evidenceRoot: process.env.ZWEB_EVIDENCE_ROOT || '/tmp/zweb-sim-migration',
    });
    return;
  }
  if (command === 'source') {
    await captureSourceReceipt({
      root, base: args.base, fixCommits: args.fix, method: args.method, paths: args.path,
      output: args.output, evidenceRoot: process.env.ZWEB_EVIDENCE_ROOT || '/tmp/zweb-sim-migration',
    });
    return;
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
