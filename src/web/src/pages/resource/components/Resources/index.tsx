"use client";

import type { GridCellProps } from "react-virtualized";
import type { RectSelectionEnd, RectSelectionMode } from "./useRectSelection";

import { AutoSizer, CellMeasurer, CellMeasurerCache, Grid } from "react-virtualized";
import React, {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from "react";
import { useUpdate, useUpdateEffect } from "react-use";

import { useRectSelection } from "./useRectSelection";

type ScrollEvent = {
  clientHeight: number;
  clientWidth: number;
  scrollHeight: number;
  scrollLeft: number;
  scrollTop: number;
  scrollWidth: number;
};

type Props = {
  columnCount: number;
  loadMore?: () => Promise<any>;
  renderCell: ({
    columnIndex, // Horizontal (column) index of cell
    // isScrolling, // The Grid is currently being scrolled
    // isVisible, // This cell is visible within the grid (eg it is not an overscanned cell)
    key, // Unique key within array of cells
    parent, // Reference to the parent Grid (instance)
    rowIndex, // Vertical (row) index of cell
    style,
    measure,
  }: GridCellProps & { measure: () => void }) => any;
  cellCount: number;
  onScroll?: (event: ScrollEvent) => any;
  onScrollToTop?: () => any;
  /** Padding `renderCell` bakes into every cell, excluded when hit-testing a selection
   *  rectangle so the gutter between two cards doesn't catch both of them. */
  cellInset?: number;
  /** Providing this enables drag-a-rectangle multi-selection over the grid. Receives the
   *  cell indices the rectangle currently covers, live while the pointer moves. */
  onRectSelectionChange?: (indices: number[], mode: RectSelectionMode) => any;
  onRectSelectionStart?: () => any;
  onRectSelectionEnd?: (result: RectSelectionEnd) => any;
  /** Fires right before the browser delivers the click that closes a rectangle drag, so
   *  a document-level click handler above this component can ignore that one. */
  onRectSelectionSuppressClick?: () => any;
  shouldStartRectSelection?: (event: MouseEvent) => boolean;
};

export type ResourcesRef = {
  /** Clear all cached measurements and re-measure. Use after column-count
   *  or other layout-defining changes that invalidate the cache. */
  rearrange: () => any;
  /** Re-measure all visible cells without clearing the cache. Use after
   *  content updates that may change cell height (e.g., Phase 2 data, UI
   *  option toggles like inlineDisplayName / hideResourceBorder). */
  measure: () => any;
};

const Resources = forwardRef<ResourcesRef, Props>(
  (
    {
      columnCount,
      loadMore,
      renderCell,
      cellCount,
      onScroll,
      onScrollToTop,
      cellInset = 0,
      onRectSelectionChange,
      onRectSelectionStart,
      onRectSelectionEnd,
      onRectSelectionSuppressClick,
      shouldStartRectSelection,
    },
    ref,
  ) => {
    const gridRef = useRef<any>();
    const cellMeasurementsRef = useRef(new Map<string, () => void>());
    const measurementFrameRef = useRef(0);
    const cacheRef = useRef(
      new CellMeasurerCache({
        defaultHeight: 180,
        defaultWidth: 160,
        fixedWidth: true,
      }),
    );
    const verScrollbarWidthRef = useRef(0);
    const prevContainerWidthRef = useRef<number | undefined>(undefined);

    const scrollTopRef = useRef(0);

    const measureVisibleCells = useCallback(() => {
      if (measurementFrameRef.current) return;
      measurementFrameRef.current = requestAnimationFrame(() => {
        measurementFrameRef.current = 0;
        // Hidden tabs have no usable dimensions. The container resize observer will
        // measure them when they become visible again.
        if (!containerRef.current?.clientWidth) return;
        for (const measure of [...cellMeasurementsRef.current.values()]) {
          measure();
        }
      });
    }, []);

    useEffect(
      () => () => {
        cancelAnimationFrame(measurementFrameRef.current);
        measurementFrameRef.current = 0;
      },
      [],
    );

    useEffect(() => {
      if (!containerRef.current) return;
      const resizeObserver = new ResizeObserver(() => {
        const clearCache = prevContainerWidthRef.current != containerRef.current?.clientWidth;

        prevContainerWidthRef.current = containerRef.current?.clientWidth;
        onResize(clearCache);
      });

      resizeObserver.observe(containerRef.current);

      return () => resizeObserver.disconnect(); // clean up
    }, []);

    const forceUpdate = useUpdate();

    const containerRef = useRef<HTMLDivElement | null>(null);

    const cellRenderer = ({
      columnIndex,
      key,
      parent,
      rowIndex,
      style,
      isScrolling,
      isVisible,
    }: GridCellProps) => (
      <CellMeasurer
        key={key}
        cache={cacheRef.current}
        columnIndex={columnIndex}
        parent={parent}
        rowIndex={rowIndex}
      >
        {({ measure }) => {
          const cell = renderCell({
            columnIndex,
            key,
            parent,
            rowIndex,
            style,
            measure,
            isScrolling,
            isVisible,
          });

          if (!cell) return null;

          return React.cloneElement(cell, {
            ref: (node: HTMLElement | null) => {
              if (node) {
                cellMeasurementsRef.current.set(key, measure);
              } else {
                cellMeasurementsRef.current.delete(key);
              }
            },
          });
        }}
      </CellMeasurer>
    );

    useUpdateEffect(() => {
      onResize(true);
    }, [columnCount]);

    const onResize = (clearCache: boolean = false) => {
      if (!containerRef.current?.clientWidth) return;
      if (clearCache) {
        // todo: clear cache will cause the grid scrolls to bottom when height downsized which may trigger load more behavior.
        cacheRef.current.clearAll();
        // Reset Grid's row offsets as well as CellMeasurer's DOM-size cache.
        gridRef.current?.recomputeGridSize();
        forceUpdate();
      }
      // Grid.measureAllCells() only reads cached row sizes; CellMeasurer's
      // measure callbacks are what actually read the mounted DOM again.
      measureVisibleCells();
    };

    useImperativeHandle(ref, () => ({
      rearrange: () => {
        onResize(true);
      },
      measure: () => {
        measureVisibleCells();
      },
    }));

    const containerWidth = containerRef.current?.clientWidth ?? 0;
    const columnWidth = (containerWidth - verScrollbarWidthRef.current) / columnCount;

    const [rectSelecting, setRectSelecting] = useState(false);

    const getRowHeight = useCallback((index: number) => cacheRef.current.rowHeight({ index }), []);

    const rectOverlayRef = useRectSelection({
      containerRef,
      cellCount,
      columnCount,
      columnWidth,
      getRowHeight,
      cellInset,
      onStart: onRectSelectionStart,
      onChange: onRectSelectionChange,
      onEnd: onRectSelectionEnd,
      onActiveChange: setRectSelecting,
      onSuppressClick: onRectSelectionSuppressClick,
      shouldStart: shouldStartRectSelection,
    });

    // While a rectangle is being dragged the cells must not react to the pointer, or
    // hover-zoomed covers and preview popups fight the drag. Leaving the key out when
    // idle preserves the Grid's own isScrolling-driven value.
    const gridContainerStyle = useMemo(
      () =>
        rectSelecting
          ? ({ overflow: "visible", pointerEvents: "none" } as const)
          : ({ overflow: "visible" } as const),
      [rectSelecting],
    );

    function renderGrid() {
      return (
        <div
          ref={(r) => {
            if (!containerRef.current) {
              containerRef.current = r;
              forceUpdate();
            }
          }}
          className={`grow min-h-[0] overflow-hidden relative ${
            rectSelecting ? "select-none" : ""
          }`}
          onWheel={(e) => {
            if (e.deltaY < 0 && scrollTopRef.current == 0) {
              onScrollToTop?.();
            }
          }}
        >
          {containerRef.current && (
            <AutoSizer>
              {({ height, width }) => (
                <Grid
                  cellRenderer={cellRenderer}
                  ref={gridRef}
                  // height={containerHeight}
                  // width={containerWidth}
                  columnCount={columnCount}
                  columnWidth={columnWidth}
                  containerStyle={gridContainerStyle}
                  height={height}
                  overscanIndicesGetter={({
                    cellCount,
                    overscanCellsCount,
                    startIndex,
                    stopIndex,
                  }) => ({
                    overscanStartIndex: Math.max(0, startIndex - overscanCellsCount),
                    overscanStopIndex: Math.min(cellCount - 1, stopIndex + overscanCellsCount),
                  })}
                  overscanRowCount={2}
                  rowCount={Math.ceil(cellCount / columnCount)}
                  rowHeight={cacheRef.current.rowHeight}
                  // Grid freezes already-rendered cells while it believes it is scrolling,
                  // which would hold the selection highlight stale for the whole of an
                  // edge auto-scroll. Drop the debounce for the duration of the drag.
                  scrollingResetTimeInterval={rectSelecting ? 0 : undefined}
                  width={width}
                  onScroll={(e) => {
                    scrollTopRef.current = e.scrollTop;
                    onScroll?.(e);
                  }}
                  onScrollbarPresenceChange={(e) => {
                    const newWidth = e.vertical ? e.size : 0;
                    if (newWidth != verScrollbarWidthRef.current) {
                      verScrollbarWidthRef.current = newWidth;
                      // Keep measured row heights while the scrollbar changes.
                      // Resetting them to estimates can toggle the scrollbar
                      // repeatedly before the real card heights are measured.
                      forceUpdate();
                      onResize();
                    }
                  }}
                />
              )}
            </AutoSizer>
          )}
          {/* Selection rectangle. Positioned imperatively by useRectSelection so that
              dragging it never re-renders the grid. */}
          <div
            ref={rectOverlayRef}
            className={
              "hidden absolute z-30 pointer-events-none rounded-sm border border-primary bg-primary/20"
            }
          />
        </div>
      );
    }

    return renderGrid();
  },
);

export default Resources;
