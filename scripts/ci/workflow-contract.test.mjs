import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { createRequire } from 'node:module';

const require = createRequire(new URL('../../ci/package.json', import.meta.url));
const YAML = require('yaml');

const source = await readFile('.github/workflows/ci.yml', 'utf8');
const workflow = YAML.parse(source);
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
