#!/usr/bin/env node
import { existsSync } from 'node:fs';
import { readdir, readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const require = createRequire(new URL('../../ci/package.json', import.meta.url));
const { parseDocument } = require('yaml');
export const canonicalMappings = [
  ['Assets/Scripts/Core/Tests.meta', 'Assets/Tests/Core.meta'],
  ['Assets/Scripts/Core/Tests/', 'Assets/Tests/Core/'],
  ['Assets/Scripts/Combat/Tests.meta', 'Assets/Tests/Combat.meta'],
  ['Assets/Scripts/Combat/Tests/', 'Assets/Tests/Combat/'],
  ['Assets/Scripts.meta', 'Packages/com.zwebglframework.runtime/Runtime.meta'],
  ['Assets/Scripts/', 'Packages/com.zwebglframework.runtime/Runtime/'],
  ['Packages/com.zwebglframework.kcp2k/', 'Packages/com.zwebglframework.runtime/ThirdParty/Kcp2k/'],
  ['Packages/com.zwebglframework.nativewebsocket/', 'Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket/'],
];
const staleRoots = ['Assets/Scripts', 'Packages/com.zwebglframework.kcp2k', 'Packages/com.zwebglframework.nativewebsocket'];
const skippedDirectories = new Set(['.git', '.codegraph', 'Library', 'Logs', 'Temp', 'bin', 'node_modules', 'obj']);
function relativePath(value, label) {
  if (typeof value !== 'string' || value.length === 0)
    throw new Error(`Invalid ${label}: path must be nonempty`);
  const normalized = value.replaceAll('\\', '/');
  if (path.posix.isAbsolute(normalized) || normalized !== path.posix.normalize(normalized) ||
      normalized === '..' || normalized.startsWith('../'))
    throw new Error(`Invalid ${label}: ${value}`);
  return normalized;
}
function mappedPath(value, mode) {
  if (mode === 'identity') return value;
  for (const [source, destination] of canonicalMappings) {
    if (source.endsWith('/') && value.startsWith(source))
      return destination + value.slice(source.length);
    if (value === source) return destination;
  }
  return value;
}
async function requiredFile(directory, name) {
  const target = path.join(directory, name); let text;
  try { text = await readFile(target, 'utf8'); }
  catch (error) { if (error.code === 'ENOENT') throw new Error(`Baseline file missing: ${name}`);
    throw error; }
  if (!text.trim()) throw new Error(`Baseline file empty: ${name}`);
  return text;
}
function lines(text) { return text.endsWith('\n') ? text.slice(0, -1).split(/\r?\n/) : text.split(/\r?\n/); }
function trackedPath(value, index) {
  if (!value.startsWith('"')) return relativePath(value, 'baseline tracked path');
  if (!value.endsWith('"')) throw new Error(`Invalid baseline tracked-files.txt line ${index + 1}`);
  const bytes = [];
  for (let offset = 1; offset < value.length - 1; offset++) {
    if (value[offset] !== '\\') { bytes.push(value.charCodeAt(offset)); continue; }
    const octal = value.slice(offset + 1, offset + 4);
    if (!/^[0-7]{3}$/.test(octal)) throw new Error(`Invalid baseline tracked-files.txt line ${index + 1}`);
    bytes.push(Number.parseInt(octal, 8)); offset += 3;
  }
  try { return relativePath(new TextDecoder('utf-8', { fatal: true }).decode(Uint8Array.from(bytes)), 'baseline tracked path'); }
  catch { throw new Error(`Invalid baseline tracked-files.txt line ${index + 1}`); }
}
async function readBaseline(directory) {
  const metaRows = lines(await requiredFile(directory, 'meta.tsv'));
  const metas = metaRows.map((line, index) => {
    const fields = line.split('\t');
    if (fields.length !== 2 || !/^[a-f0-9]{32}$/i.test(fields[1]))
      throw new Error(`Invalid baseline meta.tsv line ${index + 1}: ${line}`);
    return { path: relativePath(fields[0], 'baseline meta path'), guid: fields[1].toLowerCase() };
  });
  const asmdefs = lines(await requiredFile(directory, 'asmdefs.ndjson')).map((line, index) => {
    let value;
    try { value = JSON.parse(line); }
    catch { throw new Error(`Invalid baseline asmdefs.ndjson line ${index + 1}: invalid JSON`); }
    if (!value || typeof value.asmdef !== 'object' || typeof value.asmdef.name !== 'string' ||
        !/^[a-f0-9]{32}$/i.test(value.guid || ''))
      throw new Error(`Invalid baseline asmdefs.ndjson line ${index + 1}: invalid record`);
    return { ...value, path: relativePath(value.path, 'baseline asmdef path'),
      guid: value.guid.toLowerCase() };
  });
  const tracked = lines(await requiredFile(directory, 'tracked-files.txt')).map(trackedPath);
  const unique = (values, label) => new Set(values).size === values.length || (() => { throw new Error(`Duplicate baseline ${label}`); })();
  unique(metas.map(value => value.path), 'meta path');
  unique(metas.map(value => value.guid), 'meta GUID');
  unique(asmdefs.map(value => value.path), 'asmdef path');
  unique(tracked, 'tracked path');
  const metaByPath = new Map(metas.map(value => [value.path, value.guid]));
  const trackedSet = new Set(tracked);
  for (const value of metas) if (!trackedSet.has(value.path))
    throw new Error(`Baseline meta is not tracked: ${value.path}`);
  for (const value of asmdefs) {
    if (!trackedSet.has(value.path) || metaByPath.get(`${value.path}.meta`) !== value.guid)
      throw new Error(`Baseline asmdef evidence mismatch: ${value.path}`);
  }
  return { metas, asmdefs, tracked };
}
function parseMeta(text, label) {
  const document = parseDocument(text, { uniqueKeys: true });
  if (document.errors.length) throw new Error(`Invalid meta YAML: ${label}`);
  const node = document.get('guid', true);
  const guid = String(node?.source ?? node?.value ?? '').trim().toLowerCase();
  if (!/^[a-f0-9]{32}$/.test(guid)) throw new Error(`Invalid meta GUID: ${label}`);
  return guid;
}
async function findMetas(root, current = '') {
  const found = [];
  for (const entry of await readdir(path.join(root, current), { withFileTypes: true })) {
    const relative = current ? `${current}/${entry.name}` : entry.name;
    if (entry.isDirectory() && !skippedDirectories.has(entry.name))
      found.push(...await findMetas(root, relative));
    else if (!entry.isDirectory() && relative.endsWith('.meta')) found.push(relative);
  }
  return found;
}
function canonicalJson(value) {
  if (Array.isArray(value)) return value.map(canonicalJson);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.keys(value).sort().map(key => [key, canonicalJson(value[key])]));
}
export async function validatePackageMove({ baseline, root, allowNewMeta = [], mode = 'canonical' }) {
  const errors = [];
  if (!['canonical', 'identity'].includes(mode))
    return { errors: [`Invalid validation mode: ${mode}`] };
  let expected;
  try { expected = await readBaseline(baseline); }
  catch (error) { return { errors: [error.message] }; }
  const allowed = new Set();
  try {
    for (const value of allowNewMeta) {
      const clean = relativePath(value, 'allowed meta path');
      if (!clean.endsWith('.meta')) throw new Error(`Allowed path is not a meta: ${clean}`);
      allowed.add(clean);
    }
  } catch (error) { return { errors: [error.message] }; }
  for (const source of expected.tracked) {
    const destination = mappedPath(source, mode);
    if (!existsSync(path.join(root, destination)))
      errors.push(`Mapped destination missing: ${destination}`);
    if (mode === 'canonical' && source !== destination &&
        existsSync(path.join(root, source)) && existsSync(path.join(root, destination)))
      errors.push(`Production file copied instead of moved: ${source} -> ${destination}`);
  }
  if (mode === 'canonical') for (const stale of staleRoots)
    if (existsSync(path.join(root, stale))) errors.push(`Stale old production root: ${stale}`);
  const actualMetas = new Map();
  const guidPaths = new Map();
  for (const relative of await findMetas(root)) {
    try {
      const guid = parseMeta(await readFile(path.join(root, relative), 'utf8'), relative);
      actualMetas.set(relative, guid);
      guidPaths.set(guid, [...(guidPaths.get(guid) || []), relative]);
    } catch (error) { errors.push(error.message); }
  }
  const expectedMetaPaths = new Set();
  for (const value of expected.metas) {
    const destination = mappedPath(value.path, mode);
    expectedMetaPaths.add(destination);
    if (!actualMetas.has(destination)) errors.push(`Missing baseline meta: ${destination}`);
    else if (actualMetas.get(destination) !== value.guid)
      errors.push(`GUID changed: ${destination} expected ${value.guid} actual ${actualMetas.get(destination)}`);
    if ((guidPaths.get(value.guid) || []).length !== 1)
      errors.push(`Baseline GUID must appear exactly once: ${value.guid}`);
  }
  for (const [guid, paths] of guidPaths)
    if (paths.length > 1) errors.push(`Duplicate GUID: ${guid} at ${paths.sort().join(', ')}`);
  for (const relative of actualMetas.keys())
    if (!expectedMetaPaths.has(relative) && !allowed.has(relative))
      errors.push(`Unexpected new meta: ${relative}`);
  for (const relative of allowed)
    if (!actualMetas.has(relative)) errors.push(`Allowed new meta is missing: ${relative}`);
  for (const value of expected.asmdefs) {
    const destination = mappedPath(value.path, mode);
    if (!existsSync(path.join(root, destination))) continue;
    try {
      const actual = JSON.parse(await readFile(path.join(root, destination), 'utf8'));
      if (JSON.stringify(canonicalJson(actual)) !== JSON.stringify(canonicalJson(value.asmdef)))
        errors.push(`ASMDEF semantics changed: ${destination}`);
    } catch { errors.push(`Invalid asmdef JSON: ${destination}`); }
  }
  return { errors, baselineMetaCount: expected.metas.length, asmdefCount: expected.asmdefs.length };
}
function parseArgs(values) {
  const options = { allowNewMeta: [] };
  for (let index = 0; index < values.length; index++) {
    const key = values[index];
    if (key === '--identity') { options.mode = 'identity'; continue; }
    const value = values[++index];
    if (!['--baseline', '--root', '--allow-new-meta'].includes(key) || value === undefined)
      throw new Error(`Invalid argument: ${key}`);
    if (key === '--allow-new-meta') options.allowNewMeta.push(value);
    else options[key.slice(2)] = value;
  }
  if (!options.baseline || !options.root) throw new Error('Usage: validate-package-move.mjs --baseline <directory> --root <root> [--allow-new-meta <path>] [--identity]');
  return options;
}
async function main() {
  const result = await validatePackageMove(parseArgs(process.argv.slice(2)));
  if (result.errors.length) for (const error of result.errors) console.error(error);
  else console.log(`[validate-package-move] PASS metas=${result.baselineMetaCount} asmdefs=${result.asmdefCount}`);
  if (result.errors.length) process.exitCode = 1;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main().catch(error => { console.error(error.message); process.exitCode = 1; });
