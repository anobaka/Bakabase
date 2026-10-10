import { describe, expect, it } from "vitest";

import { buildLineOffsets, findLineAtOffset, getVisibleLineRange } from "./layout";

describe("wrapped text virtualization", () => {
  it("uses actual measured heights and estimates unmounted wrapped lines", () => {
    const offsets = buildLineOffsets(
      ["a".repeat(200), "short", "b".repeat(200)],
      22,
      14,
      200,
      true,
      new Map([[0, 330]]),
    );

    expect(offsets[1]).toBe(330);
    expect(offsets[2] - offsets[1]).toBe(22);
    expect(offsets[3] - offsets[2]).toBeGreaterThan(22);
    expect(findLineAtOffset(offsets, 329)).toBe(0);
    expect(findLineAtOffset(offsets, 330)).toBe(1);
    expect(findLineAtOffset(offsets, 352)).toBe(2);
  });

  it("keeps the rendered range bounded even inside a tall wrapped line", () => {
    const lines = Array.from({ length: 20000 }, (_, index) =>
      index === 10000 ? "x".repeat(4096) : `source row ${index}`,
    );
    const offsets = buildLineOffsets(lines, 22, 14, 200, true, new Map());
    const range = getVisibleLineRange(offsets, offsets[10000] + 20, 480);

    expect(range.start).toBe(9994);
    expect(range.end - range.start).toBeLessThan(20);
    expect(range.end).toBeLessThan(lines.length);
  });

  it("returns to one row per source line when wrapping is disabled", () => {
    const offsets = buildLineOffsets(["x".repeat(4096), "short"], 22, 14, 200, false, new Map());

    expect(offsets).toEqual([0, 22, 44]);
    expect(getVisibleLineRange([0], 0, 480)).toEqual({ start: 0, end: 0 });
  });
});
