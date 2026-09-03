#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { mkdir, lstat, readFile, realpath, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { TextDecoder } from 'node:util';

export const packagePath = 'Packages/com.zwebglframework.runtime';
export const resolvedPackages = ['com.zwebglframework.runtime', 'com.cysharp.unitask'];
const frameworkOrigin = 'https://github.com/diegozhou114-cloud/frameloom.git';
const unitaskOrigin = 'https://github.com/Cysharp/UniTask.git';
const unitaskRevision = 'e5acc106ee196bc5a32fb14cdf2987b0f96d11e0';
const fullSha = /^[a-f0-9]{40}$/;
const digest = value => createHash('sha256').update(value).digest('hex');
const byteSort = (a, b) => Buffer.compare(Buffer.from(a), Buffer.from(b));
const json = value => `${JSON.stringify(value, null, 2)}\n`;
const decoder = new TextDecoder('utf-8', { fatal: true });
const within = (child, parent) => { const value = path.relative(parent, child); return value === '' || (value !== '..' && !value.startsWith(`..${path.sep}`) && !path.isAbsolute(value)); };

function fail(message) { throw new Error(message); }
function safeRelative(value, label) {
  if (typeof value !== 'string' || !value || value.includes('\\') || /[\0\t\r\n]/.test(value) ||
      path.posix.isAbsolute(value) || value.split('/').some(part => !part || part === '.' || part === '..'))
    fail(`Unsafe ${label}: ${value}`);
  return value;
}
function regular(root, relative, label) {
  const target = path.join(root, relative); return lstat(target).then(info => {
    if (!info.isFile() || info.isSymbolicLink()) fail(`${label} must be a regular file: ${relative}`);
    return target;
  });
}
async function canonicalDirectory(input, label) {
  const resolved = path.resolve(input || ''); const info = await lstat(resolved).catch(error => fail(`${label} is unavailable: ${error.message}`));
  const canonical = await realpath(resolved);
  if (!info.isDirectory() || info.isSymbolicLink() || canonical !== resolved) fail(`${label} must be a canonical directory`);
  return canonical;
}
async function outputDirectory(input, repository) {
  if (!input) fail('Output directory is required for Git comparison');
  const resolved = path.resolve(input); const parentPath = path.dirname(resolved); const parent = await realpath(parentPath);
  if (parent !== parentPath) fail('Output parent must be canonical');
  const canonical = path.join(parent, path.basename(resolved));
  if (canonical === repository || canonical.startsWith(`${repository}${path.sep}`)) fail('Output must be outside the repository');
  const info = await lstat(resolved).catch(error => error.code === 'ENOENT' ? null : fail(`Invalid output: ${error.message}`));
  if (info && (!info.isDirectory() || info.isSymbolicLink())) fail('Output must be a canonical directory');
  if (info && (await readdir(resolved)).length) fail('Output directory must be empty');
  if (!info) await mkdir(resolved);
  return canonical;
}
function git(cwd, args) {
  try { return execFileSync('git', ['-C', cwd, ...args], { encoding: 'buffer', stdio: ['ignore', 'pipe', 'pipe'] }); }
  catch (error) { fail(`Git command failed: ${error.stderr?.toString().trim() || error.message}`); }
}
function inventoryFromGit(cwd, revision) {
  if (!fullSha.test(revision || '')) fail('Git revision must be a full lowercase 40-character SHA');
  const commit = decoder.decode(git(cwd, ['rev-parse', '--verify', `${revision}^{commit}`])).trim();
  if (commit !== revision) fail(`Revision is not a commit: ${revision}`);
  const tree = decoder.decode(git(cwd, ['rev-parse', '--verify', `${revision}:${packagePath}`])).trim();
  if (!/^[0-9a-f]{40}$/.test(tree) || decoder.decode(git(cwd, ['cat-file', '-t', tree])).trim() !== 'tree') fail('Package path is not a Git tree');
  const raw = git(cwd, ['ls-tree', '-r', '-z', tree]); const rows = [];
  for (const record of raw.toString('binary').split('\0')) {
    if (!record) continue;
    const separator = record.indexOf('\t'); if (separator < 0) fail('Malformed Git tree entry');
    const header = record.slice(0, separator).split(' '); const relative = decoder.decode(Buffer.from(record.slice(separator + 1), 'binary'));
    if (header.length !== 3 || header[1] !== 'blob' || !/^(100644|100755)$/.test(header[0]) || !/^[0-9a-f]{40}$/.test(header[2])) fail(`Unsupported Git package entry: ${relative}`);
    safeRelative(relative, 'Git package path');
    const content = git(cwd, ['cat-file', 'blob', header[2]]);
    rows.push([header[0], header[2], digest(content), relative]);
  }
  rows.sort((a, b) => byteSort(a[3], b[3]));
  if (!rows.length || new Set(rows.map(row => row[3])).size !== rows.length) fail('Git package inventory is empty or duplicated');
  const text = rows.map(row => `${row[0]}\t${row[1]}\t${row[2]}\t${row[3]}\n`).join('');
  return { tree, rows, text, inventorySha256: digest(text), fileCount: rows.length };
}
function parseInventory(bytes, label) {
  let text; try { text = decoder.decode(bytes); } catch { fail(`${label} is not valid UTF-8`); }
  if (!text.endsWith('\n') || text.includes('\r') || text.includes('\0')) fail(`${label} must be UTF-8/LF with a final newline`);
  const lines = text.slice(0, -1).split('\n'); const rows = lines.map(line => {
    const match = line.match(/^(100644|100755)\t([a-f0-9]{64})\t(.+)$/); if (!match) fail(`Malformed ${label} entry`);
    safeRelative(match[3], `${label} path`); return [match[1], match[2], match[3]];
  });
  if (!rows.length || new Set(rows.map(row => row[2])).size !== rows.length || rows.some((row, i) => i && byteSort(rows[i - 1][2], row[2]) > 0)) fail(`${label} is not sorted or is duplicated`);
  return { text, rows, inventorySha256: digest(bytes), fileCount: rows.length };
}
function equalInventory(left, right) { return left.text === right.text && left.inventorySha256 === right.inventorySha256 && left.fileCount === right.fileCount; }
function requireResolvedEquality(left, right, name) {
  const leftPaths = left.rows.map(row => row[2]); const rightPaths = right.rows.map(row => row[2]);
  if (leftPaths.join('\n') !== rightPaths.join('\n')) fail(`Resolved package path set mismatch: ${name}`);
  for (let index = 0; index < left.rows.length; index++) {
    if (left.rows[index][0] !== right.rows[index][0]) fail(`Resolved package mode mismatch: ${name}: ${left.rows[index][2]}`);
    if (left.rows[index][1] !== right.rows[index][1]) fail(`Resolved package content hash mismatch: ${name}: ${left.rows[index][2]}`);
  }
  if (!equalInventory(left, right)) fail(`Resolved package inventory digest mismatch: ${name}`);
}

