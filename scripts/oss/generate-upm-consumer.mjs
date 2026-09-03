#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { cp, lstat, mkdir, readFile, readdir, realpath, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const unitaskRevision = 'e5acc106ee196bc5a32fb14cdf2987b0f96d11e0';
export const frameworkName = 'com.zwebglframework.runtime';
export const frameworkOrigin = 'https://github.com/diegozhou114-cloud/frameloom.git';
export const frameworkPackagePath = 'Packages/com.zwebglframework.runtime';
export const unitaskName = 'com.cysharp.unitask';
export const unitaskOrigin = 'https://github.com/Cysharp/UniTask.git';
export const unitaskPackagePath = 'src/UniTask/Assets/Plugins/UniTask';
export const frameworkUrl = revision => `${frameworkOrigin}?path=/${frameworkPackagePath}#${revision}`;
export const unitaskUrl = `${unitaskOrigin}?path=${unitaskPackagePath}#${unitaskRevision}`;
export const contractSources = [
  'CommandRecorderPublicContractTests.cs',
  'PublicApiContractTestSupport.cs',
  'SessionPresentationBridgePublicContractTests.cs',
];
export const consumerAsmdef = {
  name: 'ZWebGLFrameWork.PublicApiContract.Tests',
  rootNamespace: 'ZWebGLFrameWork.PublicApiContract.Tests',
  references: ['ZWebGLFrameWork.Combat', 'ZWebGLFrameWork.SimulationCore'],
  includePlatforms: ['Editor'],
  excludePlatforms: [],
  allowUnsafeCode: false,
  overrideReferences: true,
  precompiledReferences: ['nunit.framework.dll'],
  autoReferenced: false,
  defineConstraints: ['UNITY_INCLUDE_TESTS'],
  versionDefines: [],
  noEngineReferences: false,
};

const fullSha = /^[a-f0-9]{40}$/;
const forbiddenSource = /InternalsVisibleTo|BindingFlags|\b(?:reflection|dynamic)\b|Assets\/Scripts|Packages\/com\.zwebglframework\.runtime/i;
const friendAssembly = 'ZWebGLFrameWork.PublicApiContract.Tests';
const projectVersion = 'm_EditorVersion: 2022.3.62f3c1\nm_EditorVersionWithRevision: 2022.3.62f3c1 (1623fc0bbb97)\n';
const sha256 = value => createHash('sha256').update(value).digest('hex');
const json = value => JSON.stringify(value, null, 2) + '\n';

function inside(child, parent) {
  const relative = path.relative(parent, child);
  return relative !== '' && relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}

