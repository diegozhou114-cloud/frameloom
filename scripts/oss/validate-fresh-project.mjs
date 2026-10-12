#!/usr/bin/env node
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { lstat, readFile, readdir, realpath, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { consumerAsmdef, contractSources, frameworkName, frameworkOrigin, frameworkPackagePath, frameworkUrl,
  unitaskName, unitaskOrigin, unitaskPackagePath, unitaskRevision, unitaskUrl } from './generate-upm-consumer.mjs';
const forbiddenSource = /InternalsVisibleTo|BindingFlags|\b(?:reflection|dynamic)\b|Assets\/Scripts|Packages\/com\.zwebglframework\.runtime/i;
const marker = 'Test run completed. Exiting with code 0 (Ok). Run completed.';
const shutdown = /Application::Terminate|Application::CoreShutdown|JobSystem::ShutdownJobThreads/g;
const unobserved = /buffered presentation failed|UniTaskScheduler[.:]PublishUnobservedTaskException|ExceptionHolder[.:]Finalize/gi;
const expectedCases = [
  'CommandRecorderPublicContractTests.BufferedRecord_MatchesTickResultCommandsAndTerminalFields', 'CommandRecorderPublicContractTests.BufferedRecord_OwnsCommandsAcrossParentClearReuseAndDispose',
  'CommandRecorderPublicContractTests.BufferedRecord_RejectsInvalidOutputsWithoutReplayMutation', 'SessionPresentationBridgePublicContractTests.BufferedApply_BufferReuseUpdatesOnlyAfterExplicitApply',
  'SessionPresentationBridgePublicContractTests.BufferedApply_OutputClearDoesNotChangeLaterSample', 'SessionPresentationBridgePublicContractTests.BufferedApply_OutputDisposePreservesConsumedValuesAndViews',
  'SessionPresentationBridgePublicContractTests.BufferedApply_PublishedOutputCreatesAndSamplesView',
].map(name => `ZWebGLFrameWork.PublicApiContract.Tests.${name}`).sort();
const frameworkDependencies = { 'com.unity.ugui': '1.0.0', 'com.unity.modules.audio': '1.0.0', 'com.unity.modules.assetbundle': '1.0.0', 'com.unity.modules.unitywebrequest': '1.0.0', 'com.unity.modules.jsonserialize': '1.0.0', 'com.unity.modules.physics': '1.0.0' };
const sameFrameworkDependencies = value => Object.keys(value ?? {}).length === Object.keys(frameworkDependencies).length && Object.entries(frameworkDependencies).every(([name, version]) => value?.[name] === version);
const equal = (left, right) => JSON.stringify(left) === JSON.stringify(right), sha256 = value => createHash('sha256').update(value).digest('hex');
async function regular(root, name) {
  const target = path.join(root, name); const info = await lstat(target);
  if (!info.isFile() || info.isSymbolicLink()) throw new Error('not a regular file');
  return readFile(target); }
async function json(root, name, errors) { try { return JSON.parse((await regular(root, name)).toString('utf8')); }
  catch (error) { errors.push(`Cannot read ${name}: ${error.message}`); return null; } }
async function text(root, name, errors) { try { return (await regular(root, name)).toString('utf8'); }
  catch (error) { errors.push(`Cannot read ${name}: ${error.message}`); return ''; } }
function inside(child, parent) { const relative = path.relative(parent, child); return relative === '' ||
  (relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative)); }
function validateManifest(document, revision, errors) { const expected = { [unitaskName]: unitaskUrl, 'com.unity.test-framework': '1.4.6', [frameworkName]: frameworkUrl(revision) };
  if (!equal(document?.dependencies, expected)) errors.push('Framework manifest dependency mismatch'); }
