// A fresh desktop install's first launch.
//
// A second desktop fixture, started only now on a data directory of its own, exactly as a
// fresh install starts: the long-lived unified fixture cannot show this, since it has been
// running since before the smoke began and has managed servers of its own by now.
//
// It is the one fresh install started here through the app's own host start-up, so it is where
// the startup notices' fresh-install rule is checked for real: the first start opens the notice
// baseline, and its window records every upgrade-only notice it ships with as read, and shows
// none of them.
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const { assertNoAnalytics, assertStayedLocal, confine } = require('./network.cjs');

const field = (object, name) => object?.[name] ?? object?.[name[0].toLowerCase() + name.slice(1)];

const findFile = (directory, suffix) => {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    const found = entry.isDirectory() ? findFile(full, suffix) : full.endsWith(suffix) ? full : null;
    if (found) return found;
  }
  return null;
};

/** Starts the fixture described by run.py and resolves once it reports ready. */
async function start(spec, timeout = 90000) {
  const env = { ...process.env, ...spec.env };
  for (const key of spec.unset) delete env[key];
  const log = fs.openSync(spec.log, 'w');
  const child = spawn(spec.command[0], spec.command.slice(1), { env, stdio: ['ignore', log, log] });
  fs.closeSync(log);
  const exited = new Promise(resolve => {
    child.once('exit', code => resolve(code ?? 'signal'));
    child.once('error', error => resolve(error.code ?? 'error'));
  });
  const stop = async () => {
    if (child.exitCode !== null || child.signalCode !== null) return;
    child.kill();
    const late = setTimeout(() => child.kill('SIGKILL'), 8000);
    await exited;
    clearTimeout(late);
  };
  const deadline = Date.now() + timeout;
  try {
    while (!fs.existsSync(path.join(spec.directory, 'ready'))) {
      const code = await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 200))]);
      if (code !== undefined) assert.fail(`The first-launch fixture exited during startup (${code}); see first-launch-host.log`);
      if (Date.now() > deadline) assert.fail('The first-launch fixture did not report ready; see first-launch-host.log');
    }
  } catch (error) {
    await stop();
    throw error;
  }
  return stop;
}

/** Polls a read until it satisfies `done`, or fails with the last value and parse error. */
async function until(read, done, what, timeout = 20000) {
  const deadline = Date.now() + timeout;
  let value;
  let lastParseError;
  while (Date.now() < deadline) {
    let readSucceeded = false;
    try {
      value = read();
      readSucceeded = true;
    } catch (error) {
      // JsonOptionsManager overwrites the file, so a read can observe a partial JSON write.
      // I/O errors and assertion failures still fail immediately.
      if (!(error instanceof SyntaxError)) throw error;
      lastParseError = error;
    }
    if (readSucceeded && done(value)) return value;
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  const parseFailure = lastParseError ? `; last JSON parse error: ${lastParseError}` : '';
  assert.fail(`Timed out waiting for ${what}: ${JSON.stringify(value)}${parseFailure}`);
}

/**
 * The fresh install's startup notices, end to end: the baseline its first start opened (read
 * before any page loads), then its window.
 */
async function freshInstallNotices(browser, spec) {
  const uiFile = findFile(spec.directory, path.join('configs', 'ui.json'));
  assert.ok(uiFile, 'No UI options after the first launch');
  // The options manager writes a byte order mark.
  const notices = () =>
    field(field(JSON.parse(fs.readFileSync(uiFile, 'utf8').replace(/^\uFEFF/, '')), 'UI'), 'Notices');
  // Read as the host started, before it recorded the running version over the one this
  // install last ran — none.
  const initial = await until(notices, () => true, 'the initial notice baseline JSON');
  assert.equal(field(initial, 'BaselinePending'), true, 'The first launch did not open the fresh install\'s notice baseline');

  const origins = new Set([new URL(spec.window).origin, new URL(spec.base).origin]);
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'en-US' });
  try {
    const blocked = await confine(context, url => origins.has(url.origin));
    const page = await context.newPage();
    // Not the dashboard, whose welcome would come first.
    await page.goto(spec.window + '/#/federation/devices?section=servers');
    // The window records the baseline: every upgrade-only notice it ships with, as read.
    const recorded = await until(notices, state => field(state, 'BaselinePending') === false, 'the notice baseline');
    assert.deepEqual(field(recorded, 'ReadIds'), ['multi-device'], 'The baseline did not record the upgrade-only notices');
    // ...and so greets a fresh install with none of them.
    await page.waitForLoadState('networkidle');
    assert.equal(await page.locator('[role=dialog] [data-notice-id]').count(), 0,
      'A fresh install was shown an upgrade-only notice');
    await assertStayedLocal(context, blocked, 'First launch');
  } finally {
    await context.close();
  }
}

module.exports = async function firstLaunch({ browser, config }) {
  const spec = config.firstLaunch;

  const stop = await start(spec);
  try {
    const response = await fetch(spec.base + '/federation/local/servers');
    assert.equal(response.status, 200);
    const listing = await response.json();
    // A fresh desktop install manages nothing, and has nothing it could manage from before.
    assert.deepEqual(listing, { available: true, servers: [], requests: [] },
      'The first-launch fixture did not start as a fresh desktop install');
    assert.equal(findFile(spec.directory, path.join('remote-access', 'managed', 'connection.json')), null,
      'A fresh install wrote a managed-server store before managing anything');
    await assertNoAnalytics(spec.base);
    await freshInstallNotices(browser, spec);
    return {
      freshDesktopInstall: true, managesNothing: true,
      noticeBaselineOpenedAtFirstStart: true, noUpgradeOnlyNoticeShown: true
    };
  } finally {
    await stop();
  }
};

// Offline regression tests exercise the same poll used by the browser smoke.
module.exports.until = until;
