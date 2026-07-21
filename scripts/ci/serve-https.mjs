#!/usr/bin/env node
import { createServer } from 'node:https';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';

const args = Object.fromEntries(process.argv.slice(2).reduce((pairs, value, index, all) => {
  if (index % 2 === 0) pairs.push([value.slice(2), all[index + 1]]); return pairs;
}, []));
const root = path.resolve(args.root);
const mime = new Map([['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript; charset=utf-8'],
  ['.wasm', 'application/wasm'], ['.data', 'application/octet-stream']]);
const server = createServer({ key: await readFile(args.key), cert: await readFile(args.cert) }, async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, `https://${request.headers.host}`).pathname);
    const file = path.resolve(root, pathname === '/' ? 'index.html' : pathname.replace(/^\/+/, ''));
    if (!file.startsWith(root + path.sep)) return response.writeHead(403).end('forbidden');
    if (!(await stat(file)).isFile()) throw new Error('not file');
    const bytes = await readFile(file);
    const compression = /\.(br|gz)$/i.exec(file)?.[1];
    const logical = compression ? file.slice(0, -(compression.length + 1)) : file;
    const headers = { 'content-type': mime.get(path.extname(logical)) || 'application/octet-stream',
      'content-length': bytes.length, 'cache-control': 'no-store',
      'cross-origin-opener-policy': 'same-origin', 'cross-origin-embedder-policy': 'require-corp' };
    if (compression) headers['content-encoding'] = compression === 'br' ? 'br' : 'gzip';
    response.writeHead(200, headers).end(bytes);
  } catch { response.writeHead(404).end('not found'); }
});
server.listen(Number(args.port || 8443), '127.0.0.1', () =>
  console.log(`[serve-https] https://localhost:${args.port || 8443} root=${root}`));