function validateLock(document, revision, errors) { const framework = document?.dependencies?.[frameworkName];
  if (!framework || framework.version !== frameworkUrl(revision) || framework.source !== 'git' ||
      framework.hash !== revision || framework.depth !== 0 ||
      !sameFrameworkDependencies(framework.dependencies)) errors.push('Framework lock mismatch');
  const unitask = document?.dependencies?.[unitaskName];
  if (!unitask || unitask.version !== unitaskUrl || unitask.source !== 'git' ||
      unitask.hash !== unitaskRevision || unitask.depth !== 0) errors.push('UniTask lock mismatch');
}
async function validateInventory(root, record, packageFile, errors) {
  try {
    const inventory = await regular(root, record.inventoryFile), packageBytes = await regular(root, packageFile);
    const lines = inventory.toString('utf8').split('\n');
    if (lines.at(-1) !== '') throw new Error('missing final newline');
    lines.pop(); const parsed = lines.map(line => line.match(/^(100644|100755)\t([a-f0-9]{64})\t(.+)$/));
    const paths = parsed.map(match => match?.[3]);
    const sorted = [...paths].sort((a, b) => Buffer.compare(Buffer.from(a || ''), Buffer.from(b || '')));
    const safe = parsed.every(match => match && !/[\\\t\r\n]/.test(match[3]) && !path.posix.isAbsolute(match[3]) &&
      !match[3].split('/').some(part => !part || part === '.' || part === '..'));
    const packageLine = parsed.find(match => match?.[3] === 'package.json');
    if (!safe || !equal(paths, sorted) || new Set(paths).size !== paths.length || !lines.length ||
        record.fileCount !== lines.length || record.inventorySha256 !== sha256(inventory) ||
        packageLine?.[2] !== sha256(packageBytes)) throw new Error('content mismatch');
  } catch { errors.push('Resolved package inventory mismatch'); }
}
async function validateResolved(root, document, revision, errors) {
  const specs = [
    { name: frameworkName, revision, url: frameworkUrl(revision), origin: frameworkOrigin, subdirectory: frameworkPackagePath,
      packageFile: 'resolved-package.json', version: '0.1.0', inventoryFile: 'resolved-package.inventory.tsv' },
    { name: unitaskName, revision: unitaskRevision, url: unitaskUrl, origin: unitaskOrigin, subdirectory: unitaskPackagePath,
      packageFile: 'resolved-unitask-package.json', version: '2.5.11', inventoryFile: 'resolved-unitask.inventory.tsv' },
  ];
  if (document?.schemaVersion !== 1 ||
      !equal(Object.keys(document?.packages || {}).sort(), specs.map(spec => spec.name).sort()))
    errors.push('Resolved package source mismatch');
  for (const spec of specs) {
    const value = document?.packages?.[spec.name];
    if (!value || value.source !== 'git' || value.version !== spec.url || value.origin !== spec.origin ||
        value.subdirectory !== spec.subdirectory || value.revision !== spec.revision ||
        value.cachePath !== `Library/PackageCache/${spec.name}@${spec.revision.slice(0, 10)}` ||
        value.cacheMatches !== 1 || value.cacheIsolation !== 'global-cache-observed' || value.symlink !== false ||
        !Array.isArray(value.localPaths) || value.localPaths.length || !Array.isArray(value.friendGrants) ||
        value.inventoryFile !== spec.inventoryFile) errors.push('Resolved package source mismatch');
    if (value?.friendGrants?.length) errors.push('Public contract friend access detected');
    const identity = await json(root, spec.packageFile, errors);
    if (!identity || identity.name !== spec.name || identity.version !== spec.version)
      errors.push('Resolved package identity mismatch');
    if (value) await validateInventory(root, value, spec.packageFile, errors);
  }
}
async function validateContracts(root, repositoryRoot, errors) {
  if (!equal(await json(root, 'contract-asmdef.json', errors), consumerAsmdef)) errors.push('Contract asmdef mismatch');
  let names = []; const sourceRoot = path.join(root, 'contract-sources');
  try {
    const info = await lstat(sourceRoot);
    if (!info.isDirectory() || info.isSymbolicLink() || await realpath(sourceRoot) !== sourceRoot)
      throw new Error('not a canonical directory');
    names = await readdir(sourceRoot);
  }
  catch (error) { errors.push(`Cannot read contract-sources: ${error.message}`); return; }
  if (!equal(names.sort(), [...contractSources].sort())) errors.push('Contract source set mismatch');
  for (const name of contractSources) {
    try {
      const target = path.join(sourceRoot, name); const info = await lstat(target);
      if (!info.isFile() || info.isSymbolicLink()) { errors.push(`Contract source must be a regular file: ${name}`); continue; }
      const actual = await readFile(target, 'utf8');
      if (forbiddenSource.test(actual)) errors.push(`Contract source violation: ${name}`);
      const expected = await readFile(path.join(repositoryRoot, 'Assets/Tests/PublicApiContract', name), 'utf8');
      if (actual !== expected) errors.push(`Contract source mismatch: ${name}`);
    } catch (error) { errors.push(`Cannot read contract source ${name}: ${error.message}`); }
  }
}
function rootAttributes(xml) { const tag = xml.match(/<test-run\b([^>]*)>/)?.[1] || ''; return Object.fromEntries(
  [...tag.matchAll(/([\w-]+)="([^"]*)"/g)].map(match => [match[1], match[2]])); }
async function validateUnity(root, errors) {
  const result = await json(root, 'unity-result.json', errors);
  const xml = await text(root, 'results.xml', errors); const log = await text(root, 'unity.log', errors);
  const sample = await text(root, 'unity-timeout.sample.txt', errors);
  const ledger = await text(root, 'signal-ledger.txt', errors); const recordedCases = await json(root, 'test-cases.json', errors);
  const attributes = rootAttributes(xml); const counts = ['total', 'passed', 'failed', 'inconclusive', 'skipped'];
  const run = Object.fromEntries(counts.map(name => [name, Number(attributes[name] ?? -1)]));
  if (!result || result.schemaVersion !== 1 || result.editorVersion !== '2022.3.62f3c1' ||
      result.exitCode !== 0 || result.compileErrors !== 0 || /error CS\d+|Scripts have compiler errors/i.test(log))
    errors.push('Unity result mismatch');
  if (attributes.result !== 'Passed' || !equal(run, { total: 7, passed: 7, failed: 0, inconclusive: 0, skipped: 0 }) ||
      !equal(result?.testRun, run)) errors.push('Public contract result mismatch');
  const parser = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../audit/read-test-results.py');
  const parsed = spawnSync('python3', [parser, path.join(root, 'results.xml')], { encoding: 'utf8' });
  let actualCases = [];
  try { if (parsed.status !== 0) throw new Error(); actualCases = JSON.parse(parsed.stdout); }
  catch { errors.push('Public contract XML mismatch'); }
  if (!equal(actualCases, recordedCases) || !equal(actualCases.map(value => value.name).sort(), expectedCases) ||
      actualCases.some(value => value.result !== 'Passed')) errors.push('Public contract XML mismatch');
  const lifecycle = result?.lifecycle; const completion = lifecycle?.completionEpoch;
  const xmlInfo = await stat(path.join(root, 'results.xml')).catch(() => null);
  const postExitEpoch = Math.floor((xmlInfo?.mtimeMs ?? -1000) / 1000);
  if (!lifecycle || !Number.isInteger(lifecycle.startEpoch) || !Number.isInteger(completion) ||
      !Number.isInteger(lifecycle.exitEpoch) || completion < lifecycle.startEpoch || completion > lifecycle.exitEpoch ||
      lifecycle.exitEpoch - lifecycle.startEpoch > 1200 ||
      lifecycle.completionToExitSeconds !== lifecycle.exitEpoch - completion ||
      lifecycle.completionToExitSeconds < 0 || lifecycle.completionToExitSeconds > 600 ||
      lifecycle.completionMarkerCount !== 1 || typeof lifecycle.postExitAdjudicated !== 'boolean' ||
      (lifecycle.postExitAdjudicated ? lifecycle.completionSource !== 'xml-mtime-post-exit' || completion !== postExitEpoch
        : lifecycle.completionSource !== 'poll') ||
      lifecycle.watchdogCount !== 0 || lifecycle.watchdogReason !== 'none' || lifecycle.signalCount !== 0 || ledger !== '' ||
      sample !== '' || lifecycle.shutdownSpinMatches !== 0 || lifecycle.unobservedExceptionMatches !== 0)
    errors.push('Unity lifecycle mismatch');
  const markerCount = log.split(marker).length - 1;
  const shutdownCount = (log.match(shutdown) || []).length + (sample.match(shutdown) || []).length;
  const unobservedCount = (log.match(unobserved) || []).length + (sample.match(unobserved) || []).length;
  if (markerCount !== 1 || markerCount !== lifecycle?.completionMarkerCount || shutdownCount || unobservedCount ||
      shutdownCount !== lifecycle?.shutdownSpinMatches || unobservedCount !== lifecycle?.unobservedExceptionMatches)
    errors.push('Unity lifecycle log mismatch');
  if (!result || result.graphics?.backend !== 'Metal' || result.graphics?.device !== 'Apple M3 Pro (high power)' ||
      !/^GfxDevice: creating device client$/m.test(log) || !/^Using device Apple M3 Pro \(high power\)$/m.test(log) ||
      !/^Initializing Metal device caps: Apple M3 Pro$/m.test(log))
    errors.push('Unity graphics mismatch');
  const cleanup = await json(root, 'cleanup.json', errors); const process = result?.process;
  if (!process || !Number.isInteger(process.pid) || process.pid <= 1 || process.projectPath !== cleanup?.projectPath ||
      process.observedCwd !== cleanup?.projectPath) errors.push('Unity process identity mismatch');
}
async function validateCleanup(root, repositoryRoot, errors) {
  const cleanup = await json(root, 'cleanup.json', errors); let present = true;
  try { await lstat(cleanup?.projectPath || ''); } catch (error) { if (error.code === 'ENOENT') present = false; }
  if (!cleanup || !path.isAbsolute(cleanup.projectPath || '') || !/^frameloom-upm\.[A-Za-z0-9]+$/.test(path.basename(cleanup.projectPath || '')) ||
      cleanup.removed !== true || !Array.isArray(cleanup.residualPaths) || cleanup.residualPaths.length || present)
    errors.push('Consumer cleanup mismatch');
  if (cleanup?.projectPath && inside(path.resolve(cleanup.projectPath), path.resolve(repositoryRoot)))
    errors.push('Consumer project path is inside the repository');
}
export async function validateFreshProject({ evidenceRoot, repositoryRoot, frameworkRevision, allowFixture = false }) {
  const errors = [];
  if (!/^[a-f0-9]{40}$/.test(frameworkRevision || '')) return { errors: ['Expected framework revision must be a full lowercase SHA'] };
  const requestedRoot = path.resolve(evidenceRoot); const rootInfo = await lstat(requestedRoot);
  const root = await realpath(requestedRoot); const repository = await realpath(repositoryRoot);
  if (!rootInfo.isDirectory() || rootInfo.isSymbolicLink() || root !== requestedRoot)
    return { errors: ['Evidence root must be a canonical regular directory'] };
  const input = await json(root, 'input.json', errors);
  const shared = input?.schemaVersion === 1 && input.frameworkRevision === frameworkRevision &&
    input.initialLibraryPresent === false && input.cacheIsolation === 'global-cache-observed';
  const formal = shared && ['private-authenticated', 'public-anonymous'].includes(input.access) &&
    input.fixtureOnly === false && input.formalReleaseEvidence === true;
  const fixture = shared && input.access === 'fixture-only' && input.fixtureOnly === true &&
    input.formalReleaseEvidence === false;
  if (!formal && !(allowFixture && fixture))
    errors.push('Fresh-project input mismatch');
  validateManifest(await json(root, 'manifest.json', errors), frameworkRevision, errors);
  validateLock(await json(root, 'packages-lock.json', errors), frameworkRevision, errors);
  await validateResolved(root, await json(root, 'resolved-source.json', errors), frameworkRevision, errors);
  await validateContracts(root, repository, errors); await validateUnity(root, errors);
  await validateCleanup(root, repository, errors);
  return { errors, fixtureOnly: fixture };
}
function args(values) {
  const parsed = {};
  for (let index = 0; index < values.length; index += 2) {
    const key = values[index]; const value = values[index + 1];
    if (!key?.startsWith('--') || value === undefined) throw new Error(`Invalid argument: ${key}`);
    parsed[key.slice(2)] = value; }
  if (!parsed['evidence-root'] || !parsed['repository-root'] || !parsed['framework-revision'])
    throw new Error('Usage: validate-fresh-project.mjs --evidence-root <path> --repository-root <path> --framework-revision <sha>');
  return { evidenceRoot: parsed['evidence-root'], repositoryRoot: parsed['repository-root'],
    frameworkRevision: parsed['framework-revision'], allowFixture: parsed['allow-fixture'] === 'true' }; }
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  validateFreshProject(args(process.argv.slice(2))).then(result => {
    if (result.errors.length) { console.error([...new Set(result.errors)].join('\n')); process.exitCode = 1; }
    else console.log(`[validate-fresh-project] PASS${result.fixtureOnly ? ' fixture-only' : ''}`);
  }).catch(error => { console.error(error.message); process.exitCode = 1; });
