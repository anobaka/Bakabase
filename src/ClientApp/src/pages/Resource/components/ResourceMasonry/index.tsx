import {
  AutoSizer,
  CellMeasurer,
  CellMeasurerCache,
  createMasonryCellPositioner,
  Masonry,
  WindowScroller,
} from 'react-virtualized';
import React, { useRef } from 'react';
import { useUpdateEffect } from 'react-use';

const Gap = 10;

interface IProps {
  columnCount: number;
  scrollElement: any;
  loadMore?: () => Promise<any>;
  overscanByRowCount?: number;
  renderCell: (index: number, style: any) => any;
  cellCount: number;
}

export default ({
                  columnCount,
                  scrollElement,
                  loadMore,
                  overscanByRowCount = 5,
                  renderCell,
                  cellCount,
                }: IProps) => {
  const loadingRef = useRef<boolean>(false);

  const cacheRef = useRef(new CellMeasurerCache({
    defaultHeight: 250,
    defaultWidth: 200,
    fixedWidth: true,
  }));

  const cellPositionerRef = useRef<any>();
  const masonryRef = useRef<any>();

  const gridInfoRef = useRef<{
    columnWidth?: number;
    width?: number;
    height: number;
    scrollTop: number;
    overscanByPixels: number;
  }>({
    height: 0,
    scrollTop: 0,
    overscanByPixels: 0,
  });

  useUpdateEffect(() => {
    _calculateColumnWidth();
    // console.log('resize', gridInfoRef.current.columnWidth, columnCount);
    _resetCellPositioner();
    cacheRef.current.clearAll();
    masonryRef.current.clearCellPositions();
    masonryRef.current.recomputeCellPositions();
  }, [columnCount]);

  function _calculateColumnWidth() {
    const { width } = gridInfoRef.current;

    gridInfoRef.current.columnWidth = (width! - (columnCount! - 1)! * Gap) / columnCount!;
  }

  function _cellRenderer({
                           index,
                           key,
                           parent,
                           style,
                         }) {
    const { columnWidth } = gridInfoRef.current;

    // console.log(index, style);

    return (
      <CellMeasurer cache={cacheRef.current} index={index} key={key} parent={parent}>
        {renderCell(index, {
          ...style,
          width: columnWidth,
        })}
      </CellMeasurer>
    );
  }

  function _resetCellPositioner() {
    const {
      columnWidth,
    } = gridInfoRef.current;

    console.log('reset cell positioner', columnCount, columnWidth, Gap);

    cellPositionerRef.current.reset({
      columnCount: columnCount,
      columnWidth: columnWidth!,
      spacer: Gap,
    });
  }

  function _onResize({ width }) {
    gridInfoRef.current.width = width;

    _calculateColumnWidth();
    _resetCellPositioner();
    cacheRef.current.clearAll();
    masonryRef.current.clearCellPositions();
    masonryRef.current.recomputeCellPositions();
}

  function _renderAutoSizer({
                              height,
                              scrollTop,
                            }) {
    gridInfoRef.current.height = height;
    gridInfoRef.current.scrollTop = scrollTop;

    const { overscanByPixels } = gridInfoRef.current;

    // console.log('scroll', scrollTop, height, overscanByPixels);

    return (
      <AutoSizer
        disableHeight
        height={height}
        onResize={_onResize}
        overscanByPixels={overscanByPixels}
        scrollTop={gridInfoRef.current.scrollTop}
      >
        {_renderMasonry}
      </AutoSizer>
    );
  }

  function _initCellPositioner() {
    if (typeof cellPositionerRef.current === 'undefined') {
      const {
        columnWidth,
      } = gridInfoRef.current;

      console.log('init cell positioner', columnCount, columnWidth, Gap);

      cellPositionerRef.current = createMasonryCellPositioner({
        cellMeasurerCache: cacheRef.current,
        columnCount: columnCount,
        columnWidth,
        spacer: Gap,
      });
    }
  }

  function _setMasonryRef(ref) {
    masonryRef.current = ref;
  }

  function _renderMasonry({ width }) {
    gridInfoRef.current.width = width;

    _calculateColumnWidth();
    _initCellPositioner();

    const {
      height,
      overscanByPixels,
      scrollTop,
    } = gridInfoRef.current;

    return (
      <Masonry
        autoHeight
        cellCount={cellCount}
        cellMeasurerCache={cacheRef.current}
        cellPositioner={cellPositionerRef.current}
        cellRenderer={_cellRenderer}
        height={height ?? 0}
        overscanByPixels={overscanByPixels}
        ref={_setMasonryRef}
        scrollTop={scrollTop}
        onCellsRendered={({
                            startIndex,
                            stopIndex,
                          }: { startIndex: number; stopIndex: number }) => {
          // const rowStartIndex = Math.ceil((startIndex + 1) / virtualizedRef.current.columnCount) - 1;
          const rowEndIndex = Math.ceil((stopIndex + 1) / columnCount) - 1;
          const maxRowIndex = Math.ceil(cellCount / columnCount) - 1;
          if (rowEndIndex + overscanByRowCount > maxRowIndex) {
            if (!loadingRef.current && loadMore) {
              loadingRef.current = true;
              loadMore().finally(() => {
                loadingRef.current = false;
              });
              // console.log('load more resources');
            } else {

            }
          }
        }}
        width={width}
      />
    );
  }

  return (
    <WindowScroller
      overscanByPixels={gridInfoRef.current.overscanByPixels}
      scrollElement={scrollElement}
    >
      {_renderAutoSizer}
    </WindowScroller>
  );
};
