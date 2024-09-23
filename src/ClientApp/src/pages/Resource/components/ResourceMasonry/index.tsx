import { AutoSizer, CellMeasurer, CellMeasurerCache, Grid } from 'react-virtualized';
import React, { useEffect, useRef, useState } from 'react';
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

interface IProps {
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
}

const log = buildLogger('ResourceGrid');

export default ({
                  columnCount,
                  loadMore,
                  renderCell,
                  cellCount,
                  onScroll,
                }: IProps) => {
  const loadingRef = useRef<boolean>(false);
  const gridRef = useRef<any>();
  const cacheRef = useRef(new CellMeasurerCache({
    defaultHeight: 0,
    defaultWidth: 200,
    fixedWidth: true,
  }));
  const verScrollbarWidthRef = useRef(0);
  const prevContainerWidthRef = useRef<number | undefined>(undefined);

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
      >
        {containerRef.current && (
          <AutoSizer>
            {({
                height,
                width,
              }) => (
                <Grid
                  ref={gridRef}
                  // height={containerHeight}
                  // width={containerWidth}
                  width={width}
                  height={height}
                  cellRenderer={cellRenderer}
                  overscanRowCount={2}
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
                    onScroll?.(e);
                  }}
                />)}
          </AutoSizer>
        )}
      </div>
    );
  }

  return renderGrid();
};