export async function generateConsumer({ root, output, frameworkRevision }) {
  if (!fullSha.test(frameworkRevision || '')) throw new Error('Framework revision must be a full lowercase 40-character SHA');
  const repositoryInput = path.resolve(root);
  const repository = await realpath(repositoryInput);
  const destination = path.resolve(output);
  const destinationParent = await realpath(path.dirname(destination));
  const canonicalDestination = path.join(destinationParent, path.basename(destination));
  if (inside(destination, repositoryInput) || destination === repositoryInput ||
      inside(canonicalDestination, repository) || canonicalDestination === repository)
    throw new Error('Consumer output must be outside the repository');
  try {
    const info = await lstat(destination);
    if (info.isSymbolicLink()) throw new Error('Consumer output must not be a symlink');
    if (!info.isDirectory() || (await readdir(destination)).length) throw new Error('Consumer output must be a new empty directory');
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }
  const template = await readFile(path.join(repository, 'ci/upm/manifest.template.json'), 'utf8');
  if ((template.match(/__FRAMEWORK_REVISION__/g) || []).length !== 1 ||
      (template.match(/__UNITASK_REVISION__/g) || []).length !== 1)
    throw new Error('Manifest template placeholders are invalid');
  const manifest = template.replace('__FRAMEWORK_REVISION__', frameworkRevision)
    .replace('__UNITASK_REVISION__', unitaskRevision);
  const parsedManifest = JSON.parse(manifest);
  const expectedDependencies = { [unitaskName]: unitaskUrl, 'com.unity.test-framework': '1.4.6',
    [frameworkName]: frameworkUrl(frameworkRevision) };
  if (JSON.stringify(parsedManifest.dependencies) !== JSON.stringify(expectedDependencies))
    throw new Error('Manifest template dependency contract mismatch');
  const sourceRoot = path.join(repository, 'Assets/Tests/PublicApiContract');
  const testRoot = path.join(destination, 'Assets/PublicApiContract');
  await mkdir(path.join(destination, 'Packages'), { recursive: true });
  await mkdir(path.join(destination, 'ProjectSettings'), { recursive: true });
  await mkdir(testRoot, { recursive: true });
  await writeFile(path.join(destination, 'Packages/manifest.json'), manifest, 'utf8');
  await writeFile(path.join(destination, 'ProjectSettings/ProjectVersion.txt'), projectVersion, 'utf8');
  await writeFile(path.join(testRoot, `${consumerAsmdef.name}.asmdef`),
    JSON.stringify(consumerAsmdef, null, 2) + '\n', 'utf8');
  for (const name of contractSources) {
    const source = path.join(sourceRoot, name);
    const info = await lstat(source);
    if (!info.isFile() || info.isSymbolicLink()) throw new Error(`Invalid contract source: ${name}`);
    const text = await readFile(source, 'utf8');
    if (forbiddenSource.test(text)) throw new Error(`Contract source requires internals or host paths: ${name}`);
    await cp(source, path.join(testRoot, name), { dereference: false, errorOnExist: true });
  }
  return { output: destination, manifest: parsedManifest, asmdef: consumerAsmdef,
    copiedSources: [...contractSources] };
}

async function inventory(root) {
  const rows = []; const friendGrants = [];
  async function visit(directory, relative = '') {
    const entries = await readdir(directory, { withFileTypes: true });
    entries.sort((a, b) => Buffer.compare(Buffer.from(a.name), Buffer.from(b.name)));
    for (const entry of entries) {
      const rel = relative ? `${relative}/${entry.name}` : entry.name;
      if (/[\\\t\r\n]/.test(rel) || rel.split('/').some(part => !part || part === '.' || part === '..'))
        throw new Error(`Unsafe resolved package path: ${rel}`);
      const target = path.join(directory, entry.name); const info = await lstat(target);
      if (info.isSymbolicLink()) throw new Error(`Resolved package symlink: ${rel}`);
      if (info.isDirectory()) { await visit(target, rel); continue; }
      if (!info.isFile()) throw new Error(`Resolved package special node: ${rel}`);
      const bytes = await readFile(target); const mode = info.mode & 0o111 ? '100755' : '100644';
      rows.push({ rel, line: `${mode}\t${sha256(bytes)}\t${rel}\n` });
      const utf8Friend = Buffer.from(friendAssembly);
      const utf16Friend = Buffer.from(friendAssembly, 'utf16le');
      if (bytes.includes(utf8Friend) || bytes.includes(utf16Friend)) friendGrants.push(rel);
    }
  }
  await visit(root);
  rows.sort((a, b) => Buffer.compare(Buffer.from(a.rel), Buffer.from(b.rel)));
  return { text: rows.map(row => row.line).join(''), fileCount: rows.length, friendGrants };
}

