// Drives production web assets and real ClientStartup/Service listeners; no mocked API responses.
const { chromium } = require('playwright');
const serverSwitching = require('./switching.cjs');
const firstLaunchImport = require('./first-launch.cjs');
const { LAUNCH_ARGS, confine, proveConfinement, assertStayedLocal, assertNoAnalytics } = require('./network.cjs');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const config = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const { unified, source, client } = config.hosts;
const uiBase = unified.base.replace('127.0.0.1', 'localhost');
const locales = ['en', 'cn'].map(lang => JSON.parse(fs.readFileSync(path.join(config.repo, `src/web/src/locales/${lang}/pages/federation.json`), 'utf8')));
const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const name = key => new RegExp(`^(?:${locales.map(locale => escape(locale[key])).join('|')})$`);
// For controls whose accessible name continues with an explanation after the label.
const namePrefix = key => new RegExp(`^(?:${locales.map(locale => escape(locale[key])).join('|')})`);
const hintKey = 'federation.connection-hints.v1';
const artifacts = file => path.join(config.results, file);
const hasFiles = (directory, predicate) => fs.readdirSync(directory, { withFileTypes: true }).some(entry =>
  entry.isDirectory() ? hasFiles(path.join(directory, entry.name), predicate) : predicate(path.join(directory, entry.name)));

