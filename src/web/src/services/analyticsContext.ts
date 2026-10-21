/** Analytics identity is deliberately unrelated to library, pairing or resource identities. */
export const ANALYTICS_CLIENT_KEY = "bakabase.analytics.v2.client";
const SESSION_KEY = "bakabase.analytics.v2.session";
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const isAnalyticsId = (value: unknown): value is string =>
  typeof value === "string" && UUID.test(value) && value !== "00000000-0000-0000-0000-000000000000";

export function newAnalyticsId(): string {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6]! & 0x0f) | 0x40;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

let browserId: string | undefined;
let sessionId: string | undefined;
function storedId(key: string, session: boolean): string {
  try {
    const storage = session ? sessionStorage : localStorage;
    const previous = storage.getItem(key);
    if (isAnalyticsId(previous)) return previous;
    const id = newAnalyticsId();
    storage.setItem(key, id);
    return id;
  } catch {
    // Storage may be unavailable. The caller retains this ID for the document's lifetime.
    return newAnalyticsId();
  }
}
export const browserAnalyticsId = () => (browserId ??= storedId(ANALYTICS_CLIENT_KEY, false));
export const analyticsSessionId = () => (sessionId ??= storedId(SESSION_KEY, true));

export type ClientSurface = "browser" | "desktop_webview";
export interface AnalyticsContext {
  clientId: string;
  deployment_id: string;
  server_version: string;
  ui_version: string;
  client_version: string;
  release_channel: string;
  deployment_type: "desktop_all_in_one" | "standalone_server" | "docker_server" | "unknown";
  client_surface: ClientSurface;
  server_os: string;
  server_arch: string;
  environment: string;
  connection_mode: "local" | "remote" | "desktop_relay";
}
let context: AnalyticsContext | undefined;
export const getAnalyticsContext = () => context;
export function bindAnalyticsContext(value?: AnalyticsContext): void {
  context = value;
}