export async function compareGitPackageContent({ p, r, output, repository = process.cwd() }) {
  const root = await canonicalDirectory(repository, 'Repository');
  const left = inventoryFromGit(root, p); const right = inventoryFromGit(root, r);
  const result = { schemaVersion: 1, mode: 'git', packagePath, equal: left.tree === right.tree && equalInventory(left, right),
    p: { revision: p, tree: left.tree, fileCount: left.fileCount, inventorySha256: left.inventorySha256 },
    r: { revision: r, tree: right.tree, fileCount: right.fileCount, inventorySha256: right.inventorySha256 } };
  if (p === r) fail('P and R revisions must differ');
  const parent = decoder.decode(git(root, ['rev-list', '--parents', '-n', '1', r])).trim().split(/\s+/);
  if (parent.length !== 2 || parent[1] !== p) fail('R must be the direct child of P');
  const changed = decoder.decode(git(root, ['diff-tree', '--no-commit-id', '--name-only', '-r', r])).trim().split('\n').filter(Boolean);
  if (changed.length !== 1 || changed[0] !== 'README.md') fail('P to R must change only README.md');
  const destination = await outputDirectory(output, root);
  await writeFile(path.join(destination, 'p.inventory.tsv'), left.text, 'utf8');
  await writeFile(path.join(destination, 'r.inventory.tsv'), right.text, 'utf8');
  await writeFile(path.join(destination, 'p-tree.txt'), `${left.tree}\n`); await writeFile(path.join(destination, 'r-tree.txt'), `${right.tree}\n`);
  await writeFile(path.join(destination, 'comparison.json'), json(result));
  if (!result.equal) fail('Git package tree, inventory, or digest mismatch');
  return result;
}

