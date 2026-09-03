import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { lstat, mkdir, readdir, realpath, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const approvedEmail = ['316624805+diegozhou114-cloud', 'users.noreply.github.com'].join('@');
const approved = `Diego Zhou <${approvedEmail}>`;
const approvedPrivacyRevision = '61add622c04ad72494b5da785e542b984ec18df4';
const email = /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g;
const userPath = /\/(?:Users|home)\/[^/\s]+\/|[A-Za-z]:\\Users\\[^\\\s]+\\/gi;
const policyEmail = ['diegozhou114', 'gmail.com'].join('@');
const known = [['Packages/com.zwebglframework.runtime/ThirdParty/Kcp2k/LICENSE.txt', '1a12b7192346c004f7c3e5ce90d652750aa0fe7a29df67cac5f6271433808fe0'], ['Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket/LICENSE.txt', '50e754644c6bf26a359a7d278a660625344243342087de19fb8c9c90147307f8']];
const scannerConfig = '[extend]\nuseDefault = true\n';
export const maxBlobBytes = 5 * 1024 * 1024;
const digest = value => createHash('sha256').update(value).digest('hex');
const unique = values => [...new Set(values)].sort();
function run(file, args, { cwd, env, input } = {}) { return new Promise((resolve, reject) => {
  const child = spawn(file, args, { cwd, env, stdio: ['pipe', 'pipe', 'pipe'] }); const out = []; const err = [];
  child.stdout.on('data', value => out.push(value)); child.stderr.on('data', value => err.push(value)); child.on('error', reject);
  child.on('close', code => code === 0 ? resolve(Buffer.concat(out)) : reject(Object.assign(new Error(Buffer.concat(err).toString()), { code })));
  child.stdin.end(input);
}); }
const git = (root, args, input) => run('git', args, { cwd: root, input });
async function stat(target) { try { return await lstat(target); } catch { return null; } }
async function evidenceDirectory(repository, requested) {
  const root = path.resolve(requested); const repo = await realpath(repository);
  if (path.relative(repo, root) === '' || !path.relative(repo, root).startsWith('..')) throw new Error('evidence root must be a canonical external directory');
  let current = path.parse(root).root; for (const part of root.slice(1).split('/').filter(Boolean)) { current = path.join(current, part); const entry = await stat(current); if (entry?.isSymbolicLink()) throw new Error('evidence root must be a canonical external directory'); }
  const entry = await stat(root); if (entry && (!entry.isDirectory() || (await readdir(root)).length)) throw new Error('evidence root must be empty');
  await mkdir(root, { recursive: true }); return realpath(root);
}
async function gitleaks(root, revision, env, evidence) {
  const config = path.join(evidence, 'gitleaks-config.toml'); const ignore = path.join(evidence, 'gitleaks-ignore.txt');
  await writeFile(config, scannerConfig); await writeFile(ignore, '');
  const clean = { ...env }; delete clean.GITLEAKS_CONFIG; delete clean.GITLEAKS_CONFIG_TOML;
  let raw; try { raw = (await run('gitleaks', ['version'], { cwd: root, env: clean })).toString().trim(); } catch { throw new Error('gitleaks unavailable'); }
  const match = raw.match(/^(?:gitleaks version )?(\d+\.\d+\.\d+)$/); if (!match) throw new Error('gitleaks unversioned');
  try { await run('gitleaks', ['git', '--redact', '--ignore-gitleaks-allow', '--config', config,
    '--gitleaks-ignore-path', ignore, `--log-opts=${revision}`], { cwd: root, env: clean }); }
  catch { throw new Error('gitleaks git --redact failed'); }
  return match[1];
}
async function objects(root, revision) {
  const rows = (await git(root, ['rev-list', '--objects', revision])).toString().trim().split('\n').filter(Boolean); const paths = new Map();
  for (const row of rows) { const [hash, ...rest] = row.split(' '); const names = paths.get(hash) || new Set(); names.add(rest.join(' ')); paths.set(hash, names); }
  const hashes = [...paths.keys()]; const checked = (await git(root, ['cat-file', '--batch-check=%(objectname) %(objecttype) %(objectsize)'], `${hashes.join('\n')}\n`)).toString().trim().split('\n');
  const blobs = checked.map(row => row.split(' ')).filter(([, type]) => type === 'blob').map(([hash,, size]) => ({ hash, paths: [...paths.get(hash)], size: Number(size), body: null }));
  const wanted = blobs.filter(blob => blob.size <= maxBlobBytes); const byHash = new Map(blobs.map(blob => [blob.hash, blob]));
  for (let start = 0; start < wanted.length; start += 256) { const chunk = wanted.slice(start, start + 256); const raw = await git(root, ['cat-file', '--batch'], `${chunk.map(blob => blob.hash).join('\n')}\n`); let offset = 0;
    while (offset < raw.length) { const line = raw.indexOf(10, offset); if (line < 0) throw new Error('invalid git object stream'); const [hash, type, size] = raw.subarray(offset, line).toString().split(' '); const length = Number(size); offset = line + 1; const body = raw.subarray(offset, offset + length); offset += length + 1; if (type !== 'blob' || body.length !== length || !byHash.has(hash)) throw new Error('invalid git object stream'); byHash.get(hash).body = body; } }
  return blobs;
}
async function identity(root, base, revision) {
  try { await git(root, ['merge-base', '--is-ancestor', base, revision]); } catch { return { errors: ['identity base is not an ancestor'], count: 0 }; }
  const rows = (await git(root, ['log', '--format=%H%x00%an%x00%ae%x00%cn%x00%ce%x00%aI%x00%cI', `${base}..${revision}`])).toString().trim(); const errors = [];
  for (const row of rows ? rows.split('\n') : []) { const [, an, ae, cn, ce, ai, ci] = row.split('\0'); if (`${an} <${ae}>` !== approved || `${cn} <${ce}>` !== approved) errors.push('new commit identity mismatch'); if (!/^\d{4}-\d\d-\d\dT.+/.test(ai) || !/^\d{4}-\d\d-\d\dT.+/.test(ci)) errors.push('commit timestamp mismatch'); }
  return { errors: unique(errors), count: rows ? rows.split('\n').length : 0 };
}
const licenseName = name => /(?:^|\/)(?:LICENSE(?:\.txt)?|COPYING)$/.test(name);
function licenseLike(body) { if (!body || body.length < 120) return false; const text = body.toString('utf8'); return /MIT License[\s\S]*Permission is hereby granted[\s\S]*THE SOFTWARE IS PROVIDED|(?:Apache License[\s\S]*Version 2\.0[\s\S]*TERMS AND CONDITIONS|Licensed under the Apache License, Version 2\.0[\s\S]*apache\.org\/licenses\/LICENSE-2\.0)|Redistribution and use[\s\S]*THIS SOFTWARE|GNU (?:LESSER )?GENERAL PUBLIC LICENSE|Mozilla Public License[\s\S]*Version 2\.0|ISC License[\s\S]*Permission to use, copy/i.test(text); }
function nulText(body) { const size = Math.min(body.length, 4096); let zeroes = 0; for (let index = 0; index < size; index++) if (body[index] === 0) zeroes++; return zeroes >= 2 && zeroes * 5 >= size * 2 ? body.toString('latin1').replaceAll('\0', '') : null; }
function reservedFixtureEmail(value, names) {
  const domain = value.slice(value.lastIndexOf('@') + 1).toLowerCase();
  const reserved = ['example.com', 'example.net', 'example.org'].includes(domain) || /(?:^|\.)(?:example|invalid|test)$/.test(domain);
  return reserved && [...names].every(name => /^scripts\/(?:.*\/)?[^/]+\.test\.mjs$/.test(name));
}
function privacyFindings(blobs, metadata, licensed, historicalPaths) {
  const findings = [];
  for (const row of metadata) { const [commit, author, committer] = row.split('\0'); for (const [role, value] of [['author', author], ['committer', committer]]) if (value !== approvedEmail) findings.push(`metadata\0${commit}\0${role}\0${value}`); }
  for (const blob of blobs) { if (!blob.body) continue; const encoded = nulText(blob.body), texts = [blob.body.toString('utf8'), ...(encoded ? [encoded] : [])], names = historicalPaths.get(blob.hash) || new Set(blob.paths); const retainedLicense = licensed.has(blob.hash) && [...names].every(name => licenseName(name) && name.includes('ThirdParty/'));
    const location = unique([...names]).join('\0');
    for (const text of texts) { for (const value of text.match(userPath) || []) findings.push(`path\0${blob.hash}\0${location}\0${value}`); for (const value of text.match(email) || []) { const policy = value === policyEmail && [...names].every(name => ['SECURITY.md', 'CODE_OF_CONDUCT.md'].includes(name)); if (!retainedLicense && !policy && !reservedFixtureEmail(value, names)) findings.push(`email\0${blob.hash}\0${location}\0${value}`); } } }
  return unique(findings);
}
async function licensing(root, revision, blobs) {
  const errors = []; const history = await git(root, ['log', '--format=', '--name-only', '-z', revision]); const paths = new Set(history.toString('binary').split('\0').filter(Boolean).map(name => Buffer.from(name, 'binary').toString('utf8'))); const vendors = new Set(); const byHash = new Map(blobs.map(blob => [blob.hash, blob])); const licensed = new Set(), historicalPaths = new Map();
  for (const name of paths) { const marker = name?.indexOf('/ThirdParty/') ?? -1; const at = name?.startsWith('ThirdParty/') ? 0 : marker;
    if (at >= 0) { const prefix = at ? name.slice(0, at) : ''; const vendor = name.slice(at + (at ? 12 : 11)).split('/')[0]; vendors.add(`${prefix ? `${prefix}/` : ''}ThirdParty/${vendor}`); } }
  const commits = (await git(root, ['rev-list', revision])).toString().trim().split('\n').filter(Boolean);
  for (const commit of commits) { const raw = await git(root, ['ls-tree', '-r', '-z', commit]); const entries = raw.toString('binary').split('\0').filter(Boolean).map(row => { const at = row.indexOf('\t'); const [mode, type, hash] = row.slice(0, at).split(' '); return { mode, type, hash, name: Buffer.from(row.slice(at + 1), 'binary').toString('utf8') }; });
    for (const entry of entries) { const names = historicalPaths.get(entry.hash) || new Set(); names.add(entry.name); historicalPaths.set(entry.hash, names); }
    for (const vendor of vendors) { const contents = entries.filter(entry => entry.name.startsWith(`${vendor}/`) && !licenseName(entry.name)); if (!contents.length) continue; const licenses = entries.filter(entry => [`${vendor}/LICENSE`, `${vendor}/LICENSE.txt`, `${vendor}/COPYING`].includes(entry.name));
      if (!licenses.length || licenses.some(entry => entry.mode !== '100644' || entry.type !== 'blob' || !licenseLike(byHash.get(entry.hash)?.body))) errors.push('unlicensed third-party boundary'); else for (const entry of licenses) licensed.add(entry.hash); } }
  let notices = ''; try { notices = (await git(root, ['show', `${revision}:THIRD_PARTY_NOTICES.md`])).toString(); } catch { errors.push('ambiguous third-party boundary'); }
  for (const [file, expected] of known) try { if (digest(await git(root, ['show', `${revision}:${file}`])) !== expected || !notices.includes(file.slice('Packages/com.zwebglframework.runtime/'.length))) errors.push('known third-party boundary mismatch'); } catch { errors.push('known third-party boundary mismatch'); }
  if (!notices.includes('66efda6686f649838d42f078fbaabf56ac449de4') || !notices.includes('cbbf04d42522d5f5b466224a0b8e3936676f9b32')) errors.push('known third-party boundary mismatch');
  return { errors: unique(errors), licensed, historicalPaths };
}
async function metadata(root, revision) { const value = (await git(root, ['log', '--format=%H%x00%ae%x00%ce', revision])).toString().trim(); return value ? value.split('\n') : []; }
async function changedBlobs(root, base, revision) {
  const hashes = new Set(), commits = (await git(root, ['rev-list', `${base}..${revision}`])).toString().trim().split('\n').filter(Boolean);
  for (const commit of commits) { const raw = await git(root, ['diff-tree', '-r', '-z', '--no-commit-id', '--raw', '-m', commit]), rows = raw.toString('binary').split('\0').filter(Boolean);
    for (let index = 0; index < rows.length; index += 2) { const [, , before, after] = rows[index].split(' '); if (before !== after && after && !/^0+$/.test(after)) hashes.add(after); } }
  return hashes;
}
async function privacy(root, revision, blobs, records, licenses, approvalRevision) {
  const findings = privacyFindings(blobs, records, licenses.licensed, licenses.historicalPaths);
  if (!approvalRevision) return { errors: findings.length ? ['unapproved personal data'] : [], revision: '-', count: 0 };
  let resolved; try { resolved = (await git(root, ['rev-parse', '--verify', `${approvalRevision}^{commit}`])).toString().trim(); await git(root, ['merge-base', '--is-ancestor', resolved, revision]); }
  catch { return { errors: ['privacy approval base is not an ancestor', ...(findings.length ? ['unapproved personal data'] : [])], revision: '-', count: 0 }; }
  let allowed = findings, changed = new Set();
  if (resolved !== revision) { const baseBlobs = await objects(root, resolved), baseLicenses = await licensing(root, resolved, baseBlobs); allowed = privacyFindings(baseBlobs, await metadata(root, resolved), baseLicenses.licensed, baseLicenses.historicalPaths); changed = await changedBlobs(root, resolved, revision); }
  const accepted = new Set(allowed), rejected = findings.some(value => { const [kind, hash] = value.split('\0'); return !accepted.has(value) || (kind !== 'metadata' && changed.has(hash)); });
  return { errors: rejected ? ['unapproved personal data'] : [], revision: resolved, count: accepted.size };
}
function args(values) { const result = {}; const allowed = new Set(['revision', 'evidence-root']); for (let index = 0; index < values.length; index += 2) { const name = values[index]?.slice(2); if (!values[index]?.startsWith('--') || values[index + 1] === undefined || !allowed.has(name) || name in result) throw new Error(`Invalid argument: ${values[index]}`); result[name] = values[index + 1]; }
  if (!result.revision || !result['evidence-root']) throw new Error('Usage: audit-history.mjs --revision <revision> --evidence-root <external-private-dir>'); return result; }
export async function auditHistory({ repositoryRoot = process.cwd(), revision, evidenceRoot, identityBase = 'ed23bb0', privacyApprovalRevision = approvedPrivacyRevision, env = process.env }) {
  let evidence; try { evidence = await evidenceDirectory(repositoryRoot, evidenceRoot); } catch (error) { return { errors: [error.message] }; }
  let resolved; try { resolved = (await git(repositoryRoot, ['rev-parse', '--verify', `${revision}^{commit}`])).toString().trim(); } catch { return { errors: ['invalid revision'] }; }
  const errors = []; let version = '-'; try { version = await gitleaks(repositoryRoot, resolved, env, evidence); } catch (error) { errors.push(error.message); }
  const blobs = await objects(repositoryRoot, resolved), records = await metadata(repositoryRoot, resolved), ids = await identity(repositoryRoot, identityBase, resolved);
  const licenses = await licensing(repositoryRoot, resolved, blobs), privacyResult = await privacy(repositoryRoot, resolved, blobs, records, licenses, privacyApprovalRevision); errors.push(...ids.errors, ...privacyResult.errors, ...licenses.errors); if (blobs.some(blob => blob.size > maxBlobBytes)) errors.push('oversized blob');
  const categories = unique(errors); const report = `History audit: ${categories.length ? 'FAIL' : 'PASS'}\nRevision: ${resolved}\nCommit count: ${(await git(repositoryRoot, ['rev-list', '--count', resolved])).toString().trim()}\nNew commit count: ${ids.count}\nBlob count: ${blobs.length}\ngitleaks version: ${version}\nPrivacy approval revision: ${privacyResult.revision}\nApproved privacy fingerprints: ${privacyResult.count}\nFinding categories: ${categories.join(', ') || '-'}\n`;
  const versionFile = `${version}\n`; await writeFile(path.join(evidence, 'history-audit.txt'), report); await writeFile(path.join(evidence, 'gitleaks-version.txt'), versionFile);
  await writeFile(path.join(evidence, 'history-digests.sha256'), [`${digest(scannerConfig)}  gitleaks-config.toml`, `${digest('')}  gitleaks-ignore.txt`, `${digest(versionFile)}  gitleaks-version.txt`, `${digest(report)}  history-audit.txt`].sort().join('\n') + '\n'); return { errors: categories, revision: resolved, evidenceRoot: evidence };
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) { try { const options = args(process.argv.slice(2)); const result = await auditHistory({ revision: options.revision, evidenceRoot: options['evidence-root'] }); if (result.errors.length) { console.error(result.errors.join('\n')); process.exitCode = 1; } else console.log('[audit-history] PASS'); } catch (error) { console.error(error.message); process.exitCode = 1; } }
