import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../dist/bakabase.user.js', import.meta.url), 'utf8');
// Expose the real production functions inside the single userscript scope. Keep
// DOMContentLoaded pending so no fabricated third-party page or React tree is needed.
const instrumented = source.replace(/\}\)\(\);\s*$/, 'globalThis.testApi = { getApiAddressGeneration, getApiBaseUrl, setApiBaseUrl, httpRequest, pingNow, getConnectionState, t, describeRequestError, startTaskSummaryPolling, getTaskPageUrl, isTaskSummaryVisible, setTaskSummaryVisible, onSettingsChange, buildBrowsingPage, applyQueriedContentStatus, canAutoMarkContent, formatBrowsingDistance, fetchPageBoundaries, fetchDailyHistory, getPageDay, shiftHistoryDay, includePagePriorViews, extractExhentaiContentDate, extractSoulplusPostDate, isBrowsingHistoryChartVisible, setBrowsingHistoryChartVisible, isBrowsingHistoryOlderVisible, setBrowsingHistoryOlderVisible, isBrowsingHistoryNewerVisible, setBrowsingHistoryNewerVisible }; })();');
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

const browsingPage = { filter: null, contentIds: ['current-1', 'current-2'], oldestUpdatedAt: '2026-10-01T12:00:00.000Z', newestUpdatedAt: '2026-10-02T12:00:00.000Z', hasMissingDates: false, priorViewedCount: 0, priorStatusPending: false };
const unviewed = { isViewed: false, hasUpdate: false, viewedAt: null, updatedAt: null };

test('whole-page time limits work independently of sort, deduplicate IDs, and reject incomplete timestamps', () => {
  const { api } = sandbox();
  const prior = new Map([['old', true], ['new', false]]);
  const content = [{ id: 'old', updateTime: new Date('2026-10-01T00:00:00Z') }, { id: 'new', updateTime: new Date('2026-10-02T00:00:00Z') }];
  const page = api.buildBrowsingPage([...content, content[0]], prior);
  assert.equal(page.oldestUpdatedAt, '2026-10-01T00:00:00.000Z');
  assert.equal(page.newestUpdatedAt, '2026-10-02T00:00:00.000Z');
  assert.equal(page.contentIds.length, 2);
  assert.equal(page.priorViewedCount, 1);
  assert.equal(page.priorStatusPending, false);
  for (const updateTime of [null, new Date('invalid')]) {
    const incomplete = api.buildBrowsingPage([...content, { id: 'missing', updateTime }], prior);
    assert.equal(incomplete.hasMissingDates, true);
    assert.equal(incomplete.oldestUpdatedAt, null);
    assert.equal(incomplete.priorStatusPending, true);
  }
});

test('auto mark waits for initial query, freezes prior-view state, and cannot be undone by a late query', () => {
  const { api } = sandbox();
  const statuses = new Map(), prior = new Map();
  assert.equal(api.canAutoMarkContent(undefined, false), false);
  api.applyQueriedContentStatus(statuses, prior, 'current', { ...unviewed });
  assert.equal(api.canAutoMarkContent(statuses.get('current'), prior.has('current')), true);
  statuses.get('current').isViewed = true;
  api.applyQueriedContentStatus(statuses, prior, 'current', { ...unviewed });
  assert.equal(statuses.get('current').isViewed, true);
  api.applyQueriedContentStatus(statuses, prior, 'current', { ...unviewed, isViewed: true });
  assert.equal(prior.get('current'), false);
  api.applyQueriedContentStatus(statuses, prior, 'revisit', { ...unviewed, isViewed: true });
  assert.equal(prior.get('revisit'), true);
  assert.equal(api.canAutoMarkContent(statuses.get('revisit'), true), false);
});

test('three history controls persist independently and default to chart/older on and newer off', () => {
  const { api, storage } = sandbox();
  assert.equal(api.isBrowsingHistoryChartVisible('exhentai'), true);
  assert.equal(api.isBrowsingHistoryOlderVisible('exhentai'), true);
  assert.equal(api.isBrowsingHistoryNewerVisible('exhentai'), false);
  api.setBrowsingHistoryChartVisible('exhentai', false);
  api.setBrowsingHistoryOlderVisible('exhentai', false);
  api.setBrowsingHistoryNewerVisible('exhentai', true);
  const reload = sandbox({ storedValues: [...storage] });
  assert.equal(reload.api.isBrowsingHistoryChartVisible('exhentai'), false);
  assert.equal(reload.api.isBrowsingHistoryOlderVisible('exhentai'), false);
  assert.equal(reload.api.isBrowsingHistoryNewerVisible('exhentai'), true);
  assert.equal(reload.api.isBrowsingHistoryChartVisible('soulplus'), true);
});

