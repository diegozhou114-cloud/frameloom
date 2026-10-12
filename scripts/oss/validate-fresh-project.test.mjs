import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, realpath, rename, rm, symlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { consumerAsmdef, contractSources, unitaskRevision } from './generate-upm-consumer.mjs';
let validateFreshProject; let importError;
try { ({ validateFreshProject } = await import('./validate-fresh-project.mjs')); }
catch (error) { importError = error; }
const revision = '1'.repeat(40);
const frameworkName = 'com.zwebglframework.runtime';
const frameworkUrl = `https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.runtime#${revision}`;
const unitaskUrl = `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#${unitaskRevision}`;
const marker = 'Test run completed. Exiting with code 0 (Ok). Run completed.';
const frameworkDependencies = { 'com.unity.ugui': '1.0.0', 'com.unity.modules.audio': '1.0.0', 'com.unity.modules.assetbundle': '1.0.0', 'com.unity.modules.unitywebrequest': '1.0.0', 'com.unity.modules.jsonserialize': '1.0.0', 'com.unity.modules.physics': '1.0.0' };
const cases = ['CommandRecorderPublicContractTests.BufferedRecord_MatchesTickResultCommandsAndTerminalFields', 'CommandRecorderPublicContractTests.BufferedRecord_OwnsCommandsAcrossParentClearReuseAndDispose', 'CommandRecorderPublicContractTests.BufferedRecord_RejectsInvalidOutputsWithoutReplayMutation', 'SessionPresentationBridgePublicContractTests.BufferedApply_BufferReuseUpdatesOnlyAfterExplicitApply', 'SessionPresentationBridgePublicContractTests.BufferedApply_OutputClearDoesNotChangeLaterSample', 'SessionPresentationBridgePublicContractTests.BufferedApply_OutputDisposePreservesConsumedValuesAndViews', 'SessionPresentationBridgePublicContractTests.BufferedApply_PublishedOutputCreatesAndSamplesView'].map(name => `ZWebGLFrameWork.PublicApiContract.Tests.${name}`);
const firstCase = `<test-case fullname="${cases[0]}" result="Passed" />`;
const json = value => JSON.stringify(value, null, 2) + '\n';
const sha = value => createHash('sha256').update(value).digest('hex');
async function put(root, relative, value) { const target = path.join(root, relative); await mkdir(path.dirname(target), { recursive: true }); await writeFile(target, typeof value === 'string' ? value : json(value)); }
async function mutate(root, relative, callback) { const target = path.join(root, relative); const value = JSON.parse(await readFile(target)); callback(value); await writeFile(target, json(value)); }
async function replace(root, relative, from, to) { const target = path.join(root, relative);
  await writeFile(target, (await readFile(target, 'utf8')).replace(from, to)); }
function sourceRecord({ name, url, origin, subdirectory, revision: hash, inventoryFile, raw }) {
  const inventory = `100644\t${sha(raw)}\tpackage.json\n`;
  return { inventory, record: { source: 'git', version: url, origin, subdirectory, revision: hash,
    cachePath: `Library/PackageCache/${name}@${hash.slice(0, 10)}`, cacheMatches: 1,
    cacheIsolation: 'global-cache-observed', symlink: false, localPaths: [], friendGrants: [], inventoryFile,
    inventorySha256: sha(inventory), fileCount: 1 } };
}
function xml() { return `<test-run result="Passed" total="7" passed="7" failed="0" inconclusive="0" skipped="0">\n${cases.map(name =>
  `<test-case fullname="${name}" result="Passed" />`).join('\n')}\n</test-run>\n`; }
