import { describe, expect, it } from "vitest";

import {
  pathMediaType,
  playbackKindForPath,
  playbackKindForPaths,
  resolvePlaybackMethod,
  summarizePlayablePaths,
} from "./playbackPolicy";

import { MediaType } from "@/sdk/constants";

describe("opening method independent of file classification", () => {
  it("uses normal resource-profile execution only in the app, including arbitrary files", () => {
    expect(resolvePlaybackMethod(undefined, true, true)).toBe("native");
    expect(resolvePlaybackMethod(undefined, true, false)).toBe("browser");
    expect(resolvePlaybackMethod(undefined, false, false)).toBe("browser");
    expect(resolvePlaybackMethod(undefined, false, true)).toBe("browser");
  });
  it("honors profile and explicit choices without silently launching on another machine", () => {
    expect(resolvePlaybackMethod({ mode: "browser" }, true, true)).toBe("browser");
    expect(resolvePlaybackMethod({ mode: "native" }, true, true)).toBe("native");
    expect(resolvePlaybackMethod({ mode: "native" }, true, false)).toBe("native");
    expect(resolvePlaybackMethod({ mode: "native" }, false, false)).toBe("ask");
    expect(resolvePlaybackMethod({ mode: "ask" }, true, true)).toBe("ask");
  });
});

describe("file compatibility classification", () => {
  it.each([
    ["/folder/cover.jpg", "/folder/movie.mkv"],
    ["/folder/movie.mkv", "/folder/cover.jpg"],
    ["/folder/track.flac", "/folder/movie.mp4"],
    ["/folder/unknown.bin", "/folder/movie.mp4"],
  ])("summarizes the whole collection rather than the first file", (...paths) => {
    expect(summarizePlayablePaths(paths)).toMatchObject({
      kind: "other",
      mediaType: MediaType.Unknown,
      isMixed: true,
    });
  });
  it("deduplicates and ignores invalid paths while keeping playable video candidates", () => {
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
  it("classifies the filename on Unix and Windows paths without guessing from directory names", () => {
    expect(playbackKindForPath("/album.mp4/001.JPG")).toBe("image");
    expect(playbackKindForPath("Y:\\album.jpg\\readme.TXT")).toBe("text");
    expect(playbackKindForPaths(["/album/001.jpg", "/album/002.png"])).toBe("image");
    expect(playbackKindForPaths(["/album/001.jpg", "/album/track.flac"])).toBe("other");
    expect(pathMediaType("/album.zip")).toBe(MediaType.Unknown);
  });
});
