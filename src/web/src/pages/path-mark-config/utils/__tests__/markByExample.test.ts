import { describe, expect, it } from "vitest";

import {
  layerBetween,
  markByExampleCandidates,
  normalizePath,
  preferredCandidate,
  segmentsOf,
} from "../markByExample";

/**
 * Marking by example: point at one folder you already have, rather than working out which layer
 * number describes it.
 *
 * The arithmetic has to agree with the backend's numbering exactly — a layer off by one silently
 * makes resources of the wrong folders, which looks like a working feature until somebody counts.
 */
describe("markByExample", () => {
  it("counts levels the way a layer mark does", () => {
    expect(layerBetween("/library", "/library/Interstellar")).toBe(1);
    expect(layerBetween("/library", "/library/Movies/Interstellar")).toBe(2);
  });

  it("reads either separator, and a trailing one is not a level", () => {
    expect(layerBetween("C:\\library", "C:\\library\\Movies\\Interstellar")).toBe(2);
    expect(layerBetween("/library/", "/library/Movies/")).toBe(1);
  });

  it("refuses a folder that is not above the sample", () => {
    expect(layerBetween("/elsewhere", "/library/Movies")).toBeNull();
    expect(layerBetween("/library/Movies", "/library")).toBeNull();
    expect(layerBetween("/library/Movies", "/library/Movies")).toBeNull();
  });

  /** The same folder is spelled both ways on Windows; a mismatch would offer no ancestors at all. */
  it("matches folder names case-insensitively", () => {
    expect(layerBetween("C:\\Library", "c:\\library\\Movies")).toBe(1);
  });

  it("offers every ancestor, nearest first", () => {
    const candidates = markByExampleCandidates("/library/Movies/Interstellar", () => false);

    expect(candidates.map((c) => c.rootPath)).toEqual(["/library/Movies", "/library"]);
    expect(candidates.map((c) => c.layer)).toEqual([1, 2]);
  });

  it("keeps a Windows drive as the outermost ancestor", () => {
    const candidates = markByExampleCandidates("D:\\media\\anime\\Clannad", () => false);

    expect(candidates.map((c) => c.rootPath)).toEqual(["D:/media/anime", "D:/media", "D:"]);
    expect(candidates.map((c) => c.layer)).toEqual([1, 2, 3]);
  });

  /**
   * Only the user knows which folder is the library. What can be guessed is which one they meant,
   * and a folder that already carries marks is the best evidence there is.
   */
  it("prefers an ancestor that is already marked", () => {
    const candidates = markByExampleCandidates(
      "/library/Movies/Interstellar",
      (path) => path === "/library",
    );

    expect(preferredCandidate(candidates)).toMatchObject({ rootPath: "/library", layer: 2 });
  });

  it("falls back to the sample's own parent when nothing is marked", () => {
    const candidates = markByExampleCandidates("/library/Movies/Interstellar", () => false);

    expect(preferredCandidate(candidates)).toMatchObject({ rootPath: "/library/Movies", layer: 1 });
  });

  it("has nothing to offer for a folder with no ancestors", () => {
    expect(markByExampleCandidates("/library", () => false)).toEqual([]);
    expect(preferredCandidate([])).toBeUndefined();
  });

  it("normalizes without losing the roots", () => {
    expect(normalizePath("/library/Movies/")).toBe("/library/Movies");
    expect(normalizePath("C:\\library\\")).toBe("C:/library");
    expect(normalizePath("/")).toBe("/");
    expect(segmentsOf("//library//Movies//")).toEqual(["library", "Movies"]);
  });
});
