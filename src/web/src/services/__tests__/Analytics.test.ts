import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  info: vi.fn(),
  snapshot: vi.fn(),
  sentry: vi.fn(),
  sentryTransport: vi.fn(),
  clarity: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: { app: { getAnalyticsAppInfo: mocks.info, getAppTelemetrySnapshot: mocks.snapshot } },
}));
vi.mock("@sentry/react", () => ({
  init: mocks.sentry,
  getClient: () => undefined,
  makeFetchTransport: mocks.sentryTransport,
}));
vi.mock("@microsoft/clarity", () => ({
  default: { init: mocks.clarity, identify: vi.fn(), consent: vi.fn() },
}));

const deploymentA = "10000000-0000-4000-8000-000000000001";
const deploymentB = "10000000-0000-4000-8000-000000000002";
const desktopId = "20000000-0000-4000-8000-000000000001";
const info = (deploymentId = deploymentA) => ({
  data: {
    enableAnonymousDataTracking: true,
    deploymentId,
    deviceId: "legacy-server-id-never-a-person",
    deploymentType: "docker_server",
    serverOs: "linux",
    serverArch: "arm64",
    environment: "production",
    appVersion: "2.4.0-beta.1",
    releaseChannel: "Beta",
    postHogApiKey: "phc_test",
    postHogApiHost: "https://us.i.posthog.com",
  },
});
let analytics: typeof import("../Analytics");
let context: typeof import("../analyticsContext");
let transport: ReturnType<typeof vi.fn<typeof fetch>>;
const sent = () =>
  transport.mock.calls
    .filter(([, init]) => init?.method === "POST")
    .map(([, init]) => JSON.parse(init!.body as string));
const settle = async () => {
  for (let i = 0; i < 15; i++) await Promise.resolve();
};

