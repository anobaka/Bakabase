import type { BlockPlacement, DetailLayoutConfig, SectionId } from "./types";

import React, { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { AiOutlineClose, AiOutlinePlus } from "react-icons/ai";
import { TbWaveSine } from "react-icons/tb";
import { Popover, Tooltip, PopoverTrigger, PopoverContent } from "@heroui/react";
import { useTranslation } from "react-i18next";

import { ALL_SECTIONS, SECTION_HEIGHT_BEHAVIOR } from "./defaultLayout";
import { anchorToCell, clampSpan, colUnitFor, packDesigner, settleLayout } from "./masonry";

// Square designer grid: row height = column width. Keeps colSpan and
// rowSpan visually comparable at a glance.
const DESIGNER_ROW_UNIT = "square" as const; // sentinel, computed from colUnit

// Extra rows of empty space below the lowest block so the user has room
// to drop or add new blocks without the canvas feeling cramped.
const TRAILING_EMPTY_ROWS = 4;

// A press only becomes a drag/resize once the pointer travels this far, so a
// plain click on a block stays inert instead of nudging the layout.
const DRAG_START = 6;

type Props = {
  config: DetailLayoutConfig;
  renderSection: (id: SectionId) => React.ReactNode;
  onConfigChange?: (next: DetailLayoutConfig) => void;
};

// One in-flight pointer gesture (whole-block move, or edge/corner resize).
// A session is created on pointerdown and torn down through a single exit —
// pointerup commits, pointercancel/unmount abort — so no listener, preview
// or half-applied state can outlive its gesture.
type Session = {
  id: SectionId;
  startX: number;
  startY: number;
  /** Past the click threshold — the gesture is visibly acting on the layout. */
  live: boolean;
  /** Last applied grid placement, to skip settle + render on same-cell moves. */
  lastKey: string | null;
  preview: DetailLayoutConfig | null;
} & (
  | { mode: "move"; grabOffsetX: number; grabOffsetY: number }
  | {
      mode: "resize";
      axis: "x" | "y" | "xy";
      startColSpan: number;
      startRowSpan: number;
      startColStart: number;
    }
);

export function MasonryCanvas({ config, renderSection, onConfigChange }: Props) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [containerWidth, setContainerWidth] = useState(0);
  const [hoverCell, setHoverCell] = useState<{ col: number; row: number } | null>(null);
  const [previewConfig, setPreviewConfig] = useState<DetailLayoutConfig | null>(null);
  // The block a live session is acting on — drives z-order and disables its
  // position transition so it tracks the pointer 1:1.
  const [activeId, setActiveId] = useState<SectionId | null>(null);
  const sessionRef = useRef<Session | null>(null);
  const committedRef = useRef(config);

  useEffect(() => {
    committedRef.current = config;
  }, [config]);

  useLayoutEffect(() => {
    const el = containerRef.current;

    if (!el) return;
    const ro = new ResizeObserver((entries) => {
      for (const entry of entries) setContainerWidth(entry.contentRect.width);
    });

    ro.observe(el);
    setContainerWidth(el.clientWidth);

    return () => ro.disconnect();
  }, []);

  const effective = previewConfig ?? config;
  const colUnit = colUnitFor(containerWidth, effective.gridCols, effective.gap);
  const rowUnit = DESIGNER_ROW_UNIT === "square" ? colUnit : DESIGNER_ROW_UNIT;
  const unitX = colUnit + effective.gap;
  const unitY = rowUnit + effective.gap;

  // The session's move handler lives across renders — geometry and callbacks
  // reach it through refs so listeners never need re-registering mid-gesture.
  const geomRef = useRef({ colUnit, rowUnit, unitX, unitY });

  geomRef.current = { colUnit, rowUnit, unitX, unitY };
  const onConfigChangeRef = useRef(onConfigChange);

  onConfigChangeRef.current = onConfigChange;

  // Abort an in-flight session if the canvas unmounts mid-gesture.
  const cancelSessionRef = useRef<(() => void) | null>(null);

  useEffect(() => () => cancelSessionRef.current?.(), []);

  const packed = useMemo(() => {
    if (containerWidth <= 0) {
      return { positions: {}, containerHeight: 0 };
    }

    return packDesigner({ config: effective, containerWidth, rowUnit });
  }, [effective, containerWidth, rowUnit]);

  const maxRow = Math.max(0, ...effective.blocks.map((b) => b.rowStart + b.rowSpan));
  const canvasRows = maxRow + TRAILING_EMPTY_ROWS;
  const canvasHeight = canvasRows * rowUnit + (canvasRows - 1) * effective.gap;

  // --- Pointer tracking on canvas (hover cell for the add affordance) -----

  const pointerToCell = useCallback(
    (clientX: number, clientY: number): { col: number; row: number } | null => {
      const el = containerRef.current;

      if (!el || unitX <= 0 || unitY <= 0) return null;
      const rect = el.getBoundingClientRect();
      const relX = clientX - rect.left;
      const relY = clientY - rect.top;

      if (relX < 0 || relY < 0 || relX > rect.width || relY > canvasHeight) return null;
      const col = Math.max(0, Math.min(effective.gridCols - 1, Math.floor(relX / unitX)));
      const row = Math.max(0, Math.floor(relY / unitY));

      return { col, row };
    },
    [unitX, unitY, effective.gridCols, canvasHeight],
  );

  const handleCanvasPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (sessionRef.current) return;
    const cell = pointerToCell(e.clientX, e.clientY);

    if (!cell) {
      if (hoverCell !== null) setHoverCell(null);

      return;
    }
    const occupied = effective.blocks.some(
      (b) =>
        cell.col >= b.colStart &&
        cell.col < b.colStart + b.colSpan &&
        cell.row >= b.rowStart &&
        cell.row < b.rowStart + b.rowSpan,
    );

    if (occupied) {
      if (hoverCell !== null) setHoverCell(null);

      return;
    }
    if (!hoverCell || hoverCell.col !== cell.col || hoverCell.row !== cell.row) {
      setHoverCell(cell);
    }
  };

  const handleCanvasPointerLeave = () => {
    if (hoverCell !== null) setHoverCell(null);
  };

  // --- Drag / resize sessions ---------------------------------------------

  const beginSession = (ev: React.PointerEvent<HTMLElement>, session: Session) => {
    // Left button / primary touch only, and never two gestures at once.
    if (ev.button !== 0 && ev.pointerType === "mouse") return;
    if (unitX <= 0 || sessionRef.current) return;
    ev.stopPropagation();
    ev.preventDefault();
    (ev.currentTarget as HTMLElement).setPointerCapture(ev.pointerId);
    sessionRef.current = session;

    const onMove = (e: PointerEvent) => {
      const s = sessionRef.current;

      if (!s) return;

      if (!s.live) {
        if (Math.hypot(e.clientX - s.startX, e.clientY - s.startY) < DRAG_START) return;
        s.live = true;
        setActiveId(s.id);
        setHoverCell(null);
      }

      const el = containerRef.current;
      const g = geomRef.current;

      if (!el || g.unitX <= 0) return;
      const base = committedRef.current;
      const block = base.blocks.find((b) => b.id === s.id);

      if (!block) return;

      let next: BlockPlacement;
      let key: string;

      if (s.mode === "move") {
        const anchor = anchorToCell(
          e.clientX - s.grabOffsetX + 1, // +1 to bias rounding toward the grabbed cell
          e.clientY - s.grabOffsetY + 1,
          el.getBoundingClientRect(),
          g.colUnit,
          g.rowUnit,
          base.gap,
          base.gridCols,
          block.colSpan,
          block.rowSpan,
        );
        const colStart = Math.max(0, Math.min(base.gridCols - block.colSpan, anchor.colStart));
        const rowStart = Math.max(0, anchor.rowStart);

        next = { ...block, colStart, rowStart };
        key = `${colStart}:${rowStart}`;
      } else {
        const deltaCols = Math.round((e.clientX - s.startX) / g.unitX);
        const deltaRows = Math.round((e.clientY - s.startY) / g.unitY);
        const maxColSpan = base.gridCols - s.startColStart;
        const colSpan =
          s.axis === "y" ? s.startColSpan : clampSpan(s.startColSpan + deltaCols, maxColSpan);
        const rowSpan = s.axis === "x" ? s.startRowSpan : Math.max(1, s.startRowSpan + deltaRows);

        next = { ...block, colSpan, rowSpan };
        key = `${colSpan}:${rowSpan}`;
      }

      // Same cell as the last applied placement — settling again is a no-op.
      if (key === s.lastKey) return;
      s.lastKey = key;
      // Pin during the gesture so the active block tracks the pointer; the
      // commit re-settles without pin to apply the auto-compact.
      const nextBlocks = settleLayout(
        base.blocks.map((b) => (b.id === s.id ? next : b)),
        { movedId: s.id, pinMoved: true },
      );

      s.preview = { ...base, blocks: nextBlocks };
      setPreviewConfig(s.preview);
    };

    /**
     * The ONE exit for a session — pointerup (commit), pointercancel (the
     * browser took the pointer away), or unmount. Everything is read from the
     * captured session object, never back through state closures.
     */
    const endSession = (commit: boolean) => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
      window.removeEventListener("pointercancel", onCancel);
      cancelSessionRef.current = null;
      const s = sessionRef.current;

      sessionRef.current = null;
      setActiveId(null);
      setPreviewConfig(null);
      if (!s || !commit || !s.live || !s.preview) return;
      const compacted = settleLayout(s.preview.blocks, { movedId: s.id });

      onConfigChangeRef.current?.({ ...s.preview, blocks: compacted });
    };
    const onUp = () => endSession(true);
    const onCancel = () => endSession(false);

    cancelSessionRef.current = () => endSession(false);
    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    window.addEventListener("pointercancel", onCancel);
  };

  const onBlockDragStart = (
    e: React.PointerEvent<HTMLElement>,
    id: SectionId,
    blockLeft: number,
    blockTop: number,
  ) => {
    const el = containerRef.current;

    if (!el) return;
    const rect = el.getBoundingClientRect();

    beginSession(e, {
      mode: "move",
      id,
      startX: e.clientX,
      startY: e.clientY,
      live: false,
      lastKey: null,
      preview: null,
      grabOffsetX: e.clientX - (rect.left + blockLeft),
      grabOffsetY: e.clientY - (rect.top + blockTop),
    });
  };

  const onResizeStart = (
    e: React.PointerEvent<HTMLElement>,
    id: SectionId,
    axis: "x" | "y" | "xy",
  ) => {
    const block = committedRef.current.blocks.find((b) => b.id === id);

    if (!block) return;
    beginSession(e, {
      mode: "resize",
      id,
      axis,
      startX: e.clientX,
      startY: e.clientY,
      live: false,
      lastKey: null,
      preview: null,
      startColSpan: block.colSpan,
      startRowSpan: block.rowSpan,
      startColStart: block.colStart,
    });
  };

  // --- Hide / add ---------------------------------------------------------

  const handleHide = (id: SectionId) => {
    const block = config.blocks.find((b) => b.id === id);

    if (!block) return;
    const remaining = config.blocks.filter((b) => b.id !== id);

    onConfigChange?.({
      ...config,
      blocks: settleLayout(remaining),
      hidden: [...config.hidden, block],
    });
  };

  const handleAddAtCell = (id: SectionId, cell: { col: number; row: number }) => {
    const remembered = config.hidden.find((h) => h.id === id);
    const colSpan = remembered?.colSpan ?? 1;
    const rowSpan = remembered?.rowSpan ?? 1;
    const span = clampSpan(colSpan, config.gridCols);
    const colStart = Math.max(0, Math.min(config.gridCols - span, cell.col));
    const candidate: BlockPlacement = {
      id,
      colStart,
      colSpan: span,
      rowStart: cell.row,
      rowSpan: Math.max(1, rowSpan),
    };
    const withNew = [...config.blocks, candidate];
    const resolved = settleLayout(withNew, { movedId: id });

    onConfigChange?.({
      ...config,
      blocks: resolved,
      hidden: config.hidden.filter((h) => h.id !== id),
    });
    setHoverCell(null);
  };

  // --- Render -------------------------------------------------------------

  const hiddenIds = config.hidden.map((h) => h.id);
  const allKnown = new Set<SectionId>(ALL_SECTIONS.map((s) => s.id));
  const blocksIds = new Set(config.blocks.map((b) => b.id));
  const addableIds = Array.from(allKnown).filter((id) => !blocksIds.has(id));

  return (
    <div
      ref={containerRef}
      className="relative w-full"
      style={{ height: canvasHeight }}
      onPointerLeave={handleCanvasPointerLeave}
      onPointerMove={handleCanvasPointerMove}
    >
      {colUnit > 0 && (
        <GridLines
          canvasHeight={canvasHeight}
          colUnit={colUnit}
          gap={effective.gap}
          gridCols={effective.gridCols}
          rowUnit={rowUnit}
          totalRows={canvasRows}
        />
      )}

      {effective.blocks.map((b) => {
        const pos = packed.positions[b.id];

        if (!pos) return null;

        return (
          <DesignerBlock
            key={b.id}
            block={b}
            interactive={activeId !== b.id}
            pos={pos}
            onDragStart={(e) => onBlockDragStart(e, b.id, pos.left, pos.top)}
            onHide={() => handleHide(b.id)}
            onResizeStart={(e, axis) => onResizeStart(e, b.id, axis)}
          >
            {renderSection(b.id)}
          </DesignerBlock>
        );
      })}

      {hoverCell && activeId == null && addableIds.length > 0 ? (
        <AddBlockAffordance
          addableIds={addableIds}
          cell={hoverCell}
          colUnit={colUnit}
          gap={effective.gap}
          hiddenIds={hiddenIds}
          rowUnit={rowUnit}
          onAdd={(id) => handleAddAtCell(id, hoverCell)}
        />
      ) : null}
    </div>
  );
}

