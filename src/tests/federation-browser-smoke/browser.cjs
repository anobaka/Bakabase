// Drives production web assets and real Service listeners; no mocked API responses.
const { chromium } = require('playwright');
const serverSwitching = require('./switching.cjs');
const firstLaunchImport = require('./first-launch.cjs');
const legacyClientPairing = require('./legacy-client.cjs');
const { LAUNCH_ARGS, confine, proveConfinement, assertStayedLocal, assertNoAnalytics } = require('./network.cjs');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const config = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const { unified, source } = config.hosts;
const uiBase = unified.base.replace('127.0.0.1', 'localhost');
const locales = ['en', 'cn'].map(lang => JSON.parse(fs.readFileSync(path.join(config.repo, `src/web/src/locales/${lang}/pages/federation.json`), 'utf8')));
const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const name = key => new RegExp(`^(?:${locales.map(locale => escape(locale[key])).join('|')})$`);
// For controls whose accessible name continues with an explanation after the label.
const namePrefix = key => new RegExp(`^(?:${locales.map(locale => escape(locale[key])).join('|')})`);
const artifacts = file => path.join(config.results, file);
const hasFiles = (directory, predicate) => fs.readdirSync(directory, { withFileTypes: true }).some(entry =>
  entry.isDirectory() ? hasFiles(path.join(directory, entry.name), predicate) : predicate(path.join(directory, entry.name)));

// Read-only pairing with approval, federated browsing and identity recovery.
async function federation(browser) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'en-US' });
  const sessions = new Set();
  try {
    const origins = new Set(Object.values(config.hosts).flatMap(host => [host.base, host.base.replace('127.0.0.1', 'localhost')]));
    const blocked = await confine(context, url => origins.has(url.origin));
    const api = async (base, endpoint, method = 'get', data) => {
      const response = await context.request[method](base + endpoint, { data });
      assert.ok(response.ok(), `${method.toUpperCase()} ${endpoint}: HTTP ${response.status()}`);
      return response.status() === 204 ? undefined : response.json();
    };
    const status = () => api(unified.base, '/federation/local/peers');
    const before = await status();
    assert.equal(before.browsingEnabled, false);
    assert.equal(before.peers.length, 0);
    const sourceStatus = await api(source.base, '/federation/local/peers');
    assert.notEqual(sourceStatus.identity.nodeId, before.identity.nodeId);
    const errors = [];
    context.on('page', page => page.on('pageerror', error => errors.push(error.message)));

    const devices = await context.newPage();
    await devices.goto(uiBase + '/#/federation/devices');
    await devices.getByRole('heading', { name: name('federation.devices.title'), exact: true }).waitFor();
    const mutationRequests = [];
    devices.on('request', request => {
      if (request.method() !== 'GET' && /federation\/local\/peers.*(connect|path-mappings)/.test(request.url())) mutationRequests.push(request.url());
    });

    // Entering an address starts nothing. New node access needs an independent approval from its owner.
    // The sharing form's own field: the desktop app's devices page also has "Device address"
    // for adding a server to manage, which this form must leave alone.
    const sharingForm = devices.locator('form').filter({ has: devices.getByRole('checkbox', { name: namePrefix('federation.pair.shareBack') }) });
    await sharingForm.getByLabel(name('federation.pair.address'), { exact: true }).fill(source.base);
    assert.equal(await devices.locator('#managed-servers').getByLabel(name('federation.servers.add.address'), { exact: true }).inputValue(), '');
    assert.equal(await devices.getByLabel(name('federation.pair.code'), { exact: true }).inputValue(), '');
    assert.deepEqual(mutationRequests, [], 'Entering an address must not pair or bind paths');
    assert.equal((await status()).peers.length, 0);
    // Reading the other device is all that is asked for; sharing this library back is a separate choice.
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
    assert.deepEqual(peer.pathMappings, [], 'Pairing must not bind any path');
    assert.deepEqual(paired.identity, before.identity, 'Pairing must not replace local library identity');
    assert.equal(paired.browsingEnabled, true, 'Gaining read access to another device turns browsing on');
    assert.ok(hasFiles(unified.directory, file => /\.db$/i.test(file)), 'Unified fixture has no authoritative database');

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
    assert.deepEqual(errors, []);
    await assertStayedLocal(context, blocked, 'Federation');
    const report = {
      passed: true, fixtureScope: 'Production Service HTTP pipelines; not an installed native package',
      noAuthorizationOrMappingFromAnAddress: true, explicitNewNodeApproval: true, noReverseGrant: true,
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
    const report = await federation(browser);
    report.networkConfinementProven = refusedCanary;
    // What an old thin client left on this machine: its pairing with the source. Both stages
    // below import it; see legacy-client.cjs.
    report.legacyClientPairing = await legacyClientPairing({ config });
    report.firstLaunchImport = await firstLaunchImport({ browser, config });
    report.serverSwitching = await serverSwitching({ browser, config, artifacts });
    fs.writeFileSync(artifacts('result.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify(report, null, 2));
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
