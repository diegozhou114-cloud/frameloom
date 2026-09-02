import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { gunzipSync } from 'node:zlib';
import { validatePackageGovernance } from './validate-package-governance.mjs';

const pkg = 'Packages/com.zwebglframework.runtime';
const unitask = 'e5acc106ee196bc5a32fb14cdf2987b0f96d11e0';
const native = 'cbbf04d42522d5f5b466224a0b8e3936676f9b32';
const contact = ['diegozhou114', 'gmail.com'].join('@');
const ccBy = 'https://creativecommons.org/licenses/by/4.0/';
const base = 'https://github.com/diegozhou114-cloud/frameloom/blob/v0.1.0/' + pkg;
const manifest = {
  name: 'com.zwebglframework.runtime', version: '0.1.0', displayName: 'FrameLoom',
  description: 'deterministic Unity strategy-game framework', unity: '2022.3', license: 'MIT',
  author: { name: 'Diego Zhou', url: 'https://github.com/diegozhou114-cloud' },
  dependencies: { 'com.unity.ugui': '1.0.0' },
  keywords: ['unity', 'unity3d', 'unity-package', 'upm', 'webgl',
    'deterministic-simulation', 'strategy-game', 'ecs'],
  documentationUrl: `${base}/README.md`, changelogUrl: `${base}/CHANGELOG.md`, licensesUrl: `${base}/LICENSE`,
};
const readme = `# FrameLoom
Unity 2022.3 LTS; tested on 2022.3.62f3c1, and not every 2022.3 patch is tested.
Consumers directly pin UniTask ${unitask} because Unity 2022.3 package manifests cannot express Git dependencies transitively.
SessionPresentationBridge synchronously borrows data, retains bridge-owned views, and remains valid after the parent is cleared, reused, or disposed.
CommandRecorder makes owning copies that remain valid after the parent is cleared, reused, or disposed.
`;
const changelog = `# Changelog
## [0.1.0]
One canonical Unity package supports Unity 2022.3 LTS, tested on 2022.3.62f3c1, with simulation and tick output, presentation and view pooling, Replay validation, public buffered presentation and command-recording. Consumers pin UniTask directly. Server projects remain repository-only reference and validation hosts. Replay capture stays outside allocation gates; WebGL managed allocation unmeasured. Existing serialized compatibility is preserved.
`;
const mit = `MIT License

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
const zipped = value => gunzipSync(Buffer.from(value, 'base64'));
const covenantBody = zipped('H4sIAAAAAAACA71YTY8bNxK961cQ8CHAYKyBvXua22RiZ32I7Y1nN4fBIKC6S92Mu8kOyZYs//q8qmr1hyRvBosgl5mWmixW1Xv1qqgX5j74HN2mzyHieUfe+oyHkkzY8suyL/Jq9eKF+dBH87GhsqLV6hcyNpmW2g3FdG2KyQY+WV+ahmyJV6aTDSYH09rPZDobsytcZ7ML3jhvAowWoW177/LBWFPbaFNqyeeX20hk6EtH0ZEvyGzhIO0oHoKnaxOpsrFsKCX201b4ahPKg0nuKx53LrlNgxAiDjl+KF2yG9fgoGtDufZwhB8TfTEFn1tkHJXgH2KoyMN/40p4Ip4hJvgScR48v0Z8O2r45MlBGEWuJDIYDYULLwmJCa0rTMo29zDr5bVVH7Ax8Qdju45wvtiAG/hb2JT5X2hC5FgbV4lZxAN3e+xZeBbYgyy21wLOlHZEJUucR3T8AUnf20MyubZ5Ak7XAo+OcMyeGoDifHWNpCHlCb44XzR9wicFuCbb5PowgbceSfIpYwHASavVmy+2hS+C0YZqu3OI4OTkJEebLiSXYd4QAIvBMwcE8yVFxI2SblerK/MDtcGnHBG4rwy1oFWtCfnsfOmZGzns4YkJuQaaHQU4g43fE28AmB0VedsLkKXbboEkvg+d88hkYhrRvgtI3cDqCewEKz+6HS/nFxXDBkMNji8K6sShQpxD+XBUW6JyY4vP2Hc3rmAPsMgpK8WS7YB55b7ya+Ql1yGh1uAa2FmazUHS0YKmKKep1qLn9dsYWuyYVw2OexsK4MZxAXlOvUvAImXjQza/9Umz3CeuaKQNQZUgGGwDHHnFFiEM0TbNBMQS295r2JbrbAR6jtUDjPRJREUZjDqF69ZXPYpXCrXFQzxoTAPLbc5Mc3iOBbbccY1owfuDgMyWY2ga4arzqW8ksViN8g2VhSIpR2kEcSw7LEKyQbpCTwI6DOvHftOgZPltdDuL0phE6fg61XIKsyp9N65zHulqjwrQFzWnFMyuD0nOYAFrrcNhZclKgkJzABhphiEXGbaGRYldBMSsNDjwg3C3UCUGgg52i9A3JehjOZINWLchoRtUIRIXOyQlBvilbrETMWxVvOBIosxpkop9w04XJOX285yPDiRf3Y+Fd1R0G2nibaOyXDQ2uu3hWA0kJiVDIGs6yoHgdoEmvGXvQC7mtJl7zm+21nH0MZIWEjRs6B2DF4N0HU4EBlk7gATULrNxjRfIG2gllAmoLK+qho0AukUVry/FDdskpRBdVaumnpQv3IhQpJ10ApfFYqTf4PeMgfzk9KEkJsBnJ6vxDRDvjzUdjqCrSLIcaVicfi5cFFDlAbVoBCr6pGNfT1kdSrbghCpjkoDWYkfURlxS4ZKcsa/JzyFQVf9UoC+sVg8XDuLFDagiVGamzUXCpA66OIQETQnTajnHzwSHZQlogP+WZTQSN1sufhbCmhYtwHRao2p+beZStNh4sXcYlUPudsN5hl4u6xLNSPbvnF2s47aOfy0Qs8zl0HvFmVnJNhMvR5DcNKicnLHK3SzWPORKJA925RHDhM/r03Jcrd55Lp6j5G2G9qtypPSNypS9QxlcFuHWijrAkxDzkTDzdB4JflrUNBMGOP747v2nNz8/mPsP7x/u7h/MT28e/vXhh6e1uVPEu8Zyo1TOyYHcPXGgTh87dBwHPcYXIFfb5eYwlnfDw8PdgjhztQnAWndKhUnTlhhEd4vD0DCKPvJOZEqqVOONx2YB7GVkOsuy+bHHC4bhotxJOFt0mLBnu0jztOYdJo5iboCpWVJm9fYTbyFRv/cyNEhe2ZtBxOYaBa6FRstRQjgvtVt2/YV5tcYXKofoD6urq1OHrq5uzX+01S4bwbzbqsCMLGEnkODeL/oEzwVeh0FpIwvirPXsMTo+9u7YC6FsQAMqi2FzNpycAXzNbIAAyLzEXYTHoIiyKuUwzMt9pCOmU4ZET7w0TOvHpO0x/fG6Maq9jDRzOWMPVTt00DpM9YEoMHSXa83y67X5RT3/VorvZv6gp4S+Qr83XJcNjXTTiT26oYQFs3Q5c8dEsZCes4ZbgfM9z4BDdGvzPoxjPTshG6UuZM7logvNjsphdpcc9z4FmTGovLSXh82pe7Ots74iFDZchG7rSMYpF0qByLVIsDSJQWlRv7ug4M4OkzI57REsnWBaw//pC9Yy/3At856aZBqHyWAuvmvzX02+uslDAKouCZpMLb1TZNwJQrQYAXmawwLrGZON9QPK/4C/45rvrf821gxiwJC8qNJZEMchZ57shOEaong2kI0IXuTB5DT81LJhyUiQMy3pCTWOStk8NfkFEyYH/ww2cKk7G3z/fnaBO5a1FgbKPuoyfKfePg/0Szj/c20+jl//D5yXN0rY4gtIFKz/SuCvZ1cKbVCzQUjniaoafmsYr7CRf8DosK+i4z4oZkoqLbOb2zeEeZ6Wc1oN4J+idkH0kc67PI6l35gKGcfSdkyE8Vb6eOmnpqfHGs2lQ1BPuG+jI/DJr9evMDPuMJLJLMPTR51zl25vbvb7/Xr2c9PLYrCzDrG6GfbfvL55dcPz9a9h++twcVrXuW2eHncw/bSY78+b+B53KL5Ldi7qlfvxp/AVY4D9LsnUrgTQOOdDUoPxkeLTcTVCvOej3krPT3ueJkBQTiVClG4jYmg3egN0Z9b5tym9c7y9+/fzs7C1vz89Ygdms4eIk5W2Okv9H0nNMxtPj4tPiG6C79Y8x9xKIXje4j8FdLVI9mS0AnX7Dey2N60uuNEfsviXC8nNMx1ALlfLmJ+5cb5n9QdTmOc1ZhUAAA==').toString('utf8');
const retained = {
  Kcp2k: zipped('H4sIAAAAAAACA21T3W/aMBB/919x6lMrRV3bh2ndm0lMsZYvOaGMx5AY4i3EKHaK+O93F2i7bpUQyHf3+7oTiSwhNrXunWYstIfTYHath+v6Bh7u7r9CZ/YHe//47fG/5sMd5NXY4Vfd6tp+1o9P+2bsP+u8GPfwm7FcD3vjnLE9GAetHvTmBLuh6r1uAtgOWoPdQt1Ww04H4C1U/QkOenAIsBtfmd70O6igRgWGk75FGme3/lgNGocbqJyztamQDxpbj3vd+8qT3tZ02sG1bzVcFRfE1c0k0uiqY6YH6r224Gh8a0cPg3Z+MDVxBGD6uhsb8vDaxn2ZiwLBp9iOIenoMAH5DGBvG7OlXz3FOoybzrg2gMYQ9Wb0WHRUnM4SUI4vdgCnu44hg0HfU9Z3d9MMWT/QQv1lRY4qx9buPyYxjm3HoUdJPWEaiyubFH/p2lOFxre26+yRotW2bwwlct8ZK7FVbeyLnrKcr9pbj1bPFugAh/erXlqurboONvqyMNTF9VZ/xRlI3nk8vKk6ONhh0vs35i3qLwQU2bxccSVAFpCr7FlGIoIrXuD7KoCVLBfZsgScUDwt15DNgadr+CHTKADxM1eiKCBTTCZ5LAXWZBrGy0imTzBDXJrhP0ImskTSMgMSvFBJURBZIlS4wCefyViW64DNZZkS5zxTwCHnqpThMuYK8qXKs0KgfIS0qUznClVEItLyFlWxBuIZH1AseByTFONLdK/IH4RZvlbyaVHCIosjgcWZQGd8FouzFIYKYy6TACKe8CcxoTJkUYzGzu5gtRBUIj2On7CUWUoxwiwtFT4DTKnKN+hKFiIArmRBC5mrLAkYrRMR2USCuFScWWjV8OEiOELvZSHeCCESPEaugsAU8XX49g/+jFy1fAQAAA=='),
  NativeWebSocket: zipped('H4sIAAAAAAACA2WRT2+bQBDF7/spnnxqJRenOfVPFIXarkIbYck4jXJcYIBpYZfuLiXk03dwXMlR98I+zduZ3xvWtp8c103A5cX7j9iaklpsHE3kcEWzisqjuqk7zW1U2O5arc8ffcA3dozbKadfuPop95tmvkfF87VSd1yQ8VRikF4OoSHEvS7kc6os8YOcZ2twGV3gzWxYnEqLt5/VZAd0eoKxAYMnacAeFbcEeiqoD2ADYepb1qYgjBya45BTi0g9nhrYPGjxanH3oqpzF3RQCnKaEPpPq9U4jpE+UkbW1av2xeVXd8l6m2bbd0Kq1L1pyXs4+j2wk4D5BN0LR6FzoWv1COuga0dSC3bmHB0HNvUS3lZh1I5UyT44zofwakH/qCTpuUFWpA0WcYYkW+BLnCXZUj0kh9vd/QEP8X4fp4dkm2G3x3qXbpJDsktFfUWcPuJ7km6WIFmPDKGn3s3sAsjz6qiMVEb0anhlX2B8TwVXXEgiUw+6JtT2DzkjQdCT69jPP88LWqla7jjocNT/xYmU+gvRCClgbQIAAA=='),
};
function entry(name, upstream, revision, license, copyright, modifications, licensePath, distribution) {
  return `## ${name}\n- Upstream: ${upstream}\n- Revision: ${revision}\n- License: ${license}\n- Copyright: ${copyright}\n- Modifications: ${modifications}\n- License path: ${licensePath}\n- Distribution: ${distribution}\n`;
}
const kcp = entry('Kcp2k', 'https://github.com/MirrorNetworking/kcp2k', '66efda6686f649838d42f078fbaabf56ac449de4', 'MIT', 'Copyright (c) 2016 limpo1989; Copyright (c) 2020 Paul Pacheco; Copyright (c) 2020 Lymdun; Copyright (c) 2020 vis2k', 'Upstream source unchanged; package integration metadata added.', 'ThirdParty/Kcp2k/LICENSE.txt', 'ThirdParty/Kcp2k');
const nws = entry('NativeWebSocket', 'https://github.com/endel/NativeWebSocket', native, 'Apache-2.0 baseline with file-specific MIT notice', 'Copyright 2019 Endel Dreyer; Copyright 2018 Jiri Hybek', 'WebSocket.cs conditional wrapper and package-local asmdefs/metadata added.', 'ThirdParty/NativeWebSocket/LICENSE.txt', 'ThirdParty/NativeWebSocket') + `- Apache-2.0 baseline: repository-root LICENSE.txt\n- MIT file: ThirdParty/NativeWebSocket/Runtime/Upstream/integrations/Unity/WebSocket.jspre\n- MIT metadata conflicts: ThirdParty/NativeWebSocket/Runtime/Upstream/integrations/Unity/package.json; ThirdParty/NativeWebSocket/Runtime/Upstream/src/NativeWebSocket/NativeWebSocket.csproj\n- FrameLoom relicensing: none\n`;
const uni = entry('UniTask', 'https://github.com/Cysharp/UniTask', unitask, 'MIT', 'Copyright (c) 2019 Yoshifumi Kawai / Cysharp, Inc.', 'Not vendored or modified; consumer installs directly.', 'Upstream repository LICENSE', 'Not distributed in the FrameLoom package or declared in its package.json') + '- Version: 2.5.11\n';
const packageNotice = `# Third-Party Notices\n\nPackage-relative paths below start at ${pkg}.\n\n${kcp}\n${nws}\n${uni}`;
const repoOnly = `## Repository-only dependencies\n- Unity host: Unity 2022.3.62f3c1 packages locked by Packages/manifest.json and Packages/packages-lock.json, including com.coplaydev.unity-mcp 10.1.2 at 4ce7dd3cc54e37e2ed6dc59cb5a047f3dccb3f50, com.unity.ide.rider 3.0.40, and com.unity.physics 1.3.15.\n- CI: @playwright/test 1.61.1 (Apache-2.0) and yaml 2.9.0 (ISC), locked by ci/package-lock.json.\n- Server tests: Microsoft.NET.Test.Sdk 17.11.1, NUnit 4.2.2, and NUnit3TestAdapter 4.6.0, declared in Server test projects.\n- Workflows: actions/checkout@v4, actions/setup-node@v4, actions/cache@v4, actions/setup-dotnet@v4, game-ci/unity-test-runner@v4, game-ci/unity-builder@v4, actions/upload-artifact@v4, and actions/download-artifact@v4.\n- Scope: host validation and repository tooling only; not distributed in the FrameLoom package. Each remains under its upstream terms.\n`;
const covenant = entry('Contributor Covenant', 'https://www.contributor-covenant.org/version/2/1/code_of_conduct/', '2.1', 'CC BY 4.0', 'Contributor Covenant contributors', 'Approved private contact only; core text unchanged.', ccBy, 'Repository CODE_OF_CONDUCT.md only');
const rootNotice = `${packageNotice}\n${repoOnly}\n${covenant}`;
const contributing = `# Contributing\nUse GitHub Issues and pull requests for ordinary work. Security and conduct reports use their private policies; no support or SLA is promised.\nNo CLA, DCO, Signed-off-by, bot, or signing service is required. Contributors retain copyright and agree to publish project-owned contributions under MIT, warrant submission rights and non-infringement, and disclose third-party source, license, and changes. Content with unclear permission is prohibited; vendored changes require prior maintainer approval and synchronized notices.\n`;
const security = `# Security Policy\n| Version line | Receives security reports |\n| --- | --- |\n| \`0.1.x\` | Yes |\n| \`< 0.1\` | No |\nPrefer GitHub Private Vulnerability Reporting; if unavailable, email ${contact}. Do not disclose unfixed vulnerabilities in public Issues. Reports receive intake and evaluation only; no response, remediation, patch, fix, or SLA is promised.\n`;
const conductLicense = `\nContributor Covenant 2.1 is licensed under [CC BY 4.0](${ccBy}).\n`;
const conduct = covenantBody.replace('[INSERT CONTACT METHOD]', contact) + conductLicense;
const pr = `# Pull request checklist\n- [ ] I have the right to submit this contribution.\n- [ ] I agree to publish project-owned contributions under MIT.\n- [ ] I disclosed every third-party source, license, and change, or this contains none.\n- [ ] Vendored additions or updates have prior maintainer approval and synchronized notices.\n`;
const decision = `# NativeWebSocket licensing classification\nThe root Apache-2.0 license is the repository-wide distribution baseline. WebSocket.jspre remains under its file-specific MIT notice. The Unity package.json and NativeWebSocket.csproj MIT declarations are metadata conflicts. None grants FrameLoom MIT rights over vendored content.\n`;

