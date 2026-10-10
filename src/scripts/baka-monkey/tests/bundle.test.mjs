import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../dist/bakabase.user.js', import.meta.url), 'utf8');
// Expose the real production functions inside the single userscript scope. Keep
// DOMContentLoaded pending so no fabricated third-party page or React tree is needed.
const instrumented = source.replace(/\}\)\(\);\s*$/, 'globalThis.testApi = { getApiAddressGeneration, getApiBaseUrl, setApiBaseUrl, httpRequest, pingNow, getConnectionState, t, describeRequestError, startTaskSummaryPolling, getTaskPageUrl, isTaskSummaryVisible, setTaskSummaryVisible, onSettingsChange }; })();');
const baseUrl = 'http://192.168.3.23:34567';

function sandbox({ endpoint = baseUrl, locale = 'zh', transport, timers, storedValues = [] } = {}) {
  const storage = new Map([['api_base_url', endpoint], ['locale', locale], ...storedValues]);
  const requests = [];
  const context = {
    console: { debug() {}, error() {}, log() {}, warn() {} },
    setTimeout, clearTimeout, setInterval, clearInterval, URL, URLSearchParams, queueMicrotask,
    navigator: { userAgent: 'test', language: 'en-US', languages: ['en-US'], platform: 'test' },
    document: { readyState: 'loading', addEventListener() {}, createElement: () => ({ relList: { supports: () => false } }) },
    window: {},
    GM_getValue: (key, fallback) => storage.has(key) ? storage.get(key) : fallback,
    GM_setValue: (key, value) => storage.set(key, value),
    GM_addStyle() {},
    GM_xmlhttpRequest(details) {
      requests.push(details);
      transport?.(details);
      return { abort() {} };
    },
    ...timers,
  };
  vm.runInNewContext(instrumented, context);
  return { api: context.testApi, storage, requests };
}

test('production userscript keeps GM grants in one scope without remote require modules', () => {
  assert.match(source, /^\/\/ ==UserScript==/);
  for (const grant of ['GM_getValue', 'GM_setValue', 'GM_xmlhttpRequest']) assert.match(source, new RegExp(`// @grant\\s+${grant}`));
  assert.doesNotMatch(source, /\/\/ @require|System\.register/);
  assert.notEqual(instrumented, source);
});

test('actual built bundle reaches canonical LAN health through granted GM transport', () => {
  const { api, requests } = sandbox({ transport: (details) => details.onload({ status: 200, responseText: '{"code":0}' }) });
  api.pingNow();
  assert.equal(requests[0].url, `${baseUrl}/tampermonkey/health`);
  assert.equal(api.getConnectionState().connected, true);
  assert.equal(api.getConnectionState().error, null);
});

test('actual stored trailing slash is normalized, while malformed stored origins never dispatch', () => {
  const valid = sandbox({ endpoint: `${baseUrl}/` });
  valid.api.pingNow();
  assert.equal(valid.requests[0].url, `${baseUrl}/tampermonkey/health`);
  valid.requests[0].onerror();
  for (const endpoint of [`${baseUrl}/bad-path`, 'http://localhost:0', 'http://*:34567']) {
    const invalid = sandbox({ endpoint });
    invalid.api.pingNow();
    assert.equal(invalid.requests.length, 0);
    assert.equal(invalid.api.getConnectionState().connected, false);
    assert.equal(invalid.api.getConnectionState().error.kind, 'invalid-url');
  }
});

test('canonical LAN permission failure and synchronous exception produce translated connection diagnostics', () => {
  for (const transport of [
    (details) => details.onerror({ error: 'Forbidden domain (not in @connect)', status: 0 }),
    () => { throw new Error('Permission denied'); },
  ]) {
    const { api, requests } = sandbox({ transport });
    api.pingNow();
    assert.equal(requests[0].url, `${baseUrl}/tampermonkey/health`);
    assert.equal(api.getConnectionState().connected, false);
    assert.equal(api.getConnectionState().error.kind, 'permission');
    assert.match(api.describeRequestError(api.getConnectionState().error), /阻止了请求/);
    assert.equal(api.t('disconnected'), '无法连接到 Bakabase');
  }
});

