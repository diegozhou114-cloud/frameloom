import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const validator = fileURLToPath(new URL(
  './validate-simulation-performance-report.mjs', import.meta.url));

function scenario(name, entityCount) {
  return {
    name,
    initialEntityCount: entityCount,
    finalEntityCount: entityCount,
    totalManagedAllocationEvents: 1200,
    managedAllocationEventsPerTick: { median: 2, p95: 4, max: 8 },
    millisecondsPerTick: { median: 0.1, p95: 0.2, max: 0.4 },
    generation0Collections: 0,
    generation1Collections: 0,
    generation2Collections: 0,
    finalChecksum: 'FFFFFFFFFFFFFFFF',
  };
}

function validReport() {
  return {
    schemaVersion: 2,
    generatedAtUtc: '2026-08-03T00:00:00.0000000Z',
    environment: {
      unityVersion: '2022.3.62f3c1',
      operatingSystem: 'test-os',
      isBatchMode: true,
    },
    configuration: { warmupTicks: 100, measuredTicks: 600, repetitions: 5 },
    scenarios: [scenario('standard-300', 300), scenario('stress-500', 500)],
  };
}

async function validate(document, rawJson) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-perf-validator-'));
  const reportPath = path.join(root, 'simulation-baseline.json');
  await writeFile(reportPath, rawJson ?? JSON.stringify(document));
  const result = spawnSync(process.execPath, [validator, '--report', reportPath], {
    encoding: 'utf8',
  });
  await rm(root, { recursive: true, force: true });
  return result;
}

test('accepts a valid 300 and 500 entity baseline report', async () => {
  const result = await validate(validReport());
  assert.equal(result.status, 0, result.stderr);
});

test('rejects a report missing the 500 entity scenario', async () => {
  const report = validReport();
  report.scenarios.pop();
  const result = await validate(report);
  assert.notEqual(result.status, 0);
});

test('rejects negative managed allocation', async () => {
  const report = validReport();
  report.scenarios[0].totalManagedAllocationEvents = -1;
  const result = await validate(report);
  assert.notEqual(result.status, 0);
});

test('rejects non-finite tick duration', async () => {
  const json = JSON.stringify(validReport()).replace(
    '"median":0.1', '"median":1e309');
  const result = await validate(null, json);
  assert.notEqual(result.status, 0);
});

test('rejects incorrect scenario names', async () => {
  const report = validReport();
  report.scenarios[0].name = 'arbitrary-300';
  const result = await validate(report);
  assert.notEqual(result.status, 0);
});

test('rejects unordered statistics', async () => {
  const report = validReport();
  report.scenarios[0].millisecondsPerTick.p95 = 0.05;
  const result = await validate(report);
  assert.notEqual(result.status, 0);
});

test('rejects fractional integer counters', async () => {
  for (const field of ['totalManagedAllocationEvents', 'generation0Collections']) {
    const report = validReport();
    report.scenarios[0][field] = 1.5;
    const result = await validate(report);
    assert.notEqual(result.status, 0);
    assert.ok(result.stderr.includes(`scenarios[0].${field} must be a finite non-negative safe integer`));
  }
});

test('rejects an incorrect Unity version', async () => {
  const report = validReport();
  report.environment.unityVersion = '2022.3.61f1';
  const result = await validate(report);
  assert.notEqual(result.status, 0);
});

test('accepts zero managed allocation events', async () => {
  const report = validReport();
  report.scenarios[0].totalManagedAllocationEvents = 0;
  report.scenarios[0].managedAllocationEventsPerTick = { median: 0, p95: 0, max: 0 };
  const result = await validate(report);
  assert.equal(result.status, 0, result.stderr);
});

test('preserves checksum above Number.MAX_SAFE_INTEGER exactly', async () => {
  const report = validReport();
  const serialized = JSON.stringify(report);
  assert.equal(JSON.parse(serialized).scenarios[0].finalChecksum, 'FFFFFFFFFFFFFFFF');
  const result = await validate(null, serialized);
  assert.equal(result.status, 0, result.stderr);
});

test('rejects legacy numeric checksum', async () => {
  const report = validReport();
  report.scenarios[0].finalChecksum = Number.MAX_SAFE_INTEGER + 1;
  const result = await validate(report);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /scenarios\[0\]\.finalChecksum/);
});

test('rejects unsafe integer counters', async () => {
  const unsafe = Number.MAX_SAFE_INTEGER + 1;
  const cases = [
    [report => { report.configuration.warmupTicks = unsafe; }, 'warmupTicks'],
    [report => { report.scenarios[0].initialEntityCount = unsafe; }, 'scenarios[0].initialEntityCount'],
    [report => { report.scenarios[0].totalManagedAllocationEvents = unsafe; }, 'scenarios[0].totalManagedAllocationEvents'],
    [report => { report.scenarios[0].generation0Collections = unsafe; }, 'scenarios[0].generation0Collections'],
  ];
  for (const [mutate, label] of cases) {
    const report = validReport();
    mutate(report);
    const result = await validate(report);
    assert.notEqual(result.status, 0);
    assert.ok(result.stderr.includes(`${label} must be a finite non-negative safe integer`));
  }
});

test('rejects legacy allocation byte fields', async () => {
  const report = validReport();
  const scenario = report.scenarios[0];
  scenario.totalManagedAllocatedBytes = 1200;
  scenario.allocatedBytesPerTick = { median: 2, p95: 4, max: 8 };
  const result = await validate(report);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /legacy allocation byte fields are not allowed/);
});
