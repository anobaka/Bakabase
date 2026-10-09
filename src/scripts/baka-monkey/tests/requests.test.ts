import assert from 'node:assert/strict';
import { mock, test } from 'node:test';
import { normalizeApiBaseUrl, RequestError, sendRequest, type RequestDetails } from '../src/requests.ts';

const baseUrl = 'http://192.168.3.23:34567';
const healthUrl = `${baseUrl}/tampermonkey/health`;
const errorLog = mock.method(console, 'error', () => {});
const debugLog = mock.method(console, 'debug', () => {});

test('pasted LAN URLs with a trailing slash and surrounding whitespace use the canonical health and download routes', () => {
  const base = normalizeApiBaseUrl(` \t${baseUrl}/\u00a0`);
  assert.equal(base, baseUrl);
  for (const path of ['/tampermonkey/health', '/download-task/exhentai']) {
    let sentUrl = '';
    sendRequest((details) => {
      sentUrl = details.url;
      details.onload({ status: 200, responseText: '{"code":0}' });
    }, { method: 'GET', url: `${base}${path}` });
    assert.equal(sentUrl, `${baseUrl}${path}`);
    assert.equal(new URL(sentUrl).pathname, path);
  }
});

test('supports loopback, HTTPS hosts, and IPv6 origins', () => {
  assert.equal(normalizeApiBaseUrl('http://localhost:34567/'), 'http://localhost:34567');
  assert.equal(normalizeApiBaseUrl('https://BAKABASE.example/'), 'https://bakabase.example');
  assert.equal(normalizeApiBaseUrl('http://[::1]:34567/'), 'http://[::1]:34567');
});

test('rejects missing addresses, bind addresses, invalid ports, credentials, and non-origin URLs', () => {
  for (const value of ['', '192.168.3.23:34567', 'ftp://localhost', 'http://localhost:0', 'http://localhost:99999',
    'http://0.0.0.0:34567', 'http://[::]:34567', 'http://*:34567', 'http://+:34567',
    'http://user:password@localhost', `${baseUrl}/folder`, `${baseUrl}//`, `${baseUrl}?test=1`, `${baseUrl}#test`,
    'http://localhost\\path', 'http://local host']) {
    assert.throws(() => normalizeApiBaseUrl(value), RequestError, value);
  }
});

function pendingRequest(timeout = 1_000) {
  let details!: RequestDetails;
  const successes: unknown[] = [];
  const failures: RequestError[] = [];
  let aborted = false;
  sendRequest((request) => {
    details = request;
    return { abort() { aborted = true; details.onabort(); } };
  }, {
    method: 'POST', url: healthUrl, data: { enabled: false }, timeout,
    onSuccess: (value) => successes.push(value), onError: (error) => failures.push(error),
  });
  return { details, successes, failures, isAborted: () => aborted };
}

test('serializes JSON and accepts successful JSON 2xx responses', () => {
  const request = pendingRequest();
  assert.equal(request.details.data, '{"enabled":false}');
  assert.equal(request.details.headers['Content-Type'], 'application/json');
  request.details.onload({ status: 201, responseText: '{"code":0}' });
  assert.deepEqual(request.successes, [{ code: 0 }]);
  assert.deepEqual(request.failures, []);
});

test('rejects empty, null, or primitive response bodies before consumers can get stuck dereferencing them', () => {
  for (const responseText of ['', 'null', 'true', '"success"', '[]']) {
    const request = pendingRequest();
    request.details.onload({ status: 200, responseText });
    assert.deepEqual(request.successes, []);
    assert.equal(request.failures[0].kind, 'invalid-response');
  }
});

test('reports HTTP rejection with remote-access reason instead of treating it as connectivity', () => {
  const request = pendingRequest();
  request.details.onload({ status: 403, responseText: '{"code":401}', responseHeaders: 'content-type: application/json\r\nx-bakabase-remote-access: Disabled\r\n' });
  assert.deepEqual(request.successes, []);
  assert.equal(request.failures[0].kind, 'http');
  assert.equal(request.failures[0].status, 403);
  assert.equal(request.failures[0].reason, 'Disabled');
});

test('reports malformed JSON and status-zero failures once', () => {
  const malformed = pendingRequest();
  malformed.details.onload({ status: 200, responseText: '<html>wrong server</html>' });
  malformed.details.onerror();
  assert.equal(malformed.failures.length, 1);
  assert.equal(malformed.failures[0].kind, 'invalid-response');
  assert.deepEqual(malformed.successes, []);
  const network = pendingRequest();
  network.details.onload({ status: 0, responseText: '' });
  assert.equal(network.failures[0].kind, 'network');
});

