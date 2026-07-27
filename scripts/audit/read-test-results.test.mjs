import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtemp, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

const script = path.resolve('scripts/audit/read-test-results.py');

async function parse(xml) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-results-'));
  const artifact = path.join(root, 'results.xml');
  await writeFile(artifact, xml, 'utf8');
  return JSON.parse(execFileSync('python3', [script, artifact], { encoding: 'utf8' }));
}

test('reads exact NUnit and TRX result records', async () => {
  assert.deepEqual(await parse('<test-run><test-case fullname="A.Method" result="Passed" /></test-run>'),
    [{ name: 'A.Method', result: 'Passed' }]);
  assert.deepEqual(await parse('<TestRun><Results><UnitTestResult testName="B.Method" outcome="Failed" /></Results></TestRun>'),
    [{ name: 'B.Method', result: 'Failed' }]);
});
