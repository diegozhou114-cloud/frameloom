import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { createRequire } from 'node:module';

const require = createRequire(new URL('../../ci/package.json', import.meta.url));
const YAML = require('yaml');

const source = await readFile('.github/workflows/ci.yml', 'utf8');
const playwrightConfig = await readFile('ci/smoke/playwright.config.mjs', 'utf8');
const workflow = YAML.parse(source);
const normalizeShell = value => String(value || '').replace(/\s+/g, ' ').trim();
const forbiddenReleaseCommands = [
  /(?:\.\/)?scripts\/oss\/(?:audit-history|release-evidence)\.mjs\b/i,
  /(?:\.\/)?scripts\/oss\/run-required-gates\.sh\b/i,
  /\bgit\b[^\n;&|]{0,200}\b(?:push|tag|ls-remote|remote)\b/i,
  /\bgh\b[^\n;&|]{0,200}\b(?:repo|release)\b/i,
];
const forbiddenPassClaims = [
  /GitHub Actions(?: CI)?[^\n]{0,32}\bPASS\b/i,
  /WebGL[^\n]{0,48}managed[ -]?allocation[^\n]{0,48}\bPASS\b/i,
];
const requiredPaths = [
  'Assets/**', 'Packages/**', 'ProjectSettings/**', 'Server/**',
  '.github/workflows/**', 'scripts/**', 'ci/**',
  'docs/superpowers/specs/2026-07-16-project-repair-evidence.json',
  'docs/superpowers/evidence/**',
];

test('Workflow_AllUnityActionsUsePinnedProjectVersion', () => {
  const actionSteps = Object.values(workflow.jobs).flatMap(job => job.steps || [])
    .filter(step => String(step.uses || '').startsWith('game-ci/'));
  assert.ok(actionSteps.length >= 1);
  for (const step of actionSteps)
    assert.equal(step.with?.unityVersion, '${{ env.UNITY_VERSION }}');
  assert.equal(workflow.env.UNITY_VERSION, '2022.3.62f3c1');
  assert.doesNotMatch(source, /2022\.3\.40f1|6000\./);
});

test('Workflow_TriggersCoverAllGateInputs', () => {
  for (const event of ['push', 'pull_request']) {
    const paths = workflow.on[event].paths;
    for (const required of requiredPaths)
      assert.ok(paths.includes(required), `${event} missing ${required}`);
  }
});

test('all mutating jobs end with an exact clean-tree gate', () => {
  for (const [name, job] of Object.entries(workflow.jobs)) {
    if (!job.steps) continue;
    const commands = job.steps.map(step => step.run || '').join('\n');
    assert.match(commands, /git status --porcelain --untracked-files=all/,
      `${name} has no clean-tree command`);
  }
});

test('CI_ServerProjects_BuildAndTestReleaseWarnAsError', () => {
  const server = workflow.jobs.server;
  assert.ok(server, 'server job missing');
  assert.deepEqual(server.strategy.matrix.os, ['ubuntu-latest', 'windows-latest']);
  assert.equal(server.env.ZWEB_REPO_ROOT, '${{ github.workspace }}');
  const commands = server.steps.map(step => step.run || '').join('\n');
  assert.match(commands,
    /dotnet restore Server\/ZWebGLFrameWork\.Server\.sln --locked-mode/);
  assert.match(commands,
    /dotnet build Server\/ZWebGLFrameWork\.Server\.sln -c Release --no-restore --warnaserror/);
  assert.match(commands,
    /dotnet test Server\/ZWebGLFrameWork\.Server\.sln -c Release --no-build --no-restore/);
  assert.match(commands, /--artifacts-path "\$DOTNET_ARTIFACTS"/);
  assert.match(commands, /LogFilePrefix=server/);
  assert.doesNotMatch(commands, /LogFileName=server\.trx/);
  assert.match(commands, /validate-trx\.py --results/);
});

