import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { validateSummary } from './validate-build-summary.mjs';

test('accepts a hashed successful WebGL summary', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-summary-'));
  await mkdir(path.join(root, 'builds/WebGL'), { recursive: true });
  const artifacts = {
    'builds/WebGL/index.html': '<canvas></canvas>',
    'builds/WebGL/game.wasm': 'wasm',
    'builds/WebGL/game.data': 'data',
  };
  const entries = [];
  for (const [relative, content] of Object.entries(artifacts)) {
    await writeFile(path.join(root, relative), content);
    entries.push({
      path: relative,
      bytes: Buffer.byteLength(content),
      sha256: createHash('sha256').update(content).digest('hex'),
    });
  }
  const summary = path.join(root, 'summary.json');
  await writeFile(summary, JSON.stringify({
    schemaVersion: 1,
    editorVersion: '2022.3.62f3c1',
    target: 'WebGL',
    enabledScenes: ['Assets/Scence/Launcher.unity'],
    artifacts: entries,
    result: 'Succeeded',
    warnings: 0,
    errors: 0,
  }));

  assert.deepEqual(await validateSummary(summary, root, 'WebGL'), []);
});
