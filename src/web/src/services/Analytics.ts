import Clarity from "@microsoft/clarity";
import * as Sentry from "@sentry/react";

import BApi from "@/sdk/BApi";
import {
  analyticsRoute,
  analyticsFeature,
  analyticsSessionId,
  bindAnalyticsContext,
  browserAnalyticsId,
  getAnalyticsContext,
  isAnalyticsId,
  newAnalyticsId,
  scrubAnalyticsProperties,
  type AnalyticsContext,
  type AnalyticsEvent,
} from "./analyticsContext";

export { analyticsRoute } from "./analyticsContext";

declare global {
  interface Window {
    dataLayer?: unknown[];
    gtag?: (...args: unknown[]) => void;
    clarity?: (...args: unknown[]) => void;
  }
}

interface AnalyticsInfo {
  enableAnonymousDataTracking: boolean;
  deploymentId?: string;
  deploymentType?: string;
  serverOs?: string;
  serverArch?: string;
  environment?: string;
  appVersion: string;
  releaseChannel: string;
  ga4MeasurementId?: string;
  postHogApiKey?: string;
  postHogApiHost?: string;
  sentryDsn?: string;
  clarityProjectId?: string;
  /** Recording needs separate, explicit consent; anonymous product analytics never enables it. */
  enableSessionReplay?: boolean;
}
export interface DesktopAnalyticsContext {
  clientId: string | null;
  clientSurface: "desktop_webview";
  clientVersion?: string;
  enableAnonymousDataTracking: boolean;
}
export interface AnalyticsInitOptions {
  enabled?: boolean;
  contextKey?: string;
  clientMode?: number;
  /** Re-read consent and the native bridge when a long-lived window regains focus. */
  refresh?: boolean;
}

let enabled = false;
let generation = 0;
let pendingInit: Promise<void> | undefined;
let readyKey: string | undefined;
let requestedKey = "";
let latestOptions: AnalyticsInitOptions = {};
let retryInitTimer: ReturnType<typeof setTimeout> | undefined;
let pendingRoute = "/";
let lastPage: string | undefined;
const startedSessions = new Set<string>();
let gaId: string | undefined;
let replayActive = false;
let posthog: { key: string; host: string } | undefined;
let desktopResolver: ((mode?: number) => Promise<DesktopAnalyticsContext | undefined>) | undefined;

/** A native bridge may supply this instead of HTTP; it must already enforce the local trust boundary. */
export function setDesktopAnalyticsResolver(resolver?: typeof desktopResolver): void {
  desktopResolver = resolver;
}

async function resolveDesktopContext(mode?: number): Promise<DesktopAnalyticsContext | undefined> {
  if (!navigator.userAgent.includes("BakabaseDesktop/")) return undefined;
  if (desktopResolver) {
    try {
      const value = await desktopResolver(mode);
      return validDesktopContext(value) ? value : undefined;
    } catch {
      return undefined;
    }
  }
  const path = mode === 2 ? "/client/analytics-context" : "/app/analytics-client-context";
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 3000);
  try {
    // Always this window's origin: a remote API endpoint must never receive the bridge request.
    const response = await fetch(path, { signal: controller.signal, credentials: "same-origin" });
    if (!response.ok) return undefined;
    const body = await response.json();
    return body?.code === 0 && validDesktopContext(body.data) ? body.data : undefined;
  } catch {
    return undefined;
  } finally {
    clearTimeout(timeout);
  }
}

function validDesktopContext(value: unknown): value is DesktopAnalyticsContext {
  const candidate = value as Partial<DesktopAnalyticsContext> | undefined;
  // An explicit opt-out deliberately withholds the native ID. Do not interpret that as
  // an unavailable bridge and start collecting under a new browser identity instead.
  return (
    candidate?.clientSurface === "desktop_webview" &&
    (candidate.enableAnonymousDataTracking === false ||
      (candidate.enableAnonymousDataTracking === true && isAnalyticsId(candidate.clientId)))
  );
}

