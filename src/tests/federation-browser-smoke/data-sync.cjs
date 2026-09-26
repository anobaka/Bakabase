// Data sync: definitions kept in step between the desktop app (A) and the server it manages (B),
// through the production frontend in Chromium. Real hosts; no mocked API responses.
//
// Runs after server switching, which leaves A managing B. A's window keeps in step both ways with
// B from its device map, B approves in A's window switched to B, A reviews the first sync, and a
// rename both devices made differently is decided on A's /data-sync page. The device map's rule
// editor is driven from the keyboard at 1440 and 1280 px, the page is read at 375 px, and B is
// opened as a browser on another device would open it: on its LAN-caller port, which it takes for
// a caller on another machine while nothing listens beyond this one.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { confine, assertStayedLocal } = require('./network.cjs');

/** Where RemoteConsoleOptions puts relay ports by default; nothing else on loopback is allowed. */
const RELAY_FIRST_PORT = 34650;
const RELAY_PORT_RANGE = 256;
const TICKET = '__bakabase_switch';

// What the server's enums are on the wire (src/web/src/sdk/constants.ts).
const MODE = { Off: 0, Follow: 1, TwoWay: 2 };
const STATE = { Active: 1, AwaitingAccess: 2, AwaitingReview: 3 };
const INITIATOR = { ThisDevice: 1, Peer: 2 };
const DIRECTION = { Outgoing: 2 };
const INTENT = { TwoWay: 2 };
const ITEM = { FieldConflict: 1 };
const ACTION = { KeepLocal: 1 };
const CLOSURE = { ResolvedElsewhere: 2 };
const PROBLEM = { NotAllowedOnThisDevice: 26 };
const REMOTE_ACCESS = { Enabled: 1, Unrestricted: 2 };
const SINGLE_LINE_TEXT = 1;

const escape = text => String(text).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

