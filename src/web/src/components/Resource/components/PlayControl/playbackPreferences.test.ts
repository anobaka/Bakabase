import { beforeEach, describe, expect, it, vi } from "vitest";

import {
  automaticPlaybackPreferences,
  normalizePlaybackPreferences,
  pathMediaType,
  playbackKindForPath,
  playbackKindForPaths,
  PLAYBACK_PREFERENCE_KEY,
  readBrowserPlaybackPreferences,
  resolvePlaybackPreference,
  summarizePlayablePaths,
} from "./playbackPreferences";

import { MediaType } from "@/sdk/constants";

beforeEach(() => localStorage.clear());

describe("current-device playback preferences", () => {
  it("uses web automatic for an ordinary browser even when its local native bridge is available", () => {
    expect(
      resolvePlaybackPreference(
        { mode: "automatic" },
        true,
        MediaType.Video,
        "windows",
        false,
        false,
      ),
    ).toEqual({ mode: "browser" });
    expect(
      resolvePlaybackPreference({ mode: "native" }, true, MediaType.Video, "windows", false, false),
    ).toEqual({ mode: "native" });
  });
  it.each([
    ["/folder/cover.jpg", "/folder/movie.mkv"],
    ["/folder/movie.mkv", "/folder/cover.jpg"],
    ["/folder/track.flac", "/folder/movie.mp4"],
    ["/folder/unknown.bin", "/folder/movie.mp4"],
  ])("summarizes differing media kinds from the full valid collection", (...paths) => {
    const collection = summarizePlayablePaths(paths);

    expect(collection).toMatchObject({
      kind: "other",
      mediaType: MediaType.Unknown,
      isMixed: true,
    });
    expect(
      resolvePlaybackPreference(
        { mode: "automatic" },
        true,
        collection.mediaType,
        "windows",
        collection.isMixed,
      ),
    ).toEqual({ mode: "native" });
    expect(
      resolvePlaybackPreference(
        { mode: "automatic" },
        false,
        collection.mediaType,
        "windows",
        collection.isMixed,
      ),
    ).toEqual({ mode: "browser" });
  });

  it("deduplicates and ignores invalid keys without treating Application and Unknown as different playback kinds", () => {
    expect(
      summarizePlayablePaths(["/folder/movie.mkv", "", " ", "/folder/movie.mkv"]).videoPaths,
    ).toEqual(["/folder/movie.mkv"]);
    expect(summarizePlayablePaths(["/folder/game.exe", "/folder/unknown.bin"])).toMatchObject({
      kind: "other",
      isMixed: false,
    });
    expect(summarizePlayablePaths(["/folder/001.jpg", "/folder/002.png"])).toMatchObject({
      kind: "image",
      mediaType: MediaType.Image,
      isMixed: false,
    });
  });

  it("keeps explicit modes ahead of automatic, requiring file choice for browser native mixed media", () => {
    expect(
      resolvePlaybackPreference({ mode: "browser" }, true, MediaType.Unknown, "windows", true),
    ).toEqual({ mode: "browser" });
    expect(
      resolvePlaybackPreference({ mode: "native" }, true, MediaType.Unknown, "windows", true),
    ).toEqual({ mode: "native" });
    expect(
      resolvePlaybackPreference({ mode: "ask" }, true, MediaType.Unknown, "windows", true),
    ).toEqual({ mode: "ask" });
    expect(
      resolvePlaybackPreference(
        { mode: "native", nativeSchemeId: "potplayer-windows" },
        false,
        MediaType.Unknown,
        "windows",
        true,
      ),
    ).toEqual({ mode: "ask" });
  });
  it.each([MediaType.Image, MediaType.Text, MediaType.Audio, MediaType.Video, MediaType.Unknown])(
    "keeps browser automatic on the web and configured API automatic on desktop for %s",
    (type) => {
      expect(resolvePlaybackPreference({ mode: "automatic" }, false, type, "windows")).toEqual({
        mode: "browser",
      });
      expect(resolvePlaybackPreference({ mode: "automatic" }, true, type, "windows")).toEqual({
        mode: "native",
      });
    },
  );

  it("classifies the selected file rather than its directory name and handles Windows paths", () => {
    expect(playbackKindForPath("/album.mp4/001.JPG")).toBe("image");
    expect(playbackKindForPath("Y:\\album.jpg\\readme.TXT")).toBe("text");
    expect(playbackKindForPaths(["/album/001.jpg", "/album/002.png"])).toBe("image");
    expect(playbackKindForPaths(["/album/001.jpg", "/album/track.flac"])).toBe("other");
    expect(pathMediaType("/album.zip")).toBe(MediaType.Unknown);
  });

  it("accepts only a matching AV scheme and never offers a video handler for images or text", () => {
    const preference = { mode: "native", nativeSchemeId: "potplayer-windows" } as const;

    expect(
      resolvePlaybackPreference(preference, false, MediaType.Video, "windows").scheme?.name,
    ).toBe("PotPlayer");
    expect(resolvePlaybackPreference(preference, false, MediaType.Video, "macos")).toEqual({
      mode: "ask",
    });
    expect(resolvePlaybackPreference(preference, false, MediaType.Audio, "unknown")).toEqual({
      mode: "ask",
    });
    expect(resolvePlaybackPreference(preference, false, MediaType.Image, "windows")).toEqual({
      mode: "ask",
    });
    expect(resolvePlaybackPreference(preference, false, MediaType.Text, "windows")).toEqual({
      mode: "ask",
    });
    expect(resolvePlaybackPreference(preference, true, MediaType.Video, "macos")).toEqual({
      mode: "native",
    });
  });

  it("uses safe defaults for invalid versions, malformed storage and unsupported values", () => {
    expect(normalizePlaybackPreferences({ version: 2, video: { mode: "native" } })).toEqual(
      automaticPlaybackPreferences(),
    );
    expect(
      normalizePlaybackPreferences({
        version: 1,
        video: { mode: "execute", nativeSchemeId: "x" },
        image: { mode: "ask" },
      }).video,
    ).toEqual({ mode: "automatic" });
    localStorage.setItem(PLAYBACK_PREFERENCE_KEY, "bad JSON");
    expect(readBrowserPlaybackPreferences()).toEqual(automaticPlaybackPreferences());
    vi.spyOn(Storage.prototype, "getItem").mockImplementationOnce(() => {
      throw new Error("unavailable");
    });
    expect(readBrowserPlaybackPreferences()).toEqual(automaticPlaybackPreferences());
  });

  it("keeps media defaults independent and restores automatic without keeping a stale handler", () => {
    const preferences = normalizePlaybackPreferences({
      version: 1,
      image: { mode: "browser" },
      video: { mode: "native", nativeSchemeId: "iina-macos" },
    });

    expect(preferences.image).toEqual({ mode: "browser" });
    expect(preferences.audio).toEqual({ mode: "automatic" });
    expect(
      normalizePlaybackPreferences({ ...preferences, video: { mode: "automatic" } }).video,
    ).toEqual({ mode: "automatic" });
  });
});