interface PendingCapture {
  url: string;
  body: string;
  createdAt: number;
  attempts: number;
  generation: number;
}
const MAX_QUEUE = 64;
const CAPTURE_TTL = 10 * 60_000;
const queue: PendingCapture[] = [];
const requests = new Set<AbortController>();
let retryTimer: ReturnType<typeof setTimeout> | undefined;
let sending = false;

/** Small in-memory queue: no hidden SDK URL properties, disk backlog or post-consent retries. */
async function drainCaptures(): Promise<void> {
  if (sending || !enabled || !getAnalyticsContext()) return;
  sending = true;
  try {
    while (queue.length && enabled) {
      const item = queue.shift()!;
      if (item.generation !== generation || Date.now() - item.createdAt > CAPTURE_TTL) continue;
      const controller = new AbortController();
      requests.add(controller);
      const timeout = setTimeout(() => controller.abort(), 10_000);
      let retry = false;
      try {
        const response = await fetch(item.url, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: item.body,
          credentials: "omit",
          referrerPolicy: "no-referrer",
          signal: controller.signal,
        });
        retry = response.status === 429 || response.status >= 500;
      } catch {
        retry = true;
      } finally {
        clearTimeout(timeout);
        requests.delete(controller);
      }
      if (
        retry &&
        enabled &&
        item.generation === generation &&
        ++item.attempts < 3 &&
        Date.now() - item.createdAt <= CAPTURE_TTL
      ) {
        if (queue.length < MAX_QUEUE) queue.push(item);
        retryTimer = setTimeout(() => {
          retryTimer = undefined;
          void drainCaptures();
        }, item.attempts * 5000);
        break;
      }
    }
  } finally {
    sending = false;
    if (enabled && queue.length && !retryTimer) void drainCaptures();
  }
}

function clearCaptures(): void {
  queue.length = 0;
  if (retryTimer) clearTimeout(retryTimer);
  retryTimer = undefined;
  for (const request of requests) request.abort();
  requests.clear();
}

export function stopAnalytics(): void {
  enabled = false;
  generation++;
  readyKey = undefined;
  pendingInit = undefined;
  posthog = undefined;
  lastPage = undefined;
  bindAnalyticsContext(undefined);
  clearCaptures();
  if (retryInitTimer) clearTimeout(retryInitTimer);
  retryInitTimer = undefined;
  if (gaId) (window as unknown as Record<string, unknown>)[`ga-disable-${gaId}`] = true;
  window.gtag?.("consent", "update", { analytics_storage: "denied", ad_storage: "denied" });
  gaId = undefined;
  if (window.dataLayer) window.dataLayer.length = 0;
  if (replayActive) {
    Clarity.consent(false);
    window.clarity?.("stop");
    replayActive = false;
  }
  const sentry = Sentry.getClient();
  if (sentry) sentry.getOptions().enabled = false;
}