test('CI_BuildJobs_InvokeRepositoryBuildScriptAndSmokeArtifacts', () => {
  const build = workflow.jobs.build;
  assert.ok(build, 'build job missing');
  const action = build.steps.find(step => step.uses === 'game-ci/unity-builder@v4');
  assert.equal(action.with.unityVersion, '${{ env.UNITY_VERSION }}');
  assert.equal(action.with.buildMethod, '${{ matrix.buildMethod }}');
  const methods = build.strategy.matrix.include.map(entry => entry.buildMethod);
  assert.deepEqual(methods, [
    'ZWebGLFrameWork.Editor.BuildScript.BuildWebGL',
    'ZWebGLFrameWork.Editor.BuildScript.BuildWindows',
  ]);
  assert.match(String(build.env.ZWEB_EVIDENCE_ROOT), /runner\.temp/);
  assert.match(String(action.with.buildPath), /runner\.temp/);
  const commands = build.steps.map(step => step.run || '').join('\n');
  assert.match(commands, /validate-build-summary\.mjs/);
  assert.match(commands, /--artifact-root/);
});

test('build smoke gates require trusted HTTPS and structured Windows exit', () => {
  const build = workflow.jobs.build;
  const buildCommands = build.steps.map(step => step.run || '').join('\n');
  assert.match(buildCommands, /update-ca-certificates/);
  assert.match(source + buildCommands, /webgl\.spec\.mjs/);
  assert.match(source, /name:\s*webgl-smoke-report/);
  assert.match(source, /runner\.temp.*webgl-playwright\.json/);
  assert.ok(workflow.jobs['windows-smoke'], 'windows-smoke job missing');
  const windowsCommands = workflow.jobs['windows-smoke'].steps.map(step => step.run || '').join('\n');
  assert.ok(build.steps.some(step => step.uses === 'actions/setup-node@v4'));
  assert.match(buildCommands, /npm ci --prefix ci --ignore-scripts/);
  assert.match(buildCommands, /npx --no-install playwright install/);
  assert.match(buildCommands, /npx --no-install playwright test/);
  assert.match(windowsCommands, /zwebSmokeExitFile/);
  assert.match(windowsCommands, /app-ready/);
  assert.match(windowsCommands, /-Filter ZWebGLFrameWork\.exe/);
  assert.doesNotMatch(windowsCommands, /Select-Object -First 1/);
  assert.match(windowsCommands, /validate-player-log\.mjs/);
  assert.doesNotMatch(playwrightConfig, /ignoreHTTPSErrors:\s*true/);
});

test('CI runs repository-safe OSS contracts without publication claims', () => {
  const auditCommands = workflow.jobs.audit.steps
    .map(step => normalizeShell(step.run))
    .filter(Boolean);
  assert.ok(auditCommands.includes(
    'node --test scripts/audit/evidence-ledger.test.mjs scripts/ci/*.test.mjs scripts/oss/*.test.mjs'),
  'audit job must run the repository-safe OSS fixture and negative contracts');

  const allCommands = Object.values(workflow.jobs)
    .flatMap(job => (job.steps || []).map(step => normalizeShell(step.run)))
    .filter(Boolean)
    .join('\n');
  for (const pattern of forbiddenReleaseCommands) assert.doesNotMatch(allCommands, pattern,
    'CI must not run formal release/history, remote, tag, or Release commands');
  for (const pattern of forbiddenPassClaims) assert.doesNotMatch(source, pattern,
    'CI must not claim formal CI or WebGL managed-allocation PASS');
});

test('repository-safe checks reject normalized wrapper and claim variants', () => {
  for (const command of [
    "bash -c 'node ./scripts/oss/audit-history.mjs --revision HEAD'",
    'sh scripts/oss/run-required-gates.sh --phase package-freeze',
    'env MODE=probe git -c http.version=HTTP/1.1 ls-remote origin',
    'bash -c "gh release create v0.1.0"',
  ]) assert.ok(forbiddenReleaseCommands.some(pattern => pattern.test(normalizeShell(command))), command);
  for (const claim of ['GitHub Actions PASS', 'GitHub Actions CI - PASS',
    'WebGL managed-allocation: PASS']) {
    assert.ok(forbiddenPassClaims.some(pattern => pattern.test(normalizeShell(claim))), claim);
  }
});
