"use client";

import type { GridCellProps } from "react-virtualized";

import { AutoSizer, CellMeasurer, CellMeasurerCache, Grid } from "react-virtualized";
import React, { forwardRef, useEffect, useImperativeHandle, useRef } from "react";
import { useUpdate, useUpdateEffect } from "react-use";

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
  ({ columnCount, loadMore, renderCell, cellCount, onScroll, onScrollToTop }, ref) => {
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

    function renderGrid() {
      const containerWidth = containerRef.current?.clientWidth ?? 0;
      const containerHeight = containerRef.current?.clientHeight ?? 0;

      const columnWidth = (containerWidth - verScrollbarWidthRef.current) / columnCount;

      return (
        <div
          ref={(r) => {
            if (!containerRef.current) {
              containerRef.current = r;
              forceUpdate();
            }
          }}
          className={"grow min-h-[0] overflow-hidden"}
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
                  containerStyle={{
                    overflow: 'visible',
                  }}
                  height={height}
                  overscanIndicesGetter={({
                    cellCount,
                    overscanCellsCount,
                    startIndex,
                    stopIndex,
                  }) => ({
                    overscanStartIndex: Math.max(
                      0,
                      startIndex - overscanCellsCount,
                    ),
                    overscanStopIndex: Math.min(
                      cellCount - 1,
                      stopIndex + overscanCellsCount,
                    ),
                  })}
                  overscanRowCount={2}
                  rowCount={Math.ceil(cellCount / columnCount)}
                  rowHeight={cacheRef.current.rowHeight}
                  width={width}
                  onScroll={e => {
                    scrollTopRef.current = e.scrollTop;
                    onScroll?.(e);
                  }}
                  onScrollbarPresenceChange={e => {
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
        </div>
      );
    }

    return renderGrid();
  },
);

export default Resources;
