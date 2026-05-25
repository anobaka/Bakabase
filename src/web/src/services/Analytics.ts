import Clarity from "@microsoft/clarity";
import * as Sentry from "@sentry/react";
import posthog from "posthog-js";

import BApi from "@/sdk/BApi";

const SNAPSHOT_HASH_KEY = "telemetry_last_hash";

declare global {
  interface Window {
    dataLayer?: unknown[];
    gtag?: (...args: unknown[]) => void;
  }
}

let initialized = false;
let posthogInitialized = false;

function loadGtag(measurementId: string, deviceId: string): void {
  if (typeof window === "undefined") return;
  if (window.gtag) return;

  const s = document.createElement("script");

  s.async = true;
  s.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(measurementId)}`;
  document.head.appendChild(s);

  window.dataLayer = window.dataLayer || [];
  // GA4's recommended bootstrap pattern; argument-shape varies so we forward unknown[].
  window.gtag = function gtag(...args: unknown[]) {
    window.dataLayer!.push(args);
  };
  window.gtag("js", new Date());
  window.gtag("config", measurementId, {
    client_id: deviceId,
    anonymize_ip: true,
    // Auto-flag every event in dev builds so they show up in GA4 DebugView without
    // needing a browser extension. Stripped from production builds via import.meta.env.DEV.
    ...(import.meta.env.DEV ? { debug_mode: true } : {}),
  });
  // event-scoped param applied to every subsequent event
  window.gtag("set", { environment: import.meta.env.MODE });
}

function loadPostHog(
  apiKey: string,
  apiHost: string,
  deviceId: string,
  releaseChannel: string,
): void {
  posthog.init(apiKey, {
    api_host: apiHost,
    // We always identify with a deviceId, so don't waste profile quota on anonymous users.
    person_profiles: "identified_only",
    // SPA route changes need manual fires (we do this in trackPageView). PostHog's
    // history-change auto-tracking has caveats with hash routing.
    capture_pageview: false,
    // Don't auto-capture click/input — we want explicit events only, matching GA4.
    autocapture: false,
    // Clarity already records sessions; we don't want PostHog doing the same.
    disable_session_recording: true,
    loaded: (ph) => {
      if (import.meta.env.DEV) ph.debug();
    },
  });
  posthog.identify(deviceId, { release_channel: releaseChannel });
  posthogInitialized = true;
}

/**
 * DJB2 variant. Non-cryptographic but stable and sync; sufficient for "did the snapshot
 * change since last launch?".
 */
function snapshotHash(serialized: string): string {
  let h = 5381;

  for (let i = 0; i < serialized.length; i++) {
    h = ((h << 5) + h) ^ serialized.charCodeAt(i);
  }

  return (h >>> 0).toString(36);
}

async function pushSnapshotIfChanged(): Promise<void> {
  const snapshot = await BApi.app
    .getAppTelemetrySnapshot()
    .then((r) => r.data)
    .catch(() => null);

  if (!snapshot) return;
  if (!window.gtag && !posthogInitialized) return;

  // Stable serialization: enabledEnhancers is sorted server-side; other fields are
  // either scalars or arrays whose order is fixed.
  const hash = snapshotHash(JSON.stringify(snapshot));
  const lastHash = (() => {
    try {
      return localStorage.getItem(SNAPSHOT_HASH_KEY);
    } catch {
      return null;
    }
  })();

  if (hash === lastHash) return;

  if (window.gtag) {
    window.gtag("set", "user_properties", {
      app_version: snapshot.appVersion,
      release_channel: snapshot.releaseChannel,
      os: snapshot.os,
      locale: snapshot.locale,
      enabled_enhancers: snapshot.enabledEnhancers.join(","),
      ai_enabled: snapshot.aiEnabled,
      has_media_library: snapshot.hasMediaLibrary,
    });
    window.gtag("event", "app_snapshot", {
      media_library_count: snapshot.mediaLibraryCount,
      resource_count: snapshot.resourceCount,
    });
  }

  if (posthogInitialized) {
    // PostHog stores numbers natively, so we put counts on the person profile too —
    // no need for a separate "metric" type registration the way GA4 needs.
    posthog.setPersonProperties({
      app_version: snapshot.appVersion,
      release_channel: snapshot.releaseChannel,
      os: snapshot.os,
      locale: snapshot.locale,
      enabled_enhancers: snapshot.enabledEnhancers,
      ai_enabled: snapshot.aiEnabled,
      has_media_library: snapshot.hasMediaLibrary,
      media_library_count: snapshot.mediaLibraryCount,
      resource_count: snapshot.resourceCount,
    });
    posthog.capture("app_snapshot", {
      media_library_count: snapshot.mediaLibraryCount,
      resource_count: snapshot.resourceCount,
    });
  }

  try {
    localStorage.setItem(SNAPSHOT_HASH_KEY, hash);
  } catch {
    // localStorage quota / private mode — non-fatal
  }
}

/**
 * Initialise all analytics SDKs that are both (a) configured (project id / DSN present)
 * and (b) allowed by the user's `enableAnonymousDataTracking` toggle. Idempotent — safe to
 * call multiple times; subsequent calls return immediately.
 */
export async function initAnalytics(): Promise<void> {
  if (initialized) return;
  initialized = true;

  const info = await BApi.app
    .getAnalyticsAppInfo()
    .then((r) => r.data)
    .catch(() => null);

  if (!info || !info.enableAnonymousDataTracking) return;

  // Sentry first — so its error handlers catch anything that goes wrong in subsequent
  // SDK init.
  if (info.sentryDsn) {
    try {
      Sentry.init({
        dsn: info.sentryDsn,
        release: info.appVersion,
        environment: import.meta.env.MODE,
        // Performance + Replay are off — Clarity already records sessions and we don't
        // want to double the data volume / Sentry quota.
        tracesSampleRate: 0,
        replaysSessionSampleRate: 0,
        replaysOnErrorSampleRate: 0,
        // Drop benign HTMLMediaElement noise so it doesn't dominate the issue list.
        // AbortError / NotSupportedError are surfaced to the user via the player's
        // onError path; we don't need a Sentry event for each occurrence.
        ignoreErrors: [
          "AbortError: The play() request was interrupted",
          "AbortError: The fetching process for the media resource was aborted",
          "NotSupportedError: Failed to load because no supported source was found",
          "NotSupportedError: The element has no supported sources",
          // The generated SDK throws the HttpResponse object on non-2xx; unhandled
          // bubbles up here as "[object Response]". processResponseError already
          // surfaces a user-facing toast, so the Sentry event is duplicate noise.
          "[object Response]",
        ],
        beforeSend(event) {
          // HashRouter keeps the route in window.location.hash; Sentry's default
          // request.url captures only the origin (the bare "/"), so the issue
          // stream doesn't show which route the error happened on. Surface it as
          // a `route` tag for filtering and aggregation.
          const hash = typeof window !== "undefined" ? window.location.hash : "";
          const route = hash.replace(/^#/, "") || "/";

          event.tags = { ...event.tags, route };
          return event;
        },
      });
      Sentry.setUser({ id: info.deviceId });
      Sentry.setTag("release_channel", info.releaseChannel);
    } catch (e) {
      console.warn("[analytics] Sentry init failed", e);
    }
  }

  // Clarity — qualitative recording / heatmaps
  if (info.clarityProjectId) {
    try {
      Clarity.init(info.clarityProjectId);
      Clarity.identify(info.deviceId);
      Clarity.setTag("releaseChannel", info.releaseChannel);
    } catch (e) {
      console.warn("[analytics] Clarity init failed", e);
    }
  }

  // GA4 — quantitative cohorts / event metrics. Mirrors PostHog below.
  if (info.ga4MeasurementId) {
    try {
      loadGtag(info.ga4MeasurementId, info.deviceId);
    } catch (e) {
      console.warn("[analytics] GA4 init failed", e);
    }
  }

  // PostHog — same dimensions / events as GA4, runs in parallel. Useful as a fallback
  // when GA4 is broken / unreachable, and as a primary in regions where GA4 is blocked.
  if (info.postHogApiKey && info.postHogApiHost) {
    try {
      loadPostHog(info.postHogApiKey, info.postHogApiHost, info.deviceId, info.releaseChannel);
    } catch (e) {
      console.warn("[analytics] PostHog init failed", e);
    }
  }

  // Snapshot push runs after both GA4 and PostHog so it can fan out to whichever is up.
  if (window.gtag || posthogInitialized) {
    await pushSnapshotIfChanged();
  }
}

/**
 * Manually fires the GA4 `page_view` event. SPA navigations don't trigger one
 * automatically — see https://developers.google.com/analytics/devguides/collection/ga4/single-page-applications.
 */
export function trackPageView(path: string): void {
  if (!initialized || typeof window === "undefined") return;

  if (window.gtag) {
    window.gtag("event", "page_view", {
      page_path: path,
      page_location: window.location.href,
    });
  }
  if (posthogInitialized) {
    posthog.capture("$pageview", {
      $current_url: window.location.href,
      page_path: path,
    });
  }
}

export function trackFeatureUsed(featureId: string): void {
  if (!initialized || typeof window === "undefined") return;

  if (window.gtag) {
    window.gtag("event", "feature_used", { feature_id: featureId });
  }
  if (posthogInitialized) {
    posthog.capture("feature_used", { feature_id: featureId });
  }
}

export function trackEnhancerTriggered(enhancerId: string, success: boolean): void {
  if (!initialized || typeof window === "undefined") return;

  if (window.gtag) {
    window.gtag("event", "enhancer_triggered", {
      enhancer_id: enhancerId,
      success,
    });
  }
  if (posthogInitialized) {
    posthog.capture("enhancer_triggered", {
      enhancer_id: enhancerId,
      success,
    });
  }
}