/** The only route names allowed off the device. Neither query strings nor unknown paths survive. */
export const ANALYTICS_ROUTES = new Set([
  "/",
  "/welcome",
  "/resource",
  "/media-library",
  "/path-mark-config",
  "/path-marks",
  "/resource-profile",
  "/customproperty",
  "/cache",
  "/collections",
  "/collections/detail",
  "/text",
  "/bulk-modification",
  "/comparison",
  "/alias",
  "/extension-group",
  "/data-card",
  "/health-score",
  "/steam-apps",
  "/dlsite-works",
  "/exhentai-galleries",
  "/federation/devices",
  "/federation/map",
  "/data-sync",
  "/other-devices",
  "/file-processor",
  "/downloader",
  "/bt-downloads",
  "/file-mover",
  "/file-name-modifier",
  "/subscriptions",
  "/acquisitions",
  "/workflows",
  "/workflows/editor",
  "/post-parser",
  "/collection-memo",
  "/ai/chat",
  "/ai/configuration",
  "/ai/usage",
  "/ai/audit-log",
  "/ai/cache",
  "/aigc/configs",
  "/aigc/records",
  "/configuration",
  "/third-party-configuration",
  "/background-task",
  "/profiler",
  "/log",
  "/changelog",
  "/client/path-mappings",
  "/device-switch",
]);
export function analyticsRoute(input: string): string {
  const path = input.replace(/^#/, "").split(/[?#]/, 1)[0]!.replace(/\/+$/, "") || "/";
  return ANALYTICS_ROUTES.has(path) ? path : "unknown";
}

const ROUTE_FEATURES: Record<string, string> = {
  "/": "dashboard",
  "/welcome": "onboarding",
  "/resource": "resources",
  "/media-library": "library",
  "/path-mark-config": "path_marks",
  "/path-marks": "path_marks",
  "/resource-profile": "resource_profiles",
  "/customproperty": "custom_properties",
  "/cache": "cache",
  "/collections": "collections",
  "/collections/detail": "collections",
  "/text": "text",
  "/bulk-modification": "bulk_modification",
  "/comparison": "comparison",
  "/alias": "aliases",
  "/extension-group": "extension_groups",
  "/data-card": "data_cards",
  "/health-score": "health_score",
  "/steam-apps": "third_party",
  "/dlsite-works": "third_party",
  "/exhentai-galleries": "third_party",
  "/federation/devices": "devices",
  "/federation/map": "devices",
  "/data-sync": "data_sync",
  "/other-devices": "devices",
  "/file-processor": "file_processor",
  "/downloader": "downloads",
  "/bt-downloads": "downloads",
  "/file-mover": "file_mover",
  "/file-name-modifier": "file_rename",
  "/subscriptions": "subscriptions",
  "/acquisitions": "acquisitions",
  "/workflows": "workflow",
  "/workflows/editor": "workflow",
  "/post-parser": "post_parser",
  "/collection-memo": "collection_memo",
  "/ai/chat": "ai_chat",
  "/ai/configuration": "ai_settings",
  "/ai/usage": "ai_usage",
  "/ai/audit-log": "ai_audit",
  "/ai/cache": "ai_cache",
  "/aigc/configs": "aigc",
  "/aigc/records": "aigc",
  "/configuration": "settings",
  "/third-party-configuration": "third_party",
  "/background-task": "tasks",
  "/profiler": "diagnostics",
  "/log": "diagnostics",
  "/changelog": "changelog",
  "/client/path-mappings": "path_mappings",
  "/device-switch": "devices",
};
export const analyticsFeature = (route: string) => ROUTE_FEATURES[analyticsRoute(route)];
const FEATURE_IDS = new Set([
  ...Object.values(ROUTE_FEATURES),
  "search",
  "properties",
  "playback",
  "reader",
  "help",
]);

const EVENTS = {
  ui_session_started: [],
  page_view: ["route"],
  feature_exposed: ["feature_id", "route"],
  feature_invoked: ["feature_id", "route"],
  feature_previewed: ["feature_id", "route", "outcome"],
  resource_query_presented: [
    "feature_id",
    "search_kind",
    "duration_ms",
    "result_count",
    "has_filters",
    "has_keyword",
    "outcome",
    "load_mode",
  ],
  resource_opened: ["feature_id", "source_scope"],
  playback_requested: ["feature_id", "playback_mode", "media_kind", "source_scope"],
  playback_started: ["feature_id", "playback_mode", "media_kind", "source_scope"],
  playback_handoff: ["feature_id", "playback_mode", "media_kind", "source_scope", "outcome"],
  reader_opened: ["feature_id", "media_kind", "source_scope"],
  help_opened: ["topic", "first_run"],
  help_completed: ["topic", "first_run"],
  pairing_started: ["method"],
  pairing_result: ["method", "outcome", "duration_ms"],
  server_switch_started: ["target_kind"],
  server_switch_result: ["target_kind", "outcome", "duration_ms"],
} as const;
export type AnalyticsEvent = keyof typeof EVENTS;
const EVENT_FEATURES: Partial<Record<AnalyticsEvent, string>> = {
  resource_query_presented: "search",
  resource_opened: "resources",
  playback_requested: "playback",
  playback_started: "playback",
  playback_handoff: "playback",
  reader_opened: "reader",
};
const ENUMS: Record<string, readonly string[]> = {
  search_kind: ["manual", "restored", "refresh"],
  load_mode: ["append", "replace", "prepend"],
  outcome: ["success", "failed", "cancelled", "pending", "unavailable"],
  source_scope: ["local", "managed", "mixed"],
  playback_mode: ["browser", "native", "external_player", "clipboard"],
  media_kind: ["video", "audio", "image", "text", "other"],
  method: ["code", "request"],
  target_kind: ["local", "managed"],
  topic: [
    "gettingStarted",
    "pathMark",
    "resourceProfile",
    "unmaterializedResource",
    "collection",
    "workflow",
    "subscription",
    "acquisition",
    "bulkModification",
    "multiDevice",
    "collectionMemo",
    "notices",
  ],
};
export function scrubAnalyticsProperties(
  event: string,
  properties: Record<string, unknown> = {},
): Record<string, string | number | boolean> | undefined {
  if (!Object.prototype.hasOwnProperty.call(EVENTS, event)) return undefined;
  const allowed = EVENTS[event as AnalyticsEvent] as readonly string[];
  const result: Record<string, string | number | boolean> = {};
  for (const key of allowed) {
    const value = properties[key];
    if (key === "route") {
      if (typeof value === "string") result[key] = analyticsRoute(value);
    } else if (key === "feature_id") {
      if (typeof value === "string" && FEATURE_IDS.has(value)) result[key] = value;
    } else if (ENUMS[key]) {
      if (typeof value === "string" && ENUMS[key]!.includes(value)) result[key] = value;
    } else if (["duration_ms", "result_count"].includes(key)) {
      if (typeof value === "number" && Number.isFinite(value) && value >= 0)
        result[key] = Math.min(1e9, Math.round(value));
    } else if (typeof value === "boolean") result[key] = value;
  }
  // Product events have a fixed semantic feature; callers cannot override it.
  const feature = EVENT_FEATURES[event as AnalyticsEvent];
  if (feature) result.feature_id = feature;
  if (event.startsWith("feature_") && !result.feature_id) return undefined;
  return result;
}

/** Called only by the product API adapter, never by analytics or arbitrary third-party fetches. */
export function analyticsRequestHeaders(
  input: RequestInfo | URL,
  apiBase: string,
): Record<string, string> {
  if (!context) return {};
  try {
    const url = new URL(
      typeof input === "string" ? input : input instanceof URL ? input.href : input.url,
      window.location.origin,
    );
    const base = new URL(apiBase || "/", window.location.origin);
    if (
      url.origin !== base.origin ||
      (base.pathname !== "/" && !url.pathname.startsWith(`${base.pathname.replace(/\/$/, "")}/`))
    )
      return {};
    return {
      "X-Bakabase-Analytics-Client": context.clientId,
      "X-Bakabase-Analytics-Session": analyticsSessionId(),
      "X-Bakabase-Analytics-Operation": newAnalyticsId(),
      "X-Bakabase-Analytics-Surface": context.client_surface,
      "X-Bakabase-Analytics-Consent": "1",
    };
  } catch {
    return {};
  }
}
