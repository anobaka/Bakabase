import { AutoSizer, CellMeasurer, CellMeasurerCache, Grid } from 'react-virtualized';
import React, { useEffect, useRef } from 'react';
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
    defaultHeight: 250,
    defaultWidth: 200,
    fixedWidth: true,
  }));
  const verScrollbarWidthRef = useRef(0);

  useEffect(() => {
    if (!containerRef.current) return;
    const resizeObserver = new ResizeObserver(() => {
      onResize();
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

  const onResize = () => {
    log('on resize');
    forceUpdate();
    cacheRef.current.clearAll();
  };

  function renderGrid() {
    const containerWidth = containerRef.current?.clientWidth ?? 0;
    const containerHeight = containerRef.current?.clientHeight ?? 0;

    const columnWidth = (containerWidth - verScrollbarWidthRef.current) / columnCount;

    log(containerWidth, containerHeight, columnWidth, columnCount, gridRef);

    return (
      <div
        onResize={e => {
          log('resize', e);
        }}
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
                      onResize();
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
