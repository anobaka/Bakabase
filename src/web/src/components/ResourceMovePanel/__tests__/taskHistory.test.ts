import { beforeEach, describe, expect, it } from "vitest";

import {
  clearedMoveTaskStorageKey,
  readClearedMoveTaskIds,
  saveClearedMoveTaskIds,
} from "../taskHistory";

const source = { nodeId: "node", libraryEpoch: "library" };

beforeEach(() => localStorage.clear());

describe("completed move task visibility", () => {
  it("persists exact IDs and isolates servers and library generations", () => {
    saveClearedMoveTaskIds(source, ["first"]);
    saveClearedMoveTaskIds(source, ["second", "first"]);

    expect(readClearedMoveTaskIds(source)).toEqual(new Set(["first", "second"]));
    expect(readClearedMoveTaskIds({ ...source, nodeId: "other" }).size).toBe(0);
    expect(readClearedMoveTaskIds({ ...source, libraryEpoch: "new-library" }).size).toBe(0);
  });

  it("ignores malformed stored data", () => {
    localStorage.setItem(clearedMoveTaskStorageKey(source), "invalid json");
    expect(readClearedMoveTaskIds(source).size).toBe(0);
    localStorage.setItem(clearedMoveTaskStorageKey(source), JSON.stringify(["valid", 1, null]));
    expect(readClearedMoveTaskIds(source)).toEqual(new Set(["valid"]));
  });
});