// Legacy client export/import, federated browsing and identity recovery.
async function legacyMigrationAndFederation(browser) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'en-US', acceptDownloads: true });
  const sessions = new Set();
  try {
    const origins = new Set(Object.values(config.hosts).flatMap(host => [host.base, host.base.replace('127.0.0.1', 'localhost')]));
    const blocked = await confine(context, url => origins.has(url.origin));
    const api = async (base, endpoint, method = 'get', data) => {
      const response = await context.request[method](base + endpoint, { data });
      assert.ok(response.ok(), `${method.toUpperCase()} ${endpoint}: HTTP ${response.status()}`);
      return response.status() === 204 ? undefined : response.json();
    };
    const legacy = async (...args) => {
      const envelope = await api(...args);
      assert.equal(envelope.code, 0, 'Legacy endpoint returned a failure');
      return envelope.data;
    };
    const status = () => api(unified.base, '/federation/local/peers');
    const before = await status();
    assert.equal(before.browsingEnabled, false);
    assert.equal(before.peers.length, 0);
    const sourceStatus = await api(source.base, '/federation/local/peers');
    assert.notEqual(sourceStatus.identity.nodeId, before.identity.nodeId);
    const errors = [];
    context.on('page', page => page.on('pageerror', error => errors.push(error.message)));

    // Old client genuinely acquires its own legacy credentials through its shipped connect page.
    const oldPage = await context.newPage();
    const adminInvite = await legacy(source.base, '/remote-access/pairing/code', 'post');
    await oldPage.goto(client.base + '/client/connect-page');
    await oldPage.locator('#address').fill(source.base);
    await oldPage.locator('#address-form button[type=submit]').click();
    await oldPage.locator('#pairing').waitFor({ state: 'visible' });
    await oldPage.locator('#code').fill(adminInvite.code);
    await oldPage.locator('#code-form button[type=submit]').click();
    await oldPage.waitForURL(url => url.origin === client.base && url.pathname === '/');
    const oldStatus = await legacy(client.base, '/client/status');
    assert.ok(oldStatus.activeServerId && oldStatus.serverReachable);
    await legacy(client.base, `/client/servers/${encodeURIComponent(oldStatus.activeServerId)}/path-mappings`, 'put',
      { mappings: [{ serverPath: '/legacy/media', localPath: '/legacy/local-media' }] });
    const connectionFile = path.join(client.directory, 'client', 'connection.json');
    const oldConnectionBytes = fs.readFileSync(connectionFile);
    const oldConnection = JSON.parse(oldConnectionBytes.toString());
    const oldKeys = oldConnection.Servers.map(server => server.DeviceKey);
    assert.ok(oldKeys.every(key => key.length > 10));

    // Download from the real legacy-client UI, not a synthesized hint JSON.
    await oldPage.goto(client.base + '/#/other-devices');
    const downloadEvent = oldPage.waitForEvent('download');
    await oldPage.getByRole('button', { name: name('federation.migration.export'), exact: true }).click();
    const download = await downloadEvent;
    const hintFile = artifacts('exported-connection-hints.json');
    await download.saveAs(hintFile);
    const hintText = fs.readFileSync(hintFile, 'utf8');
    const hints = JSON.parse(hintText);
    assert.deepEqual(Object.keys(hints).sort(), ['format', 'servers', 'version']);
    assert.equal(hints.format, 'bakabase-client-connection-hints');
    assert.equal(hints.version, 1);
    assert.equal(hints.servers.length, 1);
    assert.equal(hints.servers[0].address, source.base);
    assert.deepEqual(Object.keys(hints.servers[0]).sort(), ['address', 'name', 'pathMappings']);
    assert.deepEqual(hints.servers[0].pathMappings, [{ serverPath: '/legacy/media', localPath: '/legacy/local-media' }]);
    for (const key of oldKeys) assert.ok(!hintText.includes(key));
    assert.ok(!/deviceKey|deviceId|serverId|sourceRootId|updateFeed/i.test(hintText));
    await oldPage.evaluate(() => localStorage.setItem('migration-origin-sentinel', 'legacy-only'));
    await oldPage.getByRole('heading', { name: name('federation.migration.fullDesktop') }).locator('..').screenshot({ path: artifacts('legacy-export.png') });

    const devices = await context.newPage();
    await devices.goto(uiBase + '/#/federation/devices');
    await devices.getByRole('heading', { name: name('federation.devices.title'), exact: true }).waitFor();
    assert.equal(await devices.evaluate(() => localStorage.getItem('migration-origin-sentinel')), null);
    const mutationRequests = [];
    devices.on('request', request => {
      if (request.method() !== 'GET' && /federation\/local\/peers.*(connect|path-mappings)/.test(request.url())) mutationRequests.push(request.url());
    });
    await devices.getByLabel(name('federation.migration.chooseFile')).setInputFiles(hintFile);
    await devices.getByText(name('federation.migration.draftSaved'), { exact: true }).waitFor();
    await devices.getByLabel(name('federation.migration.chooseFile')).setInputFiles(hintFile);
    await devices.reload();
    await devices.getByText(name('federation.migration.draftSaved'), { exact: true }).waitFor();
    const candidate = devices.locator('article').filter({ has: devices.getByRole('button', { name: name('federation.discovery.use'), exact: true }) });
    assert.equal(await candidate.count(), 1);
    assert.deepEqual(mutationRequests, [], 'Importing hints must not pair or bind paths');
    const restored = await devices.evaluate(key => localStorage.getItem(key), hintKey);
    assert.equal(JSON.parse(restored).servers.length, 1);
    for (const key of oldKeys) assert.ok(!restored.includes(key));
    assert.equal((await status()).peers.length, 0);
    await devices.getByRole('heading', { name: name('federation.migration.import') }).locator('..').screenshot({ path: artifacts('unified-import-preview.png') });

    // Import only chooses an address. New node access needs an independent approval from its owner.
    await candidate.getByRole('button', { name: name('federation.discovery.use'), exact: true }).click();
    // The sharing form's own field: the desktop app's devices page also has "Device address"
    // for adding a server to manage, which importing hints must leave empty.
    const sharingForm = devices.locator('form').filter({ has: devices.getByRole('checkbox', { name: namePrefix('federation.pair.shareBack') }) });
    assert.equal(await sharingForm.getByLabel(name('federation.pair.address'), { exact: true }).inputValue(), source.base);
    assert.equal(await devices.locator('#managed-servers').getByLabel(name('federation.servers.add.address'), { exact: true }).inputValue(), '');
    assert.equal(await devices.getByLabel(name('federation.pair.code'), { exact: true }).inputValue(), '');
    // Migration only restores reading the old server; sharing this library back is a separate choice.
    await devices.getByRole('checkbox', { name: namePrefix('federation.pair.shareBack') }).uncheck();
    const connectResponse = devices.waitForResponse(response => response.url() === uiBase + '/federation/local/peers/connect');
    await devices.getByRole('button', { name: name('federation.pair.request'), exact: true }).click();
    assert.equal((await (await connectResponse).json()).outcome, 'awaitingApproval');
    assert.ok(!(await status()).peers.some(peer => peer.outboundGrant));
    const owner = await context.newPage();
    await owner.goto(source.base + '/#/federation/devices');
    await owner.getByRole('button', { name: name('federation.requests.approve'), exact: true }).click();
    await owner.getByRole('alertdialog').getByRole('button', { name: name('federation.confirm'), exact: true }).click();
    await devices.getByText(name('federation.pair.granted'), { exact: true }).waitFor({ timeout: 20000 });
    const paired = await status();
    const peer = paired.peers.find(peer => peer.nodeId === sourceStatus.identity.nodeId);
    assert.ok(peer.outboundGrant);
    assert.ok(!peer.inboundGrant, 'Reading a source must not authorize that source to read this library');
    assert.deepEqual(peer.pathMappings, [], 'Legacy prefixes must not become sourceRootId mappings');
    assert.deepEqual(paired.identity, before.identity, 'Import/pairing must not replace local library identity');
    assert.equal(paired.browsingEnabled, true, 'Gaining read access to another device turns browsing on');
    assert.ok(fs.readFileSync(connectionFile).equals(oldConnectionBytes), 'Migration must not change the old installation');
    assert.equal((await legacy(client.base, '/client/status')).serverReachable, true);
    assert.equal(await oldPage.evaluate(() => localStorage.getItem('migration-origin-sentinel')), 'legacy-only');
    assert.equal(await oldPage.evaluate(key => localStorage.getItem(key), hintKey), null);
    assert.ok(!hasFiles(client.directory, file => /\.(db|sqlite|sqlite3)$/i.test(file)), 'Thin client acquired a library database');
    assert.ok(hasFiles(unified.directory, file => /\.db$/i.test(file)), 'Unified fixture has no authoritative database');
    assert.ok(!fs.existsSync(path.join(unified.directory, 'client', 'connection.json')));
    hasFiles(unified.directory, file => {
      if (file.endsWith('.json')) for (const key of oldKeys) assert.ok(!fs.readFileSync(file, 'utf8').includes(key), 'Legacy key entered unified state');
      return false;
    });

    // Unified library: on after pairing, complete identities, local media URLs, and disable from another window.
    const library = await context.newPage();
    library.on('response', async response => {
      if (response.url() === uiBase + '/federation/local/queries' && response.ok()) {
        const page = await response.json();
        if (page.sessionId) sessions.add(page.sessionId);
      }
    });
    const queryResponse = library.waitForResponse(response => response.url() === uiBase + '/federation/local/queries' && response.request().method() === 'POST');
    await library.goto(uiBase + '/#/federation?scope=all');
    const query = await (await queryResponse).json();
    assert.equal(query.participants.length, 2);
    assert.equal(query.totalWithinParticipants, 114);
    assert.equal(query.coverageComplete, true);
    const cards = library.locator('button').filter({ has: library.locator('h2') });
    const remoteIndex = query.items.findIndex(item => item.ref.nodeId === peer.nodeId && item.ref.resourceId === 1);
    assert.ok(remoteIndex >= 0);
    const detailResponse = library.waitForResponse(response => response.url() === uiBase + '/federation/local/resources/resolve');
    await cards.nth(remoteIndex).click();
    const detail = (await (await detailResponse).json()).resources[0];
    assert.deepEqual(detail.ref, query.items[remoteIndex].ref);
    const pane = library.getByRole('region', { name: name('federation.detail.title') });
    assert.equal(await pane.getByRole('link', { name: name('federation.manageLocal') }).count(), 0);
    assert.equal(await pane.getByRole('button', { name: name('federation.directory.open') }).isDisabled(), true);
    assert.ok(['PathMappingRequired', 'OpenDirectoryUnavailable'].includes(detail.directoryAccess.reason));
    const playbackResponse = library.waitForResponse(response => response.url() === uiBase + '/federation/local/playback-sessions');
    await pane.getByRole('button', { name: name('federation.preview'), exact: true }).first().click();
    const playback = await (await playbackResponse).json();
    assert.equal(new URL(playback.url, uiBase).origin, uiBase);
    await library.waitForFunction(() => document.querySelector('audio')?.readyState >= 1);
    await pane.screenshot({ path: artifacts('localhost-remote-detail.png') });
    const beforeDisable = await status();
    await devices.getByRole('button', { name: name('federation.browsing.disable'), exact: true }).click();
    await library.getByRole('heading', { name: name('federation.browsing.off'), exact: true }).waitFor();
    assert.equal(await library.locator('audio').count(), 0);
    assert.equal(await cards.count(), 0);
    const afterDisable = await status();
    assert.deepEqual(afterDisable.peers, beforeDisable.peers);
    assert.equal(afterDisable.sharingEnabled, beforeDisable.sharingEnabled);
    assert.equal(afterDisable.browsingEnabled, false);
    await devices.getByRole('heading', { name: name('federation.devices.title'), exact: true }).waitFor();
    await devices.getByRole('button', { name: name('federation.migration.clearDraft'), exact: true }).click();
    assert.equal(await devices.evaluate(key => localStorage.getItem(key), hintKey), null);
    // Recovery is explicit: restoring keeps this node, while cloning replaces it.
    await devices.goto(uiBase + '/#/federation/devices?section=identity');
    await devices.locator('#federation-identity[open]').waitFor();
    const beforeRestore = await status();
    const resetThroughUi = async (key, asNewNode) => {
      await devices.getByRole('button', { name: name(key), exact: true }).click();
      const responsePromise = devices.waitForResponse(response => response.url() === uiBase + '/federation/local/peers/identity/reset');
      await devices.getByRole('alertdialog').getByRole('button', { name: name('federation.confirm'), exact: true }).click();
      const response = await responsePromise;
      assert.equal(response.status(), 200);
      assert.deepEqual(response.request().postDataJSON(), { asNewNode });
      await devices.getByRole('alertdialog').waitFor({ state: 'hidden' });
      return status();
    };
    const recovered = await resetThroughUi('federation.identity.restore', false);
    assert.equal(recovered.identity.nodeId, beforeRestore.identity.nodeId);
    assert.notEqual(recovered.identity.libraryEpoch, beforeRestore.identity.libraryEpoch);
    assert.deepEqual(recovered.peers, beforeRestore.peers);
    assert.equal(recovered.sharingEnabled, false);
    assert.equal(recovered.browsingEnabled, false);
    const cloned = await resetThroughUi('federation.identity.reset', true);
    assert.notEqual(cloned.identity.nodeId, recovered.identity.nodeId);
    assert.notEqual(cloned.identity.libraryEpoch, recovered.identity.libraryEpoch);
    assert.deepEqual(cloned.peers, []);
    assert.deepEqual(cloned.requests, []);
    assert.equal(cloned.sharingEnabled, false);
    assert.equal(cloned.browsingEnabled, false);
    assert.ok(fs.readFileSync(connectionFile).equals(oldConnectionBytes), 'The old connection file changed');
    assert.deepEqual(errors, []);
    await assertStayedLocal(context, blocked, 'Legacy migration and federation');
    const report = {
      passed: true, fixtureScope: 'Production ClientStartup and Service HTTP pipelines; not an installed native package',
      legacyUiPairAndDownload: true, safeHintExport: true, draftRestoreAndIdempotency: true,
      noAuthorizationOrMappingOnImport: true, explicitNewNodeApproval: true, noReverseGrant: true,
      independentOriginsAndData: true, oldClientStillConnectedAndUnchanged: true,
      browsingOnAfterPairing: true, participants: query.participants.length, total: query.totalWithinParticipants,
      unmappedDirectoryDisabled: true, localhostAudioMetadataReady: true,
      crossTabDisableClearsResultsAndMedia: true, peersAndSharingPreserved: true,
      explicitRestorePreservesNodeAndOutbound: true, explicitCloneCreatesFreshNode: true, pageErrors: errors,
      blockedRequests: blocked
    };
    return report;
  } finally {
    for (const session of sessions) await context.request.delete(unified.base + '/federation/local/queries/' + encodeURIComponent(session)).catch(() => {});
    await context.close();
  }
}

(async () => {
  // Before any page loads: what each Service's frontend is told about analytics.
  for (const host of [unified, source]) await assertNoAnalytics(host.base);
  const browser = await chromium.launch({ headless: true, args: LAUNCH_ARGS });
  try {
    // And that a tracker arriving some other way would be stopped and named.
    const refusedCanary = await proveConfinement(browser, unified.base);
    const report = await legacyMigrationAndFederation(browser);
    report.networkConfinementProven = refusedCanary;
    // Both need the thin client's pairing made above; see first-launch.cjs and switching.cjs.
    report.firstLaunchImport = await firstLaunchImport({ config });
    report.serverSwitching = await serverSwitching({ browser, config, artifacts });
    fs.writeFileSync(artifacts('result.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify(report, null, 2));
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