async function fixture(t) {
  const top = await realpath(await mkdtemp(path.join(os.tmpdir(), 'zweb-fresh-validator-')));
  t.after(() => rm(top, { recursive: true, force: true }));
  const root = path.join(top, 'evidence'); const repository = path.join(top, 'repository');
  await mkdir(repository); const project = path.join(top, 'frameloom-upm.removed');
  await put(root, 'input.json', { schemaVersion: 1, frameworkRevision: revision,
    access: 'fixture-only', fixtureOnly: true, formalReleaseEvidence: false,
    initialLibraryPresent: false, cacheIsolation: 'global-cache-observed' });
  await put(root, 'manifest.json', { dependencies: { 'com.cysharp.unitask': unitaskUrl,
    'com.unity.test-framework': '1.4.6', [frameworkName]: frameworkUrl } });
  await put(root, 'packages-lock.json', { dependencies: {
    'com.cysharp.unitask': { version: unitaskUrl, depth: 0, source: 'git', dependencies: {}, hash: unitaskRevision },
    [frameworkName]: { version: frameworkUrl, depth: 0, source: 'git',
      dependencies: frameworkDependencies, hash: revision } } });
  const frameworkRaw = json({ name: frameworkName, version: '0.1.0' });
  const unitaskRaw = json({ name: 'com.cysharp.unitask', version: '2.5.11' });
  await put(root, 'resolved-package.json', frameworkRaw);
  await put(root, 'resolved-unitask-package.json', unitaskRaw);
  const framework = sourceRecord({ name: frameworkName, url: frameworkUrl,
    origin: 'https://github.com/diegozhou114-cloud/frameloom.git',
    subdirectory: 'Packages/com.zwebglframework.runtime', revision,
    inventoryFile: 'resolved-package.inventory.tsv', raw: frameworkRaw });
  const unitask = sourceRecord({ name: 'com.cysharp.unitask', url: unitaskUrl,
    origin: 'https://github.com/Cysharp/UniTask.git',
    subdirectory: 'src/UniTask/Assets/Plugins/UniTask', revision: unitaskRevision,
    inventoryFile: 'resolved-unitask.inventory.tsv', raw: unitaskRaw });
  await put(root, framework.record.inventoryFile, framework.inventory);
  await put(root, unitask.record.inventoryFile, unitask.inventory);
  await put(root, 'resolved-source.json', { schemaVersion: 1,
    packages: { [frameworkName]: framework.record, 'com.cysharp.unitask': unitask.record } });
  await put(root, 'contract-asmdef.json', consumerAsmdef);
  for (const name of contractSources) {
    await put(root, `contract-sources/${name}`, 'public sealed class Contract {}\n');
    await put(repository, `Assets/Tests/PublicApiContract/${name}`, 'public sealed class Contract {}\n');
  }
  await put(root, 'unity-result.json', { schemaVersion: 1, editorVersion: '2022.3.62f3c1',
    exitCode: 0, compileErrors: 0, testRun: { total: 7, passed: 7, failed: 0, inconclusive: 0, skipped: 0 },
    lifecycle: { startEpoch: 100, completionEpoch: 101, exitEpoch: 102,
      completionToExitSeconds: 1, completionMarkerCount: 1, postExitAdjudicated: false, completionSource: 'poll',
      watchdogCount: 0, watchdogReason: 'none', signalCount: 0,
      shutdownSpinMatches: 0, unobservedExceptionMatches: 0 },
    graphics: { backend: 'Metal', device: 'Apple M3 Pro (high power)' },
    process: { pid: 424242, observedCwd: project, projectPath: project } });
  await put(root, 'results.xml', xml());
  await put(root, 'test-cases.json', cases.map(name => ({ name, result: 'Passed' })));
  await put(root, 'unity.log', `Unity Editor Version: 2022.3.62f3c1\nGfxDevice: creating device client\nMetal devices available: 1\nUsing device Apple M3 Pro (high power)\nInitializing Metal device caps: Apple M3 Pro\n${marker}\n`);
  await put(root, 'signal-ledger.txt', ''); await put(root, 'unity-timeout.sample.txt', '');
  await put(root, 'cleanup.json', { projectPath: project, removed: true, residualPaths: [] });
  return { top, root, repository, project };
}
const errors = async value => (await validateFreshProject({ evidenceRoot: value.root,
  repositoryRoot: value.repository, frameworkRevision: revision, allowFixture: true })).errors.join('\n');
