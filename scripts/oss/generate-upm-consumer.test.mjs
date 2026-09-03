import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { chmod, mkdtemp, mkdir, readFile, readdir, realpath, rename, rm, symlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

let generateConsumer; let captureResolvedPackages;
let importError;
try {
  ({ generateConsumer, captureResolvedPackages } = await import('./generate-upm-consumer.mjs'));
} catch (error) {
  importError = error;
}

test('consumer generator module is available', () => {
  assert.ifError(importError);
  assert.equal(typeof generateConsumer, 'function');
  assert.equal(typeof captureResolvedPackages, 'function');
});

const revision = '1'.repeat(40);
const sha = value => createHash('sha256').update(value).digest('hex');
const sources = ['CommandRecorderPublicContractTests.cs', 'PublicApiContractTestSupport.cs',
  'SessionPresentationBridgePublicContractTests.cs'];
const template = `{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#__UNITASK_REVISION__",
    "com.unity.test-framework": "1.4.6",
    "com.zwebglframework.runtime": "https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.runtime#__FRAMEWORK_REVISION__"
  }
}
`;

async function fixture(t) {
  const top = await realpath(await mkdtemp(path.join(os.tmpdir(), 'zweb-upm-generator-')));
  t.after(() => rm(top, { recursive: true, force: true }));
  const root = path.join(top, 'repository');
  const sourceRoot = path.join(root, 'Assets/Tests/PublicApiContract');
  await mkdir(path.join(root, 'ci/upm'), { recursive: true });
  await mkdir(sourceRoot, { recursive: true });
  await writeFile(path.join(root, 'ci/upm/manifest.template.json'), template);
  for (const name of sources) await writeFile(path.join(sourceRoot, name), `public class ${name.replace(/\W/g, '_')} {}\n`);
  return { top, root, output: path.join(top, 'consumer') };
}

async function resolvedFixture(t) {
  const value = await fixture(t); const project = path.join(value.top, 'resolved');
  const evidenceRoot = path.join(value.top, 'evidence'); const cacheRoot = path.join(project, 'Library/PackageCache');
  const framework = path.join(cacheRoot, 'com.zwebglframework.runtime@1111111111');
  const unitask = path.join(cacheRoot, 'com.cysharp.unitask@e5acc106ee');
  await mkdir(path.join(framework, 'Runtime'), { recursive: true }); await mkdir(path.join(unitask, 'Runtime'), { recursive: true });
  await mkdir(path.join(project, 'Packages')); await mkdir(evidenceRoot);
  const frameworkRaw = '{"name":"com.zwebglframework.runtime","version":"0.1.0"}\n';
  const unitaskRaw = '{"name":"com.cysharp.unitask","version":"2.5.11"}\n';
  await writeFile(path.join(framework, 'package.json'), frameworkRaw);
  await writeFile(path.join(framework, 'Runtime/Runtime.cs'), 'public class Runtime {}\n');
  await writeFile(path.join(framework, 'tool.sh'), '#!/bin/sh\n'); await chmod(path.join(framework, 'tool.sh'), 0o755);
  await writeFile(path.join(unitask, 'package.json'), unitaskRaw);
  await writeFile(path.join(unitask, 'Runtime/UniTask.cs'), 'public class UniTask {}\n');
  await writeFile(path.join(project, 'Packages/packages-lock.json'), JSON.stringify({ dependencies: {
    'com.cysharp.unitask': { source: 'git', version: `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#e5acc106ee196bc5a32fb14cdf2987b0f96d11e0`, hash: 'e5acc106ee196bc5a32fb14cdf2987b0f96d11e0' },
    'com.zwebglframework.runtime': { source: 'git', version: `https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.runtime#${revision}`, hash: revision },
  } }));
  return { top: value.top, project, evidenceRoot, cacheRoot, framework, frameworkRaw };
}

test('generates deterministic Git-pinned project without metas or friend access', async t => {
  const value = await fixture(t);
  const result = await generateConsumer({ ...value, frameworkRevision: revision });
  assert.deepEqual(Object.keys(result.manifest.dependencies), [
    'com.cysharp.unitask', 'com.unity.test-framework', 'com.zwebglframework.runtime']);
  assert.equal(result.manifest.dependencies['com.zwebglframework.runtime'],
    `https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.runtime#${revision}`);
  assert.match(result.manifest.dependencies['com.cysharp.unitask'], /#e5acc106ee196bc5a32fb14cdf2987b0f96d11e0$/);
  assert.deepEqual(result.asmdef.references,
    ['ZWebGLFrameWork.Combat', 'ZWebGLFrameWork.SimulationCore']);
  assert.deepEqual(result.asmdef.includePlatforms, ['Editor']);
  assert.deepEqual(result.asmdef.excludePlatforms, []);
  assert.equal(result.asmdef.allowUnsafeCode, false);
  assert.equal(result.asmdef.overrideReferences, true);
  assert.deepEqual(result.asmdef.precompiledReferences, ['nunit.framework.dll']);
  assert.equal(result.asmdef.autoReferenced, false);
  assert.deepEqual(result.asmdef.defineConstraints, ['UNITY_INCLUDE_TESTS']);
  assert.deepEqual(result.asmdef.versionDefines, []);
  assert.equal(result.asmdef.noEngineReferences, false);
  const generated = await readdir(path.join(value.output, 'Assets/PublicApiContract'));
  assert.deepEqual(generated.sort(), [...sources,
    'ZWebGLFrameWork.PublicApiContract.Tests.asmdef'].sort());
  assert.equal(generated.some(name => name.endsWith('.meta')), false);
  for (const name of sources) assert.equal(
    await readFile(path.join(value.output, 'Assets/PublicApiContract', name), 'utf8'),
    await readFile(path.join(value.root, 'Assets/Tests/PublicApiContract', name), 'utf8'));
});

test('rejects mutable, local, and non-full framework revisions', async t => {
  for (const frameworkRevision of ['main', 'v0.1.0', '1'.repeat(39),
    'file:../Packages/com.zwebglframework.runtime', 'A'.repeat(40)]) {
    const value = await fixture(t);
    await assert.rejects(generateConsumer({ ...value, frameworkRevision }), /full lowercase 40-character SHA/);
  }
});

test('rejects repository-local, nonempty, and symlink outputs', async t => {
  const local = await fixture(t);
  await assert.rejects(generateConsumer({ root: local.root,
    output: path.join(local.root, 'consumer'), frameworkRevision: revision }), /outside the repository/);
  const nonempty = await fixture(t); await mkdir(nonempty.output); await writeFile(path.join(nonempty.output, 'state'), 'x');
  await assert.rejects(generateConsumer({ ...nonempty, frameworkRevision: revision }), /new empty directory/);
  const linked = await fixture(t); const target = path.join(linked.top, 'target'); await mkdir(target);
  await symlink(target, linked.output);
  await assert.rejects(generateConsumer({ ...linked, frameworkRevision: revision }), /must not be a symlink/);
});

test('rejects symlinked, stale, or internal contract sources', async t => {
  const linked = await fixture(t); const source = path.join(linked.root, 'Assets/Tests/PublicApiContract', sources[0]);
  await rm(source); await symlink(path.join(linked.root, 'ci/upm/manifest.template.json'), source);
  await assert.rejects(generateConsumer({ ...linked, frameworkRevision: revision }), /Invalid contract source/);
  for (const forbidden of ['InternalsVisibleTo', 'BindingFlags', 'dynamic value',
    'Assets/Scripts/Core.cs', 'Packages/com.zwebglframework.runtime/Runtime']) {
    const value = await fixture(t);
    await writeFile(path.join(value.root, 'Assets/Tests/PublicApiContract', sources[0]), forbidden);
    await assert.rejects(generateConsumer({ ...value, frameworkRevision: revision }), /internals or host paths/);
  }
});

test('rejects semantic manifest template drift with intact placeholders', async t => {
  for (const changed of [
    template.replace('github.com/diegozhou114-cloud', 'gitee.com/diegozhou114'),
    template.replace('com.unity.test-framework": "1.4.6', 'com.unity.test-framework": "1.3.3'),
    template.replace('"com.unity.test-framework": "1.4.6",\n', ''),
  ]) {
    const value = await fixture(t);
    await writeFile(path.join(value.root, 'ci/upm/manifest.template.json'), changed);
    await assert.rejects(generateConsumer({ ...value, frameworkRevision: revision }),
      /Manifest template dependency contract mismatch/);
  }
});

test('captures exact observed package inventories without claiming isolated caches', async t => {
  const value = await resolvedFixture(t);
  const result = await captureResolvedPackages({ ...value, frameworkRevision: revision });
  assert.equal(result.packages['com.zwebglframework.runtime'].cacheIsolation, 'global-cache-observed');
  const expected = [
    `100644\t${sha('public class Runtime {}\n')}\tRuntime/Runtime.cs\n`,
    `100644\t${sha(value.frameworkRaw)}\tpackage.json\n`,
    `100755\t${sha('#!/bin/sh\n')}\ttool.sh\n`,
  ].join('');
  assert.equal(await readFile(path.join(value.evidenceRoot, 'resolved-package.inventory.tsv'), 'utf8'), expected);
  assert.equal(result.packages['com.zwebglframework.runtime'].inventorySha256, sha(expected));
  await writeFile(path.join(value.framework, 'Runtime/Friend.cs'),
    'const string Friend = "ZWebGLFrameWork.PublicApiContract.Tests";\n');
  const friend = await captureResolvedPackages({ ...value, frameworkRevision: revision });
  assert.deepEqual(friend.packages['com.zwebglframework.runtime'].friendGrants, ['Runtime/Friend.cs']);
});

test('rejects a symlinked PackageCache root without touching its target', async t => {
  const value = await resolvedFixture(t); const external = path.join(value.top, 'external-cache');
  await rename(value.cacheRoot, external); await symlink(external, value.cacheRoot);
  await assert.rejects(captureResolvedPackages({ ...value, frameworkRevision: revision }),
    /Library\/PackageCache must be a project-local directory/);
  assert.match(await readFile(path.join(external, 'com.zwebglframework.runtime@1111111111/package.json'), 'utf8'),
    /com\.zwebglframework\.runtime/);
  const linkedProject = path.join(value.top, 'linked-project'); await symlink(value.project, linkedProject);
  await assert.rejects(captureResolvedPackages({ project: linkedProject, evidenceRoot: value.evidenceRoot,
    frameworkRevision: revision }), /project must be a canonical regular directory/);
});
