import { AutoSizer, CellMeasurer, CellMeasurerCache, Grid } from 'react-virtualized';
import React, { forwardRef, useEffect, useImperativeHandle, useRef } from 'react';
import { useUpdate, useUpdateEffect } from 'react-use';
import { buildLogger } from '@/components/utils';

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
               }) => any;
  cellCount: number;
  onScroll?: (event: ScrollEvent) => any;
  onScrollToTop?: () => any;
};

export type ResourcesRef = {
  rearrange: () => any;
};

const log = buildLogger('Resources');

const Resources = forwardRef<ResourcesRef, Props>(({
                                                     columnCount,
                                                     loadMore,
                                                     renderCell,
                                                     cellCount,
                                                     onScroll,
                                                     onScrollToTop,
                                                   }, ref) => {
  const loadingRef = useRef<boolean>(false);
  const gridRef = useRef<any>();
  const cacheRef = useRef(new CellMeasurerCache({
    defaultHeight: 0,
    defaultWidth: 200,
    fixedWidth: true,
  }));
  const verScrollbarWidthRef = useRef(0);
  const prevContainerWidthRef = useRef<number | undefined>(undefined);

  const scrollTopRef = useRef(0);

  log('rendering');

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
                        }) => (
                          <CellMeasurer
                            cache={cacheRef.current}
                            columnIndex={columnIndex}
                            key={key}
                            parent={parent}
                            rowIndex={rowIndex}
                          >
                            {({ measure }) => renderCell({
        columnIndex,
        key,
        parent,
        rowIndex,
        style,
        measure,
      })}
                          </CellMeasurer>
  );

  useUpdateEffect(() => {
    onResize(true);
  }, [columnCount]);

  const onResize = (clearCache: boolean = false) => {
    log('on resize on scroll', gridRef, containerRef.current?.clientHeight);
    // gridRef.current?.scrollToPosition(scrollTopRef.current);
    if (clearCache) {
      // todo: clear cache will cause the grid scrolls to bottom when height downsized which may trigger load more behavior.
      cacheRef.current.clearAll();
    } else {
      gridRef.current?.measureAllCells();
    }
    // gridRef.current?.recomputeGridSize();
    forceUpdate();
  };

  useImperativeHandle(ref, () => ({
    rearrange: () => {
      onResize(true);
    },
  }));

  function renderGrid() {
    const containerWidth = containerRef.current?.clientWidth ?? 0;
    const containerHeight = containerRef.current?.clientHeight ?? 0;

    const columnWidth = (containerWidth - verScrollbarWidthRef.current) / columnCount;

    log(containerWidth, containerHeight, columnWidth, columnCount, gridRef);

    return (
      <div
        className={'grow min-h-[0] overflow-hidden'}
        ref={r => {
          if (!containerRef.current) {
            containerRef.current = r;
            forceUpdate();
          }
        }}
        onWheel={e => {
          log('onWheel', e);
          if (e.deltaY < 0 && scrollTopRef.current == 0) {
            onScrollToTop?.();
          }
        }}
      >
        {containerRef.current && (
          <AutoSizer>
            {({
                height,
                width,
              }) => (
                <Grid
                  containerStyle={{
                    overflow: 'visible',
                  }}
                  ref={gridRef}
                // height={containerHeight}
                // width={containerWidth}
                  width={width}
                  height={height}
                  cellRenderer={cellRenderer}
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
                  overscanRowCount={4}
                  columnCount={columnCount}
                  columnWidth={columnWidth}
                  rowCount={Math.ceil(cellCount / columnCount)}
                  rowHeight={cacheRef.current.rowHeight}
                  onScrollbarPresenceChange={e => {
                  log('onScrollbarPresenceChange', e);
                  const newWidth = e.vertical ? e.size : 0;
                  if (newWidth != verScrollbarWidthRef.current) {
                    verScrollbarWidthRef.current = newWidth;
                    onResize(true);
                  }
                }}
                  onScroll={e => {
                  log('onScroll', e);
                  scrollTopRef.current = e.scrollTop;
                  onScroll?.(e);
                }}
                />)}
          </AutoSizer>
        )}
      </div>
    );
  }

  return renderGrid();
});

export default Resources;
