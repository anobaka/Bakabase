import { MediaType } from "@/sdk/constants";
import { getEntryMediaType } from "@/components/MediaPlayer/media";
import { IwFsType } from "@/sdk/constants";

/** Classification only: these kinds do not carry saved playback policies. */
export type PlaybackKind = "image" | "text" | "audio" | "video" | "other";
export type PlaybackChoice = { mode: "browser" | "native" | "ask" };

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

/** Resource profiles choose the opening method; files only determine compatibility. */
export const resolvePlaybackMethod = (
  choice: PlaybackChoice | undefined,
  nativeAvailable: boolean,
  appWindow: boolean,
): "browser" | "native" | "ask" => {
  if (!choice) return nativeAvailable && appWindow ? "native" : "browser";
  // Native execution requires a bridge on the viewing device. A missing bridge
  // offers one-time alternatives, never starts a player on the resource server.
  if (choice.mode === "native" && !nativeAvailable) return "ask";

  return choice.mode;
};
