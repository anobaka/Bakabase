// What the smoke's browser may reach: the fixture hosts on this machine, and nothing else.
//
// The fixtures serve the production frontend, which starts Clarity, GA4, PostHog and Sentry
// whenever its server hands it a project id. The test hosts blank every one of those and turn
// anonymous tracking off (FixtureAnalytics), and `assertNoAnalytics` checks what the frontend
// is told; this is the line behind it, for a tracker or a CDN that arrives some other way.
const assert = require('node:assert/strict');

/**
 * Chromium flags: every address but localhost and 127.0.0.1 — literal IPs included — fails to
 * resolve. Covers what a route cannot refuse, such as WebSockets and preconnects.
 */
const LAUNCH_ARGS = ['--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE localhost, EXCLUDE 127.0.0.1'];

const LOOPBACK_NAMES = new Set(['127.0.0.1', 'localhost', '[::1]']);
const isLoopback = url => LOOPBACK_NAMES.has(url.hostname) || url.hostname.endsWith('.localhost');

/** The binding a page reports each WebSocket it makes through, as it makes it. */
const SOCKET_BINDING = '__bakabaseSmokeSocket';

/**
 * Runs in every frame of every page before the page's own scripts: `WebSocket` becomes a
 * subclass that reports where each socket is going and then opens it exactly as the browser's
 * own class does — the handshake, its headers included, is still the browser's. Reported
 * before the socket exists, so a page that navigates away or closes the moment it made one has
 * still said so. (Page-side because Playwright only announces a WebSocket once its handshake
 * has been sent or it has failed, and forgets it when its page navigates: a socket whose lookup
 * the resolver refuses after that is refused and never reported.)
 */
function recordSockets(binding) {
  const Native = globalThis.WebSocket;
  if (typeof Native !== 'function' || Native[binding]) return;
  class WebSocket extends Native {
    constructor(url, protocols) {
      let target;
      try {
        target = new URL(url, location.href);
        if (target.protocol === 'http:' || target.protocol === 'https:')
          target.protocol = target.protocol === 'http:' ? 'ws:' : 'wss:';
      } catch {
        target = null;
      }
      const report = globalThis[binding];
      if (typeof report === 'function') report(target ? target.href : String(url));
      super(url, protocols);
    }
  }
  Object.defineProperty(WebSocket, binding, { value: true });
  globalThis.WebSocket = WebSocket;
}

/**
 * Refuses every request of `context` that `admits` does not accept (it is given an http(s)
 * URL), and records each refused attempt once: the kind, origin and path, never the query,
 * where a tracker puts its keys. WebSockets cannot be refused this way — Playwright routes
 * them only by replacing the page's WebSocket class — so they are left to the resolver rules,
 * and recorded by two witnesses: the page itself, as each socket is made (`recordSockets`),
 * and Playwright's report, the only one a worker's socket gets. Returns the record:
 * `external` (anything off this machine) and `loopback` (a port of this machine outside the
 * fixtures). Read it through `assertStayedLocal`, which waits for every open page's reports.
 */
async function confine(context, admits) {
  const attempts = { external: [], loopback: [] };
  const noted = new Set();
  const note = (url, kind) => {
    const entry = `${kind} ${url.protocol}//${url.host}${url.pathname}`;
    // One socket is reported by both of its witnesses.
    if (noted.has(entry)) return;
    noted.add(entry);
    (isLoopback(url) ? attempts.loopback : attempts.external).push(entry);
  };
  const noteSocket = href => {
    let url;
    try { url = new URL(href); } catch { return; }
    if (!admits(new URL(url.href.replace(/^ws/, 'http')))) note(url, 'websocket');
  };
  await context.route('**/*', route => {
    const url = new URL(route.request().url());
    if (admits(url)) return route.continue();
    note(url, route.request().resourceType());
    return route.abort('blockedbyclient');
  });
  // `null` is assertStayedLocal's flush: it records nothing, and answers only once every
  // report the page made before it has been recorded.
  await context.exposeBinding(SOCKET_BINDING, (_source, href) => { if (href !== null) noteSocket(String(href)); });
  await context.addInitScript(recordSockets, SOCKET_BINDING);
  const watch = page => page.on('websocket', socket => noteSocket(socket.url()));
  context.pages().forEach(watch);
  context.on('page', watch);
  return attempts;
}

/**
 * Waits until every report each open page of `context` has made is in the record: a page's
 * binding calls are answered in the order it made them, so once a last, empty one comes back,
 * every socket it reported before is recorded. A closed page's were delivered before it closed.
 */
async function flushSocketReports(context) {
  for (const page of context.pages()) {
    if (page.isClosed()) continue;
    await page.evaluate(binding => typeof globalThis[binding] === 'function' ? globalThis[binding](null) : null,
      SOCKET_BINDING).catch(() => {});
  }
}