export async function captureResolvedPackages({ project, evidenceRoot, frameworkRevision }) {
  if (!fullSha.test(frameworkRevision || '')) throw new Error('Framework revision must be a full lowercase 40-character SHA');
  const projectPath = path.resolve(project); const projectInfo = await lstat(projectPath);
  const projectCanonical = await realpath(projectPath);
  if (!projectInfo.isDirectory() || projectInfo.isSymbolicLink() || projectCanonical !== projectPath)
    throw new Error('Resolved project must be a canonical regular directory');
  const library = path.join(projectCanonical, 'Library');
  const cacheRoot = path.join(library, 'PackageCache');
  for (const [target, label] of [[library, 'Library'], [cacheRoot, 'Library/PackageCache']]) {
    const info = await lstat(target); const canonical = await realpath(target);
    if (!info.isDirectory() || info.isSymbolicLink() || canonical !== target)
      throw new Error(`Resolved ${label} must be a project-local directory`);
  }
  const lock = JSON.parse(await readFile(path.join(projectCanonical, 'Packages/packages-lock.json'), 'utf8'));
  const names = await readdir(cacheRoot);
  const specs = [
    { name: frameworkName, revision: frameworkRevision, url: frameworkUrl(frameworkRevision),
      origin: frameworkOrigin, subdirectory: frameworkPackagePath, version: '0.1.0',
      packageFile: 'resolved-package.json', inventoryFile: 'resolved-package.inventory.tsv' },
    { name: unitaskName, revision: unitaskRevision, url: unitaskUrl, origin: unitaskOrigin,
      subdirectory: unitaskPackagePath, version: '2.5.11',
      packageFile: 'resolved-unitask-package.json', inventoryFile: 'resolved-unitask.inventory.tsv' },
  ];
  const packages = {};
  for (const spec of specs) {
    const matches = names.filter(name => name.startsWith(`${spec.name}@`));
    if (matches.length !== 1) throw new Error(`Expected one resolved cache for ${spec.name}; found ${matches.length}`);
    const cache = path.join(cacheRoot, matches[0]); const info = await lstat(cache);
    if (!info.isDirectory() || info.isSymbolicLink() || matches[0] !== `${spec.name}@${spec.revision.slice(0, 10)}`)
      throw new Error(`Resolved cache identity mismatch: ${spec.name}`);
    const canonical = await realpath(cache);
    if (!inside(canonical, cacheRoot)) throw new Error(`Resolved cache escapes project: ${spec.name}`);
    const entry = lock.dependencies?.[spec.name];
    if (!entry || entry.source !== 'git' || entry.version !== spec.url || entry.hash !== spec.revision)
      throw new Error(`Resolved lock source mismatch: ${spec.name}`);
    const packageRaw = await readFile(path.join(cache, 'package.json'));
    const identity = JSON.parse(packageRaw);
    if (identity.name !== spec.name || identity.version !== spec.version)
      throw new Error(`Resolved package identity mismatch: ${spec.name}`);
    const found = await inventory(cache); await writeFile(path.join(evidenceRoot, spec.packageFile), packageRaw);
    await writeFile(path.join(evidenceRoot, spec.inventoryFile), found.text, 'utf8');
    const parsed = new URL(entry.version);
    packages[spec.name] = { source: entry.source, version: entry.version,
      origin: `${parsed.origin}${parsed.pathname}`, subdirectory: parsed.searchParams.get('path')?.replace(/^\//, ''),
      revision: entry.hash, cachePath: `Library/PackageCache/${matches[0]}`, cacheMatches: matches.length,
      cacheIsolation: 'global-cache-observed', symlink: false, localPaths: [], friendGrants: found.friendGrants,
      inventoryFile: spec.inventoryFile, inventorySha256: sha256(found.text), fileCount: found.fileCount };
  }
  const result = { schemaVersion: 1, packages };
  await writeFile(path.join(evidenceRoot, 'resolved-source.json'), json(result), 'utf8');
  return result;
}

function args(values) {
  const parsed = {};
  for (let index = 0; index < values.length; index += 2) {
    const key = values[index]; const value = values[index + 1];
    if (!key?.startsWith('--') || value === undefined) throw new Error(`Invalid argument: ${key}`);
    parsed[key.slice(2)] = value;
  }
  return parsed;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const parsed = args(process.argv.slice(2)); const revision = parsed['framework-revision'];
  const operation = parsed['capture-project']
    ? captureResolvedPackages({ project: parsed['capture-project'], evidenceRoot: parsed['evidence-root'], frameworkRevision: revision })
    : generateConsumer({ root: parsed.root, output: parsed.output, frameworkRevision: revision });
  operation.then(result => console.log(JSON.stringify(result)))
    .catch(error => { console.error(error.message); process.exitCode = 1; });
}
