import { MouseEvent, useCallback } from 'react';
import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useUpdate, useUpdateEffect } from 'react-use';
import { DisconnectOutlined } from '@ant-design/icons';
import type { ResourcesRef } from './components/Resources';
import Resources from './components/Resources';
import styles from './index.module.scss';
import FilterPanel from './components/FilterPanel';
import type { SearchForm } from '@/pages/Resource/models';
import BApi from '@/sdk/BApi';
import Resource from '@/components/Resource';
import store from '@/store';
import BusinessConstants from '@/components/BusinessConstants';
import { Button, Chip, Link, Pagination, Spinner } from '@/components/bakaui';
import { buildLogger } from '@/components/utils';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { ResourceAdditionalItem } from '@/sdk/constants';

const PageSize = 100;

interface IPageable {
  page: number;
  pageSize?: number;
  totalCount: number;
}

const log = buildLogger('ResourcePage');

export default () => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [pageable, setPageable] = useState<IPageable>();
  const pageableRef = useRef(pageable);

  const [resources, setResources] = useState<any[]>([]);
  const resourcesRef = useRef(resources);

  const uiOptions = store.useModelState('uiOptions');

  const [columnCount, setColumnCount] = useState<number>(0);
  const [searchForm, setSearchForm] = useState<SearchForm>();
  const searchFormRef = useRef(searchForm);

  const [searching, setSearching] = useState(true);
  const searchingRef = useRef(false);

  const [selectedIds, setSelectedIds] = useState<number[]>([]);
  const selectedIdsRef = useRef(selectedIds);
  const [multiSelection, setMultiSelection] = useState<boolean>(false);
  const multiSelectionRef = useRef(multiSelection);

  const resourcesComponentRef = useRef<ResourcesRef | null>();

  const { createPortal } = useBakabaseContext();

  const initStartPageRef = useRef(1);

  useEffect(() => {
    BApi.resource.getLastResourceSearch().then(r => {
      search(r.data ?? { page: 1, pageSize: PageSize }, 'replace');
    });

    const handleScroll = () => {
      console.log(window.scrollY);
    };

    const onKeyDown = (e: KeyboardEvent) => {
      // log(e);
      if (e.key == 'Control' && !multiSelectionRef.current) {
        setMultiSelection(true);
      }
    };

    const onKeyUp = (e: KeyboardEvent) => {
      if (e.key == 'Control' && multiSelectionRef.current) {
        setMultiSelection(false);
      }
    };

    const onClick = (e: globalThis.MouseEvent) => {
      if (!multiSelectionRef.current) {
        // alert('clear all');
        setSelectedIds([]);
      }
    };

    window.addEventListener('scroll', handleScroll);
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('click', onClick);

    return () => {
      window.removeEventListener('scroll', handleScroll);
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
      window.removeEventListener('click', onClick);
    };
  }, []);

  useEffect(() => {
    multiSelectionRef.current = multiSelection;
  }, [multiSelection]);

  useEffect(() => {
    if (uiOptions.initialized) {
      const c = uiOptions?.resource?.colCount ? uiOptions.resource.colCount : BusinessConstants.DefaultResourceColumnCount;
      if (!uiOptions.resource || uiOptions.resource.colCount == 0) {
        BApi.options.patchUiOptions({
          resource: {
            ...(uiOptions.resource || {}),
            colCount: c,
          },
        });
      }
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

  useUpdateEffect(() => {
    selectedIdsRef.current = selectedIds;
    log('SelectedIds', selectedIds);
  }, [selectedIds]);

  const pageContainerRef = useRef<any>();

  const search = async (partialForm: Partial<SearchForm>, renderMode: 'append' | 'replace' | 'prepend', replaceSearchCriteria: boolean = false, save: boolean = true) => {
    const baseForm = replaceSearchCriteria ? {} : searchForm;

    const newForm = {
      ...baseForm,
      ...partialForm,
      pageSize: PageSize,
    } as SearchForm;

    setSearchForm(newForm);
    log('Search resources', newForm);

    const dto = newForm;

    if (resourcesRef.current.length == 0 || renderMode == 'replace') {
      initStartPageRef.current = newForm.page ?? 1;
    }

    if (renderMode == 'replace') {
      setResources([]);
    }

    setSearching(true);
    const rsp = await BApi.resource.searchResources(dto, { saveSearch: save });

    if (renderMode == 'replace') {
      setPageable({
        page: rsp.pageIndex!,
        pageSize: PageSize,
        totalCount: rsp.totalCount!,
      });
    }

    setSearchForm({
      ...newForm,
      page: rsp.pageIndex!,
    });

    const newResources = rsp.data || [];
    switch (renderMode) {
      case 'append':
        setResources([...resources, ...newResources]);
        break;
      case 'prepend':
        setResources([...newResources, ...resources]);
        break;
      default:
        setResources(newResources);
        break;
    }
    setSearching(false);
  };

  const renderCell = useCallback(({
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
    const selected = selectedIds.includes(resource.id);
    return (
      <div
        className={'relative p-0.5'}
        style={{
          ...style,
        }}
        key={resource.id}
        onLoad={measure}
      >
        <Resource
          resource={resource}
          showBiggerCoverOnHover={uiOptions?.resource?.showBiggerCoverWhileHover}
          disableMediaPreviewer={uiOptions?.resource?.disableMediaPreviewer}
          disableCache={uiOptions?.resource?.disableCache}
          biggerCoverPlacement={index % columnCount < columnCount / 2 ? 'right' : 'left'}
          mode={multiSelection ? 'select' : 'default'}
          selected={selected}
          onSelectedResourcesChanged={ids => {
            BApi.resource.getResourcesByKeys({ ids, additionalItems: ResourceAdditionalItem.All }).then(res => {
              const rs = res.data || [];
              for (const r of rs) {
                const idx = resourcesRef.current.findIndex(x => x.id == r.id);
                if (idx > -1) {
                  resourcesRef.current[idx] = r;
                }
              }
              setResources([...resourcesRef.current]);
            });
          }}
          onSelected={() => {
            if (selected) {
              setSelectedIds(selectedIds.filter(id => id != resource.id));
            } else {
              setSelectedIds([...selectedIds, resource.id]);
            }
          }}
          selectedResourceIds={selectedIds}
        />
      </div>
    );
  }, [resources, multiSelection, columnCount, selectedIds]);

  log(searchForm?.page, pageable?.page, 'aaaa');

  return (
    <div
      className={`${styles.resourcePage} flex flex-col h-full max-h-full relative`}
      ref={r => {
        pageContainerRef.current = r?.parentElement;
      }}
    >
      <FilterPanel
        selectedResourceIds={selectedIds}
        onSearch={f => search({
          ...f,
          page: 1,
        }, 'replace')}
        multiSelection={multiSelection}
        searchForm={searchForm}
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
        rearrangeResources={() => resourcesComponentRef.current?.rearrange()}
        onSelectAllChange={selected => {
          if (selected) {
            setSelectedIds(resources.map(r => r.id));
          } else {
            setSelectedIds([]);
          }
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
                    page: p,
                  }, 'replace');
                }}
              />
            </div>
          )}
          <Resources
            onScrollToTop={() => {
              // todo: this would be a mess because: 1. the mismatch between the columnCount and the pageSize; 2. Redefining the key of item in react-virtualized seems not to be a easy task.
              // log('scroll to top');
              // if (pageableRef.current && !searchingRef.current && searchFormRef.current) {
              //   const newPage = pageableRef.current.page - 1;
              //   if (newPage > 0 && newPage != searchFormRef.current.pageIndex) search({
              //       pageIndex: newPage,
              //     }, 'prepend', false, false);
              // }
            }}
            cellCount={resources.length}
            columnCount={columnCount}
            onScroll={e => {
              // console.log('scrolling!!!!', e);
              // return;
              if (e.scrollHeight < e.scrollTop + e.clientHeight + 200 && !searchingRef.current) {
                searchingRef.current = true;
                const totalPage = Math.ceil((pageable?.totalCount ?? 0) / PageSize);
                if (searchFormRef.current?.page != undefined && searchFormRef.current.page < totalPage) {
                  search({
                    page: searchFormRef.current.page + 1,
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
                    searchCriteria: searchFormRef.current,
                  });
                }
              }
            }}
            renderCell={renderCell}
            ref={r => {
              resourcesComponentRef.current = r;
            }}
          />
        </>
      )}
      <div
        className={'mt-10 flex items-center gap-2 justify-center left-0 w-ful bottom-0'}
        style={{ position: resources.length == 0 ? 'relative' : 'absolute' }}
      >
        {/* <Spinner label={t('Searching...')} /> */}
        {searching ? (
          <Spinner label={t('Searching...')} />
        ) : (resources.length == 0) && (
          <div className={'flex flex-col gap-2'}>
            <div className={'mb-2 flex items-center gap-1'}>
              <DisconnectOutlined className={'text-base'} />
              <Chip variant={'light'} >
                {t('Resource Not Found. You can try the following solutions:')}
              </Chip>
            </div>
            <div className={'flex flex-col gap-2'}>
              <div className={'flex items-center gap-1'}>
                {t('1. Please check if the search criteria is correct.')}
                <Button
                  size={'sm'}
                  variant={'light'}
                  radius={'sm'}
                  color={'primary'}
                  onClick={() => {
                    search({ page: 1 }, 'replace', true);
                  }}
                >
                  {t('Reset search criteria')}
                </Button>
              </div>
              <div className={'flex items-center gap-1'}>
                {t('2. Please make sure that categories have been created, the media library has been configured, and synchronization has been completed.')}
                <Link
                  size={'sm'}
                  isBlock
                  underline={'none'}
                  href={'#/category'}
                >{t('Go to category page')}</Link>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
