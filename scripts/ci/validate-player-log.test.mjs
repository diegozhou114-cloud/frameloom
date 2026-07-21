import assert from 'node:assert/strict';
import { mkdtemp, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { validatePlayerLog } from './validate-player-log.mjs';

test('requires readiness, exit, and no unallowlisted errors', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-player-log-'));
  const log = path.join(root, 'player.log');
  const allowlist = path.join(root, 'allowlist.txt');
  await writeFile(allowlist, '');
  await writeFile(log,
    '[ZWEB_SMOKE] {"event":"app-ready"}\n[ZWEB_SMOKE] {"event":"app-exit"}\n');
  assert.deepEqual(await validatePlayerLog(log, allowlist), []);
  await writeFile(log, 'Error: broken\n');
  assert.match((await validatePlayerLog(log, allowlist)).join('\n'), /Error: broken/);
});
