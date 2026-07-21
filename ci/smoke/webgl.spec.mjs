import { expect, test } from '@playwright/test';
import { readFile } from 'node:fs/promises';
const allowlist = JSON.parse(await readFile(new URL('./console-allowlist.json', import.meta.url), 'utf8'))
  .patterns.map(pattern => new RegExp(pattern));
const deferred = () => { let resolve; return { promise: new Promise(value => { resolve = value; }), resolve }; };
test('WebGL HTTPS app-ready and real button click', async ({ page }) => {
  const ready = deferred(), clicked = deferred(), failures = [];
  page.on('console', message => { const text = message.text(), index = text.indexOf('[ZWEB_SMOKE] ');
    if (index >= 0) { const marker = JSON.parse(text.slice(index + 13)); if (marker.event === 'app-ready') ready.resolve(marker); if (marker.event === 'button-clicked') clicked.resolve(marker); }
    if ((message.type() === 'error' || /mixed content/i.test(text)) && !allowlist.some(pattern => pattern.test(text))) failures.push(text); });
  page.on('pageerror', error => failures.push(error.stack || error.message));
  page.on('request', request => { if (request.url().startsWith('http://')) failures.push(`mixed content request: ${request.url()}`); });
  await page.goto('/?zweb-smoke=1', { waitUntil: 'domcontentloaded' });
  const marker = await Promise.race([ready.promise, new Promise((_, reject) => setTimeout(() => reject(new Error('app-ready timeout')), 60_000))]);
  const canvas = page.locator('canvas').first(); await expect(canvas).toBeVisible(); const box = await canvas.boundingBox(); expect(box).not.toBeNull();
  await page.mouse.click(box.x + marker.buttonX / marker.screenWidth * box.width, box.y + (1 - marker.buttonY / marker.screenHeight) * box.height);
  await Promise.race([clicked.promise, new Promise((_, reject) => setTimeout(() => reject(new Error('button-clicked timeout')), 10_000))]);
  expect(failures).toEqual([]); expect(page.url()).toMatch(/^https:\/\/localhost:8443\//);
});