test('accepts complete observed Git package and lifecycle evidence', async t => {
  assert.ifError(importError); assert.equal(await errors(await fixture(t)), '');
});
test('accepts the exact framework module map in a different serialized order', async t => { const value = await fixture(t); await mutate(value.root, 'packages-lock.json', document => { document.dependencies[frameworkName].dependencies = Object.fromEntries(Object.entries(frameworkDependencies).reverse()); }); assert.equal(await errors(value), ''); });
test('formal validation rejects fixture-only evidence', async t => {
  const value = await fixture(t); const result = await validateFreshProject({ evidenceRoot: value.root,
    repositoryRoot: value.repository, frameworkRevision: revision }); assert.match(result.errors.join('\n'), /Fresh-project input mismatch/);
});
test('rejects mutable, local, and wrong-subdirectory manifest sources', async t => {
  for (const replacement of ['main', 'v0.1.0', '1111111', 'file:../runtime',
    `https://gitee.com/diegozhou114/frameloom.git#${revision}`,
    `https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/other#${revision}`]) {
    const value = await fixture(t); await mutate(value.root, 'manifest.json', document => {
      document.dependencies[frameworkName] = replacement;
    }); assert.match(await errors(value), /Framework manifest dependency mismatch/);
  }
});
test('rejects wrong lock, identities, observed sources, and inventories', async t => {
  const lock = await fixture(t); await mutate(lock.root, 'packages-lock.json', document => {
    document.dependencies[frameworkName].hash = '2'.repeat(40);
  }); assert.match(await errors(lock), /Framework lock mismatch/);
  const identity = await fixture(t); await mutate(identity.root, 'resolved-unitask-package.json', document => {
    document.version = '2.5.10';
  }); assert.match(await errors(identity), /Resolved package identity mismatch/);
  for (const [name, field, replacement] of [[frameworkName, 'origin', 'file:///tmp/repository'],
    [frameworkName, 'subdirectory', 'Packages/other'], [frameworkName, 'revision', '3'.repeat(40)],
    ['com.cysharp.unitask', 'cacheMatches', 2], ['com.cysharp.unitask', 'symlink', true]]) {
    const value = await fixture(t); await mutate(value.root, 'resolved-source.json', document => {
      document.packages[name][field] = replacement;
    }); assert.match(await errors(value), /Resolved package source mismatch/);
  }
  const inventory = await fixture(t); await put(inventory.root, 'resolved-package.inventory.tsv',
    `100644\t${'0'.repeat(64)}\tpackage.json\n`);
  assert.match(await errors(inventory), /Resolved package inventory mismatch/);
});
test('rejects missing and wrong required framework module dependencies', async t => { for (const name of ['com.unity.modules.audio', 'com.unity.modules.assetbundle', 'com.unity.modules.unitywebrequest', 'com.unity.modules.jsonserialize', 'com.unity.modules.physics']) { const missing = await fixture(t); await mutate(missing.root, 'packages-lock.json', document => { delete document.dependencies[frameworkName].dependencies[name]; }); assert.match(await errors(missing), /Framework lock mismatch/); const wrong = await fixture(t); await mutate(wrong.root, 'packages-lock.json', document => { document.dependencies[frameworkName].dependencies[name] = '2.0.0'; }); assert.match(await errors(wrong), /Framework lock mismatch/); } });
test('rejects friend grants, stale source, reflection, and symlinks', async t => {
  const friend = await fixture(t); await mutate(friend.root, 'resolved-source.json', document => {
    document.packages[frameworkName].friendGrants = ['Runtime/Friend.cs'];
  }); assert.match(await errors(friend), /Public contract friend access detected/);
  const asmdef = await fixture(t); await mutate(asmdef.root, 'contract-asmdef.json', document => {
    document.references.push('nunit.framework.dll');
  }); assert.match(await errors(asmdef), /Contract asmdef mismatch/);
  for (const text of ['InternalsVisibleTo', 'BindingFlags.NonPublic', 'dynamic value',
    'Assets/Scripts/Core.cs', 'Packages/com.zwebglframework.runtime/Runtime']) {
    const value = await fixture(t); await put(value.root, `contract-sources/${contractSources[0]}`, text);
    assert.match(await errors(value), /Contract source violation/);
  }
  const changed = await fixture(t); await put(changed.root,
    `contract-sources/${contractSources[0]}`, 'public sealed class Different {}\n');
  assert.match(await errors(changed), /Contract source mismatch/);
  const linked = await fixture(t); const target = path.join(linked.top, 'outside.cs');
  await writeFile(target, 'public class Outside {}\n');
  await rm(path.join(linked.root, `contract-sources/${contractSources[0]}`));
  await symlink(target, path.join(linked.root, `contract-sources/${contractSources[0]}`));
  assert.match(await errors(linked), /Contract source must be a regular file/);
});
test('rejects failed compile, contracts, and lifecycle evidence', async t => {
  const compile = await fixture(t); await mutate(compile.root, 'unity-result.json', document => {
    document.exitCode = 1; document.compileErrors = 1;
  }); assert.match(await errors(compile), /Unity result mismatch/);
  const tests = await fixture(t); await mutate(tests.root, 'unity-result.json', document => {
    document.testRun.failed = 1; document.testRun.passed = 6;
  }); assert.match(await errors(tests), /Public contract result mismatch/);
  for (const [field, replacement] of [['completionMarkerCount', 0], ['watchdogCount', 1],
    ['signalCount', 1], ['completionToExitSeconds', -1], ['completionSource', 'xml-mtime-post-exit']]) {
    const value = await fixture(t); await mutate(value.root, 'unity-result.json', document => {
      document.lifecycle[field] = replacement;
    }); assert.match(await errors(value), /Unity lifecycle mismatch/);
  }
  const graphics = await fixture(t); await mutate(graphics.root, 'unity-result.json', document => {
    document.graphics.backend = 'Null';
  }); assert.match(await errors(graphics), /Unity graphics mismatch/);
  for (const text of ['Application::CoreShutdown', 'UniTaskScheduler.PublishUnobservedTaskException']) {
    const value = await fixture(t); await put(value.root, 'unity-timeout.sample.txt', text);
    assert.match(await errors(value), /Unity lifecycle log mismatch/);
  }
  const benignSample = await fixture(t); await put(benignSample.root, 'unity-timeout.sample.txt', 'sample without signature\n');
  assert.match(await errors(benignSample), /Unity lifecycle mismatch/);
  const duration = await fixture(t); await mutate(duration.root, 'unity-result.json', document => {
    Object.assign(document.lifecycle, { startEpoch: 0, completionEpoch: 601, exitEpoch: 1201, completionToExitSeconds: 600 });
  }); assert.match(await errors(duration), /Unity lifecycle mismatch/);
  for (const [file, from, to, diagnostic] of [
    ['unity.log', marker, '', /Unity lifecycle log mismatch/], ['unity.log', marker, `${marker}\n${marker}`, /Unity lifecycle log mismatch/],
    ['results.xml', '<test-run', '<broken', /Public contract result mismatch|Public contract XML mismatch/],
    ['results.xml', cases[0], 'Wrong.Case', /Public contract XML mismatch/],
    ['results.xml', `${firstCase}\n`, '', /Public contract XML mismatch/],
    ['results.xml', firstCase, `${firstCase}\n${firstCase}`, /Public contract XML mismatch/],
    ['results.xml', 'result="Passed" \/>', 'result="Skipped" />', /Public contract XML mismatch/],
    ['unity.log', 'Initializing Metal device caps: Apple M3 Pro\n', '', /Unity graphics mismatch/],
  ]) { const value = await fixture(t); await replace(value.root, file, from, to); assert.match(await errors(value), diagnostic); }
  const post = await fixture(t); await mutate(post.root, 'unity-result.json', document => Object.assign(document.lifecycle,
    { postExitAdjudicated: true, completionSource: 'xml-mtime-post-exit' }));
  assert.match(await errors(post), /Unity lifecycle mismatch/);
});
test('rejects fixture classification and dangling or retained cleanup paths', async t => {
  const classified = await fixture(t); await mutate(classified.root, 'input.json', document => {
    document.access = 'private-authenticated'; document.fixtureOnly = true; }); assert.match(await errors(classified), /Fresh-project input mismatch/);
  const retained = await fixture(t); await mkdir(retained.project);
  await mutate(retained.root, 'cleanup.json', document => { document.removed = false;
    document.residualPaths = [retained.project]; }); assert.match(await errors(retained), /Consumer cleanup mismatch/);
  const dangling = await fixture(t); await symlink(path.join(dangling.top, 'missing'), dangling.project);
  assert.match(await errors(dangling), /Consumer cleanup mismatch/);
  const sources = await fixture(t); const sourceRoot = path.join(sources.root, 'contract-sources');
  const external = path.join(sources.top, 'external-sources'); await rename(sourceRoot, external); await symlink(external, sourceRoot); assert.match(await errors(sources), /Cannot read contract-sources/);
  const linked = await fixture(t); const evidenceLink = path.join(linked.top, 'evidence-link'); await symlink(linked.root, evidenceLink);
  const result = await validateFreshProject({ evidenceRoot: evidenceLink, repositoryRoot: linked.repository,
    frameworkRevision: revision, allowFixture: true }); assert.match(result.errors.join('\n'), /Evidence root must be a canonical regular directory/);
});
