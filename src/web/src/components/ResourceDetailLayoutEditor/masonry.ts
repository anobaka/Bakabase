import type { BlockPlacement, ComputedPosition, DetailLayoutConfig } from "./types";

export type PackResult = {
  positions: Record<string, ComputedPosition>;
  containerHeight: number;
};

// --- Designer (strict grid) --------------------------------------------

export type DesignerPackInput = {
  config: DetailLayoutConfig;
  containerWidth: number;
  // Fixed row unit in pixels. Designer uses a square grid (rowUnit = colUnit)
  // so colSpan and rowSpan are directly comparable to the user's eyes.
  rowUnit: number;
};

// Strict grid placement. Each block sits at its absolute (colStart, rowStart)
// with size (colSpan, rowSpan). No dynamic heights — what the user sees in
// the designer is exactly what the config says.
export function packDesigner({ config, containerWidth, rowUnit }: DesignerPackInput): PackResult {
  const { gridCols, gap, blocks } = config;
  const colUnit = colUnitFor(containerWidth, gridCols, gap);
  const positions: Record<string, ComputedPosition> = {};
  let maxBottom = 0;

  for (const b of blocks) {
    const colSpan = clampSpan(b.colSpan, gridCols);
    const rowSpan = Math.max(1, b.rowSpan);
    const colStart = Math.max(0, Math.min(gridCols - colSpan, b.colStart));
    const rowStart = Math.max(0, b.rowStart);
    const left = colStart * (colUnit + gap);
    const top = rowStart * (rowUnit + gap);
    const width = colSpan * colUnit + (colSpan - 1) * gap;
    const height = rowSpan * rowUnit + (rowSpan - 1) * gap;

    positions[b.id] = { top, left, width, height };
    if (top + height > maxBottom) maxBottom = top + height;
  }

  return { positions, containerHeight: maxBottom };
}

// --- Runtime (column-aware waterfall) ----------------------------------

export type RuntimePackInput = {
  config: DetailLayoutConfig;
  containerWidth: number;
  // Content-driven heights, keyed by block id (from ResizeObserver).
  heights: Record<string, number>;
};

// Column-aware waterfall. Sort blocks by (rowStart, colStart); for each,
// actual top = max of current bottom-of-column across the block's column
// range. This makes blocks flow under the tallest overlapping block above,
// so dynamic-height sections stack cleanly without designer row heights
// having to match real heights.
export function packRuntime({ config, containerWidth, heights }: RuntimePackInput): PackResult {
  const { gridCols, gap, blocks } = config;
  const colUnit = colUnitFor(containerWidth, gridCols, gap);
  const positions: Record<string, ComputedPosition> = {};
  const colBottoms = new Array<number>(gridCols).fill(0);
  const sorted = [...blocks].sort((a, b) => a.rowStart - b.rowStart || a.colStart - b.colStart);

  for (const b of sorted) {
    const colSpan = clampSpan(b.colSpan, gridCols);
    const colStart = Math.max(0, Math.min(gridCols - colSpan, b.colStart));
    const height = heights[b.id] ?? 0;

    let top = 0;

    for (let c = colStart; c < colStart + colSpan; c++) {
      if (colBottoms[c] > top) top = colBottoms[c];
    }

    const left = colStart * (colUnit + gap);
    const width = colSpan * colUnit + (colSpan - 1) * gap;

    positions[b.id] = { top, left, width, height };

    const nextBottom = top + height + gap;

    for (let c = colStart; c < colStart + colSpan; c++) {
      colBottoms[c] = nextBottom;
    }
  }

  const containerHeight = Math.max(0, ...colBottoms.map((v) => v - gap));

  return { positions, containerHeight };
}

// --- Collision helpers (designer) --------------------------------------

export function blocksOverlap(a: BlockPlacement, b: BlockPlacement): boolean {
  const horiz = a.colStart < b.colStart + b.colSpan && b.colStart < a.colStart + a.colSpan;
  const vert = a.rowStart < b.rowStart + b.rowSpan && b.rowStart < a.rowStart + a.rowSpan;

  return horiz && vert;
}

export type SettleOptions = {
  // Tiebreaker for the (rowStart, colStart) sort. If the moved block lands
  // at the exact same position as another, it wins the slot and the other
  // gets bumped — so dragging onto a tile in the same position swaps them
  // instead of being silently absorbed.
  movedId?: string;
  // When true, the moved block keeps its current rowStart (only pushed
  // down on collision, never pulled up). All other blocks still compact.
  // Used during interactive drag/resize so the active block tracks the
  // pointer instead of teleporting up. On commit, call again without
  // pinMoved to apply the auto-compaction the user expects.
  pinMoved?: boolean;
};

// Settle a layout: every block (except a pinned one) is pulled up to the
// topmost row that keeps it below all already-placed column-overlapping
// neighbors. This both resolves overlaps (a colliding block falls under
// whatever's above) and compacts gaps (a block with empty space above
// slides up).
//
// Sort key is (rowStart asc, colStart asc), so the user's dropped rowStart
// encodes intent: drop low to land below other blocks; drop near the top
// to land above them.
export function settleLayout(
  blocks: BlockPlacement[],
  options?: SettleOptions,
): BlockPlacement[] {
  const movedId = options?.movedId;
  const pinMoved = options?.pinMoved ?? false;
  const sorted = [...blocks].sort((a, b) => {
    const dr = a.rowStart - b.rowStart;

    if (dr !== 0) return dr;
    const dc = a.colStart - b.colStart;

    if (dc !== 0) return dc;
    if (movedId) {
      if (a.id === movedId) return -1;
      if (b.id === movedId) return 1;
    }

    return 0;
  });
  const settled: BlockPlacement[] = [];

  for (const b of sorted) {
    let minRow = 0;

    for (const p of settled) {
      const overlap =
        b.colStart < p.colStart + p.colSpan && p.colStart < b.colStart + b.colSpan;

      if (overlap) {
        minRow = Math.max(minRow, p.rowStart + p.rowSpan);
      }
    }
    const isMoved = movedId != null && b.id === movedId;
    const newRow = pinMoved && isMoved ? Math.max(b.rowStart, minRow) : minRow;

    settled.push({ ...b, rowStart: newRow });
  }

  return settled;
}

// --- Shared utilities --------------------------------------------------

export function colUnitFor(containerWidth: number, gridCols: number, gap: number): number {
  if (containerWidth <= 0 || gridCols <= 0) return 0;

  return (containerWidth - gap * (gridCols - 1)) / gridCols;
}

export function anchorToCell(
  anchorX: number,
  anchorY: number,
  containerRect: DOMRect,
  colUnit: number,
  rowUnit: number,
  gap: number,
  gridCols: number,
  colSpan: number,
  rowSpan: number,
): { colStart: number; rowStart: number } {
  const relX = anchorX - containerRect.left;
  const relY = anchorY - containerRect.top;
  const unitX = colUnit + gap;
  const unitY = rowUnit + gap;
  const colStart = clamp(Math.round(relX / unitX), 0, gridCols - colSpan);
  const rowStart = Math.max(0, Math.round(relY / unitY));

  // rowSpan is not clamped by a "total rows" limit because the grid grows
  // vertically as needed. colSpan clamping is already handled by the caller.
  void rowSpan;

  return { colStart, rowStart };
}

export function clampSpan(span: number, gridCols: number): number {
  return Math.max(1, Math.min(gridCols, span | 0));
}

function clamp(n: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, n));
}
