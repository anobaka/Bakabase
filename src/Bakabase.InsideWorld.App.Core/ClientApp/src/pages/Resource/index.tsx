import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Pagination } from '@alifd/next';

import { useUpdate } from 'react-use';
import { CheckCircleOutlined, CheckCircleTwoTone } from '@ant-design/icons';
import styles from './index.module.scss';
import FilterPanel from './components/FilterPanel';
import type { ISearchForm } from '@/pages/Resource/models';
import { convertFilterGroupToDto, convertSearchFormFromDto } from '@/pages/Resource/helpers';
import BApi from '@/sdk/BApi';
import Resource from '@/components/Resource';
import store from '@/store';
import BusinessConstants from '@/components/BusinessConstants';
import ResourceMasonry from '@/pages/Resource/components/ResourceMasonry';

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

  const [bulkOperationMode, setBulkOperationMode] = useState<boolean>(false);
  const [selectedResourceIds, setSelectedResourceIds] = useState<number[]>([]);

  const resourceOptions = store.useModelState('resourceOptions');

  useEffect(() => {
    if (resourceOptions.initialized && !searchForm) {
      const sf = resourceOptions.lastSearchV2 == undefined ? {} : convertSearchFormFromDto(resourceOptions.lastSearchV2);
      console.log('initialize search form', sf);
      setSearchForm(sf);
      search(sf, false);
    }
  }, [resourceOptions]);

  useEffect(() => {
    const c = uiOptions.resource?.colCount ?? BusinessConstants.DefaultResourceColumnCount;
    if (uiOptions.initialized && (columnCount == 0 || columnCount != c)) {
      setColumnCount(c);
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

    const dto = {
      ...newForm,
      group: convertFilterGroupToDto(newForm.group),
    };
    const rsp = await BApi.resource.searchResourcesV2(dto);

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

  }, []);

  return (
    <div
      className={styles.resourcePage}
      ref={r => {
        pageContainerRef.current = r?.parentElement;
      }}
    >
      <FilterPanel
        onSearch={f => search({
          ...f,
          pageIndex: 1,
        }, false)}
        searchForm={searchForm}
        selectedResourceIds={selectedResourceIds}
        onBulkOperationModeChange={m => {
          setBulkOperationMode(m);
          if (!m) {
            setSelectedResourceIds([]);
          }
        }}
        reloadResources={ids => {
          BApi.resource.getResourcesByKeys({ ids }).then(r => {
            for (const res of (r.data || [])) {
              const idx = resources.findIndex(r => r.id == res.id);
              if (idx > -1) {
                resources[idx] = res;
              }
            }
            setResources([...resources]);
          });
        }}
      />
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
      {columnCount > 0 && resources.length > 0 && (
        <ResourceMasonry
          cellCount={resources.length}
          columnCount={columnCount}
          scrollElement={pageContainerRef.current}
          renderCell={(index, style) => {
            const resource = resources[index];
            const selected = selectedResourceIds.includes(resource.id);
            return (
              <div
                className={'relative'}
                style={style}
              >
                {bulkOperationMode && (
                  <div
                    className={'absolute top-0 left-0 z-10 flex items-center justify-center w-full h-full hover:bg-[hsla(var(--nextui-foreground)/0.1)] hover:cursor-pointer'}
                    onClick={() => {
                      if (bulkOperationMode) {
                        if (selected) {
                          setSelectedResourceIds(selectedResourceIds.filter(id => id != resource.id));
                        } else {
                          setSelectedResourceIds([...selectedResourceIds, resource.id]);
                        }
                      }
                    }}
                  >
                    {selected ? <CheckCircleTwoTone className={'text-5xl'} />
                      : <CheckCircleOutlined className={'text-5xl opacity-80'} />}
                  </div>
                )}
                <Resource
                  resource={resource}
                />
              </div>
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
