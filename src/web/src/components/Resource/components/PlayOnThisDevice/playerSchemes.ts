/**
 * Deep links that hand an HTTP stream to a native player installed on the
 * viewing device.
 *
 * Why bother: a browser only plays what it can demux, and for anything else the
 * server would have to transcode — expensive, lossy, and unseekable. A native
 * player pulls the same bytes and decodes MKV, HEVC, DTS and 4K itself, at full
 * quality, with working seek and zero server CPU.
 *
 * No browser can tell whether an app is installed. Tapping a scheme for a
 * missing app shows an unhelpful error (iOS), a permission prompt (desktop) or
 * silently falls back (Android). So the user picks their player once instead of
 * us guessing, and every platform keeps "copy the link" as a floor that always
 * works.
 */

export type DevicePlatform = "android" | "ios" | "windows" | "macos" | "linux" | "unknown";

export interface PlayerScheme {
  id: string;
  /** Shown as-is; these are product names and are not translated. */
  name: string;
  platforms: DevicePlatform[];
  /**
   * True when the scheme only works after the user installs a protocol handler
   * by hand. Desktop players mostly do not register one.
   */
  needsSetup?: boolean;
  /** Community-documented rather than published by the vendor. */
  unofficial?: boolean;
  build: (streamUrl: string, title: string) => string;
}

/**
 * Chrome on Android refuses plain custom schemes from a link, and wants its own
 * `intent:` form instead, with the real scheme moved into a parameter.
 * `S.browser_fallback_url` is consumed by Chrome when no app matches.
 */
const androidIntent = (packageName?: string) => (streamUrl: string, title: string) => {
  const withoutScheme = streamUrl.replace(/^https?:\/\//, "");
  const scheme = streamUrl.startsWith("https") ? "https" : "http";
  const parts = [
    `scheme=${scheme}`,
    "type=video/*",
    packageName ? `package=${packageName}` : undefined,
    `S.title=${encodeURIComponent(title)}`,
  ].filter(Boolean);

  return `intent://${withoutScheme}#Intent;${parts.join(";")};end`;
};

export const playerSchemes: PlayerScheme[] = [
  // Android — the intent: form is documented by Chrome itself.
  {
    id: "vlc-android",
    name: "VLC",
    platforms: ["android"],
    build: androidIntent("org.videolan.vlc"),
  },
  {
    id: "mx-android",
    name: "MX Player",
    platforms: ["android"],
    build: androidIntent("com.mxtech.videoplayer.ad"),
  },
  {
    id: "mx-pro-android",
    name: "MX Player Pro",
    platforms: ["android"],
    build: androidIntent("com.mxtech.videoplayer.pro"),
  },
  {
    id: "mpv-android",
    name: "mpv",
    platforms: ["android"],
    build: androidIntent("is.xyz.mpv"),
  },
  {
    // No package: Android shows its own chooser, which covers players we have no
    // entry for.
    id: "chooser-android",
    name: "Other player…",
    platforms: ["android"],
    build: androidIntent(),
  },

  // iOS — all official x-callback-url APIs except where noted.
  {
    id: "vlc-ios",
    name: "VLC",
    platforms: ["ios"],
    build: (url) => `vlc-x-callback://x-callback-url/stream?url=${encodeURIComponent(url)}`,
  },
  {
    id: "infuse-ios",
    name: "Infuse",
    platforms: ["ios"],
    build: (url) => `infuse://x-callback-url/play?url=${encodeURIComponent(url)}`,
  },
  {
    id: "nplayer-ios",
    name: "nPlayer",
    platforms: ["ios"],
    // nPlayer rewrites the scheme rather than taking a url parameter.
    build: (url) => url.replace(/^http/, "nplayer-http"),
  },
  {
    id: "senplayer-ios",
    name: "SenPlayer",
    platforms: ["ios"],
    unofficial: true,
    build: (url) => `SenPlayer://x-callback-url/play?url=${encodeURIComponent(url)}`,
  },

  // Desktop — only IINA registers a handler out of the box.
  {
    id: "iina-macos",
    name: "IINA",
    platforms: ["macos"],
    // Only url and title are passed: older IINA fed scheme parameters straight
    // into mpv options, which was exploitable.
    build: (url, title) =>
      `iina://weblink?url=${encodeURIComponent(url)}&title=${encodeURIComponent(title)}`,
  },
  {
    id: "potplayer-windows",
    name: "PotPlayer",
    platforms: ["windows"],
    needsSetup: true,
    build: (url) => `potplayer://${url}`,
  },
  {
    id: "vlc-desktop",
    name: "VLC",
    platforms: ["windows", "macos", "linux"],
    needsSetup: true,
    build: (url) => `vlc://${url}`,
  },
  {
    id: "mpv-desktop",
    name: "mpv",
    platforms: ["windows", "macos", "linux"],
    needsSetup: true,
    // mpv-handler takes the target base64-encoded, and it has to be the URL-safe
    // alphabet: plain base64 emits '/' and '+', which would break the path.
    build: (url) => `mpv-handler://play/${base64Url(url)}`,
  },
];

const base64Url = (value: string): string =>
  btoa(value).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

/**
 * Best guess at what this device is, from the user agent. Only used to decide
 * which players to offer first — the user can always pick another platform's
 * list, so a wrong guess is a nuisance rather than a dead end.
 */
export const detectPlatform = (userAgent: string = navigator.userAgent): DevicePlatform => {
  const ua = userAgent.toLowerCase();

  // iPadOS reports itself as a Mac, and is told apart by having touch points.
  const isIPadOS =
    ua.includes("macintosh") && typeof navigator !== "undefined" && navigator.maxTouchPoints > 1;

  if (/iphone|ipad|ipod/.test(ua) || isIPadOS) return "ios";
  if (ua.includes("android")) return "android";
  if (ua.includes("windows")) return "windows";
  if (ua.includes("mac os")) return "macos";
  if (ua.includes("linux") || ua.includes("x11")) return "linux";

  return "unknown";
};

export const schemesForPlatform = (platform: DevicePlatform): PlayerScheme[] =>
  playerSchemes.filter((s) => s.platforms.includes(platform));