test('old-address and older same-address health callbacks cannot overwrite the latest connection', () => {
  const { api, requests } = sandbox();
  api.pingNow();
  api.setApiBaseUrl('http://192.168.3.24:34567/');
  api.pingNow();
  requests[1].onerror({ error: 'net::ERR_CONNECTION_REFUSED' });
  requests[0].onload({ status: 200, responseText: '{"code":0}' });
  assert.equal(api.getConnectionState().connected, false);
  assert.equal(api.getConnectionState().baseUrl, 'http://192.168.3.24:34567');
  assert.equal(api.getConnectionState().error.reason, 'ConnectionRefused');
  api.pingNow();
  api.pingNow();
  requests[3].onload({ status: 200, responseText: '{"code":0}' });
  requests[2].onerror();
  assert.equal(api.getConnectionState().connected, true);
});

test('address generations distinguish a pending A response from the current A after an A → B → A switch', () => {
  const { api } = sandbox();
  const initial = api.getApiAddressGeneration();
  api.setApiBaseUrl(`${baseUrl}/`);
  assert.equal(api.getApiAddressGeneration(), initial, 'normalizing the same origin does not invalidate current requests');
  api.setApiBaseUrl('http://192.168.3.24:34567');
  assert.equal(api.getApiAddressGeneration(), initial + 1);
  api.setApiBaseUrl(baseUrl);
  assert.equal(api.getApiBaseUrl(), baseUrl);
  assert.equal(api.getApiAddressGeneration(), initial + 2, 'the old A response is stale although its address matches again');
  assert.throws(() => api.setApiBaseUrl(`${baseUrl}/invalid`));
  assert.equal(api.getApiAddressGeneration(), initial + 2);
});

test('health rejects non-Bakabase success bodies and honors explicitly stored UI language', () => {
  for (const responseText of ['{}', 'null', '', '{"code":401}']) {
    const { api } = sandbox({ transport: (details) => details.onload({ status: 200, responseText }) });
    api.pingNow();
    assert.equal(api.getConnectionState().connected, false);
    assert.equal(api.getConnectionState().error.kind, 'invalid-response');
  }
  assert.equal(sandbox({ locale: 'en' }).api.t('disconnected'), 'Cannot connect to Bakabase');
});

function fakeClock() {
  let now = 0;
  let id = 0;
  const pending = new Map();
  return {
    timers: {
      setTimeout(callback, delay) {
        pending.set(++id, { callback, at: now + delay });
        return id;
      },
      clearTimeout(timer) { pending.delete(timer); },
    },
    advance(duration) {
      const end = now + duration;
      while (true) {
        const next = [...pending].sort((a, b) => a[1].at - b[1].at)[0];
        if (!next || next[1].at > end) break;
        now = next[1].at;
        pending.delete(next[0]);
        next[1].callback();
      }
      now = end;
    },
    get size() { return pending.size; },
  };
}

const summaryResponse = (data, code = 0) => ({ status: 200, responseText: JSON.stringify({ code, data }) });

test('task summary close persists per site and settings can restore it after reload', () => {
  const { api, storage } = sandbox();
  let notifications = 0;
  const unsubscribe = api.onSettingsChange(() => notifications++);
  assert.equal(api.isTaskSummaryVisible('exhentai'), true);
  assert.equal(api.isTaskSummaryVisible('soulplus'), true);
  api.setTaskSummaryVisible('exhentai', false);
  assert.equal(api.isTaskSummaryVisible('exhentai'), false);
  assert.equal(api.isTaskSummaryVisible('soulplus'), true);
  assert.equal(notifications, 1);
  const reloaded = sandbox({ storedValues: [...storage] });
  assert.equal(reloaded.api.isTaskSummaryVisible('exhentai'), false);
  reloaded.api.setTaskSummaryVisible('exhentai', true);
  assert.equal(reloaded.api.isTaskSummaryVisible('exhentai'), true);
  unsubscribe();
});

test('task summary uses site-specific compact endpoints and opens unfiltered task pages', () => {
  for (const [target, path, page] of [
    [{ kind: 'download', source: 2 }, '/download-task/summary?thirdPartyId=2', 'downloader'],
    [{ kind: 'parse', source: 5 }, '/post-parser/task/summary?source=5', 'post-parser'],
  ]) {
    const clock = fakeClock();
    const updates = [];
    const { api, requests } = sandbox({ timers: clock.timers });
    const stop = api.startTaskSummaryPolling({ target, onUpdate: (value) => updates.push(value) });
    assert.equal(requests[0].url, `${baseUrl}${path}`);
    assert.equal(requests[0].timeout, 8000);
    requests[0].onload(summaryResponse({ completed: 3, failed: 2, total: 9 }));
    assert.equal(JSON.stringify(updates), '[null,{"completed":3,"failed":2,"total":9}]');
    assert.equal(api.getTaskPageUrl(baseUrl, target), `${baseUrl}/#/${page}`);
    stop();
    assert.equal(clock.size, 0);
  }
});

