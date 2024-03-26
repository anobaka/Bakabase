import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Pagination } from '@alifd/next';

import { useUpdate } from 'react-use';
import styles from './index.module.scss';
import FilterPanel from './components/FilterPanel';
import type { ISearchForm } from '@/pages/Resource2/models';
import { convertGroupToDto } from '@/pages/Resource2/helpers';
import BApi from '@/sdk/BApi';
import Resource from '@/components/Resource';
import store from '@/store';
import BusinessConstants from '@/components/BusinessConstants';
import ResourceMasonry from '@/pages/Resource2/components/ResourceMasonry';
import searchForm from '@/models/searchForm';

const PageSize = 100;
const MinResourceWidth = 100;

interface IPageable {
  page: number;
  pageSize?: number;
  totalCount: number;
}

export default () => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [pageable, setPageable] = useState<IPageable>();

  const [resources, setResources] = useState<any[]>([]);

  const uiOptions = store.useModelState('uiOptions');

  const [columnCount, setColumnCount] = useState<number>(0);
  const [searchForm, setSearchForm] = useState<Partial<ISearchForm>>();

  const resourceOptions = store.useModelState('resourceOptions');

  useEffect(() => {
    if (resourceOptions?.lastSearchV2 && !searchForm) {
      setSearchForm(resourceOptions.lastSearchV2);
    }
  }, [resourceOptions?.lastSearch]);

  useEffect(() => {
    if (uiOptions != undefined) {
      setColumnCount(uiOptions.resource?.colCount ?? BusinessConstants.DefaultResourceColumnCount);
      // setColumnCount(3);
    }
  }, [uiOptions]);


  const pageContainerRef = useRef<any>();

  const search = async (partialForm: Partial<ISearchForm>, append: boolean) => {
    const newForm = {
      ...searchForm,
      ...partialForm,
      pageSize: PageSize,
      save: true,
    };

    setSearchForm(newForm);

    newForm.group = convertGroupToDto(newForm.group);
    const rsp = await BApi.resource.searchResourcesV2(newForm);

    setPageable({
      page: rsp.pageIndex!,
      pageSize: PageSize,
      totalCount: rsp.totalCount!,
    });

    const newResources = rsp.data || [];
    if (append) {
      setResources([...resources, ...newResources]);
    } else {
      setResources(newResources);
    }
  };

  useEffect(() => {
    // cacheRef.current.clearAll();
    // _resetCellPositioner();
    // masonryRef.current.clearCellPositions();
  }, []);

  return (
    <div
      className={styles.resourcePage}
      ref={r => {
        pageContainerRef.current = r?.parentElement;
      }}
    >
      <FilterPanel onSearch={f => search(f, false)} />
      {pageable && (
        <div className={styles.pagination}>
          <Pagination
            pageSize={pageable.pageSize}
            total={pageable.totalCount}
            current={pageable.page}
            onChange={p => {
              search({
                pageIndex: p,
              }, false);
            }}
          />
        </div>
      )}
      {columnCount > 0 && (
        <ResourceMasonry
          cellCount={resources.length}
          columnCount={columnCount}
          scrollElement={pageContainerRef.current}
          renderCell={(index, style) => {
            return (
              <Resource
                resource={resources[index]}
                style={style}
              />
            );
          }}
          loadMore={async () => {
            const totalPage = Math.ceil((pageable?.totalCount ?? 0) / PageSize);
            if ((pageable?.page ?? 0) < totalPage) {
              await search({
                pageIndex: (pageable?.page ?? 0) + 1,
              }, true);
            }
          }}
        />
      )}
    </div>
  );
};
