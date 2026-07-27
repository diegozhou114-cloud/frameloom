import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

test('Unity generated root project files are untracked and ignored', async () => {
  const tracked = execFileSync('git', ['ls-files', '--', '*.csproj', '*.sln'], {
    encoding: 'utf8',
  }).trim().split('\n').filter(Boolean).filter(file => !file.includes('/'));

  assert.deepEqual(tracked, []);

  const gitignore = await readFile('.gitignore', 'utf8');
  assert.match(gitignore, /^\/\*\.csproj$/m);
  assert.match(gitignore, /^\/\*\.sln$/m);
});