async function put(root, relative, contents) { const target = path.join(root, relative); await mkdir(path.dirname(target), { recursive: true }); await writeFile(target, contents); }
async function fixture(t) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'zweb-governance-')); t.after(() => rm(root, { recursive: true, force: true }));
  const files = { [`${pkg}/package.json`]: JSON.stringify(manifest, null, 2) + '\n', [`${pkg}/README.md`]: readme, [`${pkg}/CHANGELOG.md`]: changelog, LICENSE: mit, [`${pkg}/LICENSE`]: mit, 'THIRD_PARTY_NOTICES.md': rootNotice, [`${pkg}/THIRD_PARTY_NOTICES.md`]: packageNotice, 'CONTRIBUTING.md': contributing, 'SECURITY.md': security, 'CODE_OF_CONDUCT.md': conduct, '.github/pull_request_template.md': pr, 'docs/dependencies/README.md': decision, [`${pkg}/ThirdParty/Kcp2k/LICENSE.txt`]: retained.Kcp2k, [`${pkg}/ThirdParty/NativeWebSocket/LICENSE.txt`]: retained.NativeWebSocket, [`${pkg}/ThirdParty/NativeWebSocket/Runtime/Upstream/integrations/Unity/package.json`]: '{"license":"MIT"}\n', [`${pkg}/ThirdParty/NativeWebSocket/Runtime/Upstream/src/NativeWebSocket/NativeWebSocket.csproj`]: '<PackageLicenseExpression>MIT</PackageLicenseExpression>\n', [`${pkg}/ThirdParty/NativeWebSocket/Runtime/Upstream/integrations/Unity/WebSocket.jspre`]: '// Licensed under the MIT License. See LICENSE in the project root for license information.\n' };
  for (const [file, contents] of Object.entries(files)) await put(root, file, contents); return root;
}
const errors = async root => (await validatePackageGovernance({ root })).errors.join('\n');
async function change(root, file, from, to) { const target = path.join(root, file); await writeFile(target, (await readFile(target, 'utf8')).replace(from, to)); }
async function changeManifest(root, mutate) { const value = structuredClone(manifest); mutate(value); await put(root, `${pkg}/package.json`, JSON.stringify(value, null, 2) + '\n'); }