/**
 * Shows, on every run, that `confine` holds: a page on `origin` sends what trackers send — a
 * fetch, a beacon, an image, WebSockets — to addresses that can never exist (`.invalid`), and
 * every one must be refused and recorded. The sockets include the ones Playwright alone never
 * reports: one whose page navigates on at once, one whose page is closed at once, and one in a
 * frame; those must be in the record without waiting for anything. Returns the record.
 */
async function proveConfinement(browser, origin) {
  const context = await browser.newContext();
  try {
    const attempts = await confine(context, url => url.origin === origin);
    const page = await context.newPage();
    await page.goto(origin + '/remote-access/server-info');
    const fetched = await page.evaluate(async () => {
      navigator.sendBeacon('https://tracker.invalid/beacon', 'canary');
      const image = new Image();
      const loaded = new Promise(resolve => { image.onload = () => resolve('loaded'); image.onerror = () => resolve('refused'); });
      image.src = 'https://tracker.invalid/pixel.gif';
      new WebSocket('wss://tracker.invalid/socket');
      const fetched = await fetch('https://tracker.invalid/collect?key=canary', { mode: 'no-cors' })
        .then(() => 'reached', () => 'refused');
      return [fetched, await loaded];
    });
    assert.deepEqual(fetched, ['refused', 'refused'], 'A request off this machine was not refused');
    // Made, then left behind by a navigation before the refused lookup could come back.
    await page.evaluate(() => { new WebSocket('wss://navigated.tracker.invalid/socket?key=canary'); });
    await page.goto(origin + '/remote-access/server-info?again');
    // Made by a frame of the page.
    await page.evaluate(() => new Promise(resolve => {
      const frame = document.createElement('iframe');
      frame.srcdoc = '<script>new WebSocket("wss://framed.tracker.invalid/socket")</script>';
      frame.onload = resolve;
      document.body.append(frame);
    }));
    // Made by a page that is closed straight after.
    const closing = await context.newPage();
    await closing.goto(origin + '/remote-access/server-info');
    await closing.evaluate(() => { new WebSocket('wss://closed.tracker.invalid/socket'); });
    await closing.close();
    await flushSocketReports(context);
    for (const url of ['wss://navigated.tracker.invalid/socket', 'wss://framed.tracker.invalid/socket',
      'wss://closed.tracker.invalid/socket', 'wss://tracker.invalid/socket'])
      assert.ok(attempts.external.includes(`websocket ${url}`), `${url} was not recorded at once: ${JSON.stringify(attempts)}`);
    // A worker's socket has no page-side witness; Playwright reports it once it has failed.
    await page.evaluate(() => {
      const source = 'new WebSocket("wss://worker.tracker.invalid/socket")';
      new Worker(URL.createObjectURL(new Blob([source], { type: 'text/javascript' })));
    });
    const expected = ['https://tracker.invalid/beacon', 'https://tracker.invalid/pixel.gif',
      'https://tracker.invalid/collect', 'wss://worker.tracker.invalid/socket'];
    const deadline = Date.now() + 5000;
    while (!expected.every(url => attempts.external.some(entry => entry.endsWith(' ' + url))) && Date.now() < deadline)
      await new Promise(resolve => setTimeout(resolve, 100));
    for (const url of expected)
      assert.ok(attempts.external.some(entry => entry.endsWith(' ' + url)), `${url} was not recorded: ${JSON.stringify(attempts)}`);
    assert.ok(attempts.external.every(entry => !entry.includes('canary')), 'A query reached the record');
    return attempts.external;
  } finally {
    await context.close();
  }
}

/**
 * Fails when any page of the stage tried to reach an address off this machine — once every
 * open page's socket reports are in, so a socket made just before cannot slip past.
 */
async function assertStayedLocal(context, attempts, stage) {
  await flushSocketReports(context);
  assert.deepEqual(attempts.external, [], `${stage}: a page tried to reach an address off this machine`);
}

/** What the frontend of the Service at `base` is told about analytics: nothing to report to. */
async function assertNoAnalytics(base) {
  const response = await fetch(base + '/app/analytics-info');
  assert.equal(response.status, 200, `${base}/app/analytics-info: HTTP ${response.status}`);
  const { data } = await response.json();
  const configured = ['clarityProjectId', 'ga4MeasurementId', 'sentryDsn', 'postHogApiKey'].filter(key => data[key]);
  assert.deepEqual(configured, [], `${base} hands its frontend analytics project ids`);
  assert.equal(data.enableAnonymousDataTracking, false, `${base} has anonymous tracking on`);
}

module.exports = { LAUNCH_ARGS, confine, proveConfinement, assertStayedLocal, assertNoAnalytics };
