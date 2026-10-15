// Production management mapping UI, durable browser drafts and real HTTP compare-and-swap.
// Uses only the disposable pair left by switching.cjs; preserves that pair's baseline mappings.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { confine, assertStayedLocal } = require('./network.cjs');

module.exports = async function mappingSmoke({ browser, config, artifacts }) {
  const { unified: a, source: b } = config.hosts;
  const home = a.window;
  const locales = ['en', 'cn'].map(lang => Object.assign({},
    ...['federation', 'managedMappings'].map(page => JSON.parse(fs.readFileSync(
      path.join(config.repo, `src/web/src/locales/${lang}/pages/${page}.json`), 'utf8')))));
  const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const name = (key, values = {}) => new RegExp(`^(?:${locales.map(locale => {
    assert.ok(locale[key], `Missing locale key ${key}`);
    return escape(locale[key].replace(/{{\s*(\w+)\s*}}/g, (_, field) => String(values[field])));
  }).join('|')})$`);
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'en-US' });
  const origins = new Set([a, b].flatMap(host => [host.base, host.base.replace('127.0.0.1', 'localhost')]));
  const blocked = await confine(context, url => origins.has(url.origin));
  const call = async (base, endpoint, method = 'GET', data) => {
    const response = await context.request.fetch(base + endpoint, { method, data });
    assert.ok(response.ok(), `${method} ${endpoint}: HTTP ${response.status()}`);
    return response.json();
  };
  const sourceId = (await call(b.base, '/remote-access/server-info')).data.id;
  const endpoint = `/federation/local/servers/${encodeURIComponent(sourceId)}/path-mappings`;
  const treePath = `/federation/local/servers/${encodeURIComponent(sourceId)}/path-mapping-tree`;
  const listing = async () => (await call(a.base, '/federation/local/servers')).servers;
  const current = async () => {
    const server = (await listing()).find(server => server.serverId === sourceId);
    assert.ok(server, 'The source management pairing must remain available');
    return server;
  };
  const before = await current();
  const baseline = before.pathMappings;
  const draftKey = `bakabase:management:mapping-draft:v1:${encodeURIComponent(new URL(home).href)}:${encodeURIComponent(sourceId)}`;
  const fixtureDirectory = fs.mkdtempSync(path.join(a.directory, 'management-mapping-browser-'));
  const [selectedPath, competingPath] = ['selected', 'competing'].map(folder => {
    const directory = path.join(fixtureDirectory, folder);
    fs.mkdirSync(directory);
    return directory;
  });
  const devices = await context.newPage();
  const errors = [];
  devices.on('pageerror', error => errors.push(error.message));
  const requests = [];
  devices.on('request', request => {
    if (new URL(request.url()).pathname === treePath) requests.push(new URL(request.url()));
  });
  const card = devices.getByTestId('managed-server').filter({ hasText: before.name });
  const editor = card.locator('details').first();
  const open = async () => {
    await card.waitFor();
    if (!(await editor.evaluate(element => element.open))) await editor.locator('summary').first().click();
  };
  const reload = async () => { await devices.reload(); await open(); };
  const localInput = () => editor.getByRole('textbox', { name: name('federation.servers.mappings.local'), exact: true }).last();
  const waitForDraft = async mappings => {
    await devices.waitForFunction(({ key, expected }) => {
      const raw = localStorage.getItem(key);
      return raw && JSON.stringify(JSON.parse(raw).mappings) === JSON.stringify(expected);
    }, { key: draftKey, expected: mappings });
    const draft = await devices.evaluate(key => JSON.parse(localStorage.getItem(key)), draftKey);
    assert.deepEqual(Object.keys(draft).sort(), ['baseMappings', 'mappings', 'pairedAt', 'version']);
    assert.deepEqual(draft.baseMappings, baseline);
    assert.equal(draft.pairedAt, before.pairedAt);
  };
  const noDraft = () => devices.waitForFunction(key => localStorage.getItem(key) === null, draftKey);
  const waitForWrite = () => devices.waitForResponse(response =>
    new URL(response.url()).pathname === endpoint && response.request().method() === 'PUT');
  try {
    await devices.goto(home + '/#/federation/devices?section=servers');
    await card.waitFor();
    assert.equal(requests.length, 0, 'Collapsed mapping editors must not fetch a remote tree');
    const pendingTree = devices.waitForResponse(response => new URL(response.url()).pathname === treePath);
    await open();
    const tree = await (await pendingTree).json();
    assert.ok(tree.libraryEpoch);
    assert.equal(tree.nodes.length, 1, 'Only the fixture resource with an owned path belongs in the tree');
    assert.equal(tree.nodes[0].resourceCount, 1);
    assert.ok(tree.nodes[0].serverPath);
    assert.equal(tree.nodes[0].sourceRootId, undefined);
    assert.ok(tree.nextCursor == null);
    const chosen = tree.nodes[0];
    const select = editor.getByRole('button', {
      name: name('federation.managedMappings.tree.select', { name: chosen.serverPath }), exact: true,
    });
    await select.click();
    // The earlier server-switching stage maps the resource's exact parent. The root aggregate
    // is normally broader; if equal, create another draft row through the existing manual path.
    if (baseline.some(mapping => mapping.serverPath === chosen.serverPath)) {
      await editor.getByRole('button', { name: name('federation.servers.mappings.add'), exact: true }).click();
      await editor.getByRole('textbox', { name: name('federation.servers.mappings.server'), exact: true }).last()
        .fill(`${chosen.serverPath.replace(/[\\/]$/, '')}/mapping-test`);
    }
    await localInput().fill(selectedPath);
    const serverPath = await editor.getByRole('textbox', { name: name('federation.servers.mappings.server'), exact: true }).last().inputValue();
    const proposed = [...baseline, { serverPath, localPath: selectedPath }];
    await waitForDraft(proposed);
    assert.deepEqual((await current()).pathMappings, baseline, 'Editing the tree must not save a mapping');

    await reload();
    await editor.getByRole('button', { name: name('federation.managedMappings.draft.restore'), exact: true }).click();
    assert.equal(await localInput().inputValue(), selectedPath);
    const pendingSave = waitForWrite();
    await editor.getByRole('button', { name: name('federation.servers.mappings.save'), exact: true }).click();
    const saved = await pendingSave;
    assert.equal(saved.status(), 200);
    assert.deepEqual(saved.request().postDataJSON(), { mappings: proposed, expectedMappings: baseline });
    await noDraft();
    assert.deepEqual((await current()).pathMappings, proposed);

    // Restore the baseline, then leave an edit pending while another window saves a different
    // destination. Restoring the draft must require a deliberate replacement decision.
    await call(a.base, endpoint, 'PUT', { mappings: baseline, expectedMappings: proposed });
    await reload();
    await editor.getByRole('button', { name: name('federation.servers.mappings.add'), exact: true }).click();
    await editor.getByRole('textbox', { name: name('federation.servers.mappings.server'), exact: true }).last().fill(serverPath);
    await localInput().fill(selectedPath);
    await waitForDraft(proposed);
    const competing = [...baseline, { serverPath, localPath: competingPath }];
    await call(a.base, endpoint, 'PUT', { mappings: competing, expectedMappings: baseline });
    await reload();
    await editor.getByRole('button', { name: name('federation.managedMappings.draft.restore'), exact: true }).click();
    await editor.getByRole('button', { name: name('federation.servers.mappings.save'), exact: true }).click();
    await editor.getByText(name('federation.managedMappings.conflict'), { exact: true }).waitFor();
    assert.deepEqual((await current()).pathMappings, competing, 'The stale draft overwrote another saved edit');
    await editor.screenshot({ path: artifacts('management-mapping-conflict.png') });
    await editor.getByRole('button', { name: name('federation.managedMappings.useCurrent'), exact: true }).click();
    await noDraft();
    assert.equal(await localInput().inputValue(), competingPath);
    assert.deepEqual((await current()).pathMappings, competing);
    assert.ok(requests.every(url => url.searchParams.get('limit') === '200'));
    assert.deepEqual(errors, []);
    await assertStayedLocal(context, blocked, 'Management mapping tree');
    return { passed: true, boundedOwnedTree: true, actualPathPrefixes: true,
      deferredTreeRead: true, draftSurvivesReload: true, savedDraftCleared: true,
      staleDraftRequiresReview: true, discardKeepsCurrentMappings: true };
  } finally {
    const latest = await current();
    await call(a.base, endpoint, 'PUT', { mappings: baseline, expectedMappings: latest.pathMappings });
    await context.close();
    fs.rmSync(fixtureDirectory, { recursive: true, force: true });
  }
};