// --- GridLines ----------------------------------------------------------

type GridLinesProps = {
  gridCols: number;
  colUnit: number;
  rowUnit: number;
  gap: number;
  totalRows: number;
  canvasHeight: number;
};

function GridLines({ gridCols, colUnit, rowUnit, gap, totalRows, canvasHeight }: GridLinesProps) {
  const verticals: React.ReactNode[] = [];
  const horizontals: React.ReactNode[] = [];

  for (let i = 0; i <= gridCols; i++) {
    const left = i * (colUnit + gap) - (i === gridCols ? gap : 0);

    verticals.push(
      <div
        key={`v-${i}`}
        className="absolute top-0 pointer-events-none"
        style={{
          left,
          height: canvasHeight,
          borderLeft: "1px dashed rgba(148, 163, 184, 0.25)",
        }}
      />,
    );
  }
  for (let i = 0; i <= totalRows; i++) {
    const top = i * (rowUnit + gap) - (i === totalRows ? gap : 0);

    horizontals.push(
      <div
        key={`h-${i}`}
        className="absolute left-0 pointer-events-none"
        style={{
          top,
          width: "100%",
          borderTop: "1px dashed rgba(148, 163, 184, 0.18)",
        }}
      />,
    );
  }

  return (
    <div className="absolute inset-0 pointer-events-none z-0">
      {verticals}
      {horizontals}
    </div>
  );
}

