import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, realpath, rm, symlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { generate, validateD0, validateGates, validateMetadata, validatePublicAssets, validateRegistry } from './release-evidence.mjs';

const registryPath = path.resolve('ci/oss/gates.json');
const sha = value => createHash('sha256').update(value).digest('hex');
const registry = async () => JSON.parse(await readFile(registryPath, 'utf8'));
const metadata = { schemaVersion: 1, p: '1'.repeat(40), r: '2'.repeat(40), t: '3'.repeat(40), tagTarget: '2'.repeat(40), frameLoomRevision: '1'.repeat(40), unity: '2022.3.62f3c1', uniTaskRevision: 'e5acc106ee196bc5a32fb14cdf2987b0f96d11e0', tools: { node: '24', npm: '11', python: '3.12', dotnet: '8.0', gitleaks: '8.24' }, environments: { host: 'macOS', graphics: 'Metal' }, failures: [], unverified: ['WebGL allocation was not measured.'], limitations: ['Local results are environment-specific.'], localCiBoundary: 'Local results are not GitHub Actions results.' };
function outcomes(value, all = false) { return value.gates.map(gate => { const pass = gate.required && (all || gate.milestone === 'package-freeze'); return { id: gate.id, revision: gate.revision, status: pass ? 'PASS' : 'NOT RUN', digest: pass ? sha(gate.id) : '-', notes: gate.notes ?? 'Recorded.', evidenceScope: pass ? 'private' : 'none', ...(pass ? { evidenceFile: `${gate.id}.digest` } : {}) }; }); }
async function fixture(t) { const top = await realpath(await mkdtemp(path.join(os.tmpdir(), 'release-evidence-'))); t.after(() => rm(top, { recursive: true, force: true })); const repo = path.join(top, 'repository'), evidence = path.join(top, 'evidence'); await mkdir(repo); await mkdir(evidence); return { top, repo, evidence }; }
async function persist(root, rows) { for (const row of rows.filter(row => row.status === 'PASS' || row.status === 'FAIL')) { const bytes = `${row.id}\n`; await writeFile(path.join(root, row.evidenceFile), bytes); row.digest = sha(bytes); } }

test('registry separates P package-freeze and final R audit/diff milestones', async () => {
  const value = await registry(); assert.deepEqual(validateRegistry(value), []);
  for (const id of ['p-history-provenance', 'p-diff-whitespace-whitelist']) assert.equal(value.gates.find(gate => gate.id === id).milestone, 'package-freeze');
  for (const id of ['r-history-provenance', 'r-diff-whitespace-whitelist']) assert.equal(value.gates.find(gate => gate.id === id).milestone, 'release-candidate');
  assert.deepEqual(validateGates(value, outcomes(value), 'package-freeze'), []);
});

test('registry and result schemas fail closed for injection, drift, and applicable non-PASS', async () => {
  const value = await registry(); const bad = structuredClone(value); bad.extra = true;
  assert.match(validateRegistry(bad).join('\n'), /Invalid gate registry schema/);
  assert.doesNotThrow(() => validateGates({ schemaVersion: 1, topics: [], gates: [null] }, [], 'package-freeze'));
  delete bad.extra; bad.gates[0].notes = 'unexpected'; assert.match(validateRegistry(bad).join('\n'), /Invalid gate schema/);
  for (const mutate of [gates => gates.pop(), gates => { gates[0].required = false; gates[0].notes = 'Downgraded.'; }, gates => gates.reverse()]) { const changed = structuredClone(value); mutate(changed.gates); assert.match(validateRegistry(changed).join('\n'), /Gate registry oracle mismatch/); }
  const rows = outcomes(value); const current = rows.find(row => row.id === 'unity-editmode'); current.status = 'NOT RUN'; current.digest = '-'; current.evidenceScope = 'none'; delete current.evidenceFile;
  assert.match(validateGates(value, rows, 'package-freeze').join('\n'), /Required gate not PASS/);
  current.status = 'PASS'; current.digest = sha('x'); current.evidenceScope = 'private'; current.evidenceFile = 'unsafe.log';
  assert.match(validateGates(value, rows, 'package-freeze').join('\n'), /Invalid terminal evidence/);
  current.evidenceFile = 'unity-editmode.digest'; current.evidenceScope = 'public'; assert.match(validateGates(value, rows, 'package-freeze').join('\n'), /Invalid terminal evidence/);
  current.evidenceScope = 'private'; const future = rows.find(row => row.id === 'r-history-provenance'); future.status = 'PASS'; future.digest = sha('future'); future.evidenceScope = 'private'; future.evidenceFile = 'future.digest'; assert.match(validateGates(value, rows, 'package-freeze').join('\n'), /Future Required gate must be NOT RUN/);
  const reordered = outcomes(value); [reordered[0], reordered[1]] = [reordered[1], reordered[0]]; assert.match(validateGates(value, reordered, 'package-freeze').join('\n'), /Gate result order mismatch/);
  assert.match(validateMetadata({ ...metadata, localCiBoundary: 'CI passed.' }).join('\n'), /Invalid summary environment metadata/);
  for (const unsafe of ['/private/tmp/run', '$HOME/output', 'http://localhost:8443']) assert.match(validateMetadata({ ...metadata, limitations: [unsafe] }).join('\n'), /Invalid summary list/);
});

