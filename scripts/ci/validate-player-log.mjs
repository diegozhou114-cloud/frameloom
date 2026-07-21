#!/usr/bin/env node
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

export async function validatePlayerLog(logPath, allowlistPath) {
  const lines = (await readFile(logPath, 'utf8')).split(/\r?\n/);
  const patterns = (await readFile(allowlistPath, 'utf8')).split(/\r?\n/)
    .filter(Boolean).map(value => new RegExp(value));
  const forbidden = /\b(?:Exception|Error|Assert|Crash|MissingReference)\b/i;
  const errors = lines.filter(line => forbidden.test(line) &&
    !patterns.some(pattern => pattern.test(line)));
  if (!lines.some(line => line.includes('"event":"app-ready"'))) errors.push('missing app-ready marker');
  if (!lines.some(line => line.includes('"event":"app-exit"'))) errors.push('missing app-exit marker');
  return errors;
}
const args = Object.fromEntries(process.argv.slice(2).reduce((pairs, value, index, all) => {
  if (index % 2 === 0) pairs.push([value.slice(2), all[index + 1]]); return pairs;
}, []));
if (process.argv[1] === fileURLToPath(import.meta.url))
  validatePlayerLog(args.log, args.allowlist).then(errors => {
    if (errors.length) { console.error(errors.join('\n')); process.exitCode = 1; }
    else console.log('[windows-smoke] PASS');
  }).catch(error => { console.error(error.stack); process.exitCode = 1; });