test('handles missing GM API and synchronous extension exceptions', () => {
  for (const transport of [undefined, () => { throw new Error('missing GM function'); }]) {
    let failure: RequestError | undefined;
    assert.doesNotThrow(() => sendRequest(transport, { method: 'GET', url: healthUrl, onError: (error) => { failure = error; } }));
    assert.equal(failure?.kind, 'transport');
  }
});

test('retains safe permission, connection, DNS and certificate error categories from GM errors', () => {
  for (const [diagnostic, kind, reason] of [
    ['Forbidden domain (not in @connect)', 'permission', undefined],
    ['URL is not permitted', 'permission', undefined],
    ['URL was permanently blocked by the user', 'permission', undefined],
    ['Request was blocked by the user', 'permission', undefined],
    ['net::ERR_NETWORK_ACCESS_DENIED', 'permission', undefined],
    ['background shutdown', 'transport', 'BackgroundUnavailable'],
    ['net::ERR_CONNECTION_REFUSED', 'network', 'ConnectionRefused'],
    ['net::ERR_NAME_NOT_RESOLVED', 'network', 'NameNotResolved'],
    ['net::ERR_CERT_AUTHORITY_INVALID', 'network', 'Certificate'],
  ] as const) {
    const request = pendingRequest();
    request.details.onerror({ error: diagnostic, status: 0 });
    assert.equal(request.failures[0].kind, kind);
    assert.equal(request.failures[0].reason, reason);
    assert.equal(request.failures[0].status, 0);
  }
  let failure: RequestError | undefined;
  sendRequest(() => { throw new Error('Permission denied'); }, { method: 'GET', url: healthUrl, onError: (error) => { failure = error; } });
  assert.equal(failure?.kind, 'permission');
});

test('logs the method and route while omitting query tokens, payloads and raw extension diagnostics', () => {
  sendRequest((details) => {
    details.onerror({ error: 'Permission denied for token=private-secret', responseText: 'private-secret' });
  }, { method: 'POST', url: `${healthUrl}?token=private-secret`, data: { cookie: 'private-secret' } });
  assert.equal(debugLog.mock.calls.at(-1)?.arguments[0], `[Bakabase] POST ${healthUrl}`);
  const failureArguments = errorLog.mock.calls.at(-1)?.arguments;
  assert.equal(failureArguments?.[0], '[Bakabase] POST request failed');
  assert.equal(failureArguments?.[1].url, healthUrl);
  assert.equal(JSON.stringify(failureArguments).includes('private-secret'), false);
});

test('rejects invalid request addresses before dispatch', () => {
  for (const url of ['/tampermonkey/health', 'file:///tmp/health', 'http://user:password@localhost/health']) {
    let dispatched = false;
    let failure: RequestError | undefined;
    sendRequest(() => { dispatched = true; }, { method: 'GET', url, onError: (error) => { failure = error; } });
    assert.equal(dispatched, false);
    assert.equal(failure?.kind, 'invalid-url');
  }
});

test('reports network, abort and native timeout callbacks exactly once', () => {
  for (const [callback, kind] of [['onerror', 'network'], ['onabort', 'aborted'], ['ontimeout', 'timeout']] as const) {
    const request = pendingRequest();
    request.details[callback]();
    request.details.onload({ status: 200, responseText: '{"code":0}' });
    assert.deepEqual(request.successes, []);
    assert.equal(request.failures.length, 1);
    assert.equal(request.failures[0].kind, kind);
  }
});

test('a silent extension failure times out, aborts, and ignores late success', async () => {
  const request = pendingRequest(5);
  await new Promise((resolve) => setTimeout(resolve, 20));
  assert.equal(request.isAborted(), true);
  assert.equal(request.failures.length, 1);
  assert.equal(request.failures[0].kind, 'timeout');
  request.details.onload({ status: 200, responseText: '{"code":0}' });
  assert.deepEqual(request.successes, []);
});

test('a throwing extension abort handle still reports timeout without an uncaught exception', async () => {
  let failure: RequestError | undefined;
  sendRequest(() => ({ abort() { throw new Error('broken handle'); } }), {
    method: 'GET', url: healthUrl, timeout: 5, onError: (error) => { failure = error; },
  });
  await new Promise((resolve) => setTimeout(resolve, 20));
  assert.equal(failure?.kind, 'timeout');
});
