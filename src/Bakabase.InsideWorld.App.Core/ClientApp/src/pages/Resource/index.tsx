import React, { useCallback, useEffect, useRef, useState } from 'react';
import './index.scss';
import { Badge, Balloon, Button, Checkbox, Loading, Message, Pagination, Select } from '@alifd/next';
import { Collapse } from 'react-collapse';
import i18n from 'i18next';
import Queue from 'queue';
import { useTranslation } from 'react-i18next';
import { resourceSearchOrders } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import type { IResourceHandler } from '@/components/Resource';
import Resource from '@/components/Resource';
import type ResourceDto from '@/core/models/Resource';
import QuickSearchSlot from '@/pages/Resource/components/QuickSearchSlot';
import FilterPanel from '@/pages/Resource/components/FilterPanel';
import FavoritesSelector from '@/pages/Resource/components/FavoritesSelector';
import store from '@/store';
import BApi from '@/sdk/BApi';
import { buildLogger } from '@/components/utils';
import MediaLibraryPathSelector from '@/components/MediaLibraryPathSelector';
import ShowTagSelector from '@/components/Resource/components/ShowTagSelector';

const orderDataSource = resourceSearchOrders.reduce<{ label: string; value: string }[]>((s, x) => {
  [{
    flag: '↑',
    asc: true,
  }, {
    flag: '↓',
    asc: false,
  }].forEach((y) => {
    s.push({
      label: `${i18n.t(x.label)} ${y.flag}`,
      value: `${x.value}-${y.asc}`,
    });
  });
  return s;
}, []);

const testResources = [
  // {
  //   fullname: '23213213213213213',
  //   id: 1,
  //   // category: NewResourceCategory.Anime,
  // },
];

const MinResourceWidth = 145;
const MinResourceColCount = 3;
const GridGap = 5;
const DefaultResourceColCount = 6;
const DefaultPageSize = 100;

const log = buildLogger('ResourcePage');

