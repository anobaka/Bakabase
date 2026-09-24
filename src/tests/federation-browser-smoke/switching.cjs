// Server switching: the all-in-one's window managing another server through a signed loopback
// relay. Real hosts, the production frontend and Chromium; no mocked API responses.
//
// "unified" (A) is composed like the desktop app (UnifiedHost: its server plus the relay
// manager), with this browser as its window. "source" (B) is the managed server; every request
// it receives is recorded before its own middleware, so the relay's traffic can be judged on B's
// side. Runs once legacy-client.cjs has left an old thin client's pairing with B on this machine.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { confine, assertStayedLocal } = require('./network.cjs');
const { connectionFile } = require('./legacy-client.cjs');

/** Where RemoteConsoleOptions puts relay ports by default; nothing else on loopback is allowed. */
const RELAY_FIRST_PORT = 34650;
const RELAY_PORT_RANGE = 256;
const TICKET = '__bakabase_switch';
const SENTINEL = 'server-switching-sentinel';

const escape = text => String(text).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** SignalR's JSON-protocol handshake, and the call B's own UI makes for everything it shows. */
const HUB_HANDSHAKE = '{"protocol":"json","version":1}\x1e';
const HUB_INITIAL_DATA = '{"type":1,"invocationId":"0","target":"GetInitialData","arguments":[]}\x1e';

/**
 * Every WebSocket `page` makes, as Playwright reports it: its address, why it failed (the
 * status the handshake was answered with, e.g. "Forbidden: 403"), and which of `markers` the
 * frames it received matched, each with the order it arrived in — never the frames themselves,
 * which carry the server's settings.
 */
let frameSequence = 0;
const watchSockets = (page, markers = {}) => {
  const sockets = [];
  page.on('websocket', socket => {
    const record = { url: socket.url(), errors: [], frames: 0, matched: [], closed: false };
    sockets.push(record);
    socket.on('socketerror', error => record.errors.push(String(error)));
    socket.on('close', () => { record.closed = true; });
    socket.on('framereceived', ({ payload }) => {
      const at = ++frameSequence;
      record.frames++;
      for (const [name, matches] of Object.entries(markers))
        if (matches(String(payload))) record.matched.push({ name, at });
    });
  });
  return sockets;
};

