// Captures the real GitHub Releases page (latest release with its downloadable
// .zip assets expanded and outlined) into reference_images/github-download-release.png.
// Usage: node capture-releases.mjs [outPath]
import { chromium } from '@playwright/test';

const out = process.argv[2] ?? '../../reference_images/github-download-release.png';
const url = 'https://github.com/vampireLibrarianMonk/secure-drive/releases/latest';

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 940 } });
await page.goto(url, { waitUntil: 'networkidle', timeout: 60_000 });
await page.waitForTimeout(1500);

// Expand the Assets section of the latest release (the .zip downloads).
const assets = page.locator('details').filter({ hasText: 'Assets' }).first();
if ((await assets.count()) > 0 && !(await assets.getAttribute('open'))) {
  await assets.locator('summary').click();
  await page.waitForTimeout(1500);
}

// Outline the assets box so the downloadable zips stand out, as the guides
// describe. This is an annotation on a genuine capture of the real page.
if ((await assets.count()) > 0) {
  await assets.evaluate((el) => {
    el.style.outline = '3px solid #0969da';
    el.style.outlineOffset = '6px';
    el.style.borderRadius = '6px';
  });
  await assets.scrollIntoViewIfNeeded();
  await page.evaluate(() => window.scrollBy(0, -90));
}

await page.waitForTimeout(700);
await page.screenshot({ path: out });
await browser.close();
console.log(`saved ${out}`);
