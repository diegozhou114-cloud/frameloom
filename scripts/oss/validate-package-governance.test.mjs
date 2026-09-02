import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

let validatePackageGovernance;
let validatorImportError;
try {
  ({ validatePackageGovernance } = await import('./validate-package-governance.mjs'));
} catch (error) {
  validatorImportError = error;
}

const packagePath = 'Packages/com.zwebglframework.runtime';
const baseUrl = 'https://github.com/diegozhou114-cloud/frameloom/blob/v0.1.0/' + packagePath;
const packageJson = {
  name: 'com.zwebglframework.runtime',
  version: '0.1.0',
  displayName: 'FrameLoom',
  description: 'deterministic Unity strategy-game framework',
  unity: '2022.3',
  license: 'MIT',
  author: { name: 'Diego Zhou', url: 'https://github.com/diegozhou114-cloud' },
  dependencies: { 'com.unity.ugui': '1.0.0' },
  keywords: [
    'unity', 'unity3d', 'unity-package', 'upm', 'webgl',
    'deterministic-simulation', 'strategy-game', 'ecs',
  ],
  documentationUrl: `${baseUrl}/README.md`,
  changelogUrl: `${baseUrl}/CHANGELOG.md`,
  licensesUrl: `${baseUrl}/LICENSE`,
};
const readme = `# FrameLoom

FrameLoom is a deterministic Unity strategy-game framework distributed as a Unity package.

## Requirements

- Unity 2022.3 LTS. FrameLoom is tested on 2022.3.62f3c1; not every 2022.3 patch is tested.
- Consumers must directly pin UniTask to e5acc106ee196bc5a32fb14cdf2987b0f96d11e0.

Unity 2022.3 package manifests cannot express Git dependencies transitively, so consumers must add
the pinned UniTask dependency directly.

## Buffered output lifetime

SessionPresentationBridge synchronously borrows a published buffer. It retains value copies and
bridge-owned views, so the parent buffer can be cleared, reused, or disposed after Apply returns.

CommandRecorder synchronously creates owning copies of published commands. Recorded data remains
valid after the parent buffer is cleared, reused, or disposed.
`;
const changelog = `# Changelog

## [0.1.0]

- Consolidated the runtime and retained transports into one canonical Unity package.
- Declared Unity 2022.3 LTS support and 2022.3.62f3c1 as the tested editor.
- Included deterministic simulation and tick output, presentation and view pooling, and Replay
  validation.
- Added public buffered presentation and command-recording APIs with explicit lifetime contracts.
- Required consumers to pin UniTask directly because Unity cannot resolve Git dependencies
  transitively.
- Kept Server projects as repository-only reference and validation hosts.
- Kept buffered Replay capture outside allocation gates and WebGL managed allocation unmeasured.
- Preserved existing namespaces, assembly names, package ID, GUIDs, and serialized compatibility.
`;

async function put(root, relative, contents) {
  const target = path.join(root, relative);
  await mkdir(path.dirname(target), { recursive: true });
  await writeFile(target, contents, 'utf8');
}

async function fixture(t) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-package-governance-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await put(root, `${packagePath}/package.json`, JSON.stringify(packageJson, null, 2) + '\n');
  await put(root, `${packagePath}/README.md`, readme);
  await put(root, `${packagePath}/CHANGELOG.md`, changelog);
  return root;
}

async function validate(root) {
  assert.ifError(validatorImportError);
  assert.equal(typeof validatePackageGovernance, 'function', 'validator API is unavailable');
  return validatePackageGovernance({ root });
}

function diagnostics(result) {
  return result.errors.join('\n');
}

async function mutatePackage(root, mutate) {
  const target = path.join(root, packagePath, 'package.json');
  const value = structuredClone(packageJson);
  mutate(value);
  await writeFile(target, JSON.stringify(value, null, 2) + '\n', 'utf8');
}

test('accepts exact task 4.1 metadata and docs without a local license', async t => {
  const root = await fixture(t);
  assert.deepEqual((await validate(root)).errors, []);
});

