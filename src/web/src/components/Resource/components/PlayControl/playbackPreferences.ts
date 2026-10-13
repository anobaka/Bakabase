import {
  schemesForPlatform,
  type DevicePlatform,
  type PlayerScheme,
} from "../PlayOnThisDevice/playerSchemes";

import { MediaType } from "@/sdk/constants";
import { getEntryMediaType } from "@/components/MediaPlayer/media";
import { IwFsType } from "@/sdk/constants";

export const playbackKinds = ["image", "text", "audio", "video", "other"] as const;
export type PlaybackKind = (typeof playbackKinds)[number];
export type PlaybackMode = "automatic" | "browser" | "native" | "ask";
export type PlaybackPreference = { mode: PlaybackMode; nativeSchemeId?: string };
export type DevicePlaybackPreferences = { version: 1 } & Record<PlaybackKind, PlaybackPreference>;
export const PLAYBACK_PREFERENCE_KEY = "bakabase.playback.device.preferences.v1";

export const automaticPlaybackPreferences = (): DevicePlaybackPreferences => ({
  version: 1,
  image: { mode: "automatic" },
  text: { mode: "automatic" },
  audio: { mode: "automatic" },
  video: { mode: "automatic" },
  other: { mode: "automatic" },
});

export const normalizePlaybackPreferences = (value: unknown): DevicePlaybackPreferences => {
  const result = automaticPlaybackPreferences();

  if (!value || typeof value !== "object") return result;
  const input = value as Record<string, unknown>;

  if (input.version !== 1) return result;
  for (const kind of playbackKinds) {
    const candidate = input[kind] as PlaybackPreference | undefined;

    if (!candidate || !["automatic", "browser", "native", "ask"].includes(candidate.mode)) continue;
    result[kind] = {
      mode: candidate.mode,
      ...(typeof candidate.nativeSchemeId === "string" &&
      /^[A-Za-z0-9_-]{1,64}$/.test(candidate.nativeSchemeId)
        ? { nativeSchemeId: candidate.nativeSchemeId }
        : {}),
    };
  }

  return result;
};

export const readBrowserPlaybackPreferences = (): DevicePlaybackPreferences => {
  try {
    return normalizePlaybackPreferences(
      JSON.parse(localStorage.getItem(PLAYBACK_PREFERENCE_KEY) || "null"),
    );
  } catch {
    return automaticPlaybackPreferences();
  }
};

export const pathMediaType = (path?: string | null): MediaType => {
  if (!path) return MediaType.Unknown;
  const name = path.split(/[/\\]/).pop() || path;

  return getEntryMediaType({
    path,
    name,
    meaningfulName: name,
    type: IwFsType.Unknown,
    passwordsForDecompressing: [],
  });
};
export const playbackKindForType = (type: MediaType): PlaybackKind =>
  (
    ({
      [MediaType.Image]: "image",
      [MediaType.Text]: "text",
      [MediaType.Audio]: "audio",
      [MediaType.Video]: "video",
    }) as Partial<Record<MediaType, PlaybackKind>>
  )[type] ?? "other";
export const playbackKindForPath = (path?: string | null): PlaybackKind =>
  playbackKindForType(pathMediaType(path));
export const playbackKindForPaths = (paths: string[]): PlaybackKind => {
  const kinds = new Set(paths.map(playbackKindForPath));

  return kinds.size === 1 ? [...kinds][0]! : "other";
};

/** Describe the effective collection, including unknown files, without guessing from its first item. */
export const summarizePlayablePaths = (paths: string[]) => {
  const validPaths = [
    ...new Set(paths.filter((path) => typeof path === "string" && path.trim().length > 0)),
  ];
  const types = new Set(validPaths.map(pathMediaType));
  const kinds = new Set(validPaths.map(playbackKindForPath));
  const isMixed = kinds.size > 1;
  const mediaType = types.size === 1 ? [...types][0]! : MediaType.Unknown;

  return {
    kind: playbackKindForPaths(validPaths),
    mediaType,
    isMixed,
    videoPaths: validPaths.filter((path) => pathMediaType(path) === MediaType.Video),
  };
};

export type EffectivePlayback = { mode: "browser" | "native" | "ask"; scheme?: PlayerScheme };
export const resolvePlaybackPreference = (
  preference: PlaybackPreference,
  desktop: boolean,
  mediaType: MediaType,
  platform: DevicePlatform,
  isMixed = false,
): EffectivePlayback => {
  if (preference.mode === "automatic") return { mode: desktop && !isMixed ? "native" : "browser" };
  if (preference.mode !== "native" || desktop) return { mode: preference.mode };
  // A protocol handler must receive one selected media file, never a mixed collection.
  if (isMixed) return { mode: "ask" };
  // URI handlers are AV-only. Images/text stay viewable even if an old preference names a video player.
  if (mediaType !== MediaType.Audio && mediaType !== MediaType.Video) return { mode: "browser" };
  const scheme = schemesForPlatform(platform, mediaType).find(
    (scheme) => scheme.id === preference.nativeSchemeId,
  );

  return scheme ? { mode: "native", scheme } : { mode: "ask" };
};
