import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../dist/bakabase.user.js', import.meta.url), 'utf8');
// Expose the real production functions inside the single userscript scope. Keep
// DOMContentLoaded pending so no fabricated third-party page or React tree is needed.
const instrumented = source.replace(/\}\)\(\);\s*$/, 'globalThis.testApi = { getApiAddressGeneration, getApiBaseUrl, setApiBaseUrl, httpRequest, pingNow, getConnectionState, t, describeRequestError }; })();');
const baseUrl = 'http://192.168.3.23:34567';

function sandbox({ endpoint = baseUrl, locale = 'zh', transport } = {}) {
  const storage = new Map([['api_base_url', endpoint], ['locale', locale]]);
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
