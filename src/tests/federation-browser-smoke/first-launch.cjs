// The desktop app's first launch on a machine where an old install of the removed thin client
// had paired: it brings the thin client's pairings over by itself, before anyone opens the
// devices page.
//
// A second desktop fixture, started only now — after legacy-client.cjs left the thin client's
// pairing with the source server on disk — on a data directory of its own and the thin client's
// data directory to read from, exactly as a fresh install finds an old one. The long-lived
// unified fixture cannot show this: it started before there was anything to import.
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const { assertNoAnalytics } = require('./network.cjs');
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

module.exports = async function firstLaunchImport({ config }) {
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
    return { importedAtFirstLaunch: true, withKeyAndMappings: true, recorded: true };
  } finally {
    await stop();
  }
};