async function resolvedSnapshot(root, name) {
  const sourcePath = await regular(root, 'resolved-source.json', 'resolved-source');
  let source; try { source = JSON.parse(decoder.decode(await readFile(sourcePath))); } catch (error) { fail(`Invalid resolved-source.json: ${error.message}`); }
  if (source?.schemaVersion !== 1 || !source.packages || Object.keys(source.packages).sort(byteSort).join('\n') !== resolvedPackages.slice().sort(byteSort).join('\n')) fail(`${name} resolved package set mismatch`);
  const packages = {};
  for (const packageName of resolvedPackages) {
    const record = source.packages[packageName]; if (!record || record.source !== 'git' || !fullSha.test(record.revision || '') || record.symlink !== false || record.cacheMatches !== 1) fail(`${name} invalid resolved record: ${packageName}`);
    const packageFile = packageName === 'com.zwebglframework.runtime' ? 'resolved-package.json' : 'resolved-unitask-package.json';
    const expectedInventory = packageName === 'com.zwebglframework.runtime' ? 'resolved-package.inventory.tsv' : 'resolved-unitask.inventory.tsv';
    const expectedOrigin = packageName === 'com.zwebglframework.runtime' ? frameworkOrigin : unitaskOrigin;
    const expectedSubdirectory = packageName === 'com.zwebglframework.runtime' ? packagePath : 'src/UniTask/Assets/Plugins/UniTask';
    const expectedRevision = packageName === 'com.zwebglframework.runtime' ? record.revision : unitaskRevision;
    const expectedVersion = `${expectedOrigin}?path=${packageName === 'com.zwebglframework.runtime' ? '/' : ''}${expectedSubdirectory}#${expectedRevision}`;
    const inventoryName = safeRelative(record.inventoryFile, `${name} inventory filename`);
    if (inventoryName !== expectedInventory || record.cacheIsolation !== 'global-cache-observed' ||
        !Array.isArray(record.localPaths) || record.localPaths.length || !Array.isArray(record.friendGrants) || record.friendGrants.length ||
        !/^[a-f0-9]{64}$/.test(record.inventorySha256 || '') || !Number.isInteger(record.fileCount) ||
        record.origin !== expectedOrigin || record.subdirectory !== expectedSubdirectory || record.version !== expectedVersion ||
        record.cachePath !== `Library/PackageCache/${packageName}@${record.revision.slice(0, 10)}`) fail(`${name} invalid persisted record: ${packageName}`);
    const inventoryBytes = await readFile(await regular(root, inventoryName, `${name} inventory`));
    const inventory = parseInventory(inventoryBytes, `${name} ${packageName} inventory`);
    if (record.inventorySha256 !== inventory.inventorySha256 || record.fileCount !== inventory.fileCount) fail(`${name} persisted inventory digest mismatch: ${packageName}`);
    const packageBytes = await readFile(await regular(root, packageFile, `${name} package identity`));
    let identity; try { identity = JSON.parse(decoder.decode(packageBytes)); } catch (error) { fail(`${name} invalid package identity: ${packageName}`); }
    const expectedPackageVersion = packageName === 'com.zwebglframework.runtime' ? '0.1.0' : '2.5.11';
    if (identity?.name !== packageName || identity?.version !== expectedPackageVersion) fail(`${name} package identity mismatch: ${packageName}`);
    const packageLine = inventory.rows.find(row => row[2] === 'package.json');
    if (!packageLine || packageLine[2] !== 'package.json' || packageLine[1] !== digest(packageBytes)) fail(`${name} package.json content hash mismatch: ${packageName}`);
    packages[packageName] = { inventory, inventoryName, packageFile };
  }
  return { packages, source };
}
export async function compareResolvedPackageContent({ resolvedP, resolvedR, output, repository = process.cwd() }) {
  const repo = await canonicalDirectory(repository, 'Repository');
  const leftRoot = await canonicalDirectory(resolvedP, 'Resolved P evidence'); const rightRoot = await canonicalDirectory(resolvedR, 'Resolved R evidence');
  if ([leftRoot, rightRoot].some(root => within(root, repo) || within(repo, root))) fail('Resolved evidence roots must be external to repository');
  if (leftRoot === rightRoot) fail('Resolved P and R evidence roots must differ');
  const left = await resolvedSnapshot(leftRoot, 'P'); const right = await resolvedSnapshot(rightRoot, 'R');
  const pRevision = left.source.packages['com.zwebglframework.runtime'].revision;
  const rRevision = right.source.packages['com.zwebglframework.runtime'].revision;
  if (pRevision === rRevision) fail('Resolved P and R runtime revisions must differ');
  for (const name of resolvedPackages) requireResolvedEquality(left.packages[name].inventory, right.packages[name].inventory, name);
  const comparisons = Object.fromEntries(resolvedPackages.map(name => [name, true]));
  const result = { schemaVersion: 1, mode: 'resolved', equal: Object.values(comparisons).every(Boolean), packages: comparisons };
  if (output) {
    const destination = await outputDirectory(output, repo);
    for (const name of resolvedPackages) {
      const safe = name.replaceAll('.', '-');
      await writeFile(path.join(destination, `p-${safe}.inventory.tsv`), left.packages[name].inventory.text, 'utf8');
      await writeFile(path.join(destination, `r-${safe}.inventory.tsv`), right.packages[name].inventory.text, 'utf8');
    }
    await writeFile(path.join(destination, 'comparison.json'), json(result));
  }
  if (!result.equal) fail('Resolved package inventory or digest mismatch');
  return result;
}
export const comparePackageContent = compareGitPackageContent;
export const compareResolvedContent = compareResolvedPackageContent;

