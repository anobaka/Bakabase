import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useUpdate } from 'react-use';
import { CheckCircleOutlined, CheckCircleTwoTone } from '@ant-design/icons';
import styles from './index.module.scss';
import FilterPanel from './components/FilterPanel';
import type { ISearchForm } from '@/pages/Resource/models';
import { convertFilterGroupToDto } from '@/pages/Resource/helpers';
import BApi from '@/sdk/BApi';
import Resource from '@/components/Resource';
import store from '@/store';
import BusinessConstants from '@/components/BusinessConstants';
import ResourceMasonry from '@/pages/Resource/components/ResourceMasonry';
import { Button, Pagination, Spinner } from '@/components/bakaui';
import type { BakabaseInsideWorldBusinessModelsInputResourceSearchInputModel } from '@/sdk/Api';

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

  const [searching, setSearching] = useState(false);

  const [bulkOperationMode, setBulkOperationMode] = useState<boolean>(false);
  const [selectedResourceIds, setSelectedResourceIds] = useState<number[]>([]);

  useEffect(() => {
    const c = uiOptions.resource?.colCount ?? BusinessConstants.DefaultResourceColumnCount;
    if (uiOptions.initialized && (columnCount == 0 || columnCount != c)) {
      setColumnCount(c);
    }
  }, [uiOptions]);

  const pageContainerRef = useRef<any>();

  const search = async (partialForm: Partial<ISearchForm>, renderMode: 'append' | 'replace', replaceSearchCriteria: boolean = false) => {
    const baseForm = replaceSearchCriteria ? {} : partialForm;

    const newForm = {
      ...baseForm,
      ...partialForm,
      pageSize: PageSize,
      save: true,
    };

    setSearchForm(newForm);
    console.log('Search resources', newForm);

    const dto: BakabaseInsideWorldBusinessModelsInputResourceSearchInputModel = {
      ...newForm,
      group: convertFilterGroupToDto(newForm.group),
      saveSearchCriteria: true,
    };

    setSearching(true);
    const rsp = await BApi.resource.searchResources(dto);

    setPageable({
      page: rsp.pageIndex!,
      pageSize: PageSize,
      totalCount: rsp.totalCount!,
    });

    const newResources = rsp.data || [];
    if (renderMode == 'append') {
      setResources([...resources, ...newResources]);
    } else {
      setResources(newResources);
    }
    setSearching(false);
  };

  useEffect(() => {
    BApi.resource.getResourceSearchCriteria().then(r => {
      // @ts-ignore
      search(r.data || {}, false);
    });
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
        }, 'replace')}
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
      {columnCount > 0 && resources.length > 0 && (
        <>
          {pageable && (
            <div className={styles.pagination}>
              <Pagination
                boundaries={3}
                showControls
                size={'sm'}
                total={pageable.pageSize == undefined ? 0 : Math.ceil(pageable.totalCount / pageable.pageSize)}
                page={pageable.page}
                onChange={p => {
                  search({
                    pageIndex: p,
                  }, 'replace');
                }}
              />
            </div>
          )}
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
                    showBiggerCoverOnHover={uiOptions?.resource?.showBiggerCoverWhileHover}
                    disableMediaPreviewer={uiOptions?.resource?.disableMediaPreviewer}
                    disableCache={uiOptions?.resource?.disableCache}
                    biggerCoverPlacement={index % columnCount < columnCount / 2 ? 'right' : 'left'}
                  />
                </div>
              );
            }}
            loadMore={async () => {
              const totalPage = Math.ceil((pageable?.totalCount ?? 0) / PageSize);
              if ((pageable?.page ?? 0) < totalPage) {
                await search({
                  pageIndex: (pageable?.page ?? 0) + 1,
                }, 'append');
              }
            }}
          />
        </>
      ) || (
        <div className={'mt-10 flex items-center gap-2 justify-center'}>
          {searching ? (
            <Spinner label={t('Searching...')} />
          ) : (
            <>
              {t('Resource not found')}
              <Button
                size={'sm'}
                variant={'light'}
                radius={'sm'}
                color={'primary'}
                onClick={() => {
                  search({ pageIndex: 1 }, 'replace', true);
                }}
              >
                {t('Reset search criteria')}
              </Button>
            </>
          )}
        </div>
      )}
    </div>
  );
};
