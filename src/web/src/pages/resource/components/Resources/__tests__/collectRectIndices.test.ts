import { describe, expect, it } from "vitest";

import { collectRectIndices } from "../useRectSelection";

/**
 * The selection rectangle is hit-tested against the grid's geometry rather than
 * against rendered DOM nodes, because react-virtualized only mounts the rows near
 * the viewport. These cases pin the arithmetic that stands in for those nodes.
 */

/** 4 columns of 100px; rows are 200px tall except row 1, which measured taller. */
const geometry = {
  cellCount: 14,
  columnCount: 4,
  columnWidth: 100,
  getRowHeight: (index: number) => (index === 1 ? 300 : 200),
  cellInset: 2,
};

// Row tops: 0, 200, 500, 700. Row 3 holds cells 12..13 only (cellCount is 14).

describe("collectRectIndices", () => {
  it("finds the single cell a small rectangle sits inside", () => {
    expect(collectRectIndices(geometry, { left: 120, top: 20, right: 180, bottom: 80 })).toEqual([
      1,
    ]);
  });

  it("spans columns and rows the rectangle touches", () => {
    expect(collectRectIndices(geometry, { left: 150, top: 150, right: 250, bottom: 250 })).toEqual([
      1, 2, 5, 6,
    ]);
  });

  it("keeps rows of differing height aligned", () => {
    // Straddles only row 1, whose measured height (300) pushes row 2 down to y=500.
    expect(collectRectIndices(geometry, { left: 0, top: 210, right: 400, bottom: 490 })).toEqual([
      4, 5, 6, 7,
    ]);
  });

  it("reaches rows far below the rendered viewport", () => {
    // What an auto-scrolling drag produces: a tall rectangle over unmounted rows.
    expect(collectRectIndices(geometry, { left: 0, top: 0, right: 400, bottom: 10000 })).toEqual([
      0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13,
    ]);
  });

  it("stops at cellCount instead of filling the last row", () => {
    expect(collectRectIndices(geometry, { left: 0, top: 750, right: 400, bottom: 850 })).toEqual([
      12, 13,
    ]);
  });

  it("ignores the gutter each cell reserves for its padding", () => {
    // 2px of inset on either side means the 4px seam between column 0 and 1
    // belongs to neither.
    expect(collectRectIndices(geometry, { left: 99, top: 50, right: 101, bottom: 60 })).toEqual([]);
    expect(collectRectIndices(geometry, { left: 97, top: 50, right: 103, bottom: 60 })).toEqual([
      0, 1,
    ]);
  });

  it("still selects on a zero-height drag along a row", () => {
    expect(collectRectIndices(geometry, { left: 0, top: 100, right: 250, bottom: 100 })).toEqual([
      0, 1, 2,
    ]);
  });

  it("returns nothing when the grid has no usable geometry", () => {
    const rect = { left: 0, top: 0, right: 400, bottom: 400 };

    expect(collectRectIndices({ ...geometry, cellCount: 0 }, rect)).toEqual([]);
    expect(collectRectIndices({ ...geometry, columnCount: 0 }, rect)).toEqual([]);
    expect(collectRectIndices({ ...geometry, columnWidth: 0 }, rect)).toEqual([]);
    expect(collectRectIndices({ ...geometry, columnWidth: Infinity }, rect)).toEqual([]);
  });
});