module.exports = async function serverSwitching({ browser, config, artifacts }) {
  const { unified, source } = config.hosts;
  // Where this browser shows A: the origin A recorded as its main window's, and so "home".
  const home = unified.window;
  const locales = ['en', 'cn'].map(lang => Object.assign({}, ...['pages/federation', 'pages/configuration', 'components/helpCenter']
    .map(file => JSON.parse(fs.readFileSync(path.join(config.repo, `src/web/src/locales/${lang}/${file}.json`), 'utf8')))));
  // A translated string as a pattern, in either UI language; {{placeholders}} take the given
  // values, or anything when not given.
  const pattern = (key, values = {}) => locales.map(locale => {
    assert.ok(locale[key], `Missing locale key ${key}`);
    return locale[key].split(/(\{\{\w+\}\})/).map(part => {
      const slot = /^\{\{(\w+)\}\}$/.exec(part);
      if (!slot) return escape(part);
      return slot[1] in values ? escape(values[slot[1]]) : '.*?';
    }).join('');
  }).join('|');
  const exactly = (key, values) => new RegExp(`^(?:${pattern(key, values)})$`);
  const containing = (key, values) => new RegExp(pattern(key, values));

  const relayPort = url => {
    const parsed = new URL(url);
    const port = Number(parsed.port);
    return parsed.protocol === 'http:' && parsed.hostname === '127.0.0.1' &&
      port >= RELAY_FIRST_PORT && port < RELAY_FIRST_PORT + RELAY_PORT_RANGE ? port : null;
  };
  const hostOrigins = new Set(Object.values(config.hosts)
    .flatMap(host => [host.base, host.base.replace('127.0.0.1', 'localhost')]));

  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'en-US' });
  const report = { fixtureScope: 'Unified host composed like UnifiedHost (server + relay manager), without Avalonia' };
  const pageErrors = [];
  let blocked = { external: [], loopback: [] };
  try {
    // The two hosts and the relay port range; nothing else, loopback or not.
    blocked = await confine(context, url => hostOrigins.has(url.origin) || relayPort(url.href) !== null);
    context.on('page', page => page.on('pageerror', error => pageErrors.push(`${page.url()}: ${error.message}`)));

    const call = async (url, options = {}) => {
      const response = await context.request.fetch(url, options);
      assert.ok(response.ok(), `${options.method ?? 'GET'} ${url}: HTTP ${response.status()}`);
      return response.status() === 204 ? undefined : response.json();
    };
    const envelope = async (url, options) => {
      const body = await call(url, options);
      assert.equal(body.code, 0, `${url} answered code ${body.code}`);
      return body.data;
    };
    /** Polls a read until it satisfies `done`, or fails with the last value. No fixed sleeps. */
    const until = async (read, done, what, timeout = 20000) => {
      const deadline = Date.now() + timeout;
      let value;
      while (Date.now() < deadline) {
        value = await read();
        if (done(value)) return value;
        await new Promise(resolve => setTimeout(resolve, 200));
      }
      assert.fail(`Timed out waiting for ${what}: ${JSON.stringify(value)}`);
    };
    const managedServers = () => call(unified.base + '/federation/local/servers');
    const settingsOf = host => envelope(host.base + '/remote-access/settings');
    // What reached a host, in arrival order: {method, path, site, dest, ticket, cookie,
    // signature, device}, plus the host's own answer {status, denial, csp} once it began one.
    const requestsTo = host => {
      const entries = [];
      const byId = new Map();
      for (const line of fs.readFileSync(host.requestLog, 'utf8').split('\n').filter(Boolean)) {
        const entry = JSON.parse(line);
        if ('method' in entry) {
          entries.push(entry);
          byId.set(entry.id, entry);
        } else Object.assign(byId.get(entry.id) ?? {}, entry);
      }
      return entries;
    };
    const received = () => requestsTo(source);
    const receivedSince = mark => received().slice(mark);
    const findFile = (directory, suffix) => {
      for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const full = path.join(directory, entry.name);
        const found = entry.isDirectory() ? findFile(full, suffix) : full.endsWith(suffix) ? full : null;
        if (found) return found;
      }
      return null;
    };
    const field = (object, name) => object[name] ?? object[name[0].toLowerCase() + name.slice(1)];

    const bInfo = await envelope(source.base + '/remote-access/server-info');
    const aInfo = await envelope(unified.base + '/remote-access/server-info');
    assert.notEqual(bInfo.id, aInfo.id);
    // What each says it is (optional fields, answered here): A is composed as the desktop app,
    // B as a headless server — ServerKind 1 and 2 — and both run on this machine's OS.
    assert.equal(aInfo.kind, 1, 'A does not say it is the desktop app');
    assert.equal(bInfo.kind, 2, 'B does not say it is a headless server');
    assert.ok(bInfo.platform >= 1 && bInfo.platform === aInfo.platform, 'The fixtures do not say what they run on');
    // The UI names servers, never ids: a check by name proves B only if A is called something else.
    assert.notEqual(bInfo.name, aInfo.name, 'The fixtures share a server name, so no check by name can tell them apart');
    const legacyFile = connectionFile(config.legacyClient.directory);
    const legacyBytes = fs.readFileSync(legacyFile);
    const legacyServer = field(JSON.parse(legacyBytes.toString()), 'Servers').find(server => field(server, 'ServerId') === bInfo.id);
    assert.ok(legacyServer, 'The old thin client\'s pairing is not with the source server');
    const legacyDevice = field(legacyServer, 'DeviceId');
    const legacyKey = field(legacyServer, 'DeviceKey');
    assert.ok(legacyDevice && legacyKey && legacyKey.length > 10);
    assert.ok((await settingsOf(source)).devices.some(device => device.id === legacyDevice));

    // (a) Import the thin client's pairing with B through A's devices page: no re-pairing.
    const initial = await managedServers();
    assert.equal(initial.available, true, 'The unified fixture did not compose the relay manager');
    assert.deepEqual(initial.servers, [], 'Nothing was imported at startup: the old pairing was written afterwards');
    const win = await context.newPage();
    // The window's WebSockets — A's own UI's hub, then B's through the relay, then the probes.
    const winSockets = watchSockets(win, {
      // B telling every UI of its own, over its hub, that live transcoding is now allowed.
      liveTranscodeOn: payload => payload.includes('"OptionsChanged"') && payload.includes('"remoteAccessOptions"') &&
        /"allowLiveTranscode"\s*:\s*true/.test(payload)
    });
    await win.goto(home + '/#/federation/devices?section=servers');
    const servers = win.locator('#managed-servers');
    await servers.getByRole('heading', { name: exactly('federation.servers.title') }).waitFor();
    await servers.getByRole('button', { name: exactly('federation.servers.import.action') }).click();
    await servers.getByRole('status').filter({ hasText: containing('federation.servers.import.done', { imported: 1, skipped: 0 }) }).waitFor();
    const card = servers.getByTestId('managed-server');
    await card.getByText(exactly('federation.servers.imported')).waitFor();
    assert.equal(await card.count(), 1);
    await servers.getByRole('button', { name: exactly('federation.servers.refresh') }).click();
    await card.getByText(exactly('federation.servers.state.1')).waitFor();
    const imported = await managedServers();
    assert.equal(imported.servers.length, 1);
    const importedServer = imported.servers[0];
    assert.equal(importedServer.serverId, bInfo.id);
    assert.equal(importedServer.address, source.base);
    assert.equal(importedServer.importedFromLegacyClient, true);
    assert.deepEqual(importedServer.pathMappings, [{ serverPath: '/legacy/media', localPath: '/legacy/local-media' }],
      'The thin client\'s path mappings come with its pairing');
    assert.ok(!JSON.stringify(imported).includes(legacyKey), 'A device key reached the listing');
    const storeFile = findFile(unified.directory, path.join('remote-access', 'managed', 'connection.json'));
    assert.ok(storeFile, 'No managed-server store in the unified fixture');
    const readStore = () => field(JSON.parse(fs.readFileSync(storeFile, 'utf8')), 'Servers');
    const storedImport = readStore().find(server => field(server, 'ServerId') === bInfo.id);
    assert.equal(field(storedImport, 'DeviceId'), legacyDevice);
    assert.equal(field(storedImport, 'DeviceKey'), legacyKey, 'The key was not imported with the pairing');
    if (process.platform !== 'win32') assert.equal(fs.statSync(storeFile).mode & 0o777, 0o600);
    assert.ok(fs.readFileSync(legacyFile).equals(legacyBytes), 'Importing changed the thin client\'s file');
    await servers.screenshot({ path: artifacts('switching-a-manages-b.png') });
    report.legacyPairingImportedWithKeyAndMappings = true;

    // This device's own window keeps its hub. A's UI negotiates and upgrades as the page on
    // A's window origin, which is the origin its handshake names: A answers 101, as the only
    // rule a browser's handshake can be judged by — its Origin — must let it.
    const ownHub = await until(() => requestsTo(unified).filter(entry => entry.path.startsWith('/hub/ui') && entry.origin === home),
      list => list.some(entry => entry.websocket && entry.status === 101) &&
        list.some(entry => entry.path === '/hub/ui/negotiate' && entry.status === 200),
      'A\'s own window to negotiate and open its hub');
    assert.deepEqual(ownHub.filter(entry => entry.status === 403), [], 'A refused its own window\'s hub');
    report.ownWindowHub = { negotiated: true, webSocketUpgraded: true, origin: home };

    // (b) The switcher at the top of the menu sends this window to B's relay.
    const switcher = win.getByTestId('server-switcher');
    const trigger = switcher.locator('button[aria-haspopup="menu"]');
    const tickets = [];
    win.on('request', request => {
      if (request.isNavigationRequest() && request.url().includes(TICKET)) tickets.push(request.url());
    });
    // Every document the window itself was answered with, in order — how a switch went, step
    // by step. Frames inside a page are not the window's.
    const documents = [];
    win.on('response', response => {
      if (response.request().isNavigationRequest() && response.request().frame() === win.mainFrame()) documents.push(response);
    });
    // A ticket is spent once used, but a report has no business repeating one.
    const shown = url => url.replace(new RegExp(`${TICKET}=[^&#]*`), `${TICKET}=…`);
    /** Fails, naming what the relay said, unless it served the document. */
    const served = async (response, what) => {
      const refusal = (await response.allHeaders())['x-bakabase-client'];
      assert.ok(response.status() === 200 && !refusal,
        `The relay refused ${what}: HTTP ${response.status()}${refusal ? ` (${refusal})` : ''} for ${shown(response.url())}`);
    };
    const pick = async local => {
      await trigger.click();
      const items = switcher.getByRole('menu').getByRole('menuitem')
        .filter({ hasNotText: containing('federation.switcher.manageDevices') });
      const item = local
        ? items.filter({ hasText: containing('federation.thisDevice') })
        : items.filter({ hasNotText: containing('federation.thisDevice') });
      // The list fills in once the menu has asked for it.
      await item.first().waitFor();
      assert.equal(await item.count(), 1);
      await item.click();
    };
    /**
     * Follows a switch into B's relay that `start` sets off, and waits for B's UI in console
     * mode on the relay's origin without the ticket — at `route`, when the switch asked for one.
     *
     * Step by step, so a switch that goes wrong says where: the ticketed navigation must be
     * admitted and answered with the landing page, and the landing page's own navigation,
     * without the ticket, served too — before any of B's UI is waited for. A missing or
     * refused ticket fails here, by name, not as a timeout on text that never appears.
     */
    const enterRelay = async (start, route) => {
      const before = tickets.length;
      const mark = documents.length;
      await start();
      const atRelay = () => documents.slice(mark).filter(response => relayPort(response.url()) !== null);
      const [landing] = await until(atRelay, list => list.length >= 1, 'the window to reach the relay');
      await served(landing, 'the switch');
      assert.ok(landing.url().includes(TICKET), `The relay admitted a switch without a ticket: ${landing.url()}`);
      const [, continued] = await until(atRelay, list => list.length >= 2, 'the landing page to move on');
      assert.ok(!continued.url().includes(TICKET), `The landing page kept the ticket: ${shown(continued.url())}`);
      await served(continued, 'the landing page\'s continuation');
      await win.waitForURL(url => relayPort(url.href) !== null && !url.href.includes(TICKET));
      // The browser never sends a fragment, so only the landing page can carry the route on.
      if (route) assert.equal(new URL(win.url()).hash, route, `The switch lost its route: landed on ${shown(win.url())}`);
      await switcher.getByText(exactly('federation.switcher.managing')).waitFor();
      assert.match(await trigger.getAttribute('title'), exactly('federation.switcher.managingName', { name: bInfo.name }));
      assert.equal(tickets.length, before + 1, 'Switching took more or fewer than one ticketed navigation');
      const relay = new URL(win.url()).origin;
      assert.equal(new URL(tickets.at(-1)).origin, relay);
      assert.ok(!win.url().includes(TICKET));
      const cdp = await context.newCDPSession(win);
      const { entries } = await cdp.send('Page.getNavigationHistory');
      await cdp.detach();
      assert.ok(entries.every(entry => !entry.url.includes(TICKET)), 'The ticket stayed in the window\'s history');
      return relay;
    };
    /** Switches to B with the switcher at the top of the menu. */
    const switchToB = () => enterRelay(() => pick(false));
    const inPage = (script, argument) => win.evaluate(script, argument);
    const fetchJson = (url, init) => inPage(async ([url, init]) => {
      const response = await fetch(url, init);
      const text = await response.text();
      let body;
      try { body = JSON.parse(text); } catch { body = text; }
      return { status: response.status, headers: Object.fromEntries(response.headers), body };
    }, [url, init ?? {}]);

    // What B received from this window or on its behalf since `mark`: every browser request
    // (only the relay can have carried one to B here) and every signed one (the relay's own).
    // The test's direct API reads carry neither. Each must verify as `device`'s signature.
    const forwardedSince = (mark, device) => {
      const forwarded = receivedSince(mark).filter(entry => entry.site || entry.signature !== 'none');
      assert.ok(forwarded.length > 0, 'Nothing reached B through the relay');
      assert.deepEqual(forwarded.filter(entry => entry.signature !== 'Authenticated' || entry.device !== device), [],
        'A request reached B through the relay without a valid signature of the managing device');
      assert.deepEqual(forwarded.filter(entry => entry.ticket || entry.cookie), [], 'A switch ticket or a cookie reached B');
      return forwarded;
    };

    await win.evaluate(key => localStorage.setItem(key, 'this-device'), SENTINEL);
    const firstMark = received().length;
    const firstRelay = await switchToB();
    // B's own UI greets a window origin it has never seen with its first-run guide.
    await win.getByRole('button', { name: exactly('helpCenter.action.getStarted') }).click();
    await win.getByRole('button', { name: exactly('helpCenter.action.getStarted') }).waitFor({ state: 'hidden' });
    // B's own UI and API, through the relay, as the imported device.
    const status = await fetchJson('/client/status');
    assert.equal(status.body.data.host, 'console');
    assert.deepEqual(status.body.data.servers.map(server => [server.serverId, server.deviceId]), [[bInfo.id, legacyDevice]]);
    assert.ok(!JSON.stringify(status.body).includes(legacyKey), '/client/status exposed a device key');
    const listed = await fetchJson('/client/switcher');
    assert.equal(listed.body.data.currentId, bInfo.id);
    assert.deepEqual(listed.body.data.targets.map(target => [target.id === bInfo.id ? 'B' : target.isLocal ? 'local' : target.id, target.isCurrent]),
      [['local', false], ['B', true]]);
    assert.equal((await fetchJson('/remote-access/server-info')).body.data.id, bInfo.id, 'The relay page does not talk to B');
    // B's page has storage of its own, apart from this device's; the value written here must
    // not be what this device's origin reads back after switching home.
    assert.equal(await inPage(key => localStorage.getItem(key), SENTINEL), null);
    await inPage(key => localStorage.setItem(key, 'relay-origin'), SENTINEL);
    // A cookie on the relay's origin must never reach B.
    await inPage(() => { document.cookie = 'relay-cookie-probe=1; path=/'; });
    assert.equal((await fetchJson('/remote-access/server-info')).status, 200);
    // Cookies ignore ports: left in place, this one would reach B directly from B's own pages.
    await inPage(() => { document.cookie = 'relay-cookie-probe=; Max-Age=0; path=/'; });
    const firstForwarded = forwardedSince(firstMark, legacyDevice);
    assert.ok(firstForwarded.some(entry => entry.method === 'GET' && entry.path === '/' && entry.dest === 'document'),
      'B\'s own UI document was not loaded through the relay');
    assert.ok(firstForwarded.some(entry => entry.path === '/remote-access/server-info'));
    await win.screenshot({ path: artifacts('switching-b-console.png') });
    report.switchedToRelayWithoutTicket = { relayPort: relayPort(firstRelay), forwardedAndSigned: firstForwarded.length };

    // (c) Into B at a route, the way a link from this device's UI into B's goes: A's own page
    // asks A for the switch to B's configuration page and sends the window there. The route
    // is the fragment, which the browser never sends, so only the landing page can carry it on.
    await pick(true);
    await win.waitForURL(url => url.origin === home);
    const route = '#/configuration';
    const routedMark = received().length;
    const routedRelay = await enterRelay(async () => {
      const opened = await fetchJson(`/federation/local/servers/${encodeURIComponent(bInfo.id)}/open`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path: '/' + route })
      });
      assert.equal(opened.status, 200, `Opening B at ${route} from A's page: HTTP ${opened.status}`);
      const { url } = opened.body;
      assert.ok(url.includes(TICKET) && url.endsWith(route), `Not a ticketed switch to ${route}: ${shown(url)}`);
      await inPage(url => location.assign(url), url);
    }, route);
    assert.equal(routedRelay, firstRelay);
    report.routedSwitchKeptItsRoute = route;

    // Full control of B through its own UI: a settings write no federated grant can make.
    assert.equal((await settingsOf(source)).allowLiveTranscode, false);
    const controlMark = received().length;
    await win.getByPlaceholder(exactly('configuration.search.placeholder')).fill(
      locales[0]['configuration.remoteAccess.liveTranscode.label']);
    const row = win.locator('div.grid').filter({ hasText: exactly('configuration.remoteAccess.liveTranscode.label') })
      .filter({ has: win.getByRole('switch') });
    // The row exists once B's remote-access settings have been read (it is hidden while off).
    await row.getByRole('switch', { checked: false }).waitFor();
    assert.equal(await row.count(), 1);
    // Controlled by what B answers: it turns on once the write has succeeded and been re-read.
    const pushMark = frameSequence;
    await row.getByRole('switch', { checked: false }).click();
    await row.getByRole('switch', { checked: true }).waitFor();
    await until(() => settingsOf(source), settings => settings.allowLiveTranscode === true, 'B to allow live transcoding');
    const write = forwardedSince(controlMark, legacyDevice)
      .find(entry => entry.method === 'PUT' && entry.path === '/remote-access/live-transcode');
    assert.ok(write, 'The setting was not written through the relay');
    assert.equal(write.status, 200);
    report.fullControlWriteOnB = 'PUT /remote-access/live-transcode through B\'s settings page';

    // B's own UI keeps its hub through the relay, as a WebSocket: the change just made is pushed
    // to it live. The relay admits the handshake because it names the relay's own page, and
    // forwards it signed and naming B's own origin — B, on this machine, takes the relay for a
    // loopback caller and judges a handshake by its Origin like A does.
    const relayHub = routedRelay.replace(/^http/, 'ws') + '/hub/ui';
    const liveHubs = await until(() => winSockets.filter(socket => socket.url.startsWith(relayHub)),
      list => list.some(socket => !socket.closed && socket.errors.length === 0 &&
        socket.matched.some(match => match.name === 'liveTranscodeOn' && match.at > pushMark)),
      'B to push the setting to its own UI over the relay\'s WebSocket');
    const hubAtB = receivedSince(routedMark).filter(entry => entry.websocket && entry.path === '/hub/ui');
    assert.ok(hubAtB.length >= 1, 'B\'s UI hub handshake did not reach B through the relay');
    assert.deepEqual(hubAtB.map(entry => [entry.origin, entry.signature, entry.device, entry.status]),
      hubAtB.map(() => [source.base, 'Authenticated', legacyDevice, 101]),
      'B\'s UI hub reached B unsigned, not as B\'s own UI, or was refused');
    report.relayPageHubLive = { relaySockets: liveHubs.length, handshakesAtB: hubAtB.length, forwardedOrigin: source.base };

    // (d) Path mappings are this device's: set in A's UI, applied by A's relay, never sent to B.
    const mappedRoot = path.join(unified.directory, 'mapped-source-library');
    assert.ok(!fs.existsSync(mappedRoot));
    const mappings = await context.newPage();
    await mappings.goto(home + '/#/federation/devices?section=servers');
    const mappingCard = mappings.locator('#managed-servers').getByTestId('managed-server');
    await mappingCard.locator('summary').click();
    await mappingCard.getByRole('button', { name: exactly('federation.servers.mappings.add') }).click();
    await mappingCard.getByLabel(exactly('federation.servers.mappings.server')).last().fill(source.directory);
    await mappingCard.getByLabel(exactly('federation.servers.mappings.local')).last().fill(mappedRoot);
    await mappingCard.getByRole('button', { name: exactly('federation.servers.mappings.save') }).click();
    await mappings.locator('#managed-servers').getByRole('status')
      .filter({ hasText: containing('federation.servers.mappings.saved') }).waitFor();
    assert.deepEqual((await managedServers()).servers[0].pathMappings, [
      { serverPath: '/legacy/media', localPath: '/legacy/local-media' },
      { serverPath: source.directory, localPath: mappedRoot }
    ]);
    await mappings.close();
    const interceptMark = received().length;
    // What B's "open folder" asks for. A's relay answers it: it reads the resource's path from
    // B, maps it here, and stops at the missing folder rather than opening anything.
    const directory = await fetchJson('/resource/directory?id=1');
    assert.equal(directory.status, 404);
    assert.ok(directory.body.message.includes(path.join(mappedRoot, 'fixture.wav')),
      `Not answered with the locally mapped path: ${directory.body.message}`);
    const unmapped = await fetchJson('/tool/open-file?path=' + encodeURIComponent('/not-a-mapped-library/clip.mp4'));
    assert.equal(unmapped.status, 404);
    assert.equal(unmapped.headers['x-bakabase-client'], 'PathNotMapped');
    assert.equal(unmapped.body.serverPath, '/not-a-mapped-library/clip.mp4');
    const intercepted = forwardedSince(interceptMark, legacyDevice);
    assert.ok(intercepted.some(entry => entry.path === '/resource/keys' && !entry.site),
      'The relay did not ask B for the resource\'s path itself');
    assert.deepEqual(receivedSince(interceptMark).filter(entry => ['/resource/directory', '/tool/open-file'].includes(entry.path)), [],
      'A user-machine action was forwarded to B');
    assert.ok(!fs.existsSync(mappedRoot));
    report.userMachineActionMappedLocallyNotForwarded = true;

    // (e) Back to this device: its own origin, with its browser storage intact.
    await pick(true);
    await win.waitForURL(url => url.origin === home);
    await trigger.waitFor();
    assert.equal(await switcher.getByText(exactly('federation.switcher.managing')).count(), 0);
    assert.equal(await win.evaluate(key => localStorage.getItem(key), SENTINEL), 'this-device');
    await win.goto(home + '/#/federation/devices?section=servers');
    await servers.getByTestId('managed-server').waitFor();
    await win.screenshot({ path: artifacts('switching-back-on-a.png') });
    report.switchedBackWithStorage = true;

    // (f) Stop managing B, then ask again without a code and have it approved on B.
    const forgetMark = received().length;
    await servers.getByTestId('managed-server').getByRole('button', { name: exactly('federation.servers.forget') }).click();
    await win.getByRole('alertdialog').getByRole('button', { name: exactly('federation.confirm') }).click();
    await win.getByRole('alertdialog').waitFor({ state: 'hidden' });
    await servers.getByText(exactly('federation.servers.empty')).waitFor();
    assert.deepEqual((await managedServers()).servers, []);
    assert.ok(!(await settingsOf(source)).devices.some(device => device.id === legacyDevice),
      'B still lets the imported device in after "Stop managing"');
    const revoke = receivedSince(forgetMark).find(entry => entry.method === 'DELETE' && entry.path === `/remote-access/devices/${legacyDevice}`);
    assert.ok(revoke && revoke.signature === 'Authenticated' && revoke.device === legacyDevice, 'The revocation was not signed as the device');
    assert.ok(!fs.readFileSync(storeFile, 'utf8').includes(legacyKey), 'The key outlived "Stop managing"');

    await servers.getByLabel(exactly('federation.servers.add.address')).fill(source.base);
    await servers.getByRole('button', { name: exactly('federation.servers.add.request') }).click();
    await servers.getByRole('status').filter({ hasText: containing('federation.servers.requested', { name: bInfo.name }) }).waitFor();
    await servers.getByTestId('managed-server-requests').locator('[data-active]')
      .getByText(containing('federation.servers.waiting', { name: bInfo.name })).waitFor();
    const waiting = await managedServers();
    assert.deepEqual(waiting.servers, []);
    assert.equal(waiting.requests.length, 1);
    assert.equal(waiting.requests[0].active, true);
    const owner = await context.newPage();
    await owner.goto(source.base + '/#/federation/devices?section=management');
    await owner.getByTestId('management-requests').getByRole('button', { name: exactly('federation.management.requests.approve') }).click();
    await owner.getByRole('alertdialog').getByRole('button', { name: exactly('federation.confirm') }).click();
    await owner.getByRole('alertdialog').waitFor({ state: 'hidden' });
    await owner.close();
    // Collected in the background by A, and noticed by the page without a reload.
    await servers.getByRole('status').filter({ hasText: containing('federation.servers.approved', { name: bInfo.name }) })
      .waitFor({ timeout: 30000 });
    await servers.getByTestId('managed-server').waitFor();
    const repaired = await managedServers();
    assert.equal(repaired.servers.length, 1);
    assert.equal(repaired.servers[0].serverId, bInfo.id);
    assert.equal(repaired.servers[0].importedFromLegacyClient, false);
    assert.deepEqual(repaired.requests, []);
    const newDevice = field(readStore().find(server => field(server, 'ServerId') === bInfo.id), 'DeviceId');
    assert.ok(newDevice && newDevice !== legacyDevice);
    assert.ok((await settingsOf(source)).devices.some(device => device.id === newDevice));
    const secondMark = received().length;
    const secondRelay = await switchToB();
    const again = await fetchJson('/client/status');
    assert.deepEqual(again.body.data.servers.map(server => [server.serverId, server.deviceId]), [[bInfo.id, newDevice]]);
    // Signed with the key collected in the background, not the one "Stop managing" dropped.
    assert.ok(forwardedSince(secondMark, newDevice).some(entry => entry.path === '/' && entry.dest === 'document'));
    report.forgetRequestApproveAndSwitchAgain = { relayPortReused: secondRelay === firstRelay };

    // (g) The relay page is the managed server's code. What it must not be able to do here:
    // 127.0.0.1 is the same site as the relay (ports do not make a site), localhost another one.
    const aTargets = [[unified.base, 'same-site'], [home, 'cross-site']];
    // Issuing replaces whatever code A has (a fixture with no screen announces one at start),
    // so an unchanged code — or none either side — shows no request of the page's got through.
    const aPairingCode = async () => (await settingsOf(unified)).pairingCode ?? null;
    const aCodeBefore = await aPairingCode();
    /** Sends a request from the relay page and reads A's own record of how it answered. */
    const refusedByA = async (url, site, dest, send) => {
      const mark = requestsTo(unified).length;
      const response = win.waitForResponse(candidate => candidate.url() === url);
      await send(url);
      // A no-cors answer is opaque to the page; the status is all the browser shows of it.
      assert.equal((await response).status(), 403, `${url} was not refused`);
      const { pathname } = new URL(url);
      const seen = requestsTo(unified).slice(mark).filter(entry => entry.path === pathname);
      assert.equal(seen.length, 1, `A saw ${seen.length} requests for ${pathname}`);
      assert.deepEqual([seen[0].site, seen[0].dest, seen[0].status, seen[0].denial], [site, dest, 403, 'HostOnly']);
      return seen[0];
    };
    /**
     * Opens a WebSocket from `page` to `url` — a channel no CORS check looks at, whose pushes
     * include every options object the hub's server has, third-party cookies and API keys
     * among them. Should it open, the page does what the relay page's code could: SignalR's
     * handshake and a request for the initial data, then counts what comes back. Returns what
     * the page saw and why Playwright says the handshake failed (the status it was answered
     * with).
     */
    const probeSocket = async (page, sockets, url) => {
      const before = sockets.length;
      const seen = await page.evaluate(([url, handshake, initialData]) => new Promise(resolve => {
        const result = { outcome: 'pending', opened: false, messages: 0 };
        const socket = new WebSocket(url);
        const finish = outcome => { clearTimeout(timer); result.outcome = outcome; resolve(result); };
        const timer = setTimeout(() => { socket.close(); finish('pending'); }, 10000);
        socket.onopen = () => {
          result.opened = true;
          socket.send(handshake);
          socket.send(initialData);
          setTimeout(() => { socket.close(); finish('open'); }, 2000);
        };
        socket.onmessage = () => { result.messages++; };
        socket.onerror = () => { if (!result.opened) finish('failed'); };
      }), [url, HUB_HANDSHAKE, HUB_INITIAL_DATA]);
      const [reported] = await until(() => sockets.slice(before).filter(socket => socket.url === url),
        list => list.length === 1 && (list[0].errors.length > 0 || seen.opened), `Playwright's report of ${url}`, 5000);
      return { url, ...seen, error: reported.errors[0] ?? null };
    };
    /**
     * A WebSocket from the relay page to A's UI hub, which A must refuse as it refuses a write:
     * by the handshake's Origin, the relay's — Chromium, and so WebView2, sends no fetch
     * metadata on a handshake. Nothing may come back over it, and A's own record must show the
     * refusal.
     */
    const refusedHubAtA = async (base, relay) => {
      const url = base.replace(/^http/, 'ws') + '/hub/ui';
      const mark = requestsTo(unified).length;
      const probe = await probeSocket(win, winSockets, url);
      assert.deepEqual([probe.outcome, probe.opened, probe.messages], ['failed', false, 0],
        `The relay page's WebSocket to ${url} got through: ${JSON.stringify(probe)}`);
      assert.match(probe.error ?? '', /\b403\b/, `The relay page's WebSocket to ${url} was not answered 403`);
      const seen = requestsTo(unified).slice(mark).filter(entry => entry.path === '/hub/ui');
      assert.equal(seen.length, 1, `A saw ${seen.length} handshakes for /hub/ui`);
      const [handshake] = seen;
      assert.deepEqual([handshake.websocket, handshake.origin, handshake.status, handshake.denial],
        [true, relay, 403, 'HostOnly'], `A's answer to ${url}: ${JSON.stringify(handshake)}`);
      // What the browser labelled it with besides: nothing, today — the reason Origin decides.
      return { url, error: probe.error, fetchMetadata: [handshake.site, handshake.dest] };
    };
    const refusedHubs = [];
    for (const [base, site] of aTargets) {
      // A write without a preflight: it would issue a pairing code for this device.
      await refusedByA(base + '/remote-access/pairing/code', site, 'empty',
        url => inPage(url => fetch(url, { method: 'POST', mode: 'no-cors', body: 'x' }).catch(() => null), url));
      // A GET whose action runs on this machine.
      await refusedByA(base + '/file/icon?type=1&path=probe.txt', site, 'empty',
        url => inPage(url => fetch(url, { mode: 'no-cors' }).catch(() => null), url));
      // This device's own UI in a frame of the managed server's page.
      const framed = await refusedByA(base + '/', site, 'iframe',
        url => inPage(url => { const frame = document.createElement('iframe'); frame.src = url; document.body.append(frame); }, url));
      assert.match(framed.csp, /frame-ancestors 'self'/);
      refusedHubs.push(await refusedHubAtA(base, secondRelay));
    }
    assert.deepEqual(await aPairingCode(), aCodeBefore, 'A issued a pairing code for a request from the relay page');
    await inPage(() => document.querySelectorAll('iframe').forEach(frame => frame.remove()));
    for (const [method, route] of [['GET', '/client/log'], ['POST', '/client/log/open'], ['GET', '/client/app/info'], ['POST', '/client/app/open']]) {
      const answer = await fetchJson(route, { method });
      assert.equal(answer.status, 404, `${method} ${route} on the relay: HTTP ${answer.status}`);
    }
    // Another origin cannot navigate into the relay without a ticket, nor with a spent one —
    // every ticket this relay's port was given has been used by a switch above.
    const spent = tickets.filter(ticket => new URL(ticket).origin === secondRelay);
    assert.ok(spent.length >= 1);
    for (const target of [secondRelay + '/', ...spent]) {
      // Any document on A's origin will do as the page that starts the navigation.
      const outsider = await context.newPage();
      await outsider.goto(home + '/remote-access/server-info');
      const navigation = outsider.waitForResponse(response => response.url() === target && response.request().isNavigationRequest());
      await outsider.evaluate(url => location.assign(url), target);
      const answer = await navigation;
      const shown = target.replace(/__bakabase_switch=\w+/, `${TICKET}=…`);
      assert.equal(answer.status(), 400, `${shown} was not refused`);
      assert.equal((await answer.allHeaders())['x-bakabase-client'], 'ForeignCaller');
      await outsider.waitForURL(target);
      assert.ok(!(await outsider.content()).includes('location.replace'), `${shown} was answered with the ticket's landing page`);
      await outsider.close();
    }
    // Nor can another page open B's hub through the relay: the relay signs what it forwards
    // with this device's key, so a socket through it is full control of B, readable by the page
    // that opened it. The handshake names that page, not the relay's, and the relay refuses it
    // before B sees anything — from this device's own window on either of its addresses, and
    // from an opaque page (Origin: null).
    const refusedByRelay = [];
    const secondRelayHub = secondRelay.replace(/^http/, 'ws') + '/hub/ui';
    for (const pageAt of [home, unified.base, null]) {
      const outsider = await context.newPage();
      const outsiderSockets = watchSockets(outsider);
      if (pageAt) await outsider.goto(pageAt + '/remote-access/server-info');
      const mark = received().length;
      const probe = await probeSocket(outsider, outsiderSockets, secondRelayHub);
      const from = pageAt ?? 'about:blank';
      assert.deepEqual([probe.outcome, probe.opened, probe.messages], ['failed', false, 0],
        `A WebSocket from ${from} to ${secondRelayHub} got through: ${JSON.stringify(probe)}`);
      assert.match(probe.error ?? '', /\b400\b/, `The relay did not refuse the WebSocket from ${from}: ${probe.error}`);
      assert.deepEqual(receivedSince(mark).filter(entry => entry.websocket || entry.path.startsWith('/hub')), [],
        `The WebSocket from ${from} reached B`);
      refusedByRelay.push({ from, error: probe.error });
      await outsider.close();
    }
    // Nothing the relay page tried above reached B unsigned, and no ticket ever did. Nothing
    // under /client — the relay's own prefix — reached B during this stage at all.
    forwardedSince(secondMark, newDevice);
    assert.deepEqual(receivedSince(firstMark).filter(entry => entry.path.startsWith('/client')), [],
      'A /client request was forwarded to B');
    report.relayPageContained = {
      crossSiteWritesRefusedByA: true, userMachineGetRefusedByA: true, framesRefusedByA: true,
      thisDevicesDiagnosticsHidden: true, untickettedNavigationRefused: true, spentTicketRefused: true,
      hubSocketsRefusedByA: true, hubSocketsFromOtherPagesRefusedByRelay: true
    };
    report.hubSockets = { refusedByA: refusedHubs, refusedByRelay };

    // The device map on A draws what A knows now: B, managed from here — one line from A to
    // B, and no line for anything A does not manage. Its panel offers what the devices page
    // offers for B. Read only: nothing is clicked that changes anything.
    const listing = await managedServers();
    const map = await context.newPage();
    await map.goto(home + '/#/federation/map');
    await map.getByRole('heading', { name: exactly('federation.map.title') }).waitFor();
    const bName = new RegExp(`^${escape(bInfo.name)}$`);
    const bCard = map.locator('g[role="button"][data-node]').filter({ has: map.locator('title', { hasText: bName }) });
    await bCard.waitFor();
    const bNode = await bCard.getAttribute('data-node');
    const managesB = map.locator(`g[role="button"][data-edge="management:${bNode}"]`);
    await managesB.waitFor();
    assert.equal(await managesB.getAttribute('data-out'), 'active', 'The map does not draw A managing B');
    assert.equal(await managesB.getAttribute('data-in'), 'none', 'The map draws B managing A');
    assert.equal(await map.locator('g[role="button"][data-edge^="management:"][data-out="active"]').count(),
      listing.servers.length, 'The map draws a managed server A does not have, or misses one');
    assert.equal(await map.locator('g[role="button"][data-node="self"]').getAttribute('data-kind'), 'desktop');
    // B is drawn as what it says it is: a headless server.
    await map.locator(`g[role="button"][data-node="${bNode}"][data-kind="server"]`).waitFor();
    await bCard.click();
    const mapPanel = map.getByTestId('device-map-panel');
    await mapPanel.getByRole('heading', { name: bName }).waitFor();
    await mapPanel.getByRole('button', { name: exactly('federation.servers.open') }).waitFor();
    await map.screenshot({ path: artifacts('device-map.png'), fullPage: true });
    // Narrower than the widest windows, the details are shown on demand: beside the map, which
    // gives up their width and is laid out again for the rest. Nothing of the map may be under
    // them — no device a keyboard can reach there, no part of the map's region — and the device
    // selected is in view. At this window's 1440 px, then at the desktop app's smallest, 1280.
    const besideTheMap = () => map.evaluate(() => {
      const layout = document.querySelector('[data-testid="device-map-layout"]');
      const details = document.querySelector('[data-testid="device-map-details"]');
      const region = layout.firstElementChild;
      const d = details.getBoundingClientRect();
      const m = region.getBoundingClientRect();
      const under = element => {
        const r = element.getBoundingClientRect();
        return [[r.left + 4, r.top + r.height / 2], [r.left + r.width / 2, r.top + r.height / 2], [r.right - 4, r.top + r.height / 2]]
          .some(([x, y]) => details.contains(document.elementFromPoint(x, y)));
      };
      const devices = [...region.querySelectorAll('[role="button"][data-node], button[data-node]')];
      const selected = region.querySelector('[aria-pressed="true"][data-node]');
      const s = selected?.getBoundingClientRect();
      return {
        details: layout.getAttribute('data-details'),
        regionOverlapsDetails: m.right > d.left && m.left < d.right && m.bottom > d.top && m.top < d.bottom,
        devicesUnderDetails: devices.filter(under).map(device => device.getAttribute('data-node')),
        selected: selected?.getAttribute('data-node') ?? null,
        selectedInView: !!s && s.top >= 0 && s.bottom <= innerHeight && s.left >= 0 && s.right <= innerWidth,
        canvas: document.querySelector('[data-testid="device-map-canvas"]')?.getBoundingClientRect().width,
        sideways: document.documentElement.scrollWidth > innerWidth + 1,
      };
    });
    const clearOfDetails = value => value.details === 'open' && !value.regionOverlapsDetails &&
      value.devicesUnderDetails.length === 0 && value.selected === bNode && value.selectedInView && !value.sideways;
    const besideAt1440 = await until(besideTheMap, clearOfDetails, 'the details beside the map at 1440 px, nothing of it under them', 5000);
    await map.setViewportSize({ width: 1280, height: 1000 });
    const besideAt1280 = await until(besideTheMap, value => clearOfDetails(value) && value.canvas < besideAt1440.canvas,
      'the map laid out again at 1280 px beside the details, nothing of it under them', 5000);
    // The cards glide to where the new layout puts them: the picture once they are there.
    let lastPlace;
    await until(async () => {
      const place = await map.locator('g[role="button"][data-node="self"]').boundingBox();
      const still = !!place && !!lastPlace && Math.abs(place.x - lastPlace.x) < 0.5 && Math.abs(place.y - lastPlace.y) < 0.5;
      lastPlace = place;
      return still;
    }, still => still, 'the map to settle at 1280 px', 5000);
    await map.screenshot({ path: artifacts('device-map-1280.png'), fullPage: true });
    assert.equal(await map.getByTestId('device-map-layout').getAttribute('data-layout'), 'on-demand');
    await map.setViewportSize({ width: 1440, height: 1000 });

    // From the keyboard, in Chromium: an action that takes away what the details show leaves
    // the keyboard in the details. Its button is disabled while it runs, and Chromium moves
    // focus off a disabled button to the page's body at once — long before the listings are
    // read again — so where the keyboard was is taken when the action starts, not when the page
    // last drew. (1) A stops managing B, on its map.
    const focused = page => page.evaluate(() => {
      const active = document.activeElement;
      return active
        ? { tag: active.tagName, id: active.id, text: active === document.body ? '' : (active.textContent ?? '').trim().slice(0, 80) }
        : null;
    });
    const onDetailsHeading = (page, what) =>
      until(() => focused(page), active => active?.id === 'device-map-panel-title', what, 15000);
    await mapPanel.getByRole('button', { name: exactly('federation.servers.forget') }).focus();
    await map.keyboard.press('Enter');
    await map.getByRole('alertdialog').waitFor();
    assert.match((await focused(map)).text, exactly('federation.confirm'));
    await map.keyboard.press('Enter');
    await until(managedServers, view => view.servers.length === 0, 'A to stop managing B');
    const afterForget = await onDetailsHeading(map, 'the keyboard back in the details after stopping to manage B');
    await map.screenshot({ path: artifacts('device-map-after-forget.png') });

    // (2) A asks to manage B again; B approves on its own map, from the keyboard. The request
    // goes at once; the device it lets in is listed only once A has collected its key, in the
    // background — the details wait for it there and move to it, still with the keyboard.
    const asker = await context.newPage();
    await asker.goto(home + '/#/federation/devices?section=servers');
    const askerServers = asker.locator('#managed-servers');
    await askerServers.getByLabel(exactly('federation.servers.add.address')).fill(source.base);
    await askerServers.getByRole('button', { name: exactly('federation.servers.add.request') }).click();
    await askerServers.getByRole('status').filter({ hasText: containing('federation.servers.requested', { name: bInfo.name }) }).waitFor();
    const onB = await context.newPage();
    await onB.goto(source.base + '/#/federation/map');
    const requestCard = onB.locator('g[role="button"][data-node^="manager-request:"]');
    await requestCard.waitFor();
    assert.equal(await requestCard.count(), 1);
    await requestCard.focus();
    await onB.keyboard.press('Enter');
    const bPanel = onB.getByTestId('device-map-panel');
    await onDetailsHeading(onB, 'the keyboard in B\'s details for the request');
    await bPanel.getByRole('button', { name: exactly('federation.management.requests.approve') }).focus();
    await onB.keyboard.press('Enter');
    await onB.getByRole('alertdialog').waitFor();
    const approval = onB.waitForResponse(response => /\/remote-access\/pairing\/requests\/[^/]+\/approve$/.test(response.url()));
    await onB.keyboard.press('Enter');
    const approvedDevice = (await (await approval).json()).data?.deviceId;
    assert.ok(approvedDevice, 'Approving did not say which device it let in');
    await until(async () => (await settingsOf(source)).devices.map(device => device.id), ids => ids.includes(approvedDevice),
      'A to collect its key, under the id approving named', 30000);
    await until(() => bPanel.getAttribute('data-overview'), overview => overview === null,
      'B\'s details to move to the device it let in');
    await bPanel.getByTestId('management-in').waitFor();
    assert.equal(await bPanel.getByTestId('management-in').getAttribute('data-status'), 'active');
    await bPanel.getByRole('status').filter({ hasText: containing('federation.management.requests.approved') }).waitFor();
    const afterApprove = await onDetailsHeading(onB, 'the keyboard in B\'s details on the device it let in');
    await onB.screenshot({ path: artifacts('device-map-after-approve.png') });
    // (3) Enter on the message's own × takes the message and the button away: the keyboard goes
    // to the details' heading, not to the page's body.
    const approvedSaid = bPanel.getByRole('status').filter({ hasText: containing('federation.management.requests.approved') });
    await approvedSaid.getByRole('button', { name: exactly('federation.dismiss') }).focus();
    await onB.keyboard.press('Enter');
    await approvedSaid.waitFor({ state: 'detached' });
    const afterDismiss = await onDetailsHeading(onB, 'the keyboard in B\'s details once what approving said is dismissed');
    await until(managedServers, view => view.servers.length === 1 && view.servers[0].serverId === bInfo.id,
      'A to manage B again');
    await Promise.all([map.close(), asker.close(), onB.close()]);

    // (4) More devices than the map can draw beside the details at the desktop app's smallest
    // window: opening them turns the drawing into the list, closing them the list back into the
    // drawing, and each rendering replaces every control of the other. The keyboard must end on
    // the device or relationship it was on, never on the page's body. The devices pair with A
    // through A's own pairing API — a code minted on A, exchanged under a name of their own —
    // and are revoked again afterwards.
    const CROWD = 13;
    const crowd = [];
    let crowded;
    try {
      for (let i = 1; i <= CROWD; i++) {
        const { code } = await envelope(unified.base + '/remote-access/pairing/code', { method: 'POST' });
        const paired = await envelope(unified.base + '/remote-access/pair/code', {
          method: 'POST',
          data: { code, deviceName: `fixture-manager-${String(i).padStart(2, '0')}`, platform: 'Android' },
        });
        assert.ok(paired?.credentials?.deviceId, `A did not pair device ${i} (failure ${paired?.failure})`);
        crowd.push(paired.credentials.deviceId);
      }
      crowded = await context.newPage();
      await crowded.setViewportSize({ width: 1280, height: 1000 });
      await crowded.goto(home + '/#/federation/map');
      await crowded.getByRole('heading', { name: exactly('federation.map.title') }).waitFor();
      const mode = () => crowded.getByTestId('device-map-canvas').getAttribute('data-mode');
      const cards = crowded.locator('g[role="button"][data-node^="manager:"]');
      await until(() => cards.count(), count => count === CROWD, `A's map to draw the ${CROWD} devices paired with it`);
      assert.equal(await mode(), 'map', 'A\'s map is not drawn at 1280 px with the details closed');
      const keyboardOn = () => crowded.evaluate(() => {
        const active = document.activeElement;
        return {
          tag: active?.tagName.toLowerCase() ?? null,
          node: active?.getAttribute('data-node') ?? null,
          edge: active?.getAttribute('data-edge') ?? null,
          body: active === document.body,
        };
      });
      const target = await cards.first().getAttribute('data-node');
      const card = crowded.locator(`g[role="button"][data-node="${target}"]`);
      const relationship = `management:${target}`;
      const detailsListTheMap = what => until(mode, value => value === 'list', `A's map listed beside the details ${what}`, 5000);
      const drawnAgain = what => until(mode, value => value === 'map', `A's map drawn again once the details closed ${what}`, 5000);

      // (a) Enter on a device's card; Escape in the details.
      await card.focus();
      await crowded.keyboard.press('Enter');
      await detailsListTheMap('opened from the keyboard');
      await onDetailsHeading(crowded, 'the keyboard in the details that listed the map');
      await crowded.screenshot({ path: artifacts('device-map-crowded-open.png'), fullPage: true });
      await crowded.keyboard.press('Escape');
      await drawnAgain('with Escape');
      const afterEscape = await until(keyboardOn, on => on.tag === 'g' && on.node === target,
        'the keyboard on the card of the device that opened the details, drawn again', 5000);

      // (b) Enter on its relationship; the details' own X, from the keyboard.
      await crowded.locator(`g[role="button"][data-edge="${relationship}"]`).focus();
      await crowded.keyboard.press('Enter');
      await detailsListTheMap('opened from a relationship');
      await onDetailsHeading(crowded, 'the keyboard in the details of the relationship');
      await crowded.getByTestId('device-map-panel').getByRole('button', { name: exactly('federation.close') }).focus();
      await crowded.keyboard.press('Enter');
      await drawnAgain('with their X');
      const afterClose = await until(keyboardOn, on => on.tag === 'g' && on.edge === relationship,
        'the keyboard on the relationship that opened the details, drawn again', 5000);

      // (c) A pointer on the card, which keeps the keyboard: the card goes with the drawing, and
      // the keyboard goes to the same device in the list; Escape there, on the map, closes the
      // details, and the keyboard is on the card drawn again.
      await card.click();
      await detailsListTheMap('opened with a pointer');
      const inTheList = await until(keyboardOn, on => on.tag === 'button' && on.node === target,
        'the keyboard on the same device in the list', 5000);
      await crowded.keyboard.press('Escape');
      await drawnAgain('with Escape on the map');
      const afterEscapeOnMap = await until(keyboardOn, on => on.tag === 'g' && on.node === target,
        'the keyboard on the device\'s card once Escape on the map closed the details', 5000);
      await crowded.screenshot({ path: artifacts('device-map-crowded-closed.png'), fullPage: true });
      report.deviceMapCrowded = {
        devices: CROWD, width: 1280, openedListsTheMap: true,
        keyboardAfterEscape: afterEscape, keyboardAfterClose: afterClose,
        keyboardInTheListAfterPointer: inTheList, keyboardAfterEscapeOnMap: afterEscapeOnMap,
      };
    } finally {
      await crowded?.close();
      for (const id of crowd)
        await call(`${unified.base}/remote-access/devices/${encodeURIComponent(id)}`, { method: 'DELETE' });
    }
    assert.deepEqual((await settingsOf(unified)).devices.filter(device => crowd.includes(device.id)), [],
      'A still lists a device paired for the crowded map');

    report.deviceMap = {
      managedServerDrawn: true, managementLines: listing.servers.length, managedServerKind: 'server',
      detailsBesideTheMap: { at1440: besideAt1440, at1280: besideAt1280 },
      keyboardAfterStopManaging: afterForget.id, keyboardAfterApproving: afterApprove.id, approvalNamedTheDevice: true,
      keyboardAfterDismissing: afterDismiss.id
    };

    assert.deepEqual(pageErrors, []);
    await assertStayedLocal(context, blocked, 'Server switching');
    report.pageErrors = pageErrors;
    report.blockedRequests = blocked;
    report.passed = true;
    return report;
  } finally {
    if (blocked.external.length) console.error('Server switching: refused requests off this machine', blocked.external);
    await context.close();
  }
};