function parseArgs(values) {
  const allowed = new Set(['p', 'r', 'output', 'resolved-p', 'resolved-r']); const parsed = {};
  if (values.length % 2) fail('Arguments must be option/value pairs');
  for (let i = 0; i < values.length; i += 2) {
    const key = values[i]?.replace(/^--/, ''); if (!values[i]?.startsWith('--') || !allowed.has(key) || !values[i + 1] || parsed[key]) fail(`Invalid argument: ${values[i]}`);
    parsed[key] = values[i + 1];
  }
  const gitMode = parsed.p && parsed.r; const resolvedMode = parsed['resolved-p'] && parsed['resolved-r'];
  if (gitMode === resolvedMode || (gitMode && !parsed.output) || (!gitMode && parsed.output && !resolvedMode) || Object.keys(parsed).some(key => (gitMode && key.startsWith('resolved')) || (resolvedMode && ['p', 'r'].includes(key)))) fail('Usage: compare-package-content.mjs --p <sha> --r <sha> --output <dir> | --resolved-p <dir> --resolved-r <dir> [--output <dir>]');
  return { ...parsed, mode: gitMode ? 'git' : 'resolved' };
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  Promise.resolve().then(() => { const args = parseArgs(process.argv.slice(2)); return args.mode === 'git'
    ? compareGitPackageContent({ p: args.p, r: args.r, output: args.output })
    : compareResolvedPackageContent({ resolvedP: args['resolved-p'], resolvedR: args['resolved-r'], output: args.output });
  }).then(result => console.log(JSON.stringify(result))).catch(error => { console.error(error.message); process.exitCode = 1; });
}
