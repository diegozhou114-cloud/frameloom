#!/usr/bin/env node
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const packagePath = 'Packages/com.zwebglframework.runtime';
const baseUrl = 'https://github.com/diegozhou114-cloud/frameloom/blob/v0.1.0/' + packagePath;
const expectedPackage = {
  name: 'com.zwebglframework.runtime',
  version: '0.1.0',
  displayName: 'FrameLoom',
  description: 'deterministic Unity strategy-game framework',
  unity: '2022.3',
  license: 'MIT',
  author: { name: 'Diego Zhou', url: 'https://github.com/diegozhou114-cloud' },
  dependencies: { 'com.unity.ugui': '1.0.0' },
  keywords: [
    'unity', 'unity3d', 'unity-package', 'upm', 'webgl',
    'deterministic-simulation', 'strategy-game', 'ecs',
  ],
  documentationUrl: `${baseUrl}/README.md`,
  changelogUrl: `${baseUrl}/CHANGELOG.md`,
  licensesUrl: `${baseUrl}/LICENSE`,
};

function equal(left, right) {
  return JSON.stringify(left) === JSON.stringify(right);
}

async function readDocument(root, filename, errors) {
  try {
    return await readFile(path.join(root, packagePath, filename), 'utf8');
  } catch (error) {
    if (error.code === 'ENOENT') errors.push(`Missing package document: ${filename}`);
    else errors.push(`Cannot read package document: ${filename}: ${error.message}`);
    return null;
  }
}

function validateMetadata(value, errors) {
  for (const key of Object.keys(value)) {
    if (!Object.hasOwn(expectedPackage, key))
      errors.push(`Unexpected package metadata field: ${key}`);
  }
  for (const [key, expected] of Object.entries(expectedPackage)) {
    if (equal(value[key], expected)) continue;
    if (key === 'author') errors.push('Package author mismatch');
    else if (key === 'dependencies') errors.push('Package dependencies mismatch');
    else if (key === 'keywords') errors.push('Package keywords mismatch');
    else errors.push(`Package metadata mismatch: ${key}`);
  }
}

function validateEnglish(filename, text, errors) {
  if (/\p{Script=Han}/u.test(text))
    errors.push(`Package document must be English: ${filename}`);
}

function validateReadme(text, errors) {
  const normalized = text.replace(/\s+/g, ' ');
  const required = [
    ['e5acc106ee196bc5a32fb14cdf2987b0f96d11e0', 'README missing required UniTask revision'],
    ['cannot express Git dependencies transitively', 'README missing Git dependency limitation'],
    ['not every 2022.3 patch is tested', 'README missing Unity patch limitation'],
    ['synchronously borrows', 'README missing borrowed-buffer lifetime'],
    ['bridge-owned views', 'README missing bridge-owned-view lifetime'],
    ['owning copies', 'README missing recorder owning-copy lifetime'],
  ];
  for (const [phrase, diagnostic] of required)
    if (!normalized.includes(phrase)) errors.push(diagnostic);
  if (!normalized.includes('Unity 2022.3 LTS') || !normalized.includes('2022.3.62f3c1'))
    errors.push('README missing tested Unity editor');
  const lifetimeCount = (normalized.match(/cleared, reused, or disposed/g) ?? []).length;
  if (lifetimeCount < 2) errors.push('README missing clear/reuse/dispose lifetime');
  const prohibited = [
    /\bfile:/i, /\bCI PASS\b/i, /!\[[^\]]*\]\([^)]*badge/i, /\bDemo\b/,
    /supports every Unity patch/i, /supports all Unity/i, /\bproduction-ready\b/i,
    /\bInternalsVisibleTo\b/, /\bBindingFlags\b/, /\breflection\b/i,
    /`[PR]`/, /#(?:main|master|v0\.1\.0)\b/,
  ];
  if (prohibited.some(pattern => pattern.test(normalized)))
    errors.push('README contains prohibited claim');
}

function validateChangelog(text, errors) {
  const sections = [...text.matchAll(/^## \[([^\]]+)\](.*)$/gm)];
  if (sections.length !== 1 || sections[0][1] !== '0.1.0' || sections[0][2].trim())
    errors.push('CHANGELOG must contain one undated [0.1.0] section');
  const normalized = text.replace(/\s+/g, ' ');
  const required = [
    'canonical Unity package', 'Unity 2022.3 LTS', '2022.3.62f3c1',
    'simulation and tick output', 'presentation and view pooling', 'Replay validation',
    'public buffered presentation', 'command-recording', 'pin UniTask directly',
    'repository-only reference and validation hosts', 'outside allocation gates',
    'WebGL managed allocation unmeasured', 'serialized compatibility',
  ];
  for (const phrase of required)
    if (!normalized.includes(phrase)) errors.push(`CHANGELOG missing required topic: ${phrase}`);
}

export async function validatePackageGovernance({ root }) {
  if (typeof root !== 'string' || root.length === 0)
    throw new Error('Invalid root: path must be nonempty');
  const errors = [];
  const manifest = await readDocument(root, 'package.json', errors);
  if (manifest !== null) {
    try {
      const value = JSON.parse(manifest);
      if (value === null || Array.isArray(value) || typeof value !== 'object')
        errors.push('Package manifest must be a JSON object');
      else validateMetadata(value, errors);
    } catch (error) {
      errors.push(`Invalid package manifest JSON: ${error.message}`);
    }
  }
  const readme = await readDocument(root, 'README.md', errors);
  if (readme !== null) {
    validateEnglish('README.md', readme, errors);
    validateReadme(readme, errors);
  }
  const changelog = await readDocument(root, 'CHANGELOG.md', errors);
  if (changelog !== null) {
    validateEnglish('CHANGELOG.md', changelog, errors);
    validateChangelog(changelog, errors);
  }
  return { errors };
}

function parseArgs(values) {
  if (values.length !== 2 || values[0] !== '--root' || !values[1])
    throw new Error('Usage: validate-package-governance.mjs --root <root>');
  return { root: values[1] };
}

async function main() {
  const result = await validatePackageGovernance(parseArgs(process.argv.slice(2)));
  for (const error of result.errors) console.error(error);
  if (result.errors.length) process.exitCode = 1;
  else console.log('[validate-package-governance] PASS');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