test('generate and independent validation require canonical external roots and exact public assets', async t => {
  const value = await registry(), state = await fixture(t), rows = outcomes(value, true); await persist(state.evidence, rows);
  const linked = path.join(state.top, 'linked'); await symlink(state.top, linked);
  await assert.rejects(generate({ registry: value, results: rows, metadata, evidenceRoot: path.join(linked, 'evidence'), repositoryRoot: state.repo, output: path.join(state.top, 'blocked') }), /Symlink ancestor rejected/);
  const output = await generate({ registry: value, results: rows, metadata, evidenceRoot: state.evidence, repositoryRoot: state.repo, output: path.join(state.top, 'public') });
  assert.deepEqual(await validatePublicAssets({ registry: value, results: rows, metadata, evidenceRoot: state.evidence, repositoryRoot: state.repo, ...output }), []);
  const records = path.join(state.top, 'results.ndjson'), meta = path.join(state.top, 'metadata.json'), cliOutput = path.join(state.top, 'cli-public'); await writeFile(records, rows.map(JSON.stringify).join('\n') + '\n'); await writeFile(meta, JSON.stringify(metadata));
  const generated = spawnSync(process.execPath, ['scripts/oss/release-evidence.mjs', 'generate', '--registry', registryPath, '--results', records, '--metadata', meta, '--evidence-root', state.evidence, '--repository-root', state.repo, '--output', cliOutput], { encoding: 'utf8' }); assert.equal(generated.status, 0);
  const checked = spawnSync(process.execPath, ['scripts/oss/release-evidence.mjs', 'validate-public', '--registry', registryPath, '--results', records, '--metadata', meta, '--evidence-root', state.evidence, '--repository-root', state.repo, '--summary', path.join(cliOutput, 'frameloom-v0.1.0-validation-summary.md'), '--checksums', path.join(cliOutput, 'frameloom-v0.1.0-sha256.txt')], { encoding: 'utf8' }); assert.equal(checked.status, 0);
  assert.deepEqual((await (await import('node:fs/promises')).readdir(path.dirname(output.summary))).sort(), ['frameloom-v0.1.0-sha256.txt', 'frameloom-v0.1.0-validation-summary.md']);
  await assert.rejects(generate({ registry: value, results: rows, metadata, evidenceRoot: state.evidence, repositoryRoot: state.repo, output: path.dirname(output.summary) }), /Output directory must be new/);
  await writeFile(output.summary, 'unsafe replacement\n'); assert.match((await validatePublicAssets({ registry: value, results: rows, metadata, evidenceRoot: state.evidence, repositoryRoot: state.repo, ...output })).join('\n'), /Public summary mismatch/);
});

test('D0 accepts only Gitee-pinned complete PASS evidence and CLI rejects malformed NDJSON', async t => {
  const state = await fixture(t), revision = '4'.repeat(40), names = ['combat', 'replay', 'pooling', 'allocation', 'soak', 'webglSmoke'], checks = {};
  const source = `https://gitee.com/zhoujiawencoding/zweb-glframe-work.git?path=/Packages/com.zwebglframework.runtime#${revision}`;
  for (const name of names) { const evidenceFile = `${name}.digest`, record = { schemaVersion: 1, check: name, revision, source, environment: 'Unity 2022.3.62f3c1 Gitee consumer', status: 'PASS' }, bytes = `${JSON.stringify(record, null, 2)}\n`; await writeFile(path.join(state.evidence, evidenceFile), bytes); checks[name] = { status: 'PASS', digest: sha(bytes), evidenceFile, evidenceScope: 'private' }; }
  const d0 = { schemaVersion: 1, revision, source, checks };
  await writeFile(path.join(state.evidence, 'd0.json'), JSON.stringify(d0)); assert.deepEqual(await validateD0({ revision, evidenceRoot: state.evidence, repositoryRoot: state.repo }), []);
  const script = path.resolve('scripts/oss/release-evidence.mjs'); const documented = spawnSync(process.execPath, [script, 'validate-d0', '--revision', revision, '--evidence-root', state.evidence], { cwd: state.repo, encoding: 'utf8' }); assert.equal(documented.status, 0);
  const extra = spawnSync(process.execPath, [script, 'validate-d0', '--revision', revision, '--evidence-root', state.evidence, '--extra', 'value'], { cwd: state.repo, encoding: 'utf8' }); assert.equal(extra.status, 1); assert.match(extra.stderr, /Invalid arguments/);
  await writeFile(path.join(state.evidence, 'combat.digest'), 'combat\n'); d0.checks.combat.digest = sha('combat\n'); await writeFile(path.join(state.evidence, 'd0.json'), JSON.stringify(d0)); assert.match((await validateD0({ revision, evidenceRoot: state.evidence, repositoryRoot: state.repo })).join('\n'), /D0 evidence binding mismatch/);
  d0.checks.replay.digest = d0.checks.combat.digest; await writeFile(path.join(state.evidence, 'd0.json'), JSON.stringify(d0)); assert.match((await validateD0({ revision, evidenceRoot: state.evidence, repositoryRoot: state.repo })).join('\n'), /Invalid D0 check: replay/);
  d0.source = 'https://example.invalid'; await writeFile(path.join(state.evidence, 'd0.json'), JSON.stringify(d0)); assert.match((await validateD0({ revision, evidenceRoot: state.evidence, repositoryRoot: state.repo })).join('\n'), /Invalid D0 evidence schema/);
  const bad = path.join(state.top, 'bad.ndjson'); await writeFile(bad, '{bad}\n'); const result = spawnSync(process.execPath, ['scripts/oss/release-evidence.mjs', 'validate-gates', '--registry', registryPath, '--results', bad, '--through', 'package-freeze'], { encoding: 'utf8' }); assert.equal(result.status, 1); assert.match(result.stderr, /Invalid NDJSON results/);
});
