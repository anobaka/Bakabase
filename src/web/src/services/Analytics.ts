import Clarity from "@microsoft/clarity";
import * as Sentry from "@sentry/react";

import envConfig from "@/config/env";

/**
 * Frontend bootstrapping payload mirrored from
 * `Bakabase.Service.Models.View.AnalyticsAppInfoViewModel`.
 *
 * Hand-typed because the typed SDK regen (`yarn gen-sdk`) is deferred until the C# side
 * lands; once it's run, this can be replaced with the generated type.
 */
type AnalyticsAppInfo = {
  enableAnonymousDataTracking: boolean;
  deviceId: string;
  appVersion: string;
  releaseChannel: string;
  clarityProjectId: string | null;
  ga4MeasurementId: string | null;
  sentryDsn: string | null;
};

/** Mirrors `Bakabase.Service.Models.View.TelemetrySnapshotViewModel`. */
type TelemetrySnapshot = {
  appVersion: string;
  releaseChannel: string;
  os: string;
  locale: string;
  mediaLibraryCount: number;
  resourceCount: number;
  enabledEnhancers: string[];
  aiEnabled: boolean;
  hasMediaLibrary: boolean;
};

const SNAPSHOT_HASH_KEY = "telemetry_last_hash";

declare global {
  interface Window {
    dataLayer?: unknown[];
    gtag?: (...args: unknown[]) => void;
  }
}

let initialized = false;

async function fetchJson<T>(
  path: string,
  init?: RequestInit,
): Promise<T | null> {
  try {
    const res = await fetch(`${envConfig.apiEndpoint}${path}`, init);

    if (!res.ok) return null;
    const body = await res.json();

    // Bakabase wraps responses as { code, message, data }; code 0 means success.
    if (body == null) return null;
    if (typeof body.code === "number" && body.code !== 0) return null;

    return (body.data ?? body) as T;
  } catch (e) {
    console.warn(`[analytics] fetch ${path} failed`, e);

    return null;
  }
}

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
  });
  // event-scoped param applied to every subsequent event
  window.gtag("set", { environment: import.meta.env.MODE });
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
  const snapshot = await fetchJson<TelemetrySnapshot>("/app/telemetry-snapshot");

  if (!snapshot || !window.gtag) return;

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

  const info = await fetchJson<AnalyticsAppInfo>("/app/analytics-info");

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

  // GA4 — quantitative cohorts / event metrics
  if (info.ga4MeasurementId) {
    try {
      loadGtag(info.ga4MeasurementId, info.deviceId);
      await pushSnapshotIfChanged();
    } catch (e) {
      console.warn("[analytics] GA4 init failed", e);
    }
  }
}

/**
 * Manually fires the GA4 `page_view` event. SPA navigations don't trigger one
 * automatically — see https://developers.google.com/analytics/devguides/collection/ga4/single-page-applications.
 */
export function trackPageView(path: string): void {
  if (!initialized || typeof window === "undefined" || !window.gtag) return;
  window.gtag("event", "page_view", {
    page_path: path,
    page_location: typeof window !== "undefined" ? window.location.href : path,
  });
}

export function trackFeatureUsed(featureId: string): void {
  if (!initialized || typeof window === "undefined" || !window.gtag) return;
  window.gtag("event", "feature_used", { feature_id: featureId });
}

export function trackEnhancerTriggered(
  enhancerId: string,
  success: boolean,
): void {
  if (!initialized || typeof window === "undefined" || !window.gtag) return;
  window.gtag("event", "enhancer_triggered", {
    enhancer_id: enhancerId,
    success,
  });
}
