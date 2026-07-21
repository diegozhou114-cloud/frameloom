#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export async function validateSummary(summaryPath, artifactRoot, expectedTarget) {
  const errors = [];
  const document = JSON.parse(await readFile(summaryPath, 'utf8'));
  if (document.schemaVersion !== 1) errors.push('schemaVersion must equal 1');
  if (document.editorVersion !== '2022.3.62f3c1') errors.push(`wrong editor ${document.editorVersion}`);
  if (document.target !== expectedTarget) errors.push(`wrong target ${document.target}`);
  if (document.result !== 'Succeeded') errors.push(`build result ${document.result}`);
  if (document.errors !== 0) errors.push(`build errors ${document.errors}`);
  if (document.enabledScenes?.[0] !== 'Assets/Scence/Launcher.unity')
    errors.push('Launcher is not enabled scene zero');

  const root = path.resolve(artifactRoot);
  const paths = [];
  for (const artifact of document.artifacts || []) {
    const absolute = path.resolve(root, artifact.path);
    if (!absolute.startsWith(root + path.sep)) {
      errors.push(`artifact escaped artifact root: ${artifact.path}`);
      continue;
    }
    let bytes;
    try { bytes = await readFile(absolute); }
    catch { errors.push(`artifact is unreadable: ${artifact.path}`); continue; }
    const digest = createHash('sha256').update(bytes).digest('hex');
    if (digest !== artifact.sha256) errors.push(`artifact hash mismatch: ${artifact.path}`);
    if (bytes.length !== artifact.bytes) errors.push(`artifact size mismatch: ${artifact.path}`);
    paths.push(artifact.path);
  }

  if (expectedTarget === 'WebGL') {
    if (!paths.some(value => /\.html$/i.test(value))) errors.push('WebGL HTML missing');
    if (!paths.some(value => /\.wasm(?:\.(?:br|gz))?$/i.test(value))) errors.push('WebGL WASM missing');
    if (!paths.some(value => /\.data(?:\.(?:br|gz))?$/i.test(value))) errors.push('WebGL data missing');
  } else if (expectedTarget === 'StandaloneWindows64') {
    if (!paths.some(value => /\.exe$/i.test(value))) errors.push('Windows executable missing');
    if (!paths.some(value => /_Data\//.test(value))) errors.push('Windows Data directory missing');
  }
  return errors;
}

async function main() {
  const values = Object.fromEntries(process.argv.slice(2).reduce((pairs, value, index, all) => {
    if (index % 2 === 0) pairs.push([value.slice(2), all[index + 1]]);
    return pairs;
  }, []));
  const errors = await validateSummary(values.summary, values['artifact-root'], values.target);
  if (errors.length) { console.error(errors.join('\n')); process.exitCode = 1; }
  else console.log(`[CI_BuildJobs_InvokeRepositoryBuildScriptAndSmokeArtifacts] PASS target=${values.target}`);
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error.stack); process.exitCode = 1; });