module.exports = async function dataSync({ browser, config, artifacts }) {
  const { unified: a, source: b } = config.hosts;
  // Where this browser shows A: the origin A recorded as its main window's.
  const home = a.window;
  assert.ok(b.lan, 'The runner gave B no port for a browser on another device');
  // Each UI language's strings, with the plural rules of the language the page registers them
  // under (en-US, zh-CN).
  const locales = [['en', 'en-US'], ['cn', 'zh-CN']].map(([dir, language]) => ({
    plural: new Intl.PluralRules(language),
    strings: Object.assign({}, ...['pages/dataSync', 'pages/federation']
      .map(file => JSON.parse(fs.readFileSync(path.join(config.repo, `src/web/src/locales/${dir}/${file}.json`), 'utf8')))),
  }));
  // A translated string as a pattern, in either UI language; {{placeholders}} take the given
  // values, or anything when not given. A given count picks the form i18next picks: the key's
  // plural form for it (English "_one" for 1) where the language has one, else the key itself.
  const pattern = (key, values = {}) => locales.map(({ plural, strings }) => {
    const text = ('count' in values ? strings[`${key}_${plural.select(Number(values.count))}`] : undefined) ?? strings[key];
    assert.ok(text, `Missing locale key ${key}`);
    return text.split(/(\{\{\w+\}\})/).map(part => {
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
  const hostOrigins = new Set([a, b].flatMap(host => [host.base, host.base.replace('127.0.0.1', 'localhost')]));

  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'en-US' });
  const report = { fixtureScope: 'A composed like UnifiedHost managing headless B; B also answers a LAN-caller port' };
  const pageErrors = [];
  let blocked = { external: [], loopback: [] };
  let lanContext;
  let lanBlocked = { external: [], loopback: [] };
  // What B's remote access and A's navigation were before this stage changed them, to put back
  // whatever happens.
  let restoreRemoteAccess;
  let restoreMenu;
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
      assert.equal(body.code, 0, `${url} answered code ${body.code}: ${body.message}`);
      return body.data;
    };
    /** A device's own loopback `/data-sync` API: what its window and its CLI call. */
    const ds = (host, route, options) => envelope(host.base + '/data-sync' + route, options);
    /** An action's answer: its problem, if any, fails the stage. */
    const act = async (host, route, options) => {
      const data = await ds(host, route, options);
      assert.ok(!data?.problem, `${options?.method ?? 'GET'} /data-sync${route} on ${host.base}: problem ${JSON.stringify(data?.problem)}`);
      return data;
    };
    /** Polls a read until it satisfies `done`, or fails with the last value. No fixed sleeps. */
    const until = async (read, done, what, timeout = 30000) => {
      const deadline = Date.now() + timeout;
      let value;
      while (Date.now() < deadline) {
        value = await read();
        if (done(value)) return value;
        await new Promise(resolve => setTimeout(resolve, 250));
      }
      assert.fail(`Timed out waiting for ${what}: ${JSON.stringify(value)?.slice(0, 2000)}`);
    };
    const linkWith = async (host, nodeId) => (await ds(host, '/links')).find(link => link.peerNodeId === nodeId);
    const openItems = async host => (await ds(host, '/inbox?openOnly=true&take=100')).items;
    const properties = async host => envelope(host.base + '/custom-property/all');
    /**
     * Syncs one link now, as its [Sync now] does, until `read` (the link, by default) satisfies
     * `done`. Asked again every few seconds: a fetch already under way when it is asked only
     * marks the link due, and may have passed it already.
     */
    const syncUntil = async (host, nodeId, done, what, read = () => linkWith(host, nodeId), timeout = 45000) => {
      const deadline = Date.now() + timeout;
      let value;
      while (Date.now() < deadline) {
        const link = await linkWith(host, nodeId);
        await act(host, '/sync-now', { method: 'POST', data: { linkId: link.id } });
        const again = Math.min(deadline, Date.now() + 3000);
        while (Date.now() < again) {
          value = await read();
          if (done(value)) return value;
          await new Promise(resolve => setTimeout(resolve, 250));
        }
      }
      assert.fail(`Timed out syncing for ${what}: ${JSON.stringify(value)?.slice(0, 2000)}`);
    };
    // What reached B, in arrival order, with B's own answer: see switching.cjs.
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
    const focused = page => page.evaluate(() => {
      const active = document.activeElement;
      return {
        tag: active?.tagName.toLowerCase() ?? null,
        id: active?.id ?? null,
        testId: active?.getAttribute('data-testid') ?? null,
        body: active === document.body,
        inDetails: !!active?.closest('[data-testid="device-map-details"]'),
      };
    });

    // ---- who is who ------------------------------------------------------------------------
    const aView = await ds(a, '/overview');
    const bView = await ds(b, '/overview');
    const aName = aView.deviceName;
    const bName = bView.deviceName;
    const aNode = aView.nodeId;
    const bNode = bView.nodeId;
    // The names the other device will be told, each its own: a check by name tells them apart.
    assert.notEqual(aName, bName, 'A and B share a name as nodes, so no check by name can tell them apart');
    assert.equal(aView.isHeadless, false, 'A is the desktop app');
    assert.equal(bView.isHeadless, true, 'B is a headless server');
    assert.deepEqual(await ds(a, '/links'), [], 'A syncs with something already');
    assert.deepEqual(await ds(b, '/links'), [], 'B syncs with something already');
    const managed = await call(a.base + '/federation/local/servers');
    const bServer = managed.servers.find(server => server.address === b.base);
    assert.ok(bServer, 'A does not manage B: server switching left it otherwise');
    // A node's id is its install's server id until it is reset: the map finds B's sync by it.
    assert.equal(bServer.serverId, bNode, 'B\'s node id is not its install id');

    // B shares its definitions (what `federation datasync share on` asks of it) and has one of its
    // own for A's first review to create.
    assert.equal(await ds(b, '/sharing', { method: 'PUT', data: { enabled: true } }), null);
    await envelope(b.base + '/custom-property', { method: 'POST', data: { name: 'Genre', type: SINGLE_LINE_TEXT } });

    // ---- (1) A keeps in step both ways with B, from its device map --------------------------
    const map = await context.newPage();
    // What A's hub pushes to this window for data sync, counted by key (UIHubConnection hands them
    // to data sync's store): never the frames themselves, which carry the server's state.
    const hubPushes = { DataSyncStatus: 0, DataSyncApplied: 0 };
    map.on('websocket', socket => socket.on('framereceived', ({ payload }) => {
      const text = String(payload);
      if (!text.includes('"GetIncrementalData"')) return;
      for (const key of Object.keys(hubPushes)) if (text.includes(`"${key}"`)) hubPushes[key]++;
    }));
    await map.goto(home + '/#/federation/map');
    await map.getByRole('heading', { name: exactly('federation.map.title') }).waitFor();
    const bCard = map.locator('g[role="button"][data-node]')
      .filter({ has: map.locator('title', { hasText: new RegExp(`^${escape(bName)}$`) }) });
    await bCard.waitFor();
    // B's line on the map, whichever of its records B's card stands for now: a managed server
    // until it is also a node A reads, then that node — the map follows the device, not the record.
    const lineOf = async () => {
      const node = await bCard.getAttribute('data-node');
      return map.locator(`g[role="button"][data-edge="sync:${node}"]`);
    };
    await bCard.click();
    const panel = map.getByTestId('device-map-panel');
    const syncSection = panel.getByTestId('device-map-sync-section');
    await syncSection.getByTestId('data-sync-start-toggle').click();
    await syncSection.getByTestId('data-sync-rule-drawing').waitFor();
    assert.equal(await syncSection.getByTestId('data-sync-arrow-receive').getAttribute('aria-pressed'), 'false');
    await syncSection.getByTestId('data-sync-mode-twoWay').click();
    // Both ways widens access: asked first, with what it turns on here (sharing is off on A).
    const twoWayDialog = map.getByRole('alertdialog');
    await twoWayDialog.waitFor();
    await twoWayDialog.getByRole('button', { name: exactly('federation.confirm') }).click();
    await twoWayDialog.waitFor({ state: 'hidden' });
    const asked = await until(() => ds(a, '/requests'),
      list => list.some(request => request.direction === DIRECTION.Outgoing && request.nodeId === bNode &&
        request.status === 'awaitingApproval'), 'A\'s request to B');
    assert.equal(asked.find(request => request.nodeId === bNode).intent, INTENT.TwoWay);
    const aLink = await linkWith(a, bNode);
    assert.deepEqual([aLink.mode, aLink.state, aLink.initiator], [MODE.TwoWay, STATE.AwaitingAccess, INITIATOR.ThisDevice]);
    assert.equal((await ds(a, '/overview')).sharingEnabled, true, 'Keeping in step both ways did not turn A\'s sharing on');
    // What A filed shows on B's node, waiting, with a line that waits too.
    await syncSection.getByTestId('data-sync-outgoing-card').waitFor();
    await until(async () => (await lineOf()).getAttribute('data-in', { timeout: 1000 }).catch(() => null),
      value => value === 'pending', 'the map to draw A\'s request to B', 20000);
    report.startedFromTheMap = { intent: 'twoWay', sharingTurnedOn: true };

    // ---- (4) B as a browser on another device sees it ---------------------------------------
    // B lets any LAN browser manage it (Unrestricted): the page opens, but nothing on it creates
    // or widens access — no sharing switch, no code, no Approve — and the server refuses it too.
    const settingsOf = host => envelope(host.base + '/remote-access/settings');
    const bRemote = await settingsOf(b);
    assert.equal(bRemote.mode, REMOTE_ACCESS.Enabled);
    /**
     * Sets B's remote access, and waits until B keeps answering with it: each write is reloaded
     * from B's options file a moment later, and the reload of the first may briefly undo the
     * second.
     */
    const remoteAccessOfB = async (mode, requirePairing) => {
      await call(b.base + '/remote-access/mode', { method: 'PUT', data: { mode } });
      await call(b.base + '/remote-access/require-pairing', { method: 'PUT', data: { require: requirePairing } });
      const holds = async () => {
        const settings = await settingsOf(b);
        return settings.mode === mode && settings.requirePairing === requirePairing;
      };
      await until(async () => await holds() && await new Promise(resolve => setTimeout(resolve, 1000)).then(holds),
        taken => taken, 'B\'s remote access to take', 15000);
    };
    restoreRemoteAccess = () => remoteAccessOfB(bRemote.mode, bRemote.requirePairing);
    await remoteAccessOfB(REMOTE_ACCESS.Unrestricted, bRemote.requirePairing);
    lanContext = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'en-US' });
    lanBlocked = await confine(lanContext, url => url.origin === b.lan);
    const lan = await lanContext.newPage();
    lan.on('pageerror', error => pageErrors.push(`${lan.url()}: ${error.message}`));
    await lan.goto(b.lan + '/#/data-sync');
    await lan.getByTestId('data-sync-page').waitFor();
    const lanWho = await lan.evaluate(() => fetch('/remote-access/context').then(response => response.json()));
    assert.deepEqual([lanWho.data.isLocal, lanWho.data.mode], [false, REMOTE_ACCESS.Unrestricted],
      'B did not take its LAN-caller port for a browser on another device');
    const lanRequests = lan.getByTestId('data-sync-requests');
    const lanCard = lanRequests.getByTestId('data-sync-request-card');
    await lanCard.waitFor();
    assert.match(await lanCard.innerText(), containing('dataSync.request.twoWay', { name: aName }));
    await lanCard.getByTestId('data-sync-request-reject').waitFor();
    assert.equal(await lan.getByTestId('data-sync-request-approve').count(), 0, 'A LAN browser was offered [Approve]');
    assert.equal(await lan.getByTestId('data-sync-request-options').count(), 0);
    assert.equal(await lan.getByTestId('data-sync-sharing-switch').count(), 0, 'A LAN browser was offered the sharing switch');
    assert.equal(await lan.getByTestId('data-sync-create-code').count(), 0, 'A LAN browser was offered [Create a code]');
    assert.equal((await lan.evaluate(() => fetch('/data-sync/overview').then(response => response.json()))).data.canManageSharing,
      false);
    const invitation = await lan.evaluate(() => fetch('/data-sync/invitations', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ allowTwoWay: false })
    }).then(response => response.json()));
    assert.equal(invitation.data?.problem?.code, PROBLEM.NotAllowedOnThisDevice,
      `A LAN browser created a code: ${JSON.stringify(invitation.data?.problem)}`);
    assert.equal(invitation.data?.invitation ?? null, null);
    await lan.screenshot({ path: artifacts('data-sync-lan-unrestricted.png'), fullPage: true });
    await lan.close();
    // Outside Unrestricted mode (and not asking to be paired, so the page opens at all) such a
    // browser is told where to go, and the page asks the server nothing.
    await remoteAccessOfB(REMOTE_ACCESS.Enabled, false);
    const outside = await lanContext.newPage();
    const asking = [];
    outside.on('request', request => {
      if (new URL(request.url()).pathname.startsWith('/data-sync')) asking.push(new URL(request.url()).pathname);
    });
    // The window around the page (options, app info, updates, notifications) is refused here as
    // host-only, and says so with errors of its own; nothing of data sync may be among them.
    const refusedAround = [];
    outside.on('response', response => {
      if (response.status() >= 400)
        refusedAround.push(`${response.status()} ${new URL(response.url()).pathname} ${response.headers()['x-bakabase-remote-access'] ?? ''}`.trim());
    });
    await outside.goto(b.lan + '/#/data-sync');
    await outside.getByTestId('data-sync-not-available').waitFor();
    assert.match(await outside.getByTestId('data-sync-not-available').innerText(), containing('dataSync.notAvailable'));
    // Long enough for the layout (the status indicator included) to have asked, if it would.
    await outside.waitForLoadState('networkidle');
    assert.deepEqual(asking, [], 'A LAN browser outside Unrestricted mode asked data sync');
    assert.deepEqual(refusedAround.filter(entry => !/^403 \S+ HostOnly$/.test(entry) || entry.includes('/data-sync')), [],
      'A LAN browser outside Unrestricted mode was refused something else');
    await outside.screenshot({ path: artifacts('data-sync-lan-not-available.png') });
    await outside.close();
    await restoreRemoteAccess();
    restoreRemoteAccess = undefined;
    await assertStayedLocal(lanContext, lanBlocked, 'Data sync (LAN browser)');
    report.lanBrowser = {
      unrestrictedMayNotCreateAccess: true, invitationRefused: 'NotAllowedOnThisDevice', otherwiseNotAvailable: true,
      hostOnlyAroundThePageOutsideUnrestricted: refusedAround,
    };

    // ---- (3) In A's window switched to B, B's own page approves -----------------------------
    const relay = await context.newPage();
    await relay.goto(home + '/#/data-sync');
    await relay.getByTestId('data-sync-page').waitFor();
    // A's page shows the device it asked, waiting for it.
    await relay.locator(`[data-testid="data-sync-peer"][data-sync-peer="${bNode}"]`).waitFor();
    const opened = await relay.evaluate(async ([id, route]) => {
      const response = await fetch(`/federation/local/servers/${encodeURIComponent(id)}/open`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path: route })
      });
      return { status: response.status, body: await response.json() };
    }, [bServer.serverId, '/#/data-sync?tab=requests']);
    assert.equal(opened.status, 200, `Opening B's data sync from A's window: HTTP ${opened.status}`);
    const approveMark = requestsTo(b).length;
    await relay.evaluate(url => location.assign(url), opened.body.url);
    await relay.waitForURL(url => relayPort(url.href) !== null && !url.href.includes(TICKET) && url.hash.startsWith('#/data-sync'));
    const relayOrigin = new URL(relay.url()).origin;
    await relay.getByTestId('data-sync-page').waitFor();
    // B's own page: B is this device there, and A's request waits in its Requests.
    const relayRequest = relay.getByTestId('data-sync-requests').getByTestId('data-sync-request-card');
    await relayRequest.waitFor();
    assert.match(await relayRequest.innerText(), containing('dataSync.request.twoWay', { name: aName }));
    assert.equal(await relayRequest.getByTestId('data-sync-request-receive-back').isChecked(), true,
      'Receiving back is not offered checked for a request to keep in step both ways');
    await relayRequest.getByTestId('data-sync-request-approve').click();
    const approveDialog = relay.getByRole('alertdialog');
    await approveDialog.getByRole('button', { name: exactly('federation.confirm') }).click();
    await approveDialog.waitFor({ state: 'hidden' });
    const bLink = await until(() => linkWith(b, aNode), link => !!link && link.peerMayReadUs,
      'B to read A back once it approved');
    assert.deepEqual([bLink.mode, bLink.initiator], [MODE.TwoWay, INITIATOR.Peer]);
    // B's page shows the device it now keeps in step with.
    await relay.locator(`[data-testid="data-sync-peer"][data-sync-peer="${aNode}"]`).waitFor();
    // The approval reached B as the device A manages it with — "a paired caller may".
    const approval = requestsTo(b).slice(approveMark)
      .find(entry => entry.method === 'POST' && /^\/data-sync\/requests\/[^/]+\/approve$/.test(entry.path));
    assert.ok(approval, 'The approval did not reach B through the relay');
    assert.equal(approval.signature, 'Authenticated', 'The approval reached B unsigned');
    assert.ok((await settingsOf(b)).devices.some(device => device.id === approval.device),
      'The approval was signed by a device B does not list');
    await relay.screenshot({ path: artifacts('data-sync-relay-approved.png'), fullPage: true });
    report.approvedInTheWindowSwitchedToB = { relayPort: relayPort(relayOrigin), signedBy: 'managed device' };

    // ---- A reviews its first sync with B ------------------------------------------------------
    const aReview = await until(() => linkWith(a, bNode), link => link.state === STATE.AwaitingReview && !!link.reviewId,
      'A\'s first review of B');
    const page = await context.newPage();
    await page.goto(`${home}/#/data-sync?link=${aReview.id}&review=1`);
    const review = page.getByTestId('data-sync-review');
    await review.getByTestId('data-sync-review-two-way').waitFor();
    await review.getByTestId('data-sync-plan-item').first().waitFor();
    const apply = review.getByTestId('data-sync-review-apply');
    assert.match(await apply.innerText(), exactly('dataSync.review.apply', { count: 1 }));
    await apply.click();
    await review.getByTestId('data-sync-review-done').waitFor({ timeout: 30000 });
    assert.match(await review.getByTestId('data-sync-review-done').innerText(), containing('dataSync.review.inStep', { name: bName }));
    await review.getByRole('button', { name: exactly('dataSync.done') }).click();
    assert.deepEqual((await properties(a)).map(property => property.name), ['Genre'], 'A\'s review did not create B\'s definition');
    await syncUntil(b, aNode, link => link.state === STATE.Active && !!link.lastSyncedAt, 'B in step with A');
    await syncUntil(a, bNode, link => link.state === STATE.Active && !!link.lastSyncedAt, 'A in step with B');
    report.firstReviewApplied = { created: 1 };

    // ---- both rename it, differently, while both are paused ---------------------------------
    for (const host of [a, b]) await act(host, '/paused', { method: 'PUT', data: { paused: true } });
    const renames = [[a, 'Genres'], [b, 'Style']];
    for (const [host, name] of renames) {
      const [genre] = await properties(host);
      await envelope(`${host.base}/custom-property/${genre.id}`, { method: 'PUT', data: { name, type: SINGLE_LINE_TEXT } });
    }
    for (const host of [a, b]) await act(host, '/paused', { method: 'PUT', data: { paused: false } });
    await syncUntil(b, aNode, items => items.some(item => item.type === ITEM.FieldConflict), 'B\'s conflict',
      () => openItems(b));
    await syncUntil(a, bNode, link => link.openItems === 1 && link.peerAttention?.openDecisions === 1,
      'A\'s conflict, with B\'s own waiting there');

    // ---- (1) A's map: the sync line both ways, its state and B's attention --------------------
    await map.reload();
    await map.getByRole('heading', { name: exactly('federation.map.title') }).waitFor();
    await bCard.waitFor();
    const syncEdge = await lineOf();
    await syncEdge.waitFor();
    assert.deepEqual([await syncEdge.getAttribute('data-in'), await syncEdge.getAttribute('data-out')], ['active', 'active'],
      'The map does not draw the sync line both ways');
    assert.equal(await syncEdge.getAttribute('data-kind'), 'sync');
    await syncEdge.focus();
    await map.keyboard.press('Enter');
    await until(() => focused(map), active => active.id === 'device-map-panel-title',
      'the keyboard in the details of the sync line', 15000);
    const status = syncSection.getByTestId('data-sync-status');
    await status.getByText(containing('dataSync.status.NeedsYou', { count: 1 })).first().waitFor();
    await status.getByText(containing('dataSync.status.NeedsYouThere', { name: bName, count: 1 })).first().waitFor();
    assert.equal(await syncSection.getByTestId('data-sync-arrow-receive').getAttribute('aria-pressed'), 'true');
    assert.equal(await syncSection.getByTestId('data-sync-arrow-read').getAttribute('aria-pressed'), 'true');
    await map.screenshot({ path: artifacts('data-sync-map.png'), fullPage: true });
    report.mapLine = { in: 'active', out: 'active', needsYou: 1, waitsThere: 1 };

    // ---- (2) A's /data-sync: the diagram shows B; the conflict is decided there -------------
    await page.goto(home + '/#/data-sync');
    const peerCard = page.locator(`[data-testid="data-sync-peer"][data-sync-peer="${bNode}"]`);
    await peerCard.waitFor();
    await peerCard.getByTestId('data-sync-open-items').waitFor();
    const inbox = page.getByTestId('data-sync-inbox');
    const conflict = inbox.locator('[data-testid="data-sync-inbox-card"][data-type="FieldConflict"]');
    await conflict.waitFor();
    assert.equal(await conflict.count(), 1);
    await conflict.getByRole('radio', { name: exactly('dataSync.inbox.action.KeepLocal') }).check();
    await conflict.getByTestId('data-sync-inbox-apply').click();
    await until(() => openItems(a), items => items.length === 0, 'A\'s conflict to close once decided');
    await conflict.waitFor({ state: 'detached', timeout: 30000 });
    const decided = (await ds(a, '/inbox?openOnly=false&take=100')).items.find(item => item.type === ITEM.FieldConflict);
    assert.equal(decided.action, ACTION.KeepLocal, 'A\'s conflict closed some other way');
    // B takes the decision and closes its own item, saying where it was decided.
    const closedOnB = await syncUntil(b, aNode,
      items => items.some(item => item.type === ITEM.FieldConflict && item.closedAt), 'B\'s conflict to close',
      async () => (await ds(b, '/inbox?openOnly=false&take=100')).items);
    const bItem = closedOnB.find(item => item.type === ITEM.FieldConflict);
    assert.deepEqual([bItem.closure, bItem.closedByName], [CLOSURE.ResolvedElsewhere, aName]);
    assert.deepEqual((await properties(b)).map(property => property.name), ['Genres']);
    await syncUntil(a, bNode, link => link.openItems === 0 && link.peerAttention?.openDecisions === 0,
      'A to see B has nothing waiting any more');
    await page.screenshot({ path: artifacts('data-sync-page.png'), fullPage: true });
    report.conflictDecidedOnThePage = { keptOnA: 'Genres', closedOnB: 'ResolvedElsewhere' };

    // ---- (1) the rule editor from the keyboard, at 1440 and 1280 px --------------------------
    // Pressing an arrow disables it while its action runs, and Chromium moves focus off a
    // disabled button to the page's body at once: the details must take it back.
    await map.reload();
    await bCard.waitFor();
    const line = await lineOf();
    await line.waitFor();
    const toggles = {};
    for (const width of [1440, 1280]) {
      await map.setViewportSize({ width, height: 1000 });
      await line.focus();
      await map.keyboard.press('Enter');
      await until(() => focused(map), active => active.id === 'device-map-panel-title', `the details at ${width} px`, 15000);
      const arrow = syncSection.getByTestId('data-sync-arrow-receive');
      // Off: asked first.
      await arrow.focus();
      await map.keyboard.press('Enter');
      const offDialog = map.getByRole('alertdialog');
      await offDialog.waitFor();
      assert.match(await offDialog.innerText(), containing('dataSync.off.title', { name: bName }));
      await map.keyboard.press('Enter');
      await until(() => linkWith(a, bNode), link => link.mode === MODE.Off, `A to stop receiving from B at ${width} px`);
      const afterOff = await until(() => focused(map), active => active.inDetails && !active.body,
        `the keyboard in the details once receiving stopped at ${width} px`, 15000);
      await until(() => arrow.getAttribute('aria-pressed'), value => value === 'false', 'the arrow to show receiving off');
      // On again: back to the link's last mode, at once.
      await arrow.focus();
      await map.keyboard.press('Enter');
      await until(() => linkWith(a, bNode), link => link.mode === MODE.TwoWay, `A to receive from B again at ${width} px`);
      const afterOn = await until(() => focused(map), active => active.inDetails && !active.body,
        `the keyboard in the details once receiving resumed at ${width} px`, 15000);
      await until(() => arrow.getAttribute('aria-pressed'), value => value === 'true', 'the arrow to show receiving on');
      await map.screenshot({ path: artifacts(`data-sync-map-${width}.png`), fullPage: true });
      toggles[width] = { afterOff, afterOn };
      // Escape closes the details again, for the next width to open them afresh.
      await map.keyboard.press('Escape');
    }
    await map.setViewportSize({ width: 1440, height: 1000 });
    await syncUntil(a, bNode, link => link.state === STATE.Active && link.openItems === 0, 'A in step with B again');
    report.ruleEditorFromTheKeyboard = toggles;

    // ---- (3) B's own page, in the switched window: its links and its history ----------------
    await relay.goto(relayOrigin + '/#/data-sync');
    await relay.locator(`[data-testid="data-sync-peer"][data-sync-peer="${aNode}"]`).waitFor();
    const history = relay.getByTestId('data-sync-history');
    await history.getByTestId('data-sync-history-entry').first().waitFor();
    assert.match(await history.innerText(), new RegExp(escape(aName)), 'B\'s history does not name A');
    assert.ok((await ds(b, '/history')).length > 0, 'B has no history');
    await relay.screenshot({ path: artifacts('data-sync-relay-history.png'), fullPage: true });
    report.switchedWindowShowsBsLinksAndHistory = true;

    // ---- the page at 375 px -----------------------------------------------------------------
    // The window's own navigation takes most of a phone's width until it is folded, as someone on
    // one would fold it; the page is judged in what is left.
    const uiOptions = await envelope(a.base + '/options/ui');
    await call(a.base + '/options/ui', { method: 'PATCH', data: { isMenuCollapsed: true } });
    restoreMenu = () => call(a.base + '/options/ui', { method: 'PATCH', data: { isMenuCollapsed: !!uiOptions.isMenuCollapsed } });
    const narrow = await context.newPage();
    await narrow.setViewportSize({ width: 375, height: 812 });
    await narrow.goto(home + '/#/data-sync');
    const narrowDiagram = narrow.getByTestId('data-sync-diagram');
    await until(() => narrowDiagram.getAttribute('data-mode'), mode => mode === 'list', 'the diagram listed at 375 px', 10000);
    await narrow.locator(`[data-testid="data-sync-peer-row"][data-sync-peer="${bNode}"]`).click();
    await narrow.getByTestId('data-sync-link-details').waitFor();
    const at375 = await until(() => narrow.evaluate(() => {
      const layout = document.querySelector('[data-testid="data-sync-layout"]');
      const region = document.querySelector('[data-testid="data-sync-diagram-region"]').getBoundingClientRect();
      const details = document.querySelector('[data-testid="data-sync-details"]').getBoundingClientRect();
      const drawing = document.querySelector('[data-testid="data-sync-details"] [data-testid="data-sync-rule-drawing"]');
      return {
        layout: layout.getAttribute('data-layout'),
        detailsUnderTheDiagram: details.top >= region.bottom - 1,
        overlap: region.right > details.left && region.left < details.right && region.bottom > details.top + 1 &&
          region.top < details.bottom,
        detailsWithinWidth: details.right <= innerWidth + 1 && details.left >= -1,
        drawing: drawing?.getAttribute('data-orientation') ?? null,
        sideways: document.documentElement.scrollWidth > innerWidth + 1,
      };
    }), value => value.layout === 'stacked' && value.detailsUnderTheDiagram && !value.overlap &&
      value.detailsWithinWidth && value.drawing === 'vertical' && !value.sideways,
    'the details under the diagram at 375 px, nothing sideways', 10000);
    await narrow.screenshot({ path: artifacts('data-sync-375.png'), fullPage: true });
    report.narrow = { width: 375, ...at375 };
    await narrow.close();
    await restoreMenu();
    restoreMenu = undefined;

    // The status after every change, and what the first review wrote, reached the open window.
    assert.ok(hubPushes.DataSyncStatus > 0, 'A\'s hub pushed no DataSyncStatus to its window');
    assert.ok(hubPushes.DataSyncApplied > 0, 'A\'s hub pushed no DataSyncApplied to its window');
    report.hubPushes = hubPushes;

    assert.deepEqual(pageErrors, []);
    await assertStayedLocal(context, blocked, 'Data sync');
    report.pageErrors = pageErrors;
    report.blockedRequests = { window: blocked, lanBrowser: lanBlocked };
    report.passed = true;
    return report;
  } finally {
    if (restoreRemoteAccess) await restoreRemoteAccess().catch(error => console.error('Data sync: B\'s remote access', error));
    if (restoreMenu) await restoreMenu().catch(error => console.error('Data sync: A\'s navigation', error));
    if (blocked.external.length) console.error('Data sync: refused requests off this machine', blocked.external);
    await lanContext?.close();
    await context.close();
  }
};