// --- DesignerBlock ------------------------------------------------------

type DesignerBlockProps = {
  block: BlockPlacement;
  pos: { top: number; left: number; width: number; height: number };
  interactive: boolean;
  onDragStart: (e: React.PointerEvent<HTMLElement>) => void;
  onResizeStart: (e: React.PointerEvent<HTMLElement>, axis: "x" | "y" | "xy") => void;
  onHide: () => void;
  children: React.ReactNode;
};

function DesignerBlock({
  block,
  pos,
  interactive,
  onDragStart,
  onResizeStart,
  onHide,
  children,
}: DesignerBlockProps) {
  const { t } = useTranslation();
  const isDynamicHeight = SECTION_HEIGHT_BEHAVIOR[block.id] === "dynamic";
  const outlineClass = isDynamicHeight
    ? "outline outline-1 outline-dashed outline-warning/60"
    : "outline outline-1 outline-dashed outline-primary/50";

  return (
    <div
      className={`absolute box-border group rounded-medium touch-none cursor-grab active:cursor-grabbing ${outlineClass}`}
      style={{
        top: pos.top,
        left: pos.left,
        width: pos.width,
        height: pos.height,
        transition: interactive
          ? "top 120ms ease-out, left 120ms ease-out, width 120ms ease-out, height 120ms ease-out"
          : "none",
        zIndex: interactive ? 1 : 2,
      }}
      onPointerDown={onDragStart}
    >
      <div className="absolute inset-0 overflow-hidden rounded-medium pointer-events-none select-none">
        {children}
      </div>

      {isDynamicHeight && (
        <Tooltip content={t<string>("resource.detailLayout.dynamicHeightHint")}>
          <span
            aria-label={t<string>("resource.detailLayout.dynamicHeightLabel")}
            className="absolute top-1 left-1 z-10 w-5 h-5 flex items-center justify-center rounded-small bg-warning/20 text-warning text-[11px]"
          >
            <TbWaveSine />
          </span>
        </Tooltip>
      )}
      <Tooltip content={t<string>("resource.detailLayout.hideSection")}>
        <button
          aria-label={t<string>("resource.detailLayout.hideSection")}
          className="absolute top-1 right-1 z-10 w-6 h-6 flex items-center justify-center rounded-small bg-default-200/90 hover:bg-danger-300 text-default-700 hover:text-white cursor-pointer opacity-0 group-hover:opacity-100 transition-opacity"
          onClick={(e) => {
            e.stopPropagation();
            onHide();
          }}
          onPointerDown={(e) => e.stopPropagation()}
        >
          <AiOutlineClose />
        </button>
      </Tooltip>

      {/* right edge — width resize */}
      <div
        className="absolute top-0 bottom-0 right-0 w-2 -mr-1 cursor-col-resize z-10 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center"
        onPointerDown={(e) => onResizeStart(e, "x")}
      >
        <div className="h-8 w-1 rounded-full bg-primary/60" />
      </div>
      {/* bottom edge — height resize */}
      <div
        className="absolute left-0 right-0 bottom-0 h-2 -mb-1 cursor-row-resize z-10 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center"
        onPointerDown={(e) => onResizeStart(e, "y")}
      >
        <div className="w-8 h-1 rounded-full bg-primary/60" />
      </div>
      {/* bottom-right corner — both */}
      <div
        className="absolute right-0 bottom-0 w-3 h-3 -mr-1 -mb-1 cursor-nwse-resize z-20 opacity-0 group-hover:opacity-100 transition-opacity"
        onPointerDown={(e) => onResizeStart(e, "xy")}
      >
        <div className="w-full h-full rounded-sm bg-primary/70" />
      </div>
    </div>
  );
}

