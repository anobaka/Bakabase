import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Pagination } from '@alifd/next';
import { Masonry, WindowScroller, AutoSizer, CellMeasurer, createMasonryCellPositioner, CellMeasurerCache } from 'react-virtualized';
import styles from './index.module.scss';
import FilterPanel from './components/FilterPanel';
import type { ISearchForm } from '@/pages/Resource2/models';
import { convertGroupToDto } from '@/pages/Resource2/helpers';
import BApi from '@/sdk/BApi';
import Resource from '@/components/Resource';

const PageSize = 100;

interface IPageable {
  page: number;
  pageSize?: number;
  totalCount: number;
}

export default () => {
  const { t } = useTranslation();
  const [pageable, setPageable] = useState<IPageable>();

  const [resources, setResources] = useState<any[]>([]);

  const search = async (form: ISearchForm) => {
    const dto = {
      ...form,
      group: convertGroupToDto(form.group),
      pageSize: PageSize,
    };

    const rsp = await BApi.resource.searchResourcesV2(dto);

    setPageable({
      page: rsp.pageIndex!,
      totalCount: rsp.totalCount!,
    });

    setResources(rsp.data || []);
  };

  useEffect(() => {
    search({});
  }, []);

  const virtualizedRef = useRef<{
    width: number;
    height: number;
    scrollTop: number;
    overscanByPixels: number;
    columnWidth: number;
    gutterSize: number;
    columnCount: number;
  }>({
    width: 0,
    overscanByPixels: 0,
    height: 0,
    scrollTop: 0,
    columnWidth: 200,
    gutterSize: 10,
    columnCount: 0,
  });

  const _cache = new CellMeasurerCache({
    defaultHeight: 250,
    defaultWidth: 200,
    fixedWidth: true,
  });

  const cellPositionerRef = useRef<any>();
  const masonryRef = useRef<any>();

  function _calculateColumnCount() {
    const { columnWidth, gutterSize, width } = virtualizedRef.current;

    virtualizedRef.current.columnCount = Math.floor(width / (columnWidth + gutterSize));
  }

  function _cellRenderer({ index, key, parent, style }) {
    const list = resources;
    const { columnWidth } = virtualizedRef.current;

    const resource = resources[index];

    return (
      <CellMeasurer cache={_cache} index={index} key={key} parent={parent}>
        <div
          className={styles.Cell}
          style={{
            ...style,
            width: columnWidth,
          }}
        >
          <Resource resource={resource} />
        </div>
      </CellMeasurer>
    );
  }

  function _resetCellPositioner() {
    const { columnWidth, gutterSize } = virtualizedRef.current;

    cellPositionerRef.current.reset({
      columnCount: virtualizedRef.current.columnCount,
      columnWidth,
      spacer: gutterSize,
    });
  }

  function _onResize({ width }) {
    virtualizedRef.current.width = width;

    _calculateColumnCount();
    _resetCellPositioner();
    masonryRef.current.recomputeCellPositions();
  }

  function _renderAutoSizer({ height, scrollTop }) {
    virtualizedRef.current.height = height;
    virtualizedRef.current.scrollTop = scrollTop;

    const { overscanByPixels } = virtualizedRef.current;

    return (
      <AutoSizer
        disableHeight
        height={height}
        onResize={_onResize}
        overscanByPixels={overscanByPixels}
        scrollTop={virtualizedRef.current.scrollTop}
      >
        {_renderMasonry}
      </AutoSizer>
    );
  }

  function _initCellPositioner() {
    if (typeof cellPositionerRef.current === 'undefined') {
      const { columnWidth, gutterSize, columnCount } = virtualizedRef.current;

      cellPositionerRef.current = createMasonryCellPositioner({
        cellMeasurerCache: _cache,
        columnCount: columnCount,
        columnWidth,
        spacer: gutterSize,
      });
    }
  }

  function _setMasonryRef(ref) {
    masonryRef.current = ref;
  }

  function _renderMasonry({ width }) {
    virtualizedRef.current.width = width;

    _calculateColumnCount();
    _initCellPositioner();

    const { height, overscanByPixels, scrollTop } = virtualizedRef.current;

    return (
      <Masonry
        autoHeight
        cellCount={resources.length}
        cellMeasurerCache={_cache}
        cellPositioner={cellPositionerRef.current}
        cellRenderer={_cellRenderer}
        height={height}
        overscanByPixels={overscanByPixels}
        ref={_setMasonryRef}
        scrollTop={scrollTop}
        width={width}
      />
    );
  }

  return (
    <div className={styles.resourcePage}>
      <FilterPanel onSearch={search} />
      {pageable && (
        <div className={styles.pagination}>
          <Pagination
            pageSize={pageable.pageSize}
            total={pageable.totalCount}
            current={pageable.page}
          />
        </div>
      )}
      <WindowScroller overscanByPixels={virtualizedRef.current.overscanByPixels}>
        {_renderAutoSizer}
      </WindowScroller>
    </div>
  );
};