const ResourcePage = (props) => {
  const { t } = useTranslation();

  const resourceHandlersRef = useRef<{ [id: number]: IResourceHandler }>({});
  const [resources, setResources] = useState<ResourceDto[]>(testResources);
  const resourcesRef = useRef<ResourceDto[]>([]);

  // Functional
  const [showBiggerCoverOnHover, setShowBiggerCoverOnHover] = useState(true);
  const multiselectionRef = useRef<boolean>();
  const shiftIsDownRef = useRef<boolean>();

  const pageableRef = useRef({
    pageSize: DefaultPageSize,
    totalCount: 0,
    pageIndex: 1,
    pageCount: 0,
  });
  const [loading, setLoading] = useState(true);
  const [selectedResourceIds, setSelectedResourceIds] = useState<number[]>([]);


  const [multiselection, setMultiselection] = useState(false);
  const thirdPartyOptions = store.useModelState('thirdPartyOptions');
  const uiOptions = store.useModelState('uiOptions');
  const resourcesDomRef = useRef<HTMLDivElement>(null);

  const [panelOpened, setPanelOpened] = useState(true);


  const [searchForm, setSearchForm] = useState({});
  const searchFormRef = useRef({});

  const searchFormCallback = useCallback((f) => search(f), []);

  const resourceLoadCtsRef = useRef<AbortController>();

  const queueRef = useRef(new Queue({
    concurrency: 6,
    autostart: true,
  }));

  const setResourceRef = useCallback((ref) => {
    if (ref) {
      resourceHandlersRef.current[ref.id] = ref;
    }
  }, []);

  const renderTimesRef = useRef(0);
  renderTimesRef.current += 1;
  log(renderTimesRef.current);

  const maxResourceColCount = resourcesDomRef.current ? Math.floor((resourcesDomRef.current.getBoundingClientRect().width + GridGap) / (MinResourceWidth + GridGap)) : DefaultResourceColCount;

  useEffect(() => {
    resourcesRef.current = resources;
  }, [resources]);

  useEffect(() => {
    BApi.options.getResourceOptions()
      .then((a) => {
        const t = a.data?.lastSearch || {};
        log('Got previous search form', t);
        search(t);
      });

    const onKeyDown = (e) => {
      log('Keydown', e);
      switch (e.code) {
        case 'ArrowLeft':
          changePage(pageableRef.current.pageIndex - 1);
          break;
        case 'ArrowRight':
          changePage(pageableRef.current.pageIndex + 1);
          break;
        case 'KeyA': {
          if (e.ctrlKey) {
            if (e.target.tagName?.toLowerCase() != 'input') {
              e.preventDefault();
              e.stopPropagation();
              if (resourcesRef.current?.length > 0) {
                setMultiselection(true);
                setSelectedResourceIds(resourcesRef.current.map((r) => r.id));
              }
            }
          }
          break;
        }
        case 'ShiftLeft': {
          shiftIsDownRef.current = true;
          e.preventDefault();
          e.stopPropagation();
          break;
        }
        case 'Escape':
          if (multiselectionRef.current) {
            exitMultiselectionMode();
          }
          break;
      }
    };

    const onKeyUp = (e) => {
      log('Keyup', e);
      switch (e.code) {
        case 'ShiftLeft': {
          shiftIsDownRef.current = false;
          break;
        }
      }
    };

    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);

    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
    };
  }, []);

  useEffect(() => {
    multiselectionRef.current = multiselection;
  }, [multiselection]);

  useEffect(() => {
    searchFormRef.current = searchForm;
  }, [searchForm]);


  const onTagSearchCallback = useCallback((tagId: number, append: boolean) => {
    const tagIds: number[] = (searchFormRef.current.tagIds ?? []).filter(a => a != tagId);
    if (append) {
      tagIds.push(tagId);
    } else {
      tagIds.splice(0, tagIds.length, tagId);
    }
    search({
      ...searchFormRef.current,
      tagIds,
    });
  }, []);

  const search = useCallback((f = {}) => {
    log('Searching', f);
    if (JSON.stringify(f) !== JSON.stringify(searchFormRef.current)) {
      setSearchForm({ ...f });
    }
    resourceLoadCtsRef.current?.abort();
    BApi.resource.searchResources({
      ...f,
      save: true,
    })
      .then((t) => {
        if (!t.code) {
          resourceLoadCtsRef.current = new AbortController();
          const data = t.data || [];
          pageableRef.current = {
            totalCount: t.totalCount!,
            pageSize: t.pageSize!,
            pageCount: Math.ceil(t.totalCount! / t.pageSize!),
            pageIndex: t.pageIndex!,
          };
          // @ts-ignore
          setResources(data || []);
        }
      })
      .finally(() => {
        setLoading(false);
      });
  }, []);

  const changePage = useCallback(async (p) => {
    // console.log(pageableRef);
    if (p <= pageableRef.current.pageCount && p > 0) {
      search({
        ...searchFormRef.current,
        pageIndex: p,
      });
      document.getElementsByClassName('resource-page')[0].parentElement?.scroll(0, 0);
    }
  }, []);

  const showPagination = useCallback(() => {
    if (pageableRef.current.pageCount > 1) {
      return (
        <div className="pagination">
          <Pagination
            pageShowCount={8}
            current={pageableRef.current.pageIndex}
            pageSize={pageableRef.current.pageSize}
            total={pageableRef.current.totalCount}
            onChange={changePage}
          />
        </div>
      );
    }
    return;
  }, []);

  const afterResourceRemoving = useCallback((id) => {
    setResources(resourcesRef.current.filter((a) => a.id != id));
  }, []);

  const exitMultiselectionMode = () => {
    setMultiselection(false);
    setSelectedResourceIds([]);
  };

  const colCounts: number[] = [];
  for (let i = MinResourceColCount; i <= maxResourceColCount; i++) {
    colCounts.push(i);
  }

  log('Rendering');

  const colCount = uiOptions.resource?.colCount ?? DefaultResourceColCount;

  const renderBulkOperations = useCallback(() => {
    if (multiselection) {
      const operations: { label: string; onClick: () => any; icon: string }[] = [
        {
          label: 'Delete covers',
          icon: 'image-block',
          onClick: () => {
            if (confirm(t('Are you sure to delete those covers and automatically find other candidates?(Source covers will also be deleted)'))) {
              BApi.resource.discardResourceCoverAndFindAnotherOne({
                ids: selectedResourceIds,
              })
                .then((a) => {
                  if (!a.code) {
                    Message.success(t('Operation complete'));
                    BApi.resource.getResourcesByKeys({
                      ids: selectedResourceIds,
                    })
                      .then((b) => {
                        if (!b.code) {
                          selectedResourceIds.forEach(id => {
                            const r = resourceHandlersRef.current[id];
                            if (r) {
                              r.reload();
                            }
                          });
                        }
                      });
                  }
                });
            }
          },
        },
        {
          label: 'Set tags',
          icon: 'tags',
          onClick: () => {
            const data = selectedResourceIds.map(id => {
              const r = resources.find(r => r.id == id)!;
              return {
                ...r,
                handler: resourceHandlersRef.current[id],
              };
            });

            ShowTagSelector(data);
          },
        },
        {
          label: 'Move',
          icon: 'move',
          onClick: () => {
            MediaLibraryPathSelector.show({
              onSelect: path => BApi.resource.moveResources({
                ids: selectedResourceIds,
                path,
              }),
            });
          },
        },
        {
          label: 'Add to favorites',
          icon: 'star',
          onClick: () => {
            FavoritesSelector.show({
              resourceIds: resources.filter((r) => selectedResourceIds.indexOf(r.id) > -1)
                .map(r => r.id),
            });
          },
        },
      ];
      return operations.map(o => {
        return (
          <Balloon.Tooltip
            key={o.label}
            v2
            triggerType={'hover'}
            trigger={(
              <Button
                disabled={!(selectedResourceIds?.length > 0)}
                onClick={o.onClick}
                type={'normal'}
                size={'small'}
              >
                <CustomIcon type={o.icon} />
                {selectedResourceIds?.length > 0 && (
                  <>
                    &nbsp;
                    <Badge count={selectedResourceIds?.length ?? 0} overflowCount={999999} />
                  </>
                )}
              </Button>
            )}
          >
            {t(o.label)}
          </Balloon.Tooltip>
        );
      });
    }
    return;
  }, [multiselection, selectedResourceIds]);


  const renderAdditionalOptionsInFilterPanel = useCallback(() => (
    <>
      <div className="item">
        <Balloon.Tooltip
          trigger={(
            <Checkbox
              onChange={(c) => {
                // console.log(44444);
                setShowBiggerCoverOnHover(c);
              }}
              checked={showBiggerCoverOnHover}
            >
              {t('Larger cover')}
            </Checkbox>
          )}
          align={'t'}
          triggerType={'hover'}
        >
          {t('Show larger cover on mouse hover')}
        </Balloon.Tooltip>
      </div>
      <div className="item">
        <Select
          label={t('Column count')}
          size={'small'}
          dataSource={colCounts}
          onChange={(v) => {
            const n = parseInt(v, 10);
            const patches = {
              resource: {
                ...(uiOptions?.resource || {}),
                colCount: n,
              },
            };
            BApi.options.patchUiOptions(patches);
          }}
          value={colCount}
        />
      </div>
    </>
  ), [showBiggerCoverOnHover, colCount]);

  return (
    <div className="resource-page">
      <div className="panel">
        <Collapse isOpened={panelOpened}>
          <FilterPanel
            search={searchFormCallback}
            searchForm={searchForm}
            renderAdditionalOptions={renderAdditionalOptionsInFilterPanel}
          />
          <div className="other-operations">
            <div className="left">
              <QuickSearchSlot currentSearchForm={searchForm} onSelect={search} />
            </div>
            <div className="right">
              <div className="orders">
                <Select
                  label={t('Orders')}
                  autoWidth
                  mode={'multiple'}
                  style={{
                    maxWidth: 500,
                    minWidth: 200,
                  }}
                  showSearch
                  dataSource={orderDataSource.map((a) => ({
                    ...a,
                    label: t(a.label),
                  }))}
                  value={(searchForm.orders || []).map((a) => `${a.order}-${a.asc}`)}
                  size={'small'}
                  onChange={(arr) => {
                    // console.log(arr);
                    const orderKeys = {};
                    const orders = [];
                    for (let i = arr.length - 1; i >= 0; i--) {
                      const vl = arr[i].split('-');
                      const o = vl[0];
                      if (!(o in orderKeys)) {
                        const a = vl[1];
                        orders.splice(0, 0, {
                          order: parseInt(o),
                          asc: a == 'true',
                        });
                        orderKeys[o] = a;
                      }
                      // console.log(vl, o, orders);
                    }
                    search({
                      ...searchForm,
                      orders,
                    });
                  }}
                />
              </div>
              <div className={'bulk-operations'}>
                {renderBulkOperations()}
                <Balloon.Tooltip
                  trigger={(
                    <Button
                      // type={'primary'}
                      size={'small'}
                      warning={multiselection}
                      text
                      onClick={() => {
                        if (multiselection) {
                          exitMultiselectionMode();
                        } else {
                          setMultiselection(true);
                        }
                      }}
                    >
                      <CustomIcon type={multiselection ? 'exit' : 'Multiselect'} />
                    </Button>
                  )}
                  v2
                  triggerType={'hover'}
                >
                  {t('Bulk operations')}
                </Balloon.Tooltip>
              </div>
            </div>
          </div>
        </Collapse>
        <div className="down" onClick={() => setPanelOpened(!panelOpened)}>
          <CustomIcon size={'small'} type={panelOpened ? 'caret-up' : 'caret-down'} />
        </div>
      </div>

      <Loading fullScreen visible={loading} />
      {showPagination()}
      <div
        className="resources"
        ref={resourcesDomRef}
        style={{
          gridTemplateColumns: `repeat(${colCount}, minmax(0, 1fr))`,
          gap: GridGap,
        }}
      >
        {resources.map((r, i) => {
          // console.log('ready to load cover', r.id, readyToLoadCover);
          return (
            <div
              key={r.id}
              className={`resource-container ${multiselection ? 'multiselection' : ''} ${selectedResourceIds.indexOf(r.id) > -1 ? 'selected' : ''}`}
            >
              <div
                className={'selection-overlay'}
                onClick={() => {
                  const idx = selectedResourceIds.indexOf(r.id);

                  if (shiftIsDownRef.current) {
                    const prevId = selectedResourceIds[selectedResourceIds.length - 1];
                    if (prevId > 0) {
                      const prevIndex = resourcesRef.current.findIndex((a) => a.id == prevId);
                      const startIndex = Math.min(prevIndex, i);
                      const endIndex = Math.max(prevIndex, i);
                      for (let x = startIndex; x <= endIndex; x++) {
                        selectedResourceIds.push(resourcesRef.current[x].id);
                      }
                    } else {
                      selectedResourceIds.push(r.id);
                    }
                    console.log(prevId, idx);
                  } else if (idx == -1) {
                    selectedResourceIds.push(r.id);
                  } else {
                    selectedResourceIds.splice(idx, 1);
                  }
                  setSelectedResourceIds(selectedResourceIds.filter((id, i) => i == selectedResourceIds.indexOf(id)));
                }}
              >
                <CustomIcon size={'xxxl'} type={selectedResourceIds.indexOf(r.id) > -1 ? 'check-circle' : 'plus-circle'} />
              </div>
              <Resource
                queue={queueRef.current}
                showBiggerCoverOnHover={showBiggerCoverOnHover}
                ref={setResourceRef}
                resource={r}
                ct={resourceLoadCtsRef.current!.signal}
                onRemove={afterResourceRemoving}
                // @ts-ignore
                searchEngines={thirdPartyOptions.simpleSearchEngines}
                onTagSearch={onTagSearchCallback}
              />
            </div>
          );
        })}
      </div>
      {showPagination()}
    </div>
  );
};

export default ResourcePage;
