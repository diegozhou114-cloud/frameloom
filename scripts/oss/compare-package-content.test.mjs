import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, realpath, rm, symlink, writeFile } from 'node:fs/promises';
import { execFileSync, spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

const repository = path.resolve(new URL('../..', import.meta.url).pathname);
const driver = path.join(repository, 'scripts/oss/compare-package-content.mjs');

function git(cwd, ...args) { return execFileSync('git', args, { cwd, encoding: 'utf8' }).trim(); }
async function gitFixture(t, mutate = false) {
  const top = await mkdtemp(path.join(await realpath(os.tmpdir()), 'zweb-package-content-')); const root = path.join(top, 'repo');
  t.after(() => rm(top, { recursive: true, force: true })); await mkdir(root);
  git(root, 'init', '-q'); git(root, 'config', 'user.email', 'test@example.invalid'); git(root, 'config', 'user.name', 'Test');
  await mkdir(path.join(root, 'Packages/com.zwebglframework.runtime/Runtime'), { recursive: true });
  await writeFile(path.join(root, 'Packages/com.zwebglframework.runtime/package.json'), '{"name":"com.zwebglframework.runtime"}\n');
  await writeFile(path.join(root, 'Packages/com.zwebglframework.runtime/Runtime/A.cs'), 'class A {}\n');
  await writeFile(path.join(root, 'README.md'), 'base\n');
  git(root, 'add', '.'); git(root, 'commit', '-qm', 'base'); const p = git(root, 'rev-parse', 'HEAD');
  if (mutate) {
    await writeFile(path.join(root, 'Packages/com.zwebglframework.runtime/Runtime/A.cs'), 'class Changed {}\n');
  }
  await writeFile(path.join(root, 'README.md'), 'release\n');
  if (mutate) await writeFile(path.join(root, 'extra-root.txt'), 'extra\n');
  git(root, 'add', '.'); git(root, 'commit', '-qm', 'release');
  return { top, root, p, r: git(root, 'rev-parse', 'HEAD') };
}

function run(args, cwd) { return spawnSync(process.execPath, [driver, ...args], { cwd, encoding: 'utf8' }); }

function sha(value) { return createHash('sha256').update(value).digest('hex'); }
async function resolvedFixture(t, changed = false, revision = '1'.repeat(40)) {
  const root = await mkdtemp(path.join(await realpath(os.tmpdir()), 'zweb-resolved-content-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await writeFile(path.join(root, 'resolved-package.json'), '{"name":"com.zwebglframework.runtime","version":"0.1.0"}\n');
  await writeFile(path.join(root, 'resolved-unitask-package.json'), '{"name":"com.cysharp.unitask","version":"2.5.11"}\n');
  const runtimeHash = sha(await readFile(path.join(root, 'resolved-package.json')));
  const unitaskHash = sha(await readFile(path.join(root, 'resolved-unitask-package.json')));
  const runtime = `100644\t${changed ? 'f'.repeat(64) : runtimeHash}\tpackage.json\n`;
  const unitask = `100644\t${unitaskHash}\tpackage.json\n`;
  await writeFile(path.join(root, 'resolved-package.inventory.tsv'), runtime);
  await writeFile(path.join(root, 'resolved-unitask.inventory.tsv'), unitask);
  const record = (name, revision, inventoryFile, text) => {
    const framework = name === 'com.zwebglframework.runtime'; const origin = framework ? 'https://github.com/diegozhou114-cloud/frameloom.git' : 'https://github.com/Cysharp/UniTask.git';
    const subdirectory = framework ? 'Packages/com.zwebglframework.runtime' : 'src/UniTask/Assets/Plugins/UniTask';
    const actualRevision = framework ? revision : 'e5acc106ee196bc5a32fb14cdf2987b0f96d11e0';
    return { source: 'git', version: `${origin}?path=${framework ? '/' : ''}${subdirectory}#${actualRevision}`, origin, subdirectory, revision: actualRevision, cachePath: `Library/PackageCache/${name}@${actualRevision.slice(0, 10)}`, cacheMatches: 1, cacheIsolation: 'global-cache-observed', symlink: false, localPaths: [], friendGrants: [], inventoryFile, inventorySha256: sha(text), fileCount: text.trimEnd().split('\n').length };
  };
  const runtimeText = await readFile(path.join(root, 'resolved-package.inventory.tsv'), 'utf8');
  const unitaskText = await readFile(path.join(root, 'resolved-unitask.inventory.tsv'), 'utf8');
  await writeFile(path.join(root, 'resolved-source.json'), JSON.stringify({ schemaVersion: 1, packages: {
    'com.zwebglframework.runtime': record('com.zwebglframework.runtime', revision, 'resolved-package.inventory.tsv', runtimeText),
    'com.cysharp.unitask': record('com.cysharp.unitask', 'e5acc106ee196bc5a32fb14cdf2987b0f96d11e0', 'resolved-unitask.inventory.tsv', unitaskText),
  } }, null, 2) + '\n');
  return root;
}

test('Git object comparison writes deterministic equal inventories', async t => {
  const value = await gitFixture(t); const output = path.join(value.top, 'evidence');
  const result = run(['--p', value.p, '--r', value.r, '--output', output], value.root);
  assert.equal(result.status, 0, result.stderr);
  const inventory = await readFile(path.join(output, 'p.inventory.tsv'), 'utf8');
  assert.match(inventory, /^100644\t[0-9a-f]{40}\t[0-9a-f]{64}\tRuntime\/A\.cs\n/m);
  assert.equal(inventory, await readFile(path.join(output, 'r.inventory.tsv'), 'utf8'));
  assert.match(await readFile(path.join(output, 'comparison.json'), 'utf8'), /"equal": true/);
});

test('Git comparison fails on changed content and symlink entries', async t => {
  const value = await gitFixture(t, true); const output = path.join(value.top, 'evidence');
  const result = run(['--p', value.p, '--r', value.r, '--output', output], value.root);
  assert.notEqual(result.status, 0); assert.match(result.stderr, /README|inventory|content|digest|tree/i);
  assert.notEqual(run(['--p', value.p, '--r', value.p, '--output', path.join(value.top, 'same')], value.root).status, 0);
  await writeFile(path.join(value.root, 'README.md'), 'later\n'); git(value.root, 'add', '.'); git(value.root, 'commit', '-qm', 'later');
  const later = git(value.root, 'rev-parse', 'HEAD'); assert.notEqual(run(['--p', value.p, '--r', later, '--output', path.join(value.top, 'later-out')], value.root).status, 0);
  const linkTop = await mkdtemp(path.join(await realpath(os.tmpdir()), 'zweb-package-link-')); const linkRoot = path.join(linkTop, 'repo'); t.after(() => rm(linkTop, { recursive: true, force: true })); await mkdir(linkRoot);
  git(linkRoot, 'init', '-q'); git(linkRoot, 'config', 'user.email', 'test@example.invalid'); git(linkRoot, 'config', 'user.name', 'Test');
  await mkdir(path.join(linkRoot, 'Packages/com.zwebglframework.runtime'), { recursive: true }); await writeFile(path.join(linkRoot, 'README.md'), 'base\n'); await symlink('/tmp/outside', path.join(linkRoot, 'Packages/com.zwebglframework.runtime/escape'));
  git(linkRoot, 'add', '-A'); git(linkRoot, 'commit', '-qm', 'link'); const revision = git(linkRoot, 'rev-parse', 'HEAD');
  await writeFile(path.join(linkRoot, 'README.md'), 'release\n'); git(linkRoot, 'add', '.'); git(linkRoot, 'commit', '-qm', 'release'); const linkR = git(linkRoot, 'rev-parse', 'HEAD');
  const linkResult = run(['--p', revision, '--r', linkR, '--output', path.join(linkTop, 'out')], linkRoot);
  assert.notEqual(linkResult.status, 0); assert.match(linkResult.stderr, /symlink|mode|package/i);
});

test('resolved comparison validates persisted inventories and detects differences', async t => {
  const left = await resolvedFixture(t); const right = await resolvedFixture(t, false, '3'.repeat(40)); const output = await mkdtemp(path.join(await realpath(os.tmpdir()), 'zweb-resolved-out-'));
  t.after(() => rm(output, { recursive: true, force: true }));
  let result = run(['--resolved-p', left, '--resolved-r', right, '--output', output], repository);
  assert.equal(result.status, 0, result.stderr);
  assert.notEqual(run(['--resolved-p', left, '--resolved-r', left], repository).status, 0);
  const changed = await resolvedFixture(t, true, '3'.repeat(40)); result = run(['--resolved-p', left, '--resolved-r', changed], repository);
  assert.notEqual(result.status, 0); assert.match(result.stderr, /inventory|content|digest|hash/i);
});

test('resolved inventory rejects missing, extra, unsorted, mode, hash, and unsafe path fixtures', async t => {
  const left = await resolvedFixture(t); const packageHash = sha(await readFile(path.join(left, 'resolved-package.json')));
  const cases = [
    ['', /inventory|ENOENT/i],
    [`100644\t${sha(Buffer.from('x'))}\textra.txt\n100644\t${packageHash}\tpackage.json\n`, /path|extra/i],
    [`100644\t${sha(Buffer.from('x'))}\tz.txt\n100644\t${packageHash}\tpackage.json\n`, /sorted/i],
    [`100755\t${packageHash}\tpackage.json\n`, /mode/i],
    [`100644\t${'f'.repeat(64)}\tpackage.json\n`, /hash/i],
    [`100644\t${packageHash}\t..\/escape\n`, /path/i],
  ];
  for (const [text, diagnostic] of cases) {
    const right = await resolvedFixture(t, false, '3'.repeat(40)); const inventory = path.join(right, 'resolved-package.inventory.tsv'); if (text) await writeFile(inventory, text); else await rm(inventory);
    if (text) { const source = JSON.parse(await readFile(path.join(right, 'resolved-source.json'), 'utf8')); source.packages['com.zwebglframework.runtime'].inventorySha256 = sha(text); source.packages['com.zwebglframework.runtime'].fileCount = text.trimEnd().split('\n').length; await writeFile(path.join(right, 'resolved-source.json'), JSON.stringify(source) + '\n'); }
    const result = run(['--resolved-p', left, '--resolved-r', right], repository);
    assert.notEqual(result.status, 0); assert.match(result.stderr, diagnostic);
  }
});

test('CLI rejects ambiguous, unsafe, and dirty output inputs', async t => {
  const value = await gitFixture(t); const output = path.join(value.root, 'evidence'); await mkdir(output);
  await writeFile(path.join(output, 'existing.txt'), 'do not overwrite\n');
  for (const args of [[], ['--p', value.p, '--r', value.r], ['--p', value.p, '--r', value.r, '--output', output],
    ['--p', value.p, '--r', value.r, '--output', path.join(value.root, 'a'), '--unknown', 'x']]) {
    const result = run(args, value.root); assert.notEqual(result.status, 0);
  }
  const inside = path.join(value.root, 'inside'); await mkdir(inside); assert.notEqual(run(['--p', value.p, '--r', value.r, '--output', inside], value.root).status, 0);
  const left = await resolvedFixture(t); const right = await resolvedFixture(t, false, '3'.repeat(40));
  assert.notEqual(run(['--resolved-p', left, '--resolved-r', right, '--output', path.join(value.root, 'resolved-out')], value.root).status, 0);
  const linkedParent = path.join(value.top, 'linked-parent'); await symlink(value.top, linkedParent);
  assert.notEqual(run(['--p', value.p, '--r', value.r, '--output', path.join(linkedParent, 'out')], value.root).status, 0);
});