test('task summary serializes polling and cleanup ignores an in-flight response', () => {
  const clock = fakeClock();
  const updates = [];
  const { api, requests } = sandbox({ timers: clock.timers });
  const stop = api.startTaskSummaryPolling({ target: { kind: 'download', source: 2 }, onUpdate: (value) => updates.push(value) });
  clock.advance(4000);
  assert.equal(requests.length, 1, 'the pending request has no competing poll');
  requests[0].onload(summaryResponse({ completed: 0, failed: 0, total: 0 }));
  clock.advance(9999);
  assert.equal(requests.length, 1);
  clock.advance(1);
  assert.equal(requests.length, 2);
  stop();
  const before = JSON.stringify(updates);
  requests[1].onload(summaryResponse({ completed: 1, failed: 0, total: 1 }));
  clock.advance(30000);
  assert.equal(JSON.stringify(updates), before);
  assert.equal(requests.length, 2, 'a hidden or closed summary must not resume polling');
  assert.equal(clock.size, 0);
});

test('task summary clears stale numbers on error, validates responses and recovers', () => {
  const clock = fakeClock();
  const updates = [];
  const { api, requests } = sandbox({ timers: clock.timers });
  const stop = api.startTaskSummaryPolling({ target: { kind: 'parse', source: 5 }, onUpdate: (value) => updates.push(value) });
  requests[0].onload(summaryResponse({ completed: 7, failed: 1, total: 10 }));
  clock.advance(10000);
  requests.at(-1).onerror();
  assert.equal(updates.at(-1), null);
  for (const invalid of [undefined, { completed: -1, failed: 0, total: 1 }, { completed: 2, failed: 1, total: 2 }, { completed: '1', failed: 0, total: 2 }]) {
    clock.advance(10000);
    requests.at(-1).onload(summaryResponse(invalid));
    assert.equal(updates.at(-1), null);
  }
  clock.advance(10000);
  requests.at(-1).onload(summaryResponse({ completed: 0, failed: 0, total: 0 }));
  assert.equal(JSON.stringify(updates.at(-1)), '{"completed":0,"failed":0,"total":0}');
  stop();
});

test('task summary timeout permits a later retry and rejects a late answer', () => {
  const clock = fakeClock();
  const updates = [];
  const { api, requests } = sandbox({ timers: clock.timers });
  const stop = api.startTaskSummaryPolling({ target: { kind: 'parse', source: 5 }, onUpdate: (value) => updates.push(value) });
  clock.advance(8000);
  requests[0].onload(summaryResponse({ completed: 3, failed: 0, total: 3 }));
  assert.equal(updates.at(-1), null);
  clock.advance(10000);
  assert.equal(requests.length, 2);
  requests[1].onload(summaryResponse({ completed: 4, failed: 0, total: 4 }));
  assert.equal(updates.at(-1).completed, 4);
  stop();
});

test('task summary never publishes counts from an old server, including A to B to A', () => {
  const clock = fakeClock();
  const updates = [];
  const { api, requests } = sandbox({ timers: clock.timers });
  const oldStop = api.startTaskSummaryPolling({ target: { kind: 'download', source: 2 }, onUpdate: (value) => updates.push(value) });
  api.setApiBaseUrl('http://192.168.3.24:34567');
  api.setApiBaseUrl(baseUrl);
  const stop = api.startTaskSummaryPolling({ target: { kind: 'download', source: 2 }, onUpdate: (value) => updates.push(value) });
  requests[1].onload(summaryResponse({ completed: 5, failed: 0, total: 5 }));
  requests[0].onload(summaryResponse({ completed: 99, failed: 0, total: 99 }));
  assert.equal(updates.at(-1).completed, 5);
  clock.advance(10000);
  assert.equal(requests.length, 3, 'only the current server poll continues');
  requests[2].onerror();
  oldStop();
  stop();
});