beforeEach(async () => {
  vi.resetModules();
  vi.clearAllMocks();
  vi.stubEnv("DEV", false);
  localStorage.clear();
  sessionStorage.clear();
  delete window.gtag;
  delete window.dataLayer;
  mocks.info.mockResolvedValue(info());
  mocks.snapshot.mockResolvedValue({ data: null });
  transport = vi.fn<typeof fetch>().mockResolvedValue(new Response("{}", { status: 200 }));
  vi.stubGlobal("fetch", transport);
  analytics = await import("../Analytics");
  context = await import("../analyticsContext");
});
afterEach(() => {
  analytics.stopAnalytics();
  vi.useRealTimers();
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("analytics v2 lifecycle", () => {
  it.each([
    ["resource_query_presented", "search"],
    ["resource_opened", "resources"],
    ["playback_requested", "playback"],
    ["playback_started", "playback"],
    ["playback_handoff", "playback"],
    ["reader_opened", "reader"],
  ])("fixes the semantic feature of %s independently of caller properties", (event, feature) => {
    expect(context.scrubAnalyticsProperties(event, { feature_id: "settings" })).toMatchObject({
      feature_id: feature,
    });
    expect(context.scrubAnalyticsProperties(event, { feature_id: "/private/file" })).toMatchObject({
      feature_id: feature,
    });
  });

  it.each(["development", "test", "unknown", "custom", undefined])(
    "disables every destination and API attribution in %s environments",
    async (environment) => {
      mocks.info.mockResolvedValue({
        data: {
          ...info().data,
          environment,
          ga4MeasurementId: "G-TEST",
          sentryDsn: "https://key@sentry.example/1",
        },
      });
      await analytics.initAnalytics();
      await settle();
      expect(context.getAnalyticsContext()).toBeUndefined();
      expect(sent()).toHaveLength(0);
      expect(mocks.sentry).not.toHaveBeenCalled();
      expect(window.gtag).toBeUndefined();
      expect(localStorage.getItem(context.ANALYTICS_CLIENT_KEY)).toBeNull();
    },
  );

  it("uses a persistent client identity, independent of server identity, and restores the first route", async () => {
    analytics.trackPageView("/resource?keyword=private-name#secret");
    await analytics.initAnalytics({ enabled: true, contextKey: "A", clientMode: 1 });
    await settle();
    const events = sent();
    expect(events.map((e) => e.event)).toEqual([
      "ui_session_started",
      "page_view",
      "feature_exposed",
    ]);
    const actor = events[0].properties.distinct_id;
    expect(actor).toBe(`v2:client:${localStorage.getItem(context.ANALYTICS_CLIENT_KEY)}`);
    expect(actor).not.toContain("legacy-server-id");
    expect(events[1].properties).toMatchObject({
      route: "/resource",
      schema_version: 2,
      identity_version: 2,
      actor_type: "client",
      deployment_id: deploymentA,
      client_surface: "browser",
      server_version: "2.4.0-beta.1",
    });
    expect(JSON.stringify(events)).not.toMatch(/private-name|secret|legacy-server-id/);
    expect(mocks.snapshot).not.toHaveBeenCalled();
    expect(transport.mock.calls[0][1]).toMatchObject({
      credentials: "omit",
      referrerPolicy: "no-referrer",
    });
  });

  it("deduplicates concurrent initialization and retries an initial network failure", async () => {
    mocks.info.mockRejectedValueOnce(new Error("offline"));
    await analytics.initAnalytics();
    expect(sent()).toHaveLength(0);
    await Promise.all([analytics.initAnalytics(), analytics.initAnalytics()]);
    await settle();
    expect(mocks.info).toHaveBeenCalledTimes(2);
    expect(sent().filter((e) => e.event === "ui_session_started")).toHaveLength(1);
  });

  it("does not send after consent is revoked during initialization", async () => {
    let resolve!: (value: ReturnType<typeof info>) => void;
    mocks.info.mockReturnValue(
      new Promise((r) => {
        resolve = r;
      }),
    );
    const pending = analytics.initAnalytics();
    await analytics.initAnalytics({ enabled: false });
    resolve(info());
    await pending;
    expect(sent()).toHaveLength(0);
    expect(context.analyticsRequestHeaders("/resource/search", "")).toEqual({});
  });

  it("rebinds server context without creating or merging a different client", async () => {
    await analytics.initAnalytics({ contextKey: "A" });
    await settle();
    const first = sent()[0].properties.distinct_id;
    mocks.info.mockResolvedValue(info(deploymentB));
    await analytics.initAnalytics({ contextKey: "B" });
    await settle();
    const second = sent().find((e) => e.properties.deployment_id === deploymentB);
    expect(second.properties.distinct_id).toBe(first);
    expect(second.properties.$groups).toBeUndefined();
    expect(sent().some((e) => e.event === "$identify" || e.event === "deployment_snapshot")).toBe(
      false,
    );
  });

  it("uses only the trusted native context for a shared desktop identity", async () => {
    vi.spyOn(navigator, "userAgent", "get").mockReturnValue("BakabaseDesktop/1.0");
    analytics.setDesktopAnalyticsResolver(async () => ({
      clientId: desktopId,
      clientSurface: "desktop_webview",
      clientVersion: "2.3.0",
      enableAnonymousDataTracking: true,
    }));
    await analytics.initAnalytics({ clientMode: 2 });
    await settle();
    expect(sent()[0].properties).toMatchObject({
      distinct_id: `v2:client:${desktopId}`,
      client_surface: "desktop_webview",
      connection_mode: "desktop_relay",
      client_version: "2.3.0",
      server_version: "2.4.0-beta.1",
      ui_version: "2.4.0-beta.1",
    });
  });

  it("honors desktop consent even when the target server allows collection", async () => {
    vi.spyOn(navigator, "userAgent", "get").mockReturnValue("BakabaseDesktop/1.0");
    analytics.setDesktopAnalyticsResolver(async () => ({
      clientId: null,
      clientSurface: "desktop_webview",
      enableAnonymousDataTracking: false,
    }));
    await analytics.initAnalytics({ clientMode: 2 });
    expect(sent()).toHaveLength(0);
    expect(context.getAnalyticsContext()).toBeUndefined();
    expect(localStorage.getItem(context.ANALYTICS_CLIENT_KEY)).toBeNull();
  });

  it("honors the HTTP native bridge's null-ID opt-out and stops an existing client", async () => {
    vi.spyOn(navigator, "userAgent", "get").mockReturnValue("BakabaseDesktop/1.0");
    let enabled = true;
    transport.mockImplementation(async (_url, request) =>
      request?.method === "POST"
        ? new Response("{}", { status: 200 })
        : new Response(
            JSON.stringify({
              code: 0,
              data: {
                clientId: enabled ? desktopId : null,
                clientSurface: "desktop_webview",
                enableAnonymousDataTracking: enabled,
              },
            }),
            { status: 200 },
          ),
    );
    await analytics.initAnalytics({ clientMode: 2 });
    await settle();
    expect(context.getAnalyticsContext()?.clientId).toBe(desktopId);
    const before = sent().length;
    enabled = false;
    await analytics.initAnalytics({ clientMode: 2, refresh: true });
    analytics.trackPageView("/collections");
    await settle();
    expect(sent()).toHaveLength(before);
    expect(context.getAnalyticsContext()).toBeUndefined();
    expect(localStorage.getItem(context.ANALYTICS_CLIENT_KEY)).toBeNull();
  });

  it("keeps UUID and event time on retry and discards retries on opt out", async () => {
    vi.useFakeTimers();
    transport.mockResolvedValue(new Response("{}", { status: 503 }));
    await analytics.initAnalytics();
    await settle();
    const original = sent()[0];
    await vi.advanceTimersByTimeAsync(5000);
    await settle();
    // The failed event is queued behind the remaining original events.
    expect(sent().every((e) => typeof e.uuid === "string" && typeof e.timestamp === "string")).toBe(
      true,
    );
    analytics.stopAnalytics();
    const count = sent().length;
    await vi.advanceTimersByTimeAsync(60_000);
    expect(sent()).toHaveLength(count);
    expect(context.getAnalyticsContext()).toBeUndefined();
    expect(original.uuid).toMatch(/^[0-9a-f-]{36}$/);
  });

  it("aborts in-flight delivery and clears the queue when disabled", async () => {
    let signal: AbortSignal | undefined;
    transport.mockImplementation(
      (_url, init) =>
        new Promise((_resolve, reject) => {
          signal = init?.signal as AbortSignal;
          signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")));
        }),
    );
    await analytics.initAnalytics();
    analytics.trackAnalyticsEvent("resource_opened");
    analytics.stopAnalytics();
    expect(signal?.aborted).toBe(true);
    await settle();
    expect(sent()).toHaveLength(1);
    analytics.trackAnalyticsEvent("resource_opened");
    expect(sent()).toHaveLength(1);
  });
});

describe("privacy boundary and request attribution", () => {
  it("drops arbitrary events, values, IDs, strings and dynamic paths", async () => {
    await analytics.initAnalytics();
    await settle();
    const before = sent().length;
    analytics.trackAnalyticsEvent("not_allowed" as never, { secret: "secret" });
    analytics.trackAnalyticsEvent("resource_query_presented", {
      has_keyword: true,
      result_count: 4,
      duration_ms: 1.5,
      keyword: "private",
      path: "/private/file",
      error: "secret",
      outcome: "success",
    });
    await settle();
    expect(sent()).toHaveLength(before + 1);
    const last = sent().at(-1).properties;
    expect(last).toMatchObject({
      has_keyword: true,
      result_count: 4,
      duration_ms: 2,
      outcome: "success",
    });
    expect(JSON.stringify(last)).not.toMatch(/private|secret/);
    expect(context.analyticsRoute("/resource/private/123?token=secret")).toBe("unknown");
    expect(context.scrubAnalyticsProperties("help_opened", { topic: "private-name" })).toEqual({});
  });

  it("sends attribution only to the configured product API after initialization", async () => {
    expect(
      context.analyticsRequestHeaders("https://api.example/resource", "https://api.example"),
    ).toEqual({});
    await analytics.initAnalytics();
    const first = context.analyticsRequestHeaders(
      "https://api.example/api/resource",
      "https://api.example/api",
    );
    const second = context.analyticsRequestHeaders(
      "https://api.example/api/resource",
      "https://api.example/api",
    );
    expect(first["X-Bakabase-Analytics-Consent"]).toBe("1");
    expect(first["X-Bakabase-Analytics-Client"]).toBe(context.getAnalyticsContext()!.clientId);
    expect(first["X-Bakabase-Analytics-Session"]).toBe(second["X-Bakabase-Analytics-Session"]);
    expect(first["X-Bakabase-Analytics-Operation"]).not.toBe(
      second["X-Bakabase-Analytics-Operation"],
    );
    expect(
      context.analyticsRequestHeaders(
        "https://thirdparty.example/api/resource",
        "https://api.example/api",
      ),
    ).toEqual({});
    expect(
      context.analyticsRequestHeaders(
        "https://api.example/api-evil/resource",
        "https://api.example/api",
      ),
    ).toEqual({});
  });

  it("keeps Clarity recording off unless the separate recording permission is explicit", async () => {
    mocks.info.mockResolvedValue({ data: { ...info().data, clarityProjectId: "project" } });
    await analytics.initAnalytics();
    expect(mocks.clarity).not.toHaveBeenCalled();
  });
});

describe("delivery and context race regressions", () => {
  it("retries a captured event with exactly the same UUID and timestamp", async () => {
    vi.useFakeTimers();
    await analytics.initAnalytics();
    await settle();
    transport.mockClear();
    transport.mockResolvedValue(new Response("{}", { status: 503 }));
    analytics.trackAnalyticsEvent("resource_opened");
    await settle();
    await vi.advanceTimersByTimeAsync(5000);
    await settle();
    await vi.advanceTimersByTimeAsync(10_000);
    await settle();
    expect(sent()).toHaveLength(3);
    expect(sent()[0]).toEqual(sent()[1]);
    expect(sent()[0]).toEqual(sent()[2]);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(sent()).toHaveLength(3);
  });

  it("bounds the pending queue and sends only one small event per capture request", async () => {
    await analytics.initAnalytics();
    await settle();
    transport.mockClear();
    let release!: (value: Response) => void;
    transport.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          release = resolve;
        }),
    );
    for (let i = 0; i < 100; i++) analytics.trackAnalyticsEvent("resource_opened");
    release(new Response("{}"));
    await settle();
    for (let i = 0; i < 100; i++) await Promise.resolve();
    expect(sent()).toHaveLength(65); // one in flight and at most 64 waiting
    expect(sent().every((event) => !event.batch && JSON.stringify(event).length < 4096)).toBe(true);
  });

  it("ignores an older server initialization that finishes after the next server", async () => {
    let resolveOld!: (value: ReturnType<typeof info>) => void;
    mocks.info.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveOld = resolve;
      }),
    );
    const old = analytics.initAnalytics({ contextKey: "old" });
    mocks.info.mockResolvedValue(info(deploymentB));
    await analytics.initAnalytics({ contextKey: "new" });
    resolveOld(info(deploymentA));
    await old;
    await settle();
    expect(context.getAnalyticsContext()!.deployment_id).toBe(deploymentB);
    expect(sent().every((event) => event.properties.deployment_id === deploymentB)).toBe(true);
  });

  it("rechecks consent on focus without inflating launches or page views", async () => {
    await analytics.initAnalytics({ contextKey: "A" });
    await settle();
    const count = sent().length;
    await analytics.initAnalytics({ contextKey: "A", refresh: true });
    await settle();
    expect(sent()).toHaveLength(count);
    mocks.info.mockResolvedValue({ data: { ...info().data, enableAnonymousDataTracking: false } });
    await analytics.initAnalytics({ contextKey: "A", refresh: true });
    expect(context.getAnalyticsContext()).toBeUndefined();
  });

  it("fails closed and retries when a native bridge cannot be read", async () => {
    vi.useFakeTimers();
    vi.spyOn(navigator, "userAgent", "get").mockReturnValue("BakabaseDesktop/1.0");
    const resolver = vi.fn().mockRejectedValueOnce(new Error("unavailable")).mockResolvedValue({
      clientId: desktopId,
      clientSurface: "desktop_webview",
      enableAnonymousDataTracking: true,
    });
    analytics.setDesktopAnalyticsResolver(resolver);
    await analytics.initAnalytics({ clientMode: 2 });
    await settle();
    expect(sent()).toHaveLength(0);
    expect(context.getAnalyticsContext()).toBeUndefined();
    expect(localStorage.getItem(context.ANALYTICS_CLIENT_KEY)).toBeNull();
    await vi.advanceTimersByTimeAsync(5000);
    expect(sent()[0].properties.distinct_id).toBe(`v2:client:${desktopId}`);
    expect(sent()[0].properties.client_surface).toBe("desktop_webview");
  });

  it("does not collect under a browser ID after a native HTTP bridge timeout", async () => {
    vi.useFakeTimers();
    vi.spyOn(navigator, "userAgent", "get").mockReturnValue("BakabaseDesktop/1.0");
    transport.mockImplementation(
      (_url, request) =>
        new Promise((_resolve, reject) => {
          request?.signal?.addEventListener("abort", () =>
            reject(new DOMException("Abort", "AbortError")),
          );
        }),
    );
    const initializing = analytics.initAnalytics({ clientMode: 2 });
    await settle();
    await vi.advanceTimersByTimeAsync(3000);
    await initializing;
    expect(sent()).toHaveLength(0);
    expect(context.getAnalyticsContext()).toBeUndefined();
    expect(localStorage.getItem(context.ANALYTICS_CLIENT_KEY)).toBeNull();
  });

  it("uses semantic feature IDs, strips dynamic route data and does not create person profiles", async () => {
    await analytics.initAnalytics();
    await settle();
    analytics.trackPageView("/workflows?definition=private");
    await settle();
    expect(sent().at(-1).properties).toMatchObject({
      feature_id: "workflow",
      route: "/workflows",
      $process_person_profile: false,
    });
    expect(
      context.scrubAnalyticsProperties("feature_invoked", { feature_id: "/private/path" }),
    ).toBeUndefined();
    expect(context.scrubAnalyticsProperties("toString", {})).toBeUndefined();
  });

  it("retains static Sentry bundle locations while dropping private request and diagnostic content", async () => {
    mocks.info.mockResolvedValue({
      data: { ...info().data, sentryDsn: "https://key@sentry.example/1" },
    });
    await analytics.initAnalytics();
    const config = mocks.sentry.mock.calls[0][0];
    expect(config.autoSessionTracking).toBe(false);
    expect(config.sendClientReports).toBe(false);
    const clean = config.beforeSend({
      request: { url: "https://local/file?key=private" },
      extra: { secret: "private" },
      contexts: { raw: { path: "private" } },
      breadcrumbs: [{ message: "private" }],
      transaction: "private",
      fingerprint: ["private"],
      exception: {
        values: [
          {
            type: "TypeError",
            value: "private",
            mechanism: { type: "generic", data: { secret: "private" } },
            stacktrace: {
              frames: [
                {
                  filename: "http://local/assets/index-ab12.js?token=private",
                  lineno: 14,
                  colno: 4,
                  vars: { secret: "private" },
                },
              ],
            },
          },
        ],
      },
    });
    expect(JSON.stringify(clean)).not.toContain("private");
    expect(clean.exception.values[0].stacktrace.frames[0]).toMatchObject({
      filename: "index-ab12.js",
      lineno: 14,
      colno: 4,
    });
    analytics.stopAnalytics();
    expect(config.beforeSend({})).toBeNull();
  });

  it("aborts Sentry delivery and blocks stale transport calls after revocation", async () => {
    mocks.info.mockResolvedValue({
      data: { ...info().data, sentryDsn: "https://key@sentry.example/1" },
    });
    await analytics.initAnalytics();
    await settle();
    mocks.sentry.mock.calls[0][0].transport({ url: "https://sentry.example/envelope" });
    const send = mocks.sentryTransport.mock.calls[0][1];
    let signal: AbortSignal | undefined;
    transport.mockImplementation(
      (_url, request) =>
        new Promise((_resolve, reject) => {
          signal = request?.signal as AbortSignal;
          expect(request?.keepalive).toBe(false);
          expect(request?.referrerPolicy).toBe("no-referrer");
          signal?.addEventListener("abort", () => reject(new DOMException("Abort", "AbortError")));
        }),
    );
    const pending = send("https://sentry.example/envelope", { method: "POST", body: "sanitized" });
    const cancelled = expect(pending).rejects.toMatchObject({ name: "AbortError" });
    analytics.stopAnalytics();
    await cancelled;
    expect(signal?.aborted).toBe(true);
    const count = transport.mock.calls.length;
    await expect(send("https://sentry.example/envelope", {})).rejects.toMatchObject({
      name: "AbortError",
    });
    expect(transport.mock.calls.length).toBe(count);
  });
});
