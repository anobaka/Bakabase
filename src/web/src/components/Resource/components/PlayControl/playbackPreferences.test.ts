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
} from "./playbackPreferences";

import { MediaType } from "@/sdk/constants";

beforeEach(() => localStorage.clear());

describe("current-device playback preferences", () => {
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
      mode: "browser",
    });
    expect(resolvePlaybackPreference(preference, false, MediaType.Text, "windows")).toEqual({
      mode: "browser",
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
