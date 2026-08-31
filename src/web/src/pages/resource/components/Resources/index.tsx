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

const Gap = 10;

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
    },
    ref,
  ) => {
    const loadingRef = useRef<boolean>(false);
    const gridRef = useRef<any>();
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
          return renderCell({
            columnIndex,
            key,
            parent,
            rowIndex,
            style,
            measure,
            isScrolling,
            isVisible,
          });
        }}
      </CellMeasurer>
    );

    useUpdateEffect(() => {
      onResize(true);
    }, [columnCount]);

    const onResize = (clearCache: boolean = false) => {
      if (clearCache) {
        // todo: clear cache will cause the grid scrolls to bottom when height downsized which may trigger load more behavior.
        cacheRef.current.clearAll();
        forceUpdate();
        // After React re-renders with default heights, re-measure once the
        // new DOM is laid out. Without this, cells stay at defaultHeight
        // until something else triggers a measurement (used to be the
        // per-image `onLoad={measure}` cascade in ResourceTabContent).
        requestAnimationFrame(() => {
          gridRef.current?.measureAllCells();
        });
      } else {
        gridRef.current?.measureAllCells();
        forceUpdate();
      }
    };

    useImperativeHandle(ref, () => ({
      rearrange: () => {
        onResize(true);
      },
      measure: () => {
        // rAF so the caller can fire this immediately after a setState
        // without worrying about commit timing.
        requestAnimationFrame(() => {
          gridRef.current?.measureAllCells();
        });
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
                      onResize(true);
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
