// What an old install of the removed thin client (Bakabase.Client) leaves on a machine: its
// pairing with the source server, in its own AppData, in its own file format. The desktop app
// imports it — on its first launch (first-launch.cjs) and on request (switching.cjs).
//
// The thin client itself no longer exists, so nothing here runs it. The pairing is still real:
// the source issues the device and its key through its own pairing API, exactly as it did for
// the thin client (a code from the host, exchanged by the device), and lists that device from
// then on. Only the file is written here, the way the thin client's ClientConnectionStore wrote
// it — property names as they are in C#, enums by name, owner-only on Unix.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

/** Where the thin client kept its pairings under its AppData directory. */
const connectionFile = directory => path.join(directory, 'client', 'connection.json');

/** The thin client's own path mapping for the source, which the import must bring along. */
const LEGACY_MAPPING = { ServerPath: '/legacy/media', LocalPath: '/legacy/local-media' };

/** `RemoteDevicePlatform` by name, as the thin client stored and reported it. */
const platform = { win32: 'Windows', darwin: 'MacOS', linux: 'Linux' }[process.platform] ?? 'Unknown';

async function envelope(url, init) {
  const response = await fetch(url, init);
  assert.ok(response.ok, `${init?.method ?? 'GET'} ${url}: HTTP ${response.status}`);
  const body = await response.json();
  assert.equal(body.code, 0, `${url} answered code ${body.code}`);
  return body.data;
}

module.exports = async function legacyClientPairing({ config }) {
  const { source } = config.hosts;
  const file = connectionFile(config.legacyClient.directory);
  assert.ok(!fs.existsSync(file), 'An old thin client\'s pairing is already on disk');

  const info = await envelope(source.base + '/remote-access/server-info');
  // Host-only: a code is bearer access, so only the source's own machine can mint one.
  const { code } = await envelope(source.base + '/remote-access/pairing/code', { method: 'POST' });
  const deviceName = 'fixture-thin-client';
  const result = await envelope(source.base + '/remote-access/pair/code', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code, deviceName, platform }),
  });
  const credentials = result?.credentials;
  assert.ok(credentials?.deviceId && credentials?.key?.length > 10,
    `The source did not pair the device (failure ${result?.failure})`);

  // The source now knows the device, by the id it issued.
  const settings = await envelope(source.base + '/remote-access/settings');
  assert.ok(settings.devices.some(device => device.id === credentials.deviceId),
    'The source does not list the device it just paired');

  const now = new Date().toISOString();
  const data = {
    Servers: [{
      ServerId: info.id,
      ServerName: info.name,
      BaseAddress: source.base,
      DeviceId: credentials.deviceId,
      DeviceKey: credentials.key,
      PairedAt: now,
      LastConnectedAt: now,
      PathMappings: [LEGACY_MAPPING],
    }],
    ActiveServerId: info.id,
    DeviceName: deviceName,
    Platform: platform,
  };
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(data, null, 2), { mode: 0o600 });

  return { pairedThroughTheSourcesOwnApi: true, thinClientFileFormat: true };
};

module.exports.connectionFile = connectionFile;