test('publication distance has minute precision, handles zero/invalid values and language', () => {
  const { api } = sandbox();
  assert.equal(api.formatBrowsingDistance('2026-10-01T00:00:00Z', '2026-10-03T03:04:00Z'), '2天 3时 4分');
  assert.equal(api.formatBrowsingDistance('2026-10-01T00:00:00Z', '2026-10-01T00:00:20Z'), '不足1分');
  assert.equal(api.formatBrowsingDistance('2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z'), '0分');
  assert.equal(api.formatBrowsingDistance('invalid', '2026-10-01T00:00:00Z'), null);
  assert.equal(sandbox({ locale: 'en' }).api.formatBrowsingDistance('2026-10-01T00:00:00Z', '2026-10-03T03:04:00Z'), '2d 3h 4m');
});

function historyRequest() {
  const environment = sandbox(), updates = [], errors = [];
  const stop = environment.api.fetchPageBoundaries({ page: browsingPage, domainKey: 'exhentai', filter: null,
    onUpdate: (value) => updates.push(value), onError: (error) => errors.push(error) });
  return { ...environment, updates, errors, stop };
}

test('boundary request excludes all page IDs and rejects ambiguous dates, self and wrong-side anchors', () => {
  const request = historyRequest();
  assert.deepEqual(JSON.parse(request.requests[0].data).excludeContentIds, browsingPage.contentIds);
  request.requests[0].onload(summaryResponse({ older: { contentId: 'previous', updatedAt: '2026-09-30T12:00:00Z' }, newer: null }));
  assert.equal(request.updates[0].older.contentId, 'previous');
  for (const older of [undefined, { contentId: 'current-1', updatedAt: '2026-09-30T00:00:00Z' },
    { contentId: 'wrong-side', updatedAt: '2026-10-04T00:00:00Z' }, { contentId: 'ambiguous', updatedAt: '2026-09-30T00:00:00' }]) {
    const invalid = historyRequest();
    invalid.requests[0].onload(summaryResponse({ older, newer: null }));
    assert.equal(invalid.errors[0].kind, 'invalid-response');
    assert.equal(invalid.updates.length, 0);
  }
});

test('cancelled and old-server callbacks cannot replace history; 404 remains an error', () => {
  for (const invalidate of [(r) => r.stop(), (r) => { r.api.setApiBaseUrl('http://192.168.3.24:34567'); r.api.setApiBaseUrl(baseUrl); }]) {
    const request = historyRequest(); invalidate(request);
    request.requests[0].onload(summaryResponse({ older: null, newer: null }));
    assert.equal(request.updates.length, 0); assert.equal(request.errors.length, 0);
  }
  const unavailable = historyRequest();
  unavailable.requests[0].onload({ status: 404, responseText: '{}' });
  assert.equal(unavailable.errors[0].status, 404);
});

test('chart dates honor timezone and calendar shifts across DST and year boundaries', () => {
  const { api } = sandbox();
  assert.equal(api.getPageDay('2026-10-01T18:00:00Z', 'Asia/Shanghai'), '2026-10-02');
  assert.equal(api.getPageDay('2026-10-01T18:00:00Z', 'UTC'), '2026-10-01');
  assert.equal(api.shiftHistoryDay('2026-03-08', 7), '2026-03-15');
  assert.equal(api.shiftHistoryDay('2026-01-01', -7), '2025-12-25');
});

test('daily history validates a complete ordered 31-day window of nonnegative counts', () => {
  const { api, requests } = sandbox(), updates = [], errors = [];
  const input = { page: browsingPage, domainKey: 'soulplus', filter: null, centerDay: '2026-10-01', timeZoneId: 'Asia/Shanghai',
    onUpdate: (value) => updates.push(value), onError: (error) => errors.push(error) };
  const days = Array.from({ length: 31 }, (_, index) => ({ date: api.shiftHistoryDay(input.centerDay, index - 15), count: index }));
  api.fetchDailyHistory(input);
  const sent = JSON.parse(requests[0].data);
  assert.equal(sent.timeZoneId, 'Asia/Shanghai'); assert.equal(sent.domainKey, 'soulplus');
  assert.deepEqual(sent.excludeContentIds, browsingPage.contentIds);
  requests[0].onload(summaryResponse({ days })); assert.equal(updates[0].days.length, 31);
  for (const invalid of [days.slice(1), [...days].reverse(), days.map((day) => ({ ...day, count: -1 })), days.map((day) => ({ ...day, count: '1' }))]) {
    api.fetchDailyHistory(input); requests.at(-1).onload(summaryResponse({ days: invalid }));
    assert.equal(errors.at(-1).kind, 'invalid-response');
  }
  const before = updates.length, cancel = api.fetchDailyHistory(input); cancel();
  requests.at(-1).onload(summaryResponse({ days })); assert.equal(updates.length, before);
});


