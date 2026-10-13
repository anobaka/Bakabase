// Production mapping UI and real HTTP CAS; directories belong only to disposable TestHosts.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

module.exports = async function mappingSmoke({ context, devices, config, peer, uiBase, status, artifacts }) {
  const locales = ['en', 'cn'].map(lang => JSON.parse(fs.readFileSync(
    path.join(config.repo, `src/web/src/locales/${lang}/pages/federation.json`), 'utf8')));
  const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const name = (key, values = {}) => new RegExp(`^(?:${locales.map(locale => escape(
    locale[key].replace(/{{\s*(\w+)\s*}}/g, (_, field) => String(values[field])))).join('|')})$`);
  const endpoint = `/federation/local/peers/${encodeURIComponent(peer.nodeId)}/path-mappings`;
  const treePath = `/federation/local/peers/${encodeURIComponent(peer.nodeId)}/mapping-tree`;
  const draftPrefix = 'bakabase:federation:mapping-draft:v1:';
  const draftSuffix = `:${encodeURIComponent(peer.nodeId)}`;
  const requests = [];
  const nativeActions = [];
  const observe = request => {
    const url = new URL(request.url());
    if (url.pathname.endsWith('/mapping-tree')) requests.push({
      pathname: url.pathname, parent: url.searchParams.get('parent'),
      cursor: url.searchParams.get('cursor'), limit: url.searchParams.get('limit'),
    });
    if (request.method() === 'POST' && /\/federation\/local\/(playback-sessions|open-directory)$/.test(url.pathname))
      nativeActions.push(url.pathname);
  };
  context.on('request', observe);
  const fixtureDirectory = fs.mkdtempSync(path.join(config.hosts.unified.directory, 'mapping-browser-'));
  const [firstPath, proposedPath, competingPath] = ['first', 'proposed', 'competing'].map(folder => {
    const directory = path.join(fixtureDirectory, folder);
    fs.mkdirSync(directory);
    return directory;
  });
  const card = devices.getByTestId('sharing-outbound').getByRole('group', { name: peer.label, exact: true });
  const editor = card.locator('details').first();
  const localInput = () => editor.getByRole('textbox', { name: name('federation.mappings.local'), exact: true });
  const currentMappings = async () => {
    const current = (await status()).peers.find(item => item.nodeId === peer.nodeId);
    assert.ok(current?.outboundGrant, 'The isolated source grant went away during mapping checks');
    return current.pathMappings;
  };
  const putMappings = async (mappings, expectedMappings) => {
    const response = await context.request.put(config.hosts.unified.base + endpoint, {
      data: { mappings, expectedMappings },
    });
    assert.equal(response.status(), 200, 'The isolated mapping write was refused');
  };
  const draftEntries = () => devices.evaluate(({ prefix, suffix }) => Object.keys(localStorage)
    .filter(key => key.startsWith(prefix) && key.endsWith(suffix))
    .map(key => ({ key, value: JSON.parse(localStorage.getItem(key)) })),
  { prefix: draftPrefix, suffix: draftSuffix });
  const waitForDraft = async mappings => {
    await devices.waitForFunction(({ prefix, suffix, expected }) => Object.keys(localStorage)
      .some(key => key.startsWith(prefix) && key.endsWith(suffix) &&
        JSON.stringify(JSON.parse(localStorage.getItem(key))?.mappings) === JSON.stringify(expected)),
    { prefix: draftPrefix, suffix: draftSuffix, expected: mappings });
    const entries = await draftEntries();
    assert.equal(entries.length, 1, 'More than one current-peer mapping draft was saved');
    assert.equal(entries[0].key, `${draftPrefix}${encodeURIComponent(new URL(uiBase).href)}${draftSuffix}`);
    assert.deepEqual(Object.keys(entries[0].value).sort(), ['grantId', 'grantRevision', 'mappings', 'version']);
    assert.deepEqual(entries[0].value, {
      version: 1, mappings, grantId: peer.outboundGrant.grantId,
      grantRevision: peer.outboundGrant.revision,
    }, 'Drafts must contain mapping edits and grant metadata only');
  };
  const waitForNoDraft = () => devices.waitForFunction(({ prefix, suffix }) => !Object.keys(localStorage)
    .some(key => key.startsWith(prefix) && key.endsWith(suffix)),
  { prefix: draftPrefix, suffix: draftSuffix });
  const openEditor = async () => {
    await card.waitFor();
    if (!(await editor.evaluate(element => element.open))) await editor.locator('summary').first().click();
  };
  const reloadEditor = async () => {
    await devices.reload();
    await openEditor();
  };
  const waitForWrite = (expectedStatus = 200) => devices.waitForResponse(response =>
    new URL(response.url()).pathname === endpoint && response.request().method() === 'PUT' &&
    response.status() === expectedStatus);
  const saveWithoutConflict = async (mappings, expectedMappings) => {
    const pending = waitForWrite();
    await editor.getByRole('button', { name: name('federation.save'), exact: true }).click();
    const response = await pending;
    assert.deepEqual(response.request().postDataJSON(), { mappings, expectedMappings });
    await waitForNoDraft();
    assert.deepEqual(await currentMappings(), mappings);
  };
  const review = () => editor.getByRole('alertdialog', { name: name('federation.mappings.conflictTitle'), exact: true });
  const saveReviewed = async (decision, mappings, expectedMappings) => {
    const pending = waitForWrite();
    await review().getByRole('button', { name: name(decision), exact: true }).click();
    const response = await pending;
    assert.deepEqual(response.request().postDataJSON(), { mappings, expectedMappings });
    await review().waitFor({ state: 'hidden' });
    await waitForNoDraft();
    assert.deepEqual(await currentMappings(), mappings);
  };
  let releasePut;
  let heldHandler;
  let routeHandler;
  try {
    assert.deepEqual(await currentMappings(), []);
    await devices.goto(uiBase + '/#/federation/devices?section=browsing');
    await card.waitFor();
    assert.equal(await editor.evaluate(element => element.open), false);
    assert.deepEqual(requests, [], 'Closed per-peer editors must not request a directory tree');
    const rootResponse = devices.waitForResponse(response => {
      const url = new URL(response.url());
      return url.pathname === treePath && !url.searchParams.has('parent');
    });
    await openEditor();
    const root = await rootResponse;
    assert.equal(root.status(), 200);
    const validatePage = page => {
      assert.equal(page.nodes.length, 1, 'Only the single owned fixture resource has a path');
      assert.ok(page.nodes.length <= 200);
      assert.ok(!page.nextCursor, 'The tiny fixture unexpectedly required another page');
      for (const node of page.nodes) {
        assert.match(node.sourceRootId, /^g[0-9a-f]{64}$/);
        assert.equal(node.resourceCount, 1, 'Metadata-only resources must not become mapping directories');
        assert.ok(node.browseToken);
        assert.ok(!path.isAbsolute(node.name) && !/^[a-z]:[\\/]/i.test(node.name), 'Tree labels exposed absolute paths');
      }
      return page.nodes[0];
    };
    let node = validatePage(await root.json());
    let branch = [node.name];
    let childReads = 0;
    assert.equal(requests.length, 1, 'Opening one editor must read only its root page');
    while (node.hasChildren) {
      assert.ok(childReads < 16, 'The fixture tree failed to reach its resource directory');
      const label = branch.join(' / ');
      const childResponse = devices.waitForResponse(response => {
        const url = new URL(response.url());
        return url.pathname === treePath && url.searchParams.get('parent') === node.browseToken;
      });
      await editor.getByRole('button', { name: name('federation.mappings.tree.expand', { name: label }), exact: true }).click();
      const child = await childResponse;
      assert.equal(child.status(), 200);
      const next = validatePage(await child.json());
      await editor.getByRole('button', { name: name('federation.mappings.tree.select', { name: [...branch, next.name].join(' / ') }), exact: true }).waitFor();
      const readCount = requests.length;
      // Collapsing and opening a loaded branch reuses its page, without eagerly reading descendants.
      await editor.getByRole('button', { name: name('federation.mappings.tree.collapse', { name: label }), exact: true }).click();
      await editor.getByRole('button', { name: name('federation.mappings.tree.expand', { name: label }), exact: true }).click();
      await editor.getByRole('button', { name: name('federation.mappings.tree.select', { name: [...branch, next.name].join(' / ') }), exact: true }).waitFor();
      assert.equal(requests.length, readCount, 'A loaded branch was fetched again or expanded recursively');
      node = next;
      branch.push(node.name);
      childReads++;
    }
    assert.ok(childReads > 0, 'The real fixture did not exercise a lazy child request');
    assert.equal(node.name, path.basename(config.hosts.source.directory));
    const mapping = localPath => [{ sourceRootId: node.sourceRootId, localPath }];
    await editor.getByRole('button', { name: name('federation.mappings.tree.select', { name: branch.join(' / ') }), exact: true }).click();
    assert.equal(await localInput().inputValue(), '');
    await localInput().fill(firstPath);
    await waitForDraft(mapping(firstPath));
    assert.deepEqual(await currentMappings(), [], 'Selecting a tree directory must not apply its draft');

    await reloadEditor();
    await editor.getByRole('button', { name: name('federation.mappings.draft.restore'), exact: true }).click();
    assert.equal(await localInput().inputValue(), firstPath);
    assert.deepEqual(await currentMappings(), []);
    await editor.getByRole('button', { name: name('federation.mappings.draft.discard'), exact: true }).click();
    await waitForNoDraft();
    assert.equal(await localInput().count(), 0);
    assert.deepEqual(await currentMappings(), []);

    const addTypedMapping = async localPath => {
      await editor.getByRole('button', { name: name('federation.mappings.add'), exact: true }).click();
      await editor.locator('summary').filter({ hasText: name('federation.mappings.manual') }).click();
      await editor.getByRole('textbox', { name: name('federation.mappings.rootId'), exact: true }).fill(node.sourceRootId);
      await localInput().fill(localPath);
      await waitForDraft(mapping(localPath));
    };
    await addTypedMapping(firstPath);
    await reloadEditor();
    await editor.getByRole('button', { name: name('federation.mappings.draft.discard'), exact: true }).click();
    await waitForNoDraft();
    assert.equal(await localInput().count(), 0, 'Discarding an unrestored draft applied its rows');
    assert.deepEqual(await currentMappings(), []);
    await addTypedMapping(firstPath);
    await saveWithoutConflict(mapping(firstPath), []);

    await localInput().fill(proposedPath);
    await waitForDraft(mapping(proposedPath));
    await editor.getByRole('button', { name: name('federation.save'), exact: true }).click();
    await review().waitFor();
    // Pause, rather than mock, one real browser PUT so a real competing write wins first.
    let markHeld;
    const held = new Promise(resolve => { markHeld = resolve; });
    const released = new Promise(resolve => { releasePut = resolve; });
    routeHandler = async route => {
      heldHandler = (async () => {
        markHeld(route.request().postDataJSON());
        await released;
        await route.continue();
      })();
      await heldHandler;
    };
    await devices.route(uiBase + endpoint, routeHandler);
    const rejected = waitForWrite(409);
    const browserPut = devices.waitForRequest(request =>
      new URL(request.url()).pathname === endpoint && request.method() === 'PUT');
    await review().getByRole('button', { name: name('federation.mappings.replace'), exact: true }).click();
    await browserPut;
    assert.deepEqual(await held, { mappings: mapping(proposedPath), expectedMappings: mapping(firstPath) });
    await putMappings(mapping(competingPath), mapping(firstPath));
    releasePut();
    const rejection = await rejected;
    const failure = await rejection.json();
    assert.equal((failure.error ?? failure).code, 'PathMappingsChanged');
    await heldHandler;
    await devices.unroute(uiBase + endpoint, routeHandler);
    routeHandler = undefined;
    await editor.getByText(name('federation.mappings.changedDuringReview'), { exact: true }).waitFor();
    assert.equal(await localInput().inputValue(), proposedPath, 'A CAS rejection discarded the proposed path');
    assert.deepEqual(await currentMappings(), mapping(competingPath), 'A stale draft overwrote the competing mapping');
    await waitForDraft(mapping(proposedPath));
    await editor.screenshot({ path: artifacts('mapping-cas-draft-retained.png') });

    await reloadEditor();
    await editor.getByRole('button', { name: name('federation.mappings.draft.restore'), exact: true }).click();
    assert.equal(await localInput().inputValue(), proposedPath);
    await editor.getByRole('button', { name: name('federation.save'), exact: true }).click();
    await review().waitFor();
    await saveReviewed('federation.mappings.replace', mapping(proposedPath), mapping(competingPath));
    // Keeping an existing rule really keeps it; removing it requires explicit replacement.
    await editor.getByRole('button', { name: name('federation.mappings.remove'), exact: true }).click();
    await editor.getByRole('button', { name: name('federation.save'), exact: true }).click();
    await review().waitFor();
    await saveReviewed('federation.mappings.keep', mapping(proposedPath), mapping(proposedPath));
    await editor.getByRole('button', { name: name('federation.mappings.remove'), exact: true }).click();
    await editor.getByRole('button', { name: name('federation.save'), exact: true }).click();
    await review().waitFor();
    await saveReviewed('federation.mappings.replace', [], mapping(proposedPath));
    assert.ok(requests.every(request => request.pathname === treePath && request.limit === '200'));
    assert.deepEqual(nativeActions, [], 'Mapping edits must not launch a player or open a directory');
    return {
      passed: true, closedEditorsDoNotRead: true, ownMetadataOnly: true,
      boundedLazyChildReads: childReads, loadedBranchesReused: true,
      typedDirectoryBelongsToFixture: true, draftScopedToEndpointAndPeer: true,
      draftRestoresAfterReload: true, restoredAndUnrestoredDraftDiscarded: true,
      successfulSaveClearsDraft: true, realCasRejectsStaleWrite: true,
      rejectedDraftSurvivesReload: true, existingRulesRequireExplicitDecision: true,
      nativeActions: nativeActions.length,
    };
  } finally {
    releasePut?.();
    await heldHandler?.catch(() => {});
    if (routeHandler) await devices.unroute(uiBase + endpoint, routeHandler).catch(() => {});
    context.off('request', observe);
    // Leave the existing disable/recovery stages exactly the paired, unmapped fixture they expect.
    await putMappings([], await currentMappings());
    await devices.evaluate(({ prefix, suffix }) => Object.keys(localStorage)
      .filter(key => key.startsWith(prefix) && key.endsWith(suffix))
      .forEach(key => localStorage.removeItem(key)), { prefix: draftPrefix, suffix: draftSuffix });
    fs.rmSync(fixtureDirectory, { recursive: true, force: true });
  }
};
