// Production UI + real video decoder; the paired hosts and fault relay are owned by media-stream.py.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const config = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const locales = ['en', 'cn'].map(lang => require(`../../web/src/locales/${lang}/pages/federation.json`));
const escape = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const name = key => new RegExp(`^(?:${locales.map(locale => escape(locale[key])).join('|')})$`);

(async () => {
  const browser = await chromium.launch({ headless: true });
  const report = { passed: false, pageErrors: [] };
  try {
    const context = await browser.newContext({ viewport: { width: 1400, height: 1000 } });
    await context.route('**/*', route => new URL(route.request().url()).origin === config.base ? route.continue() : route.abort());
    const page = await context.newPage();
    page.on('pageerror', error => report.pageErrors.push(error.message));
    const { nodeId, libraryEpoch, resourceId } = config.ref;
    await page.goto(`${config.base}/#/federation?node=${nodeId}&epoch=${libraryEpoch}&resource=${resourceId}`);
    const pane = page.getByRole('region', { name: name('federation.detail.title') });
    const started = Date.now();
    await pane.getByRole('button', { name: name('federation.preview'), exact: true }).first().click();
    await page.waitForFunction(() => document.querySelector('video')?.readyState >= 2, undefined, { timeout: 15000 });
    await page.locator('video').evaluate(video => { video.muted = true; return video.play(); });
    report.firstPresentedFrame = await page.locator('video').evaluate(video => new Promise((resolve, reject) => {
      let callback;
      const timer = setTimeout(() => { video.cancelVideoFrameCallback(callback); reject(new Error('No presented video frame')); }, 15000);
      callback = video.requestVideoFrameCallback((_, metadata) => {
        clearTimeout(timer);
        resolve({ mediaTime: metadata.mediaTime, presentedFrames: metadata.presentedFrames });
      });
    }));
    report.firstDecodedFrameMs = Date.now() - started;
    assert.ok(report.firstDecodedFrameMs < 15000);
    const initial = await page.locator('video').evaluate(video => ({ duration: video.duration, width: video.videoWidth, height: video.videoHeight, time: video.currentTime }));
    assert.ok(initial.duration >= 100 && initial.width > 0 && initial.height > 0);
    report.video = initial;
    await page.locator('video').evaluate(video => video.pause());
    const paused = await page.locator('video').evaluate(video => video.currentTime);
    await page.waitForTimeout(2200); // Deliberate clock-stability assertion, not UI synchronization.
    assert.ok(Math.abs(await page.locator('video').evaluate(video => video.currentTime) - paused) < 0.1);
    report.pauseClockStable = true;
    await page.locator('video').evaluate(video => video.play());
    await page.waitForFunction(time => document.querySelector('video').currentTime > time + 1, paused);
    report.seekStartedMs = Date.now();
    report.seekPresentedFrame = await page.locator('video').evaluate(video => new Promise((resolve, reject) => {
      let callback;
      const timer = setTimeout(() => { video.cancelVideoFrameCallback(callback); reject(new Error('Seek never presented a target-time frame')); }, 20000);
      const presented = (_, metadata) => {
        if (metadata.mediaTime >= 90 && metadata.mediaTime < 92) {
          clearTimeout(timer);
          resolve({ mediaTime: metadata.mediaTime, presentedFrames: metadata.presentedFrames });
        } else callback = video.requestVideoFrameCallback(presented);
      };
      callback = video.requestVideoFrameCallback(presented);
      video.currentTime = 90;
    }));
    await page.waitForFunction(() => {
      const video = document.querySelector('video');
      return video && !video.seeking && video.currentTime > 90.5 && video.readyState >= 2;
    }, undefined, { timeout: 20000 });
    report.seekSeconds = await page.locator('video').evaluate(video => video.currentTime);
    await pane.screenshot({ path: path.join(config.results, 'decoded-remote-video.png') });
    assert.deepEqual(report.pageErrors, []);
    report.passed = true;
  } catch (error) {
    report.error = error.message;
    throw error;
  } finally {
    await browser.close();
    fs.writeFileSync(path.join(config.results, 'browser-result.json'), JSON.stringify(report, null, 2));
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