function loadGtag(measurementId: string, clientId: string): void {
  gaId = measurementId;
  (window as unknown as Record<string, unknown>)[`ga-disable-${measurementId}`] = false;
  if (!window.gtag) {
    window.dataLayer = [];
    window.gtag = (...args: unknown[]) => {
      window.dataLayer!.push(args);
    };
    window.gtag("consent", "default", { analytics_storage: "denied", ad_storage: "denied" });
    window.gtag("js", new Date());
    const script = document.createElement("script");
    script.async = true;
    script.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(measurementId)}`;
    script.referrerPolicy = "no-referrer";
    document.head.appendChild(script);
  }
  window.gtag("consent", "update", { analytics_storage: "granted", ad_storage: "denied" });
  window.gtag("config", measurementId, {
    client_id: `v2:client:${clientId}`,
    send_page_view: false,
    anonymize_ip: true,
    allow_google_signals: false,
    allow_ad_personalization_signals: false,
    page_location: "https://bakabase.invalid/",
    page_referrer: "",
    page_title: "Bakabase",
  });
}

function loadSentry(info: AnalyticsInfo): void {
  if (!info.sentryDsn || import.meta.env.DEV) return;
  const permission = generation;
  Sentry.init({
    dsn: info.sentryDsn,
    release: info.appVersion,
    environment: info.environment,
    sendDefaultPii: false,
    tracesSampleRate: 0,
    replaysSessionSampleRate: 0,
    replaysOnErrorSampleRate: 0,
    autoSessionTracking: false,
    sendClientReports: false,
    transport: (options) =>
      Sentry.makeFetchTransport(options, async (input, init) => {
        if (!enabled || permission !== generation || !getAnalyticsContext())
          throw new DOMException("Analytics disabled", "AbortError");
        const controller = new AbortController();
        requests.add(controller);
        const timeout = setTimeout(() => controller.abort(), 10_000);
        try {
          return await fetch(input, {
            ...init,
            credentials: "omit",
            keepalive: false,
            referrerPolicy: "no-referrer",
            signal: controller.signal,
          });
        } finally {
          clearTimeout(timeout);
          requests.delete(controller);
        }
      }),
    beforeBreadcrumb: () => null,
    beforeSend(event) {
      const context = getAnalyticsContext();
      if (!enabled || !context) return null;
      // Backend messages, request bodies and URLs can all contain local media names and secrets.
      event.request = undefined;
      event.breadcrumbs = undefined;
      event.extra = undefined;
      event.contexts = undefined;
      event.message = event.message ? "Client error" : undefined;
      event.user = { id: `v2:client:${context.clientId}` };
      event.tags = {
        route: analyticsRoute(pendingRoute),
        deployment_type: context.deployment_type,
      };
      for (const exception of event.exception?.values ?? []) {
        exception.value = "Client error";
        exception.type = ["TypeError", "RangeError", "SyntaxError", "ReferenceError"].includes(
          exception.type ?? "",
        )
          ? exception.type
          : "Error";
        exception.mechanism = exception.mechanism
          ? { type: "generic", handled: exception.mechanism.handled }
          : undefined;
        for (const frame of exception.stacktrace?.frames ?? []) {
          frame.filename = frame.filename?.match(/(?:^|\/)([\w.-]+\.[cm]?js)(?:[?#].*)?$/)?.[1];
          frame.abs_path = undefined;
          frame.module = undefined;
          frame.vars = undefined;
          frame.pre_context = undefined;
          frame.post_context = undefined;
          frame.context_line = undefined;
        }
      }
      return {
        type: undefined,
        event_id: event.event_id,
        timestamp: event.timestamp,
        platform: "javascript",
        level: event.level,
        release: event.release,
        environment: event.environment,
        exception: event.exception,
        message: event.message,
        user: event.user,
        tags: event.tags,
      };
    },
  });
}

const safeVersion = (value?: string) =>
  value && /^[\w.+-]{1,80}$/.test(value) ? value : "unknown";
const known = (value: string | undefined, values: readonly string[]) =>
  value && values.includes(value) ? value : "unknown";

/** Retryable, latest-context-wins initialization. A failed/disabled attempt never latches success. */
export function initAnalytics(options: AnalyticsInitOptions = {}): Promise<void> {
  latestOptions = options;
  if (options.enabled === false) {
    stopAnalytics();
    return Promise.resolve();
  }
  const key = `${options.contextKey ?? ""}:${options.clientMode ?? ""}`;
  if (!options.refresh && enabled && readyKey === key && getAnalyticsContext())
    return Promise.resolve();
  if (pendingInit && requestedKey === key) return pendingInit;
  const previousContext = getAnalyticsContext();
  const previousPage = readyKey === key ? lastPage : undefined;
  stopAnalytics();
  enabled = true;
  requestedKey = key;
  const attempt = generation;
  const work = (async () => {
    try {
      const info = (await BApi.app.getAnalyticsAppInfo({ showErrorToast: false })).data as
        | AnalyticsInfo
        | undefined;
      if (attempt !== generation || !enabled) return;
      if (!info) throw new Error("Analytics context unavailable");
      if (!info.enableAnonymousDataTracking) {
        stopAnalytics();
        return;
      }
      if (
        import.meta.env.DEV ||
        !["production", "staging"].includes(info.environment?.toLowerCase() ?? "")
      ) {
        stopAnalytics();
        return;
      }
      if (!isAnalyticsId(info.deploymentId)) throw new Error("Analytics context unavailable");
      const desktop = await resolveDesktopContext(options.clientMode);
      if (attempt !== generation || !enabled) return;
      // A native window must verify local consent before using a remote server's settings.
      // Bridge failure is retryable; falling back to a browser ID would bypass local opt-out.
      if (navigator.userAgent.includes("BakabaseDesktop/") && !desktop)
        throw new Error("Desktop analytics context unavailable");
      if (desktop?.enableAnonymousDataTracking === false) {
        stopAnalytics();
        return;
      }
      const context: AnalyticsContext = {
        clientId: desktop?.clientId ?? browserAnalyticsId(),
        deployment_id: info.deploymentId,
        server_version: safeVersion(info.appVersion),
        ui_version: safeVersion(import.meta.env.VITE_APP_VERSION || info.appVersion),
        client_version: safeVersion(
          desktop ? desktop.clientVersion : import.meta.env.VITE_APP_VERSION || info.appVersion,
        ),
        release_channel: known(info.releaseChannel?.toLowerCase(), [
          "stable",
          "beta",
          "rc",
          "dev",
          "alpha",
        ]),
        deployment_type: known(info.deploymentType, [
          "desktop_all_in_one",
          "standalone_server",
          "docker_server",
        ]) as AnalyticsContext["deployment_type"],
        client_surface: desktop ? "desktop_webview" : "browser",
        server_os: known(info.serverOs?.toLowerCase(), [
          "windows",
          "macos",
          "linux",
          "android",
          "ios",
        ]),
        server_arch: known(info.serverArch?.toLowerCase(), ["x64", "x86", "arm64", "arm", "amd64"]),
        environment: import.meta.env.DEV
          ? "development"
          : known(info.environment?.toLowerCase(), [
              "production",
              "development",
              "test",
              "staging",
            ]),
        connection_mode:
          options.clientMode === 2
            ? "desktop_relay"
            : options.clientMode === 0
              ? "local"
              : "remote",
      };
      bindAnalyticsContext(context);
      if (
        previousContext?.clientId === context.clientId &&
        previousContext?.deployment_id === context.deployment_id
      )
        lastPage = previousPage;
      readyKey = key;
      try {
        loadSentry(info);
      } catch {
        /* An unavailable destination must not stop product use. */
      }
      if (info.ga4MeasurementId) {
        try {
          loadGtag(info.ga4MeasurementId, context.clientId);
        } catch {
          /* independent destination */
        }
      }
      if (info.enableSessionReplay === true && info.clarityProjectId && !import.meta.env.DEV) {
        try {
          Clarity.init(info.clarityProjectId);
          Clarity.identify(`v2:client:${context.clientId}`);
          Clarity.consent(true);
          replayActive = true;
        } catch {
          /* independent destination */
        }
      }
      if (info.postHogApiKey && info.postHogApiHost && !import.meta.env.DEV) {
        const host = new URL(info.postHogApiHost);
        if (host.protocol === "https:" && !host.username && !host.password) {
          posthog = {
            key: info.postHogApiKey,
            host: `${host.origin}${host.pathname.replace(/\/$/, "")}/capture/`,
          };
        }
      }
      const sessionKey = `${context.clientId}:${context.deployment_id}:${analyticsSessionId()}`;
      if (!startedSessions.has(sessionKey)) {
        startedSessions.add(sessionKey);
        trackAnalyticsEvent("ui_session_started");
      }
      trackPageView(pendingRoute);
      if (gaId) void pushGaSnapshot(context, attempt);
    } catch {
      if (attempt === generation && enabled) {
        bindAnalyticsContext(undefined);
        readyKey = undefined;
        retryInitTimer = setTimeout(() => {
          retryInitTimer = undefined;
          void initAnalytics(latestOptions);
        }, 5000);
      }
    }
  })();
  pendingInit = work;
  void work.finally(() => {
    if (pendingInit === work) pendingInit = undefined;
  });
  return work;
}

/** GA keeps its compatibility snapshot; PostHog snapshots belong exclusively to the server. */
async function pushGaSnapshot(context: AnalyticsContext, attempt: number): Promise<void> {
  const key = `bakabase.analytics.v2.ga.snapshot:${gaId}:${context.deployment_id}`;
  try {
    let lastSent = 0;
    try {
      lastSent = Number(localStorage.getItem(key));
    } catch {
      /* Storage is optional. */
    }
    if (Date.now() - lastSent < 24 * 60 * 60_000) return;
    const snapshot = (await BApi.app.getAppTelemetrySnapshot({ showErrorToast: false })).data;
    if (!snapshot || !enabled || attempt !== generation || !gaId || !window.gtag) return;
    window.gtag("event", "deployment_snapshot", {
      ...commonProperties(context),
      media_library_count: snapshot.mediaLibraryCount,
      resource_count: snapshot.resourceCount,
      ai_enabled: snapshot.aiEnabled,
      page_location: "https://bakabase.invalid/",
      page_referrer: "",
      page_title: "Bakabase",
    });
    try {
      localStorage.setItem(key, String(Date.now()));
    } catch {
      /* Storage is optional. */
    }
  } catch {
    /* No durable success marker when loading/delivery scheduling fails. */
  }
}

function commonProperties(context: AnalyticsContext) {
  const { clientId: _clientId, ...properties } = context;
  return {
    ...properties,
    schema_version: 2,
    identity_version: 2,
    actor_type: "client",
    session_id: analyticsSessionId(),
  };
}

export function trackAnalyticsEvent(
  event: AnalyticsEvent,
  input: Record<string, unknown> = {},
): void {
  const context = getAnalyticsContext();
  if (!enabled || !context) return;
  const properties = scrubAnalyticsProperties(event, input);
  if (!properties) return;
  const common = commonProperties(context);
  if (gaId && window.gtag)
    window.gtag("event", event, {
      ...common,
      ...properties,
      page_location: `https://bakabase.invalid${analyticsRoute(pendingRoute) === "unknown" ? "/" : analyticsRoute(pendingRoute)}`,
      page_referrer: "",
      page_title: "Bakabase",
    });
  if (posthog && queue.length < MAX_QUEUE) {
    queue.push({
      url: posthog.host,
      createdAt: Date.now(),
      attempts: 0,
      generation,
      body: JSON.stringify({
        api_key: posthog.key,
        event,
        uuid: newAnalyticsId(),
        timestamp: new Date().toISOString(),
        properties: {
          ...common,
          ...properties,
          distinct_id: `v2:client:${context.clientId}`,
          $session_id: analyticsSessionId(),
          $process_person_profile: false,
          $geoip_disable: true,
        },
      }),
    });
    if (!retryTimer) void drainCaptures();
  }
}

export function trackPageView(path: string): void {
  pendingRoute = analyticsRoute(path);
  const context = getAnalyticsContext();
  if (!enabled || !context) return;
  const key = `${context.deployment_id}:${pendingRoute}`;
  if (key === lastPage) return;
  lastPage = key;
  trackAnalyticsEvent("page_view", { route: pendingRoute });
  const feature = analyticsFeature(pendingRoute);
  if (feature) trackAnalyticsEvent("feature_exposed", { feature_id: feature, route: pendingRoute });
}

export function trackFeatureUsed(featureId: string): void {
  trackAnalyticsEvent("feature_invoked", { feature_id: featureId });
}
