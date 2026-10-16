// A's own resource profiles matching B's resources, through the existing editor and signed
// relay. A manages B, never the reverse. No OS program is launched and no response is mocked.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { confine, assertStayedLocal, assertNoAnalytics } = require('./network.cjs');

const escape = text => String(text).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const relayOrigin = url => url.protocol === 'http:' && url.hostname === '127.0.0.1' &&
  Number(url.port) >= 34650 && Number(url.port) < 34906;

module.exports = async function resourceProfiles({ browser, config, artifacts }) {
  const { unified: a, source: b } = config.hosts;
  const home = a.window;
  const locales = ['en', 'cn'].map(lang => Object.assign({}, ...[
    'common', 'pages/resourceProfile', 'components/resourceFilter', 'components/property', 'pages/federation',
  ].map(file => JSON.parse(fs.readFileSync(path.join(config.repo, `src/web/src/locales/${lang}/${file}.json`), 'utf8')))));
  const pattern = key => locales.map(locale => {
    assert.ok(locale[key], `Missing locale key ${key}`);
    return escape(locale[key]).replace(/\\\{\\\{\w+\\\}\\\}/g, '.*?');
  }).join('|');
  const exactly = key => new RegExp(`^(?:${pattern(key)})$`);
  const containing = key => new RegExp(pattern(key));
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'en-US' });
  const origins = new Set([a, b].flatMap(host => [host.base, host.base.replace('127.0.0.1', 'localhost')]));
  const blocked = await confine(context, url => origins.has(url.origin) || relayOrigin(url));
  const errors = [];
  context.on('page', page => page.on('pageerror', error => errors.push(error.message)));
  const call = async (url, options = {}) => {
    const response = await context.request.fetch(url, options);
    assert.ok(response.ok(), `${options.method ?? 'GET'} ${new URL(url).pathname}: HTTP ${response.status()}`);
    return response.json();
  };
  const envelope = async (url, options) => {
    const body = await call(url, options);
    assert.equal(body.code, 0, `${new URL(url).pathname}: ${body.message ?? body.code}`);
    return body.data;
  };
  const success = async responsePromise => {
    const response = await responsePromise;
    assert.ok(response.ok(), `${response.request().method()} ${new URL(response.url()).pathname}: HTTP ${response.status()}`);
    const body = await response.json();
    assert.equal(body.code, 0, body.message);
    return body;
  };
  const pageResponse = (page, suffix, method = 'POST') => page.waitForResponse(response =>
    new URL(response.url()).pathname === suffix && response.request().method() === method);
  const requestsTo = host => fs.readFileSync(host.requestLog, 'utf8').split('\n').filter(Boolean)
    .map(line => JSON.parse(line)).filter(entry => 'method' in entry);
  const created = { profile: undefined, aProperty: undefined, bProperty: undefined };
  // An inert fixture file exercises the existing path autocomplete, never Process.Start.
  const playerFile = path.join(a.directory, 'source-profile-smoke-player');
  const playerCommand = '--fixture-source-profile "{0}"';
  let page;
  try {
    for (const host of [a, b]) await assertNoAnalytics(host.base);
    const aInfo = await envelope(a.base + '/remote-access/server-info');
    const bInfo = await envelope(b.base + '/remote-access/server-info');
    const managed = await call(a.base + '/federation/local/servers');
    assert.ok(managed.servers.some(server => server.serverId === bInfo.id), 'Switching must leave A managing B');
    const bSettingsBefore = await envelope(b.base + '/remote-access/settings');
    const bProfilesBefore = await envelope(b.base + '/resource-profile');
    const aProfilesBefore = await envelope(a.base + '/resource-profile');
    const aPropertyName = 'Smoke property belonging to A';
    const bPropertyName = 'Smoke property belonging to B';
    const value = 'only-b-resource-one';
    created.aProperty = (await envelope(a.base + '/custom-property', {
      method: 'POST', data: { name: aPropertyName, type: 1 },
    })).id;
    created.bProperty = (await envelope(b.base + '/custom-property', {
      method: 'POST', data: { name: bPropertyName, type: 1 },
    })).id;
    assert.equal(created.aProperty, created.bProperty, 'The fixture must exercise colliding property IDs on A and B');
    await envelope(b.base + '/resource/1/property-value', {
      method: 'PUT', data: { propertyId: created.bProperty, isCustomProperty: true, value },
    });
    fs.writeFileSync(playerFile, 'Inert browser smoke fixture; never execute.\n', { flag: 'wx', mode: 0o600 });

    page = await context.newPage();
    const sourcesResponse = pageResponse(page, '/resource-profile/sources', 'GET');
    await page.goto(home + '/#/resource-profile');
    const sources = (await success(sourcesResponse)).data;
    assert.equal(sources.filter(source => source.deviceId === bInfo.id).length, 1);
    assert.ok(!sources.some(source => source.deviceId === aInfo.id), 'A must not offer itself as a foreign source');
    await page.getByRole('button', { name: exactly('resourceProfile.action.addProfile') }).first().click();
    const createDialog = page.getByRole('dialog', { name: exactly('resourceProfile.modal.addResourceProfileTitle') });
    await createDialog.getByRole('button', { name: containing('resourceProfile.source.label') }).click();
    // HeroUI portals the popup outside its enclosing modal's accessibility subtree.
    // Require the visible option text; do not rely on the modal's temporary aria-hidden tree.
    await page.getByText(bInfo.name, { exact: true }).last().click();
    const profileName = 'A local player for B resources';
    await createDialog.getByRole('textbox', { name: containing('resourceProfile.label.name') }).fill(profileName);
    const createdResponse = pageResponse(page, '/resource-profile');
    await createDialog.getByRole('button', { name: exactly('resourceProfile.basic.createAndConfigure') }).click();
    const profile = (await success(createdResponse)).data;
    created.profile = profile.id;
    assert.equal(profile.sourceDeviceId, bInfo.id);
    assert.ok(profile.sourceLibraryEpoch, 'Creating a source profile must pin B library generation');
    console.log('Source profiles: created source-scoped profile on A');
    await createDialog.waitFor({ state: 'hidden' });
    const scope = `/resource-profile/sources/${encodeURIComponent(bInfo.id)}`;
    const editScope = page.getByRole('button', { name: exactly('resourceProfile.action.editScope') });
    await editScope.click();
    await page.getByRole('button', { name: exactly('resourceFilter.toolbar.addCondition') }).click();
    const propertyDialog = page.getByRole('dialog', { name: exactly('property.modal.selectProperty') });
    await propertyDialog.getByText(bPropertyName, { exact: true }).waitFor();
    assert.equal(await propertyDialog.getByText(aPropertyName, { exact: true }).count(), 0,
      'The source selector showed A metadata for B property ID');
    const valuePropertyResponse = pageResponse(page, scope + '/filter-value');
    await propertyDialog.getByText(bPropertyName, { exact: true }).click();
    assert.equal((await success(valuePropertyResponse)).data.name, bPropertyName);
    await propertyDialog.waitFor({ state: 'hidden' });
    await page.getByPlaceholder(exactly('common.placeholder.typeHere')).fill(value);
    const saveScopeResponse = pageResponse(page, `/resource-profile/${profile.id}`, 'PUT');
    await page.getByRole('button', { name: exactly('common.action.save') }).click();
    await success(saveScopeResponse);
    await editScope.waitFor();
    const storedScope = await envelope(a.base + `/resource-profile/${profile.id}`);
    const filter = storedScope.search.group.filters[0];
    assert.equal(filter.propertyPool, 4);
    assert.equal(filter.propertyId, created.bProperty);
    assert.equal(filter.dbValue, value);
    const localMatches = await call(a.base + '/resource/search?saveSearch=false', {
      method: 'POST', data: { page: 1, pageSize: 25, group: { combinator: 1, filters: [filter] } },
    });
    assert.equal(localMatches.totalCount, 0, 'The same IDs and value must not match A own data');
    console.log('Source profiles: selected B property and saved source filter');

    // Reopening hydrates the persisted source filter on B, not the same numeric ID on A.
    const resolveResponse = pageResponse(page, scope + '/search/resolve');
    await page.reload();
    const resolved = (await success(resolveResponse)).data;
    assert.equal(resolved.group.filters[0].property.name, bPropertyName);
    await page.getByText(bPropertyName, { exact: true }).waitFor();
    const previewResponse = pageResponse(page, scope + '/preview');
    await page.getByRole('button', { name: exactly('resourceProfile.action.testCriteria') }).click();
    const preview = await success(previewResponse);
    assert.equal(preview.totalCount, 1);
    assert.deepEqual(preview.data.map(resource => resource.id), [1]);
    assert.ok(preview.data[0].path.startsWith(b.directory), 'Preview returned A local resource instead of B resource');
    const previewDialog = page.getByRole('dialog', { name: containing('resourceProfile.preview.title') });
    const previewRow = previewDialog.getByRole('button', { name: containing('resourceProfile.preview.openResource') });
    await previewRow.waitFor();
    assert.equal(await previewRow.isDisabled(), true, 'A source preview must not open a local detail with the same resource ID');
    await previewDialog.screenshot({ path: artifacts('resource-profiles-source-preview.png') });
    await previewDialog.getByRole('button', { name: exactly('common.action.close') }).last().click();
    await previewDialog.waitFor({ state: 'hidden' });
    console.log('Source profiles: B-only preview and persisted metadata verified');

    // Individual categories now save through the revision-checked rule endpoint,
    // which returns the updated profile directly rather than an API envelope.
    const rulePath = `/resource-scope/profile/${profile.id}/rule`;
    const openingResponse = pageResponse(page, rulePath, 'PATCH');
    await page.getByRole('button', { name: containing('resourceProfile.source.openingMode') }).click();
    await page.getByText(exactly('resourceProfile.source.mode.native')).last().click();
    const openingSaved = await openingResponse;
    assert.equal(openingSaved.status(), 200, 'Saving the opening-mode rule failed');
    assert.equal((await openingSaved.json()).openingMode, 'native');
    await page.getByRole('heading', { name: exactly('resourceProfile.label.players') }).locator('..')
      .getByRole('button', { name: exactly('common.action.configure') }).click();
    const players = page.getByRole('dialog', { name: exactly('resourceProfile.modal.configurePlayersTitle') });
    await players.getByRole('button', { name: exactly('resourceProfile.action.addPlayer') }).click();
    const executable = players.getByRole('combobox', { name: containing('resourceProfile.label.executablePath') });
    // Real HeroUI behavior: adding an entry opens its fields, then its ordinary trigger can
    // collapse and reopen them. Mocked Accordion tests cannot detect a wrong controlled prop.
    await executable.waitFor({ state: 'visible' });
    const playerToggle = players.locator('button[aria-expanded]').filter({ hasText: containing('resourceProfile.label.newPlayer') });
    await playerToggle.click();
    await executable.waitFor({ state: 'hidden' });
    await playerToggle.click();
    await executable.waitFor({ state: 'visible' });
    // PathAutocomplete suppresses its initial 300 ms debounce. Let that initial pass finish
    // before entering a full path at once (a person reaches this field after that interval).
    await page.waitForTimeout(350);
    const pathSuggestions = page.waitForResponse(response =>
      new URL(response.url()).pathname === '/file/search-fs-entries' &&
      new URL(response.url()).searchParams.get('prefix') === playerFile);
    await executable.fill(playerFile);
    await success(pathSuggestions);
    await page.getByText(playerFile, { exact: true }).last().click();
    await players.getByRole('textbox', { name: containing('resourceProfile.players.openTheseFiles') }).fill('wav smoke-custom');
    await players.locator('summary').filter({ hasText: containing('resourceProfile.players.advancedTitle') }).click();
    await players.getByRole('textbox', { name: containing('resourceProfile.label.commandTemplate') }).fill(playerCommand);
    await players.getByRole('button', { name: containing('resourceProfile.players.http.label') }).click();
    await page.getByText(exactly('resourceProfile.players.http.supported')).last().click();
    await players.screenshot({ path: artifacts('resource-profiles-local-player-editor.png') });
    const playerResponse = pageResponse(page, rulePath, 'PATCH');
    await players.getByRole('button', { name: exactly('common.action.save') }).click();
    const playerSaved = await playerResponse;
    assert.equal(playerSaved.status(), 200, 'Saving the player rule failed');
    assert.equal((await playerSaved.json()).id, profile.id);
    await players.waitFor({ state: 'hidden' });
    const saved = await envelope(a.base + `/resource-profile/${profile.id}`);
    assert.equal(saved.openingMode, 'native');
    assert.deepEqual(saved.playerOptions.players, [{
      executablePath: playerFile, command: playerCommand, extensions: ['.wav', '.smoke-custom'], supportsHttp: true,
    }]);
    assert.equal((await envelope(a.base + '/resource-profile')).length, aProfilesBefore.length + 1);
    assert.deepEqual(await envelope(b.base + '/resource-profile'), bProfilesBefore,
      'Editing A playback must not create or mutate profiles on B');
    console.log('Source profiles: existing player editor saved only on A');
    await page.screenshot({ path: artifacts('resource-profiles-saved-on-a.png') });

    // Switch the actual A window to B. Metadata goes through A's local relay handler, not B.
    const relay = await context.newPage();
    await relay.goto(home + '/#/configuration');
    const opened = await relay.evaluate(async id => {
      const response = await fetch(`/federation/local/servers/${encodeURIComponent(id)}/open`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path: '/#/configuration' }),
      });
      return { status: response.status, body: await response.json() };
    }, bInfo.id);
    assert.equal(opened.status, 200);
    await relay.evaluate(url => location.assign(url), opened.body.url);
    await relay.waitForURL(url => relayOrigin(url) && !url.searchParams.has('__bakabase_switch') && url.hash === '#/configuration');
    const sourceMark = requestsTo(b).length;
    const fetchFromRelay = (route, body) => relay.evaluate(async ({ route, body }) => {
      const response = await fetch(route, body === undefined ? {} : {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
      });
      return { status: response.status, body: await response.json() };
    }, { route, body });
    const batch = await fetchFromRelay('/player/resource-profiles', {
      sourceDeviceId: bInfo.id, libraryEpoch: profile.sourceLibraryEpoch, resourceIds: [1, 2],
    });
    assert.equal(batch.status, 200);
    assert.equal(batch.body.code, 0);
    assert.deepEqual(batch.body.data.find(item => item.resourceId === 1), {
      resourceId: 1, openingMode: 'native', profileIds: [profile.id],
    });
    const unmatched = batch.body.data.find(item => item.resourceId === 2);
    assert.ok(unmatched && unmatched.openingMode == null && unmatched.profileIds.length === 0);
    const single = await fetchFromRelay(`/player/resource-profile?${new URLSearchParams({
      sourceDeviceId: bInfo.id, libraryEpoch: profile.sourceLibraryEpoch, resourceId: '1',
    })}`);
    assert.equal(single.status, 200);
    assert.deepEqual(single.body.data, { openingMode: 'native', profileIds: [profile.id] });
    assert.ok(!JSON.stringify([batch, single]).includes(playerFile));
    assert.ok(!JSON.stringify([batch, single]).includes(playerCommand));
    const foreign = await fetchFromRelay('/player/resource-profiles', { sourceDeviceId: aInfo.id, resourceIds: [1] });
    assert.equal(foreign.status, 400, 'B relay must not disclose A own opening configuration');
    assert.ok(!requestsTo(b).slice(sourceMark).some(entry => entry.path === '/player/resource-profiles' || entry.path === '/player/resource-profile'),
      'Local opening metadata was forwarded to B instead of resolved on A');
    assert.deepEqual(await envelope(b.base + '/resource-profile'), bProfilesBefore);
    const bSettingsAfter = await envelope(b.base + '/remote-access/settings');
    assert.equal(bSettingsAfter.mode, bSettingsBefore.mode);
    assert.deepEqual(bSettingsAfter.devices.map(device => device.id).sort(),
      bSettingsBefore.devices.map(device => device.id).sort(), 'The stage changed the one-way management relation');
    assert.deepEqual(errors, []);
    await assertStayedLocal(context, blocked, 'Source resource profiles');
    assert.deepEqual(blocked.loopback, [], 'Source resource profiles reached a non-fixture loopback port');
    return {
      passed: true, fixtureScope: 'A desktop-composed Service managing B headless Service; no native player launch',
      sourceSelectedThroughUi: true, profileStoredOnlyOnA: true, collidingPropertyIdsResolveOnB: true,
      sourceSearchPreviewMatchesOnlyB: true, existingPlayerEditorPreservesCustomArgumentsAndExtensions: true,
      relayResolvesLocalOpeningMode: true, relayDoesNotExposeExecutables: true, relayRejectsForeignSource: true,
      pageErrors: errors, blockedRequests: blocked,
    };
  } catch (error) {
    if (page && !page.isClosed()) {
      await page.screenshot({ path: artifacts('resource-profiles-failure.png'), fullPage: true }).catch(() => {});
      const controls = await page.locator('input,textarea,button,summary,[role="dialog"],[role="option"]').evaluateAll(elements =>
        elements.map(element => ({
          tag: element.tagName, role: element.getAttribute('role'), label: element.getAttribute('aria-label'),
          hidden: element.getAttribute('aria-hidden'),
          text: element.matches('button,summary,[role="option"]') ? element.textContent?.trim() : undefined,
          labelledBy: (element.getAttribute('aria-labelledby') ?? '').split(' ').map(id => document.getElementById(id)?.textContent ?? '').join(' '),
        }))).catch(() => []);
      fs.writeFileSync(artifacts('resource-profiles-failure-controls.json'), JSON.stringify(controls, null, 2));
    }
    throw error;
  } finally {
    // Later data-sync starts from the same definitions it would have without this stage.
    if (created.profile) await envelope(a.base + `/resource-profile/${created.profile}`, { method: 'DELETE' }).catch(() => {});
    if (created.aProperty) await envelope(a.base + `/custom-property/${created.aProperty}`, { method: 'DELETE' }).catch(() => {});
    if (created.bProperty) await envelope(b.base + `/custom-property/${created.bProperty}`, { method: 'DELETE' }).catch(() => {});
    if (fs.existsSync(playerFile)) fs.unlinkSync(playerFile);
    if (blocked.external.length) console.error('Source resource profiles: refused external requests', blocked.external);
    await context.close();
  }
};
