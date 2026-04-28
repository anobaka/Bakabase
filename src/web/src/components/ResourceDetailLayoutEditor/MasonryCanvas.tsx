import type { BlockPlacement, DetailLayoutConfig, SectionId } from "./types";

import React, { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { AiOutlineClose, AiOutlineHolder, AiOutlinePlus } from "react-icons/ai";
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

type Props = {
  config: DetailLayoutConfig;
  renderSection: (id: SectionId) => React.ReactNode;
  onConfigChange?: (next: DetailLayoutConfig) => void;
};

type DragState = {
  id: SectionId;
  // Offset (in pixels) from the block's top-left to the pointer at drag start.
  grabOffsetX: number;
  grabOffsetY: number;
};

type ResizeState = {
  id: SectionId;
  axis: "x" | "y" | "xy";
  startPointerX: number;
  startPointerY: number;
  startColSpan: number;
  startRowSpan: number;
  startColStart: number;
};

export function MasonryCanvas({ config, renderSection, onConfigChange }: Props) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [containerWidth, setContainerWidth] = useState(0);
  const [hoverCell, setHoverCell] = useState<{ col: number; row: number } | null>(null);
  const [previewConfig, setPreviewConfig] = useState<DetailLayoutConfig | null>(null);
  const dragStateRef = useRef<DragState | null>(null);
  const resizeStateRef = useRef<ResizeState | null>(null);
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

  const packed = useMemo(() => {
    if (containerWidth <= 0) {
      return { positions: {}, containerHeight: 0 };
    }

    return packDesigner({ config: effective, containerWidth, rowUnit });
  }, [effective, containerWidth, rowUnit]);

  const maxRow = Math.max(0, ...effective.blocks.map((b) => b.rowStart + b.rowSpan));
  const canvasRows = maxRow + TRAILING_EMPTY_ROWS;
  const canvasHeight = canvasRows * rowUnit + (canvasRows - 1) * effective.gap;

  // --- Pointer tracking on canvas (hover cell + drag/resize pointermove) --

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
    if (dragStateRef.current || resizeStateRef.current) return;
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

  // --- Drag ---------------------------------------------------------------

  const onBlockDragStart = (
    e: React.PointerEvent<HTMLElement>,
    id: SectionId,
    blockLeft: number,
    blockTop: number,
  ) => {
    if (unitX <= 0) return;
    e.stopPropagation();
    e.preventDefault();
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);

    const el = containerRef.current;

    if (!el) return;
    const rect = el.getBoundingClientRect();
    const grabOffsetX = e.clientX - (rect.left + blockLeft);
    const grabOffsetY = e.clientY - (rect.top + blockTop);

    dragStateRef.current = { id, grabOffsetX, grabOffsetY };
    setHoverCell(null);
  };

  const onDragPointerMove = useCallback(
    (e: PointerEvent) => {
      const ds = dragStateRef.current;

      if (!ds) return;
      const el = containerRef.current;

      if (!el || unitX <= 0) return;
      const rect = el.getBoundingClientRect();
      const base = committedRef.current;
      const block = base.blocks.find((b) => b.id === ds.id);

      if (!block) return;

      const anchor = anchorToCell(
        e.clientX - ds.grabOffsetX + 1, // +1 to bias rounding toward the grabbed cell
        e.clientY - ds.grabOffsetY + 1,
        rect,
        colUnit,
        rowUnit,
        base.gap,
        base.gridCols,
        block.colSpan,
        block.rowSpan,
      );

      const colStart = Math.max(0, Math.min(base.gridCols - block.colSpan, anchor.colStart));
      const rowStart = Math.max(0, anchor.rowStart);

      const moved: BlockPlacement = { ...block, colStart, rowStart };
      // Pin during drag so the active block tracks the pointer; on commit
      // we re-settle without pin to apply the auto-compact.
      const nextBlocks = settleLayout(
        base.blocks.map((b) => (b.id === ds.id ? moved : b)),
        { movedId: ds.id, pinMoved: true },
      );

      setPreviewConfig({ ...base, blocks: nextBlocks });
    },
    [colUnit, rowUnit, unitX],
  );

  const onDragPointerUp = useCallback(() => {
    const ds = dragStateRef.current;

    dragStateRef.current = null;
    if (!ds) return;
    setPreviewConfig((prev) => {
      if (prev) {
        const compacted = settleLayout(prev.blocks, { movedId: ds.id });

        onConfigChange?.({ ...prev, blocks: compacted });
      }

      return null;
    });
  }, [onConfigChange]);

  useEffect(() => {
    window.addEventListener("pointermove", onDragPointerMove);
    window.addEventListener("pointerup", onDragPointerUp);
    window.addEventListener("pointercancel", onDragPointerUp);

    return () => {
      window.removeEventListener("pointermove", onDragPointerMove);
      window.removeEventListener("pointerup", onDragPointerUp);
      window.removeEventListener("pointercancel", onDragPointerUp);
    };
  }, [onDragPointerMove, onDragPointerUp]);

  // --- Resize -------------------------------------------------------------

  const onResizeStart = (
    e: React.PointerEvent<HTMLElement>,
    id: SectionId,
    axis: "x" | "y" | "xy",
  ) => {
    if (unitX <= 0) return;
    e.stopPropagation();
    e.preventDefault();
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    const base = committedRef.current;
    const block = base.blocks.find((b) => b.id === id);

    if (!block) return;
    resizeStateRef.current = {
      id,
      axis,
      startPointerX: e.clientX,
      startPointerY: e.clientY,
      startColSpan: block.colSpan,
      startRowSpan: block.rowSpan,
      startColStart: block.colStart,
    };
    setHoverCell(null);
  };

  const onResizePointerMove = useCallback(
    (e: PointerEvent) => {
      const rs = resizeStateRef.current;

      if (!rs || unitX <= 0) return;
      const base = committedRef.current;
      const block = base.blocks.find((b) => b.id === rs.id);

      if (!block) return;
      const dx = e.clientX - rs.startPointerX;
      const dy = e.clientY - rs.startPointerY;
      const deltaCols = Math.round(dx / unitX);
      const deltaRows = Math.round(dy / unitY);

      const maxColSpan = base.gridCols - rs.startColStart;
      const nextColSpan =
        rs.axis === "y" ? rs.startColSpan : clampSpan(rs.startColSpan + deltaCols, maxColSpan);
      const nextRowSpan =
        rs.axis === "x" ? rs.startRowSpan : Math.max(1, rs.startRowSpan + deltaRows);

      if (nextColSpan === rs.startColSpan && nextRowSpan === rs.startRowSpan && !previewConfig) {
        return;
      }

      const next: BlockPlacement = {
        ...block,
        colSpan: nextColSpan,
        rowSpan: nextRowSpan,
      };
      // Pin during resize so the block's top edge stays under the user's
      // anchor; on commit we re-settle without pin so the resized block
      // also auto-compacts if it now has space above.
      const nextBlocks = settleLayout(
        base.blocks.map((b) => (b.id === rs.id ? next : b)),
        { movedId: rs.id, pinMoved: true },
      );

      setPreviewConfig({ ...base, blocks: nextBlocks });
    },
    [unitX, unitY, previewConfig],
  );

  const onResizePointerUp = useCallback(() => {
    const rs = resizeStateRef.current;

    resizeStateRef.current = null;
    if (!rs) return;
    setPreviewConfig((prev) => {
      if (prev) {
        const compacted = settleLayout(prev.blocks, { movedId: rs.id });

        onConfigChange?.({ ...prev, blocks: compacted });
      }

      return null;
    });
  }, [onConfigChange]);

  useEffect(() => {
    window.addEventListener("pointermove", onResizePointerMove);
    window.addEventListener("pointerup", onResizePointerUp);
    window.addEventListener("pointercancel", onResizePointerUp);

    return () => {
      window.removeEventListener("pointermove", onResizePointerMove);
      window.removeEventListener("pointerup", onResizePointerUp);
      window.removeEventListener("pointercancel", onResizePointerUp);
    };
  }, [onResizePointerMove, onResizePointerUp]);

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
        const isDragging = dragStateRef.current?.id === b.id;
        const isResizing = resizeStateRef.current?.id === b.id;

        return (
          <DesignerBlock
            key={b.id}
            block={b}
            interactive={!isDragging && !isResizing}
            pos={pos}
            onDragStart={(e) => onBlockDragStart(e, b.id, pos.left, pos.top)}
            onHide={() => handleHide(b.id)}
            onResizeStart={(e, axis) => onResizeStart(e, b.id, axis)}
          >
            {renderSection(b.id)}
          </DesignerBlock>
        );
      })}

      {hoverCell && !dragStateRef.current && !resizeStateRef.current && addableIds.length > 0 ? (
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
      className={`absolute box-border group rounded-medium ${outlineClass}`}
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
      <Tooltip content={t<string>("resource.detailLayout.dragToMove")}>
        <button
          aria-label={t<string>("resource.detailLayout.dragToMove")}
          className="absolute top-1 right-1 z-10 w-6 h-6 flex items-center justify-center rounded-small bg-default-200/90 hover:bg-default-300 text-default-700 cursor-grab active:cursor-grabbing opacity-0 group-hover:opacity-100 transition-opacity"
          onPointerDown={onDragStart}
        >
          <AiOutlineHolder />
        </button>
      </Tooltip>
      <Tooltip content={t<string>("resource.detailLayout.hideSection")}>
        <button
          aria-label={t<string>("resource.detailLayout.hideSection")}
          className="absolute top-1 right-8 z-10 w-6 h-6 flex items-center justify-center rounded-small bg-default-200/90 hover:bg-danger-300 text-default-700 hover:text-white opacity-0 group-hover:opacity-100 transition-opacity"
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