// --- AddBlockAffordance -------------------------------------------------

type AddBlockAffordanceProps = {
  cell: { col: number; row: number };
  colUnit: number;
  rowUnit: number;
  gap: number;
  addableIds: SectionId[];
  hiddenIds: SectionId[];
  onAdd: (id: SectionId) => void;
};

function AddBlockAffordance({
  cell,
  colUnit,
  rowUnit,
  gap,
  addableIds,
  hiddenIds,
  onAdd,
}: AddBlockAffordanceProps) {
  const { t } = useTranslation();
  const left = cell.col * (colUnit + gap);
  const top = cell.row * (rowUnit + gap);
  const hiddenSet = new Set(hiddenIds);
  // Hidden sections are most natural to re-add; unplaced-but-not-hidden
  // (which generally means "never placed") come after.
  const sorted = [...addableIds].sort((a, b) => {
    const ah = hiddenSet.has(a) ? 0 : 1;
    const bh = hiddenSet.has(b) ? 0 : 1;

    return ah - bh;
  });

  return (
    <div className="absolute z-30" style={{ left, top, width: colUnit, height: rowUnit }}>
      <Popover placement="bottom">
        <PopoverTrigger>
          <button
            aria-label={t<string>("resource.detailLayout.addSection")}
            className="w-full h-full rounded-medium border-1 border-dashed border-primary/60 bg-primary/5 hover:bg-primary/15 text-primary flex items-center justify-center transition-colors"
          >
            <AiOutlinePlus className="text-xl" />
          </button>
        </PopoverTrigger>
        <PopoverContent>
          <div className="flex flex-col gap-0.5 p-1 min-w-[180px]">
            <div className="text-xs text-default-500 px-2 py-1">
              {t<string>("resource.detailLayout.addSectionMenu")}
            </div>
            {sorted.map((id) => (
              <button
                key={id}
                className="text-left text-sm px-2 py-1 rounded-small hover:bg-default-100 flex items-center justify-between gap-2"
                onClick={() => onAdd(id)}
              >
                <span>{t<string>(`resource.detailLayout.section.${id}`)}</span>
                {hiddenSet.has(id) ? (
                  <span className="text-[10px] text-default-400">
                    {t<string>("resource.detailLayout.addSectionHiddenTag")}
                  </span>
                ) : null}
              </button>
            ))}
          </div>
        </PopoverContent>
      </Popover>
    </div>
  );
}