test('frozen prior page views appear in day bars while current-visit marks stay excluded', () => {
  const { api } = sandbox();
  const page = api.buildBrowsingPage([
    { id: 'old', updateTime: new Date('2026-10-01T18:00:00Z') },
    { id: 'new', updateTime: new Date('2026-10-01T19:00:00Z') },
  ], new Map([['old', true], ['new', false]]));
  const counts = api.includePagePriorViews({ days: [{ date: '2026-10-02', count: 4 }] }, page, 'Asia/Shanghai');
  assert.equal(counts.days[0].count, 5);
  assert.equal(page.priorViewedCount, 1);
  assert.equal(api.canAutoMarkContent({ ...unviewed, isViewed: true }, true, new Date()), true, 'a real revisit can enrich a legacy record missing its publication date');
  assert.equal(api.formatBrowsingDistance('2026-10-01T16:00:00Z', '2026-10-04T16:00:00Z', 'day'), '3天');
});

test('Northplus uses the original author date in list and wall layouts, never a last-reply timestamp', () => {
  const { api } = sandbox();
  for (const [tagName, selector] of [['TR', 'td.tal.y-style .f10.gray2'], ['DIV', '.section-intro table tr td:last-child']]) {
    const element = { tagName, querySelector: (query) => ({ textContent: query === selector ? '2026-10-01' : '2026-10-11 23:59' }) };
    assert.equal(api.extractSoulplusPostDate(element).toISOString(), '2026-09-30T16:00:00.000Z');
    for (const text of ['2026-02-30', '10-01', '2026-10-01 23:59', 'unknown']) {
      assert.equal(api.extractSoulplusPostDate({ tagName, querySelector: () => ({ textContent: text }) }), null);
    }
    assert.equal(api.extractSoulplusPostDate({ tagName, querySelector: (query) => query === selector ? null : ({ textContent: '2026-10-11 23:59' }) }), null);
  }
});

test('ExHentai reads each gallery publication time in all four layouts, independently of the visit date', () => {
  const { api } = sandbox();
  const posted = '2024-02-29 23:59';
  for (const mode of ['gld', 'gltm', 'gltc', 'glte']) {
    const element = {
      querySelector: (selector) => selector === '[id="posted_123"]' ? { textContent: posted } : null,
    };
    const contentDate = api.extractExhentaiContentDate(element, mode, '123');
    assert.equal(contentDate.toISOString(), '2024-02-29T23:59:00.000Z');
    assert.equal(api.getPageDay(contentDate.toISOString(), 'Asia/Shanghai'), '2024-03-01');
  }
});

test('ExHentai validates source dates and falls back within the same gallery without inventing a visit timestamp', () => {
  const { api } = sandbox();
  function gallery(mode, posted, fallback) {
    const text = { textContent: fallback };
    return {
      querySelector: (selector) => {
        if (selector === '[id="posted_123"]') return { textContent: posted };
        if (mode === 'gltc' && selector === '.gl2c > div:last-child') return { firstElementChild: text };
        return null;
      },
      querySelectorAll: (selector) => {
        if (mode === 'gld' && selector === '.gl5t > div:first-child > div') return [{ textContent: 'category' }, text];
        if (mode === 'gltm' && selector === '.gl2m > div') return [{ textContent: 'category' }, text];
        if (mode === 'glte' && selector === '.gl3e > div') return [{ textContent: 'category' }, text];
        return [];
      },
    };
  }
  for (const mode of ['gld', 'gltm', 'gltc', 'glte']) {
    assert.equal(api.extractExhentaiContentDate(gallery(mode, 'not a date', '2025-01-02 03:04:05'), mode, '123').toISOString(),
      '2025-01-02T03:04:05.000Z');
    assert.equal(api.extractExhentaiContentDate(gallery(mode, '2024-02-29 23:59', '2026-10-11 12:00'), mode, '123').toISOString(),
      '2024-02-29T23:59:00.000Z', 'The explicit posted date takes precedence over other row text.');
    for (const invalid of ['2025-02-29 12:00', '2026-02-30 12:00', '2026-10-11 24:00', '2026-10-11 12:60',
      '2026-10-11 12:00:60', '0000-01-01 00:00', 'unknown', '']) {
      assert.equal(api.extractExhentaiContentDate(gallery(mode, invalid, invalid), mode, '123'), null);
    }
    assert.equal(api.extractExhentaiContentDate(gallery(mode, '2025-01-02 03:04', ''), mode, 'invalid"id'), null);
  }
});
