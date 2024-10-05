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
import { buildLogger } from '@/components/utils';
import { CoverFit } from '@/sdk/constants';

const PageSize = 100;
const MinResourceWidth = 100;

interface IPageable {
  page: number;
  pageSize?: number;
  totalCount: number;
}

const log = buildLogger('ResourcePage');

const convertToInputModel = (form: ISearchForm) => {
  return {
    ...form,
    group: convertFilterGroupToDto(form.group),
  };
};

export default () => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [pageable, setPageable] = useState<IPageable>();
  const pageableRef = useRef(pageable);

  const [resources, setResources] = useState<any[]>([]);
  const resourcesRef = useRef(resources);

  const uiOptions = store.useModelState('uiOptions');

  const [columnCount, setColumnCount] = useState<number>(0);
  const [searchForm, setSearchForm] = useState<ISearchForm>();
  const searchFormRef = useRef(searchForm);

  const [searching, setSearching] = useState(true);
  const searchingRef = useRef(false);

  const [bulkOperationMode, setBulkOperationMode] = useState<boolean>(false);
  const [selectedResourceIds, setSelectedResourceIds] = useState<number[]>([]);

  const initStartPageRef = useRef(1);

  useEffect(() => {
    if (uiOptions.initialized) {
      const c = uiOptions.resource?.colCount ?? BusinessConstants.DefaultResourceColumnCount;
      if ((columnCount == 0 || columnCount != c)) {
        setColumnCount(c);
      }
    }
  }, [uiOptions]);

  useEffect(() => {
    searchFormRef.current = searchForm;
  }, [searchForm]);

  useEffect(() => {
    searchingRef.current = searching;
  }, [searching]);

  useEffect(() => {
    pageableRef.current = pageable;
  }, [pageable]);

  useEffect(() => {
    resourcesRef.current = resources;
  }, [resources]);

  const pageContainerRef = useRef<any>();

  const search = async (partialForm: Partial<ISearchForm>, renderMode: 'append' | 'replace', replaceSearchCriteria: boolean = false, save: boolean = true) => {
    const baseForm = replaceSearchCriteria ? {} : searchForm;

    const newForm = {
      ...baseForm,
      ...partialForm,
      pageSize: PageSize,
    } as ISearchForm;

    setSearchForm(newForm);
    log('Search resources', newForm);

    const dto = {
      ...convertToInputModel(newForm),
      saveSearchCriteria: save,
      skipCount: 0,
    };

    if (resourcesRef.current.length == 0 || renderMode == 'replace') {
      initStartPageRef.current = newForm.pageIndex ?? 1;
    }

    if (renderMode == 'replace') {
      setResources([]);
    }

    setSearching(true);
    const rsp = await BApi.resource.searchResources(dto);

    if (renderMode == 'replace') {
      setPageable({
        page: rsp.pageIndex!,
        pageSize: PageSize,
        totalCount: rsp.totalCount!,
      });
    }

    setSearchForm({
      ...newForm,
      pageIndex: rsp.pageIndex!,
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
      search(r.data || {}, 'replace');
    });

    const handleScroll = () => {
      console.log(window.scrollY);
    };

    window.addEventListener('scroll', handleScroll);

    return () => {
      window.removeEventListener('scroll', handleScroll);
    };
  }, []);

  return (
    <div
      className={`${styles.resourcePage} flex flex-col h-full max-h-full relative`}
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
            onScroll={e => {
              // console.log('scrolling!!!!', e);
              // return;
              if (e.scrollHeight < e.scrollTop + e.clientHeight + 200 && !searchingRef.current) {
                searchingRef.current = true;
                const totalPage = Math.ceil((pageable?.totalCount ?? 0) / PageSize);
                if (searchFormRef.current?.pageIndex != undefined && searchFormRef.current.pageIndex < totalPage) {
                  search({
                    pageIndex: searchFormRef.current.pageIndex + 1,
                  }, 'append', false, false);
                }
              }

              const items = pageContainerRef.current?.querySelectorAll("div[role='resource']");
              if (items && items.length > 0) {
                const x = e.clientWidth / 2;
                const y = e.scrollTop + e.clientHeight / 2;
                log('on Scroll', `center: ${x},${y}`);
                let closest = items[0];
                let minDis = Number.MAX_VALUE;
                for (const item of items) {
                  const parent = item.parentElement;
                  const ix = parent.offsetLeft + parent.clientWidth / 2;
                  const iy = parent.offsetTop + parent.clientHeight / 2;
                  const dis = Math.abs((ix - x) ** 2 + (iy - y) ** 2);
                  if (dis < minDis) {
                    minDis = dis;
                    closest = item;
                  }
                }
                const centerResourceId = parseInt(closest.getAttribute('data-id'), 10);
                const pageOffset = Math.floor(resources.findIndex(r => r.id == centerResourceId) / PageSize);
                const currentPage = pageOffset + initStartPageRef.current;
                log('on Scroll', 'center item', centerResourceId, closest, 'page offset', pageOffset, 'active page', currentPage);
                if (currentPage != pageableRef.current?.page) {
                  setPageable({
                    ...pageableRef.current!,
                    page: currentPage,
                  });
                  BApi.options.patchResourceOptions({
                    searchCriteria: convertToInputModel({
                      ...searchFormRef.current!,
                      pageIndex: currentPage,
                    }),
                  });
                }
              }
            }}
            renderCell={({
                           columnIndex, // Horizontal (column) index of cell
                           // isScrolling, // The Grid is currently being scrolled
                           // isVisible, // This cell is visible within the grid (eg it is not an overscanned cell)
                           key, // Unique key within array of cells
                           parent, // Reference to the parent Grid (instance)
                           rowIndex, // Vertical (row) index of cell
                           style, // Style object to be applied to cell (to position it);
                           // This must be passed through to the rendered cell element.
              measure,
                         }) => {
              const index = rowIndex * columnCount + columnIndex;
              if (index >= resources.length) {
                return null;
              }
              const resource = resources[index];
              const selected = selectedResourceIds.includes(resource.id);
              return (
                <div
                  className={'relative p-0.5'}
                  style={{
                    ...style,
                  }}
                  onLoad={measure}
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
          />
        </>
      )}
      <div
        className={'mt-10 flex items-center gap-2 justify-center bottom-0 left-0 w-full'}
        style={{ position: resources.length == 0 ? 'relative' : 'absolute' }}
      >
        {/* <Spinner label={t('Searching...')} /> */}
        {searching ? (
          <Spinner label={t('Searching...')} />
        ) : (resources.length == 0) && (
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
    </div>
  );
};
