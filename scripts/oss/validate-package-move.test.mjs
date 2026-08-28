import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

let validatePackageMove;
let validatorImportError;
try {
  ({ validatePackageMove } = await import('./validate-package-move.mjs'));
} catch (error) {
  validatorImportError = error;
}

const codeGuid = '11111111111111111111111111111111';
const asmdefGuid = '22222222222222222222222222222222';
const sourceCode = 'Assets/Scripts/Core/Core.cs';
const sourceAsmdef = 'Assets/Scripts/Core/ZWebGLFrameWork.Core.asmdef';
const destination = 'Packages/com.zwebglframework.runtime/Runtime/Core';
const destinationCode = `${destination}/Core.cs`;
const destinationAsmdef = `${destination}/ZWebGLFrameWork.Core.asmdef`;
const asmdef = { name: 'ZWebGLFrameWork.Core', references: [] };
const quotedTrackedPath = '"docs/\\303\\251.md"';

async function put(root, relative, contents) {
  const target = path.join(root, relative);
  await mkdir(path.dirname(target), { recursive: true });
  await writeFile(target, contents, 'utf8');
}

function meta(guid) {
  return `fileFormatVersion: 2\nguid: ${guid}\nTextScriptImporter:\n  externalObjects: {}\n`;
}

async function fixture(t, mode = 'canonical') {
  const top = await mkdtemp(path.join(os.tmpdir(), 'zweb-package-move-'));
  t.after(() => rm(top, { recursive: true, force: true }));
  const baseline = path.join(top, 'baseline');
  const root = path.join(top, 'root');
  await mkdir(baseline);
  await mkdir(root);
  await put(baseline, 'meta.tsv',
    `${sourceCode}.meta\t${codeGuid}\n${sourceAsmdef}.meta\t${asmdefGuid}\n`);
  await put(baseline, 'asmdefs.ndjson', JSON.stringify({
    path: sourceAsmdef, guid: asmdefGuid, asmdef,
  }) + '\n');
  await put(baseline, 'tracked-files.txt', [
    sourceCode, `${sourceCode}.meta`, sourceAsmdef, `${sourceAsmdef}.meta`, quotedTrackedPath,
  ].join('\n') + '\n');
  const codePath = mode === 'identity' ? sourceCode : destinationCode;
  const asmdefPath = mode === 'identity' ? sourceAsmdef : destinationAsmdef;
  await put(root, codePath, 'public sealed class Core {}\n');
  await put(root, `${codePath}.meta`, meta(codeGuid));
  await put(root, asmdefPath,
    JSON.stringify({ references: [], name: 'ZWebGLFrameWork.Core' }, null, 2) + '\n');
  await put(root, `${asmdefPath}.meta`, meta(asmdefGuid));
  await put(root, 'docs/é.md', 'tracked non-ASCII path\n');
  return { baseline, root, codePath, asmdefPath };
}

async function validate(options) {
  assert.ifError(validatorImportError);
  assert.equal(typeof validatePackageMove, 'function', 'validator API is unavailable');
  return validatePackageMove(options);
}

function diagnostics(result) {
  return result.errors.join('\n');
}

test('accepts a path-only canonical relocation', async t => {
  const value = await fixture(t);
  const result = await validate(value);
  assert.deepEqual(result.errors, []);
});

test('accepts the unchanged baseline through identity mode', async t => {
  const value = await fixture(t, 'identity');
  const result = await validate({ ...value, mode: 'identity' });
  assert.deepEqual(result.errors, []);
});

test('rejects a changed baseline GUID', async t => {
  const value = await fixture(t);
  await put(value.root, `${value.codePath}.meta`, meta('3'.repeat(32)));
  assert.match(diagnostics(await validate(value)), /GUID changed:/);
});

test('rejects a missing baseline meta', async t => {
  const value = await fixture(t);
  await rm(path.join(value.root, `${value.codePath}.meta`));
  assert.match(diagnostics(await validate(value)), /Missing baseline meta:/);
});

test('rejects a duplicate GUID', async t => {
  const value = await fixture(t);
  await put(value.root, 'ApprovedExtra.meta', meta(codeGuid));
  const result = await validate({ ...value, allowNewMeta: ['ApprovedExtra.meta'] });
  assert.match(diagnostics(result), /Duplicate GUID:/);
});

test('rejects a production file copied instead of moved', async t => {
  const value = await fixture(t);
  await put(value.root, sourceCode, 'public sealed class Core {}\n');
  assert.match(diagnostics(await validate(value)), /Production file copied instead of moved:/);
});

test('rejects an unapproved new meta', async t => {
  const value = await fixture(t);
  await put(value.root, 'Unexpected.meta', meta('4'.repeat(32)));
  assert.match(diagnostics(await validate(value)), /Unexpected new meta:/);
});

test('rejects an asmdef semantic change', async t => {
  const value = await fixture(t);
  await put(value.root, value.asmdefPath,
    JSON.stringify({ name: 'ZWebGLFrameWork.Core', references: ['Changed'] }) + '\n');
  assert.match(diagnostics(await validate(value)), /ASMDEF semantics changed:/);
});

test('rejects a stale old production or package root', async t => {
  const value = await fixture(t);
  await put(value.root, 'Packages/com.zwebglframework.kcp2k/stale.cs', 'stale\n');
  assert.match(diagnostics(await validate(value)), /Stale old production root:/);
});

test('rejects a missing mapped destination', async t => {
  const value = await fixture(t);
  await rm(path.join(value.root, value.codePath));
  assert.match(diagnostics(await validate(value)), /Mapped destination missing:/);
});

test('rejects missing and unparseable baseline evidence', async t => {
  const missing = await fixture(t);
  await rm(path.join(missing.baseline, 'meta.tsv'));
  assert.match(diagnostics(await validate(missing)), /Baseline file missing: meta.tsv/);

  const malformed = await fixture(t);
  await put(malformed.baseline, 'asmdefs.ndjson', '{bad json}\n');
  assert.match(diagnostics(await validate(malformed)),
    /Invalid baseline asmdefs.ndjson line 1:/);
});