test('accepts exact task 4.1 and 4.2 contract', async t => assert.equal(await errors(await fixture(t)), ''));
test('retains task 4.1 metadata diagnostics', async t => {
  const cases = [[v => { v.displayName = 'Other'; }, /Package metadata mismatch: displayName/], [v => { v.repository = 'mutable'; }, /Unexpected package metadata field: repository/], [v => { v.author.email = 'public@example.com'; }, /Package author mismatch/], [v => { v.dependencies.extra = '1'; }, /Package dependencies mismatch/], [v => v.keywords.reverse(), /Package keywords mismatch/], [v => { v.documentationUrl = v.documentationUrl.replace('v0.1.0', 'main'); }, /Package metadata mismatch: documentationUrl/]];
  for (const [mutate, diagnostic] of cases) { const root = await fixture(t); await changeManifest(root, mutate); assert.match(await errors(root), diagnostic); }
});
test('retains task 4.1 document diagnostics', async t => {
  const missing = await fixture(t); await rm(path.join(missing, pkg, 'README.md')); assert.match(await errors(missing), /Missing package document: README.md/);
  for (const [from, to, diagnostic] of [[unitask, 'main', /README missing required UniTask revision/], ['not every 2022.3 patch is tested', 'all patches are supported', /README missing Unity patch limitation/], ['synchronously borrows', 'uses', /README missing borrowed-buffer lifetime/], ['owning copies', 'references', /README missing recorder owning-copy lifetime/]]) { const root = await fixture(t); await change(root, `${pkg}/README.md`, from, to); assert.match(await errors(root), diagnostic); }
  for (const phrase of ['file:../runtime', 'CI PASS', 'Demo', 'supports every Unity patch', '内部接口']) { const root = await fixture(t); await put(root, `${pkg}/README.md`, `${readme}\n${phrase}\n`); assert.match(await errors(root), phrase === '内部接口' ? /must be English/ : /prohibited claim/); }
});
test('retains wrapping and changelog contracts', async t => {
  const wrapped = await fixture(t); await change(wrapped, `${pkg}/README.md`, 'synchronously borrows', 'synchronously\nborrows'); await change(wrapped, `${pkg}/README.md`, 'cleared, reused, or disposed', 'cleared, reused, or\ndisposed'); assert.equal(await errors(wrapped), '');
  for (const addition of ['\n## [Unreleased]\n', '\n## [0.1.0] - 2026-09-02\n', '\n## [0.1.0]\n']) { const root = await fixture(t); await put(root, `${pkg}/CHANGELOG.md`, changelog + addition); assert.match(await errors(root), /CHANGELOG must contain one undated/); }
});
test('rejects changed MIT and retained-license bytes', async t => {
  const mitRoot = await fixture(t); await change(mitRoot, 'LICENSE', 'Diego Zhou', 'Other'); assert.match(await errors(mitRoot), /MIT license mismatch/);
  const mitPackage = await fixture(t); await change(mitPackage, `${pkg}/LICENSE`, 'Diego Zhou', 'Other'); assert.match(await errors(mitPackage), /MIT license mismatch/);
  const retainedRoot = await fixture(t); await change(retainedRoot, `${pkg}/ThirdParty/Kcp2k/LICENSE.txt`, 'limpo1989', 'Other'); assert.match(await errors(retainedRoot), /Retained license mismatch: Kcp2k/);
});
test('rejects notice drift, missing licenses, and undisclosed paths', async t => {
  const drift = await fixture(t); await change(drift, 'THIRD_PARTY_NOTICES.md', native, '1'.repeat(40)); assert.match(await errors(drift), /Notice common-field mismatch: NativeWebSocket/);
  const version = await fixture(t); await change(version, 'THIRD_PARTY_NOTICES.md', 'Version: 2.5.11', 'Version: 2.5.10'); assert.match(await errors(version), /Notice common-field mismatch: UniTask/);
  const missing = await fixture(t); await rm(path.join(missing, `${pkg}/ThirdParty/Kcp2k/LICENSE.txt`)); assert.match(await errors(missing), /Retained license missing: Kcp2k/);
  const undisclosed = await fixture(t); await put(undisclosed, `${pkg}/ThirdParty/Other/source.cs`, 'x\n'); assert.match(await errors(undisclosed), /Undisclosed package third-party path: Other/);
  const rootOnly = await fixture(t); await change(rootOnly, 'THIRD_PARTY_NOTICES.md', 'actions/cache@v4', 'actions/cache'); assert.match(await errors(rootOnly), /Root notice missing repository-only dependency/);
});
test('rejects wrong UniTask revision and excess governance email', async t => {
  const sha = await fixture(t); await change(sha, `${pkg}/THIRD_PARTY_NOTICES.md`, unitask, '2'.repeat(40)); assert.match(await errors(sha), /UniTask revision mismatch/);
  const email = await fixture(t); await put(email, 'CONTRIBUTING.md', contributing + contact + '\n'); assert.match(await errors(email), /Governance email exposure/);
});
test('rejects missing or reinterpreted NativeWebSocket indicators and deferrals', async t => {
  for (const [file, from] of [['Runtime/Upstream/integrations/Unity/WebSocket.jspre', 'MIT'], ['Runtime/Upstream/integrations/Unity/package.json', 'MIT'], ['Runtime/Upstream/src/NativeWebSocket/NativeWebSocket.csproj', 'MIT']]) { const root = await fixture(t); await change(root, `${pkg}/ThirdParty/NativeWebSocket/${file}`, from, 'Other'); assert.match(await errors(root), /NativeWebSocket MIT indicator missing/); }
  const metadata = await fixture(t); await change(metadata, `${pkg}/THIRD_PARTY_NOTICES.md`, 'MIT metadata conflicts:', 'MIT metadata:'); assert.match(await errors(metadata), /Package notice fact mismatch: NativeWebSocket.MIT metadata conflicts/);
  const relicensed = await fixture(t); await change(relicensed, `${pkg}/THIRD_PARTY_NOTICES.md`, 'FrameLoom relicensing: none', 'FrameLoom relicensing: MIT'); assert.match(await errors(relicensed), /NativeWebSocket relicensing mismatch/);
  const todo = await fixture(t); await put(todo, 'docs/dependencies/README.md', 'TODO(formal-project-review)\n'); assert.match(await errors(todo), /Unresolved licensing-review deferral/);
});
test('rejects policy promises and noncanonical conduct', async t => {
  const sla = await fixture(t); await put(sla, 'SECURITY.md', security + 'Response within 24 hours.\n'); assert.match(await errors(sla), /Security policy overpromises/);
  const contribution = await fixture(t); await change(contribution, 'CONTRIBUTING.md', 'retain copyright', 'waive copyright'); assert.match(await errors(contribution), /CONTRIBUTING missing required policy: retain copyright/);
  const pullRequest = await fixture(t); await change(pullRequest, '.github/pull_request_template.md', 'right to submit', 'permission to submit'); assert.match(await errors(pullRequest), /Pull request template mismatch: right to submit/);
  const contactRoot = await fixture(t); await change(contactRoot, 'CODE_OF_CONDUCT.md', contact, '[INSERT CONTACT METHOD]'); assert.match(await errors(contactRoot), /Code of Conduct mismatch/);
  const rewritten = await fixture(t); await change(rewritten, 'CODE_OF_CONDUCT.md', 'harassment-free', 'friendly'); assert.match(await errors(rewritten), /Code of Conduct mismatch/);
});
