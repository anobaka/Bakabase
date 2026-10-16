import { beforeEach, describe, expect, it, vi } from "vitest";

import { readWindowBounds, restoredWindowBounds, writeWindowBounds } from "./persistence";

beforeEach(() => localStorage.clear());

describe("local window bounds", () => {
  it("remembers only geometry and leaves windows without a key unchanged", () => {
    const bounds = { x: 140, y: 90, width: 700, height: 450 };

    writeWindowBounds("player", bounds);
    expect(readWindowBounds("player")).toEqual(bounds);
    expect(readWindowBounds("other")).toBeUndefined();
    expect(restoredWindowBounds({ persistenceKey: "player" }, 1280, 720)).toMatchObject(bounds);
    expect(restoredWindowBounds(undefined, 1280, 720)).toMatchObject({ width: 1000, height: 688 });
  });

  it.each([
    "broken json",
    JSON.stringify({ version: 2, bounds: { x: 0, y: 0, width: 700, height: 400 } }),
    JSON.stringify({ version: 1, bounds: { x: "10", y: 0, width: 700, height: 400 } }),
    JSON.stringify({ version: 1, bounds: { x: 0, y: 0, width: -1, height: 400 } }),
    JSON.stringify({ version: 1, bounds: { x: 0, y: 0, width: 700, height: null } }),
  ])("ignores malformed, unsupported, or invalid saved geometry: %s", (data) => {
    localStorage.setItem("bakabase-window-bounds:player", data);
    expect(readWindowBounds("player")).toBeUndefined();
    expect(restoredWindowBounds({ persistenceKey: "player" }, 1280, 720)).toMatchObject({
      width: 1000,
      height: 688,
    });
  });

  it("fits saved desktop geometry to a narrow viewport without changing the saved preference", () => {
    const bounds = { x: 1700, y: -300, width: 1400, height: 900 };

    writeWindowBounds("player", bounds);
    expect(restoredWindowBounds({ persistenceKey: "player" }, 320, 240)).toMatchObject({
      x: 32,
      y: 0,
      width: 288,
      height: 208,
    });
    expect(readWindowBounds("player")).toEqual(bounds);
  });

  it("keeps opening and resizing usable when browser storage is unavailable", () => {
    const blocked = {
      getItem: vi.fn(() => {
        throw new Error("blocked");
      }),
      setItem: vi.fn(() => {
        throw new Error("full");
      }),
    };

    expect(readWindowBounds("player", blocked)).toBeUndefined();
    expect(() =>
      writeWindowBounds("player", { x: 0, y: 0, width: 600, height: 400 }, blocked),
    ).not.toThrow();
    writeWindowBounds("player", { x: Number.NaN, y: 0, width: 600, height: 400 }, blocked);
    expect(blocked.setItem).toHaveBeenCalledTimes(1);
  });
});
