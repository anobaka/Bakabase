// The desktop app's first launch on a machine where an old install of the removed thin client
// had paired: it brings the thin client's pairings over by itself, before anyone opens the
// devices page.
//
// A second desktop fixture, started only now — after legacy-client.cjs left the thin client's
// pairing with the source server on disk — on a data directory of its own and the thin client's
// data directory to read from, exactly as a fresh install finds an old one. The long-lived
// unified fixture cannot show this: it started before there was anything to import.
//
// It is also the one fresh install started here through the app's own host start-up, so it is
// where the startup notices' fresh-install rule is checked for real: the first start opens the
// notice baseline, and its window, having brought the thin client's pairings over, is shown the
// thin client's notice and no other upgrade-only one.
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const { assertNoAnalytics, assertStayedLocal, confine } = require('./network.cjs');
const { connectionFile } = require('./legacy-client.cjs');

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

/** Polls a read until it satisfies `done`, or fails with the last value. */
async function until(read, done, what, timeout = 20000) {
  const deadline = Date.now() + timeout;
  let value;
  while (Date.now() < deadline) {
    value = read();
    if (done(value)) return value;
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  assert.fail(`Timed out waiting for ${what}: ${JSON.stringify(value)}`);
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
  assert.equal(field(notices(), 'BaselinePending'), true, 'The first launch did not open the fresh install\'s notice baseline');

  const origins = new Set([new URL(spec.window).origin, new URL(spec.base).origin]);
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'en-US' });
  try {
    const blocked = await confine(context, url => origins.has(url.origin));
    const page = await context.newPage();
    // Not the dashboard, whose welcome would come first.
    await page.goto(spec.window + '/#/federation/devices?section=servers');
    await page.locator('[role=dialog] [data-notice-id="thin-client-discontinued"]').waitFor();
    // The window recorded the baseline: every upgrade-only notice but the thin client's, which
    // this install is for after all — it brought the thin client's pairings over.
    const recorded = await until(notices, state => field(state, 'BaselinePending') === false, 'the notice baseline');
    assert.deepEqual(field(recorded, 'ReadIds'), ['multi-device'], 'The baseline did not leave the thin client\'s notice out');
    assert.equal(await page.locator('[data-notice-id]').count(), 1);
    await assertStayedLocal(context, blocked, 'First launch');
  } finally {
    await context.close();
  }
}

module.exports = async function firstLaunchImport({ browser, config }) {
  const spec = config.firstLaunch;
  const { source } = config.hosts;
  const legacyFile = connectionFile(config.legacyClient.directory);
  const legacyBytes = fs.readFileSync(legacyFile);
  const legacy = JSON.parse(legacyBytes.toString());
  const legacyServers = field(legacy, 'Servers');
  assert.equal(legacyServers.length, 1, 'The thin client should have paired with the source server alone');
  const [legacyServer] = legacyServers;
  const legacyKey = field(legacyServer, 'DeviceKey');
  assert.ok(legacyKey && legacyKey.length > 10);

  const stop = await start(spec);
  try {
    // Ready waits for the manager's startup work, so this is what the first launch did.
    const response = await fetch(spec.base + '/federation/local/servers');
    assert.equal(response.status, 200);
    const listing = await response.json();
    assert.equal(listing.available, true, 'The first-launch fixture did not compose the relay manager');
    assert.deepEqual(listing.servers.map(server => [server.serverId, server.address, server.importedFromLegacyClient]),
      [[field(legacyServer, 'ServerId'), source.base, true]], 'The first launch did not bring the thin client\'s pairing over');
    assert.deepEqual(listing.servers[0].pathMappings,
      field(legacyServer, 'PathMappings').map(mapping => ({ serverPath: field(mapping, 'ServerPath'), localPath: field(mapping, 'LocalPath') })));
    assert.ok(!JSON.stringify(listing).includes(legacyKey), 'A device key reached the listing');

    const storeFile = findFile(spec.directory, path.join('remote-access', 'managed', 'connection.json'));
    assert.ok(storeFile, 'No managed-server store after the first launch');
    const store = JSON.parse(fs.readFileSync(storeFile, 'utf8'));
    const [stored] = field(store, 'Servers');
    assert.deepEqual([field(stored, 'DeviceId'), field(stored, 'DeviceKey')],
      [field(legacyServer, 'DeviceId'), legacyKey], 'The first launch did not keep the thin client\'s device');
    // Recorded, so later launches leave the thin client alone; the devices page can still import.
    assert.ok(field(store, 'LegacyClientImportedAt'), 'The first launch did not record its import');
    if (process.platform !== 'win32') assert.equal(fs.statSync(storeFile).mode & 0o777, 0o600);
    assert.ok(fs.readFileSync(legacyFile).equals(legacyBytes), 'The first launch changed the thin client\'s file');
    await assertNoAnalytics(spec.base);
    await freshInstallNotices(browser, spec);
    return {
      importedAtFirstLaunch: true, withKeyAndMappings: true, recorded: true,
      noticeBaselineOpenedAtFirstStart: true, thinClientNoticeShownAfterImport: true
    };
  } finally {
    await stop();
  }
};
