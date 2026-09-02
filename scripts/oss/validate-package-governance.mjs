#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const pkg = 'Packages/com.zwebglframework.runtime';
const contact = ['diegozhou114', 'gmail.com'].join('@');
const placeholder = '[INSERT CONTACT METHOD]';
const ccBy = 'https://creativecommons.org/licenses/by/4.0/';
const unitask = 'e5acc106ee196bc5a32fb14cdf2987b0f96d11e0';
const base = 'https://github.com/diegozhou114-cloud/frameloom/blob/v0.1.0/' + pkg;
const expectedPackage = {
  name: 'com.zwebglframework.runtime', version: '0.1.0', displayName: 'FrameLoom',
  description: 'deterministic Unity strategy-game framework', unity: '2022.3', license: 'MIT',
  author: { name: 'Diego Zhou', url: 'https://github.com/diegozhou114-cloud' },
  dependencies: { 'com.unity.ugui': '1.0.0' },
  keywords: ['unity', 'unity3d', 'unity-package', 'upm', 'webgl',
    'deterministic-simulation', 'strategy-game', 'ecs'],
  documentationUrl: `${base}/README.md`, changelogUrl: `${base}/CHANGELOG.md`, licensesUrl: `${base}/LICENSE`,
};
const expectedMit = `MIT License

Copyright (c) 2026 Diego Zhou

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
`;
const noticeFacts = {
  Kcp2k: { Upstream: 'https://github.com/MirrorNetworking/kcp2k', Revision: '66efda6686f649838d42f078fbaabf56ac449de4', License: 'MIT', Copyright: 'Copyright (c) 2016 limpo1989; Copyright (c) 2020 Paul Pacheco; Copyright (c) 2020 Lymdun; Copyright (c) 2020 vis2k', Modifications: 'Upstream source unchanged; package integration metadata added.', 'License path': 'ThirdParty/Kcp2k/LICENSE.txt', Distribution: 'ThirdParty/Kcp2k' },
  NativeWebSocket: { Upstream: 'https://github.com/endel/NativeWebSocket', Revision: 'cbbf04d42522d5f5b466224a0b8e3936676f9b32', License: 'Apache-2.0 baseline with file-specific MIT notice', Copyright: 'Copyright 2019 Endel Dreyer; Copyright 2018 Jiri Hybek', Modifications: 'WebSocket.cs conditional wrapper and package-local asmdefs/metadata added.', 'License path': 'ThirdParty/NativeWebSocket/LICENSE.txt', Distribution: 'ThirdParty/NativeWebSocket', 'Apache-2.0 baseline': 'repository-root LICENSE.txt', 'MIT file': 'ThirdParty/NativeWebSocket/Runtime/Upstream/integrations/Unity/WebSocket.jspre', 'MIT metadata conflicts': 'ThirdParty/NativeWebSocket/Runtime/Upstream/integrations/Unity/package.json; ThirdParty/NativeWebSocket/Runtime/Upstream/src/NativeWebSocket/NativeWebSocket.csproj', 'FrameLoom relicensing': 'none' },
  UniTask: { Upstream: 'https://github.com/Cysharp/UniTask', Revision: unitask, License: 'MIT', Copyright: 'Copyright (c) 2019 Yoshifumi Kawai / Cysharp, Inc.', Modifications: 'Not vendored or modified; consumer installs directly.', 'License path': 'Upstream repository LICENSE', Distribution: 'Not distributed in the FrameLoom package or declared in its package.json', Version: '2.5.11' },
};
const commonFields = ['Upstream', 'Revision', 'License', 'Copyright', 'Modifications', 'Version'];
const retained = { Kcp2k: ['ThirdParty/Kcp2k/LICENSE.txt', '1a12b7192346c004f7c3e5ce90d652750aa0fe7a29df67cac5f6271433808fe0'], NativeWebSocket: ['ThirdParty/NativeWebSocket/LICENSE.txt', '50e754644c6bf26a359a7d278a660625344243342087de19fb8c9c90147307f8'] };
const covenantHash = '369bf7301883368fc19203bd0f1233fed2b83f0378ad19c4d0708bf61925339b';

