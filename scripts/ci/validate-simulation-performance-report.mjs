#!/usr/bin/env node
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

function number(value, label, errors, integer = false) {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0 ||
      (integer && !Number.isSafeInteger(value)))
    errors.push(`${label} must be a finite non-negative${integer ? ' safe integer' : ' number'}`);
}

function text(value, label, errors) {
  if (typeof value !== 'string' || value.trim() === '') errors.push(`${label} is required`);
}

function fixedInteger(value, label, expected, errors) {
  number(value, label, errors, true);
  if (value !== expected) errors.push(`${label} must equal ${expected}`);
}

function statistics(value, label, errors) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    errors.push(`${label} is required`);
    return;
  }
  number(value.median, `${label}.median`, errors);
  number(value.p95, `${label}.p95`, errors);
  number(value.max, `${label}.max`, errors);
  if (value.median > value.p95 || value.p95 > value.max)
    errors.push(`${label} must satisfy median <= p95 <= max`);
}

function scenario(value, index, errors) {
  const label = `scenarios[${index}]`;
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    errors.push(`${label} is required`);
    return;
  }
  text(value.name, `${label}.name`, errors);
  number(value.initialEntityCount, `${label}.initialEntityCount`, errors, true);
  number(value.finalEntityCount, `${label}.finalEntityCount`, errors, true);
  if (value.initialEntityCount !== value.finalEntityCount)
    errors.push(`${label} entity count changed during measurement`);
  number(value.totalManagedAllocationEvents,
    `${label}.totalManagedAllocationEvents`, errors, true);
  statistics(value.managedAllocationEventsPerTick,
    `${label}.managedAllocationEventsPerTick`, errors);
  statistics(value.millisecondsPerTick, `${label}.millisecondsPerTick`, errors);
  for (const generation of [0, 1, 2])
    number(value[`generation${generation}Collections`],
      `${label}.generation${generation}Collections`, errors, true);
  if (typeof value.finalChecksum !== 'string' ||
      !/^[0-9A-F]{16}$/.test(value.finalChecksum))
    errors.push(`${label}.finalChecksum must match ^[0-9A-F]{16}$`);
  if ('totalManagedAllocatedBytes' in value || 'allocatedBytesPerTick' in value)
    errors.push(`${label} legacy allocation byte fields are not allowed`);
}

export async function validateReport(reportPath) {
  const errors = [];
  const document = JSON.parse(await readFile(reportPath, 'utf8'));
  if (document.schemaVersion !== 2) errors.push('schemaVersion must equal 2');
  text(document.generatedAtUtc, 'generatedAtUtc', errors);
  if (!document.environment || typeof document.environment !== 'object') {
    errors.push('environment is required');
  } else {
    if (document.environment.unityVersion !== '2022.3.62f3c1')
      errors.push('environment.unityVersion must equal 2022.3.62f3c1');
    text(document.environment.operatingSystem, 'environment.operatingSystem', errors);
    if (typeof document.environment.isBatchMode !== 'boolean')
      errors.push('environment.isBatchMode must be boolean');
  }
  const configuration = document.configuration;
  if (!configuration || typeof configuration !== 'object') {
    errors.push('configuration is required');
  } else {
    fixedInteger(configuration.warmupTicks, 'warmupTicks', 100, errors);
    fixedInteger(configuration.measuredTicks, 'measuredTicks', 600, errors);
    fixedInteger(configuration.repetitions, 'repetitions', 5, errors);
  }
  if (!Array.isArray(document.scenarios)) {
    errors.push('scenarios must be an array');
  } else {
    document.scenarios.forEach((value, index) => scenario(value, index, errors));
    for (const [name, count] of [['standard-300', 300], ['stress-500', 500]]) {
      const matches = document.scenarios.filter(value =>
        value?.name === name && value?.initialEntityCount === count &&
        value?.finalEntityCount === count);
      if (matches.length !== 1)
        errors.push(`expected exactly one ${name} scenario with ${count} entities`);
    }
    if (document.scenarios.length !== 2) errors.push('scenarios must contain exactly two entries');
  }
  return errors;
}

async function main() {
  const args = process.argv.slice(2);
  if (args.length !== 2 || args[0] !== '--report' || !path.isAbsolute(args[1]))
    throw new Error('Usage: validate-simulation-performance-report.mjs --report <absolute-path>');
  const errors = await validateReport(args[1]);
  if (errors.length) {
    console.error(errors.join('\n'));
    process.exitCode = 1;
  } else {
    console.log('[simulation-performance-report] PASS');
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error.stack); process.exitCode = 1; });