test('rejects changed or extra package metadata fields', async t => {
  const changed = await fixture(t);
  await mutatePackage(changed, value => { value.displayName = 'Other'; });
  assert.match(diagnostics(await validate(changed)), /Package metadata mismatch: displayName/);

  const extra = await fixture(t);
  await mutatePackage(extra, value => { value.repository = 'mutable'; });
  assert.match(diagnostics(await validate(extra)), /Unexpected package metadata field: repository/);
});

test('rejects author email and extra dependencies', async t => {
  const author = await fixture(t);
  await mutatePackage(author, value => { value.author.email = 'public@example.com'; });
  assert.match(diagnostics(await validate(author)), /Package author mismatch/);

  const dependency = await fixture(t);
  await mutatePackage(dependency, value => { value.dependencies['com.example.extra'] = '1.0.0'; });
  assert.match(diagnostics(await validate(dependency)), /Package dependencies mismatch/);
});

test('rejects reordered keywords and mutable documentation URLs', async t => {
  const keywords = await fixture(t);
  await mutatePackage(keywords, value => { value.keywords.reverse(); });
  assert.match(diagnostics(await validate(keywords)), /Package keywords mismatch/);

  const url = await fixture(t);
  await mutatePackage(url, value => { value.documentationUrl = value.documentationUrl.replace('v0.1.0', 'main'); });
  assert.match(diagnostics(await validate(url)), /Package metadata mismatch: documentationUrl/);
});

test('rejects missing task 4.1 package documents', async t => {
  const root = await fixture(t);
  await rm(path.join(root, packagePath, 'README.md'));
  assert.match(diagnostics(await validate(root)), /Missing package document: README.md/);
});

test('rejects incomplete README dependency and compatibility facts', async t => {
  const sha = await fixture(t);
  await put(sha, `${packagePath}/README.md`, readme.replace('e5acc106ee196bc5a32fb14cdf2987b0f96d11e0', 'main'));
  assert.match(diagnostics(await validate(sha)), /README missing required UniTask revision/);

  const unity = await fixture(t);
  await put(unity, `${packagePath}/README.md`, readme.replace('not every 2022.3 patch is tested', 'all patches are supported'));
  assert.match(diagnostics(await validate(unity)), /README missing Unity patch limitation/);
});

test('rejects incomplete README buffered-lifetime facts', async t => {
  const borrowed = await fixture(t);
  await put(borrowed, `${packagePath}/README.md`, readme.replace('synchronously borrows', 'uses'));
  assert.match(diagnostics(await validate(borrowed)), /README missing borrowed-buffer lifetime/);

  const owning = await fixture(t);
  await put(owning, `${packagePath}/README.md`, readme.replace('owning copies', 'references'));
  assert.match(diagnostics(await validate(owning)), /README missing recorder owning-copy lifetime/);
});

test('accepts required README facts across Markdown line wrapping', async t => {
  const root = await fixture(t);
  await put(root, `${packagePath}/README.md`, readme
    .replace('synchronously borrows', 'synchronously\nborrows')
    .replaceAll('cleared, reused, or disposed', 'cleared, reused, or\ndisposed'));
  assert.deepEqual((await validate(root)).errors, []);
});

test('rejects local dependencies and unsupported public claims', async t => {
  for (const phrase of ['file:../runtime', 'CI PASS', 'Demo', 'supports every Unity patch']) {
    const root = await fixture(t);
    await put(root, `${packagePath}/README.md`, `${readme}\n${phrase}\n`);
    assert.match(diagnostics(await validate(root)), /README contains prohibited claim/);
  }
});

test('rejects non-English package docs', async t => {
  const root = await fixture(t);
  await put(root, `${packagePath}/README.md`, `${readme}\n内部接口\n`);
  assert.match(diagnostics(await validate(root)), /Package document must be English: README.md/);
});

test('rejects changelog dates, Unreleased, and duplicate version sections', async t => {
  for (const addition of ['\n## [Unreleased]\n', '\n## [0.1.0] - 2026-09-02\n', '\n## [0.1.0]\n']) {
    const root = await fixture(t);
    await put(root, `${packagePath}/CHANGELOG.md`, changelog + addition);
    assert.match(diagnostics(await validate(root)), /CHANGELOG must contain one undated \[0\.1\.0\] section/);
  }
});