const digest = value => createHash('sha256').update(value).digest('hex');
const equal = (left, right) => JSON.stringify(left) === JSON.stringify(right);
async function readBytes(root, relative, errors, missing) {
  try { return await readFile(path.join(root, relative)); }
  catch (error) { errors.push(error.code === 'ENOENT' ? missing : `Cannot read ${relative}: ${error.message}`); return null; }
}
async function readText(root, relative, errors, missing) {
  const value = await readBytes(root, relative, errors, missing); return value === null ? null : value.toString('utf8');
}
async function optionalText(root, relative) { try { return await readFile(path.join(root, relative), 'utf8'); } catch { return ''; } }
function validateMetadata(value, errors) {
  for (const key of Object.keys(value)) if (!Object.hasOwn(expectedPackage, key)) errors.push(`Unexpected package metadata field: ${key}`);
  for (const [key, expected] of Object.entries(expectedPackage)) if (!equal(value[key], expected)) errors.push(key === 'author' ? 'Package author mismatch' : key === 'dependencies' ? 'Package dependencies mismatch' : key === 'keywords' ? 'Package keywords mismatch' : `Package metadata mismatch: ${key}`);
}
function validateReadme(text, errors) {
  const normalized = text.replace(/\s+/g, ' ');
  const required = [[unitask, 'README missing required UniTask revision'], ['cannot express Git dependencies transitively', 'README missing Git dependency limitation'], ['not every 2022.3 patch is tested', 'README missing Unity patch limitation'], ['synchronously borrows', 'README missing borrowed-buffer lifetime'], ['bridge-owned views', 'README missing bridge-owned-view lifetime'], ['owning copies', 'README missing recorder owning-copy lifetime']];
  for (const [phrase, diagnostic] of required) if (!normalized.includes(phrase)) errors.push(diagnostic);
  if (!normalized.includes('Unity 2022.3 LTS') || !normalized.includes('2022.3.62f3c1')) errors.push('README missing tested Unity editor');
  if ((normalized.match(/cleared, reused, or disposed/g) ?? []).length < 2) errors.push('README missing clear/reuse/dispose lifetime');
  const prohibited = [/\bfile:/i, /\bCI PASS\b/i, /!\[[^\]]*\]\([^)]*badge/i, /\bDemo\b/, /supports every Unity patch/i, /supports all Unity/i, /\bproduction-ready\b/i, /\bInternalsVisibleTo\b/, /\bBindingFlags\b/, /\breflection\b/i, /`[PR]`/, /#(?:main|master|v0\.1\.0)\b/];
  if (prohibited.some(pattern => pattern.test(normalized))) errors.push('README contains prohibited claim');
}
function validateChangelog(text, errors) {
  const sections = [...text.matchAll(/^## \[([^\]]+)\](.*)$/gm)];
  if (sections.length !== 1 || sections[0][1] !== '0.1.0' || sections[0][2].trim()) errors.push('CHANGELOG must contain one undated [0.1.0] section');
  const normalized = text.replace(/\s+/g, ' ');
  for (const phrase of ['canonical Unity package', 'Unity 2022.3 LTS', '2022.3.62f3c1', 'simulation and tick output', 'presentation and view pooling', 'Replay validation', 'public buffered presentation', 'command-recording', 'pin UniTask directly', 'repository-only reference and validation hosts', 'outside allocation gates', 'WebGL managed allocation unmeasured', 'serialized compatibility']) if (!normalized.includes(phrase)) errors.push(`CHANGELOG missing required topic: ${phrase}`);
}
function parseNotice(text, label, errors) {
  const entries = new Map();
  for (const section of text.split(/^## /m).slice(1)) {
    const [name, ...lines] = section.split('\n'); const fields = {};
    if (entries.has(name)) errors.push(`Duplicate notice entry: ${label}: ${name}`);
    for (const line of lines) { const match = line.match(/^- ([^:]+): (.*)$/); if (!match) continue; if (Object.hasOwn(fields, match[1])) errors.push(`Duplicate notice field: ${label}: ${name}.${match[1]}`); fields[match[1]] = match[2]; }
    entries.set(name, fields);
  }
  return entries;
}
function validateNoticeFacts(packageEntries, rootEntries, errors) {
  for (const [name, expected] of Object.entries(noticeFacts)) {
    const actual = packageEntries.get(name);
    if (!actual) { errors.push(`Missing package notice entry: ${name}`); continue; }
    for (const [field, value] of Object.entries(expected)) if (actual[field] !== value) errors.push(name === 'UniTask' && field === 'Revision' ? 'UniTask revision mismatch' : `Package notice fact mismatch: ${name}.${field}`);
    const root = rootEntries.get(name);
    if (!root || commonFields.some(field => root[field] !== actual[field])) errors.push(`Notice common-field mismatch: ${name}`);
  }
}
async function validateLicenses(root, errors) {
  const rootLicense = await readText(root, 'LICENSE', errors, 'Missing governance document: LICENSE');
  const packageLicense = await readText(root, `${pkg}/LICENSE`, errors, 'Missing package document: LICENSE');
  if (rootLicense !== null && rootLicense !== expectedMit) errors.push('MIT license mismatch: LICENSE');
  if (packageLicense !== null && packageLicense !== expectedMit) errors.push('MIT license mismatch: package LICENSE');
  if (rootLicense !== null && packageLicense !== null && rootLicense !== packageLicense) errors.push('MIT license mismatch: root/package bytes');
  for (const [name, [relative, expected]] of Object.entries(retained)) { const value = await readBytes(root, `${pkg}/${relative}`, errors, `Retained license missing: ${name}`); if (value !== null && digest(value) !== expected) errors.push(`Retained license mismatch: ${name}`); }
}
async function validateThirdPartyTree(root, errors) {
  try { for (const item of await readdir(path.join(root, pkg, 'ThirdParty'), { withFileTypes: true })) if (item.isDirectory() && !['Kcp2k', 'NativeWebSocket'].includes(item.name)) errors.push(`Undisclosed package third-party path: ${item.name}`); }
  catch (error) { errors.push(`Cannot audit package third-party paths: ${error.message}`); }
  const basePath = `${pkg}/ThirdParty/NativeWebSocket/`;
  const indicators = [['Runtime/Upstream/integrations/Unity/WebSocket.jspre', /Licensed under the MIT License/], ['Runtime/Upstream/integrations/Unity/package.json', /"license"\s*:\s*"MIT"/], ['Runtime/Upstream/src/NativeWebSocket/NativeWebSocket.csproj', /<PackageLicenseExpression>MIT<\/PackageLicenseExpression>/]];
  for (const [relative, pattern] of indicators) { const text = await readText(root, basePath + relative, errors, 'NativeWebSocket MIT indicator missing'); if (text !== null && !pattern.test(text)) errors.push(`NativeWebSocket MIT indicator missing: ${relative}`); }
}
async function validateGovernance(root, rootNotice, errors) {
  const files = {};
  for (const relative of ['CONTRIBUTING.md', 'SECURITY.md', 'CODE_OF_CONDUCT.md', '.github/pull_request_template.md', 'docs/dependencies/README.md']) files[relative] = await readText(root, relative, errors, `Missing governance document: ${relative}`);
  const contributing = files['CONTRIBUTING.md']?.replace(/\s+/g, ' ') ?? '';
  const contributionPolicies = [
    ['GitHub Issues and pull requests', /\bGitHub Issues\b.*\bpull requests\b/i],
    ['No CLA, DCO, Signed-off-by, bot, or signing service', /\bNo CLA, DCO, Signed-off-by, bot, or signing service\b/i],
    ['retain copyright', /\bretain copyright\b/i],
    ['publish project-owned contributions under MIT', /\bpublish project-owned contributions under (?:the )?MIT(?: license)?\b/i],
    ['submission rights and non-infringement', /\bsubmission rights\b.*\bnon-infring(?:ement|ing)\b/i],
    ['third-party source, license, and changes', /\bthird-party source, license, and changes?\b/i],
    ['unclear permission is prohibited', /\bunclear permission is prohibited\b/i],
    ['prior maintainer approval and synchronized notices', /\bprior maintainer approval and synchronized notices\b/i],
  ];
  for (const [policy, pattern] of contributionPolicies) if (!pattern.test(contributing)) errors.push(`CONTRIBUTING missing required policy: ${policy}`);
  const security = files['SECURITY.md'] ?? '';
  for (const phrase of ['`0.1.x` | Yes', '`< 0.1` | No', 'GitHub Private Vulnerability Reporting', contact, 'intake and evaluation only', 'no response, remediation, patch, fix, or SLA is promised']) if (!security.replace(/\s+/g, ' ').includes(phrase)) errors.push('Security policy mismatch');
  if (/\b(?:response|respond|fix|patch|remediation)\s+within\b/i.test(security) || /\bguaranteed?\b/i.test(security)) errors.push('Security policy overpromises');
  const conduct = files['CODE_OF_CONDUCT.md'] ?? ''; const suffix = `\nContributor Covenant 2.1 is licensed under [CC BY 4.0](${ccBy}).\n`;
  const core = conduct.endsWith(suffix) ? conduct.slice(0, -suffix.length) : '';
  if (conduct.split(contact).length !== 2 || core.includes(placeholder) || digest(core.replace(contact, placeholder)) !== covenantHash) errors.push('Code of Conduct mismatch');
  const pr = files['.github/pull_request_template.md'] ?? '';
  for (const phrase of ['right to submit', 'project-owned contributions under MIT', 'third-party source, license, and change', 'prior maintainer approval and synchronized notices']) if (!pr.includes(phrase)) errors.push(`Pull request template mismatch: ${phrase}`);
  const decision = files['docs/dependencies/README.md']?.replace(/\s+/g, ' ') ?? '';
  const classification = [/\brepository-wide distribution baseline\b/i, /\bfile-specific MIT notice\b/i,
    /\bMIT declarations\b.*\bmetadata conflicts\b/i, /\bNone grants FrameLoom MIT rights\b/i];
  if (classification.some(pattern => !pattern.test(decision))) errors.push('NativeWebSocket classification mismatch');
  if (/TODO\(formal-project-review\)|\b(?:TBD|unresolved|unknown)\b/i.test(rootNotice + decision)) errors.push('Unresolved licensing-review deferral');
  const exposed = ['README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CONTRIBUTING.md', '.github/pull_request_template.md', 'docs/dependencies/README.md', `${pkg}/package.json`, `${pkg}/README.md`, `${pkg}/CHANGELOG.md`, `${pkg}/LICENSE`, `${pkg}/THIRD_PARTY_NOTICES.md`];
  for (const relative of exposed) if ((await optionalText(root, relative)).includes(contact)) errors.push(`Governance email exposure: ${relative}`);
}
async function validateNotices(root, errors) {
  const packageNotice = await readText(root, `${pkg}/THIRD_PARTY_NOTICES.md`, errors, 'Missing package document: THIRD_PARTY_NOTICES.md');
  const rootNotice = await readText(root, 'THIRD_PARTY_NOTICES.md', errors, 'Missing governance document: THIRD_PARTY_NOTICES.md');
  if (packageNotice === null || rootNotice === null) return;
  const packageEntries = parseNotice(packageNotice, 'package', errors); const rootEntries = parseNotice(rootNotice, 'root', errors);
  validateNoticeFacts(packageEntries, rootEntries, errors);
  const nativeEntry = packageEntries.get('NativeWebSocket') ?? {};
  if (nativeEntry['FrameLoom relicensing'] !== 'none') errors.push('NativeWebSocket relicensing mismatch');
  const requiredRoot = ['com.coplaydev.unity-mcp 10.1.2', '4ce7dd3cc54e37e2ed6dc59cb5a047f3dccb3f50', 'com.unity.ide.rider 3.0.40', 'com.unity.physics 1.3.15', '@playwright/test 1.61.1', 'yaml 2.9.0', 'Microsoft.NET.Test.Sdk 17.11.1', 'NUnit 4.2.2', 'NUnit3TestAdapter 4.6.0', 'actions/checkout@v4', 'actions/setup-node@v4', 'actions/cache@v4', 'actions/setup-dotnet@v4', 'game-ci/unity-test-runner@v4', 'game-ci/unity-builder@v4', 'actions/upload-artifact@v4', 'actions/download-artifact@v4', ccBy];
  for (const phrase of requiredRoot) if (!rootNotice.includes(phrase)) errors.push(`Root notice missing repository-only dependency: ${phrase}`);
  const covenant = rootEntries.get('Contributor Covenant');
  if (!covenant || covenant.Upstream !== 'https://www.contributor-covenant.org/version/2/1/code_of_conduct/' || covenant.Revision !== '2.1' || covenant.License !== 'CC BY 4.0' || covenant['License path'] !== ccBy) errors.push('Contributor Covenant notice mismatch');
  await validateGovernance(root, rootNotice, errors);
}

export async function validatePackageGovernance({ root }) {
  if (typeof root !== 'string' || root.length === 0) throw new Error('Invalid root: path must be nonempty');
  const errors = [];
  const manifest = await readText(root, `${pkg}/package.json`, errors, 'Missing package document: package.json');
  if (manifest !== null) { try { const value = JSON.parse(manifest); if (value === null || Array.isArray(value) || typeof value !== 'object') errors.push('Package manifest must be a JSON object'); else validateMetadata(value, errors); } catch (error) { errors.push(`Invalid package manifest JSON: ${error.message}`); } }
  const readme = await readText(root, `${pkg}/README.md`, errors, 'Missing package document: README.md');
  if (readme !== null) { if (/\p{Script=Han}/u.test(readme)) errors.push('Package document must be English: README.md'); validateReadme(readme, errors); }
  const changelog = await readText(root, `${pkg}/CHANGELOG.md`, errors, 'Missing package document: CHANGELOG.md');
  if (changelog !== null) { if (/\p{Script=Han}/u.test(changelog)) errors.push('Package document must be English: CHANGELOG.md'); validateChangelog(changelog, errors); }
  await validateLicenses(root, errors); await validateNotices(root, errors); await validateThirdPartyTree(root, errors);
  return { errors };
}
function parseArgs(values) { if (values.length !== 2 || values[0] !== '--root' || !values[1]) throw new Error('Usage: validate-package-governance.mjs --root <root>'); return { root: values[1] }; }
async function main() { const result = await validatePackageGovernance(parseArgs(process.argv.slice(2))); for (const error of result.errors) console.error(error); if (result.errors.length) process.exitCode = 1; else console.log('[validate-package-governance] PASS'); }
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main().catch(error => { console.error(error.message); process.exitCode = 1; });
