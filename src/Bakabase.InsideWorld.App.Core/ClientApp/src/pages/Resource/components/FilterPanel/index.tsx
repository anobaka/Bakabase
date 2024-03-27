import { Balloon, Button, Checkbox, Dropdown, Input, Notification, Overlay } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import React, { useCallback, useEffect, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import styles from './index.module.scss';
import FilterGroupsPanel from './components/FilterGroupsPanel';
import BApi from '@/sdk/BApi';
import CustomIcon from '@/components/CustomIcon';
import ShowTagSelector from '@/components/Resource/components/ShowTagSelector';
import MediaLibraryPathSelector from '@/components/MediaLibraryPathSelector';
import FavoritesSelector from '@/pages/Resource/components/FavoritesSelector';
import store from '@/store';
import ClickableIcon from '@/components/ClickableIcon';
import OrderSelector from '@/pages/Resource/components/FilterPanel/components/OrderSelector';
import { PlaylistCollection } from '@/components/Playlist';
import type { ISearchForm } from '@/pages/Resource/models';

const { Popup } = Overlay;

interface IProps {
  onSelectionModeChange?: (mode: 'single' | 'multiple') => any;
  selectedResourceIds?: number[];
  maxResourceColCount?: number;
  searchForm?: Partial<ISearchForm>;
  onSearch?: (form: Partial<ISearchForm>) => any;
}

const MinResourceColCount = 3;
const DefaultResourceColCount = 6;
const DefaultMaxResourceColCount = 10;

export default ({
                  selectedResourceIds,
                  onSelectionModeChange,
                  maxResourceColCount = DefaultMaxResourceColCount,
                  onSearch,
                  searchForm: propsSearchForm,
                }: IProps) => {
  const { t } = useTranslation();

  const [selectionMode, setSelectionMode] = useState<'single' | 'multiple'>('single');

  const [panelVisible, setPanelVisible] = useState(true);
  const uiOptions = store.useModelState('uiOptions');

  const [colCountsDataSource, setColCountsDataSource] = useState<{ label: any; value: number }[]>([]);
  const colCount = uiOptions.resource?.colCount ?? DefaultResourceColCount;

  const [searchForm, setSearchForm] = useState<Partial<ISearchForm>>(propsSearchForm || {});

  useUpdateEffect(() => {
    setSearchForm(propsSearchForm || {});
  }, [propsSearchForm]);

  useEffect(() => {
    const ccds: { label: any; value: number }[] = [];
    for (let i = MinResourceColCount; i <= maxResourceColCount; i++) {
      ccds.push({
        label: i,
        value: i,
      });
    }
    setColCountsDataSource(ccds);
  }, [maxResourceColCount]);

  useUpdateEffect(() => {
    onSelectionModeChange?.(selectionMode);
  }, [selectionMode]);

  const renderBulkOperations = useCallback(() => {
    if (selectionMode == 'multiple') {
      const operations: { label: string; onClick: () => any; icon: string }[] = [
        {
          label: 'Set tags',
          icon: 'tags',
          onClick: () => {
            const data = selectedResourceIds?.map(id => {
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

      const anyResourceSelected = selectedResourceIds && selectedResourceIds.length > 0;

      return operations.map(o => {
        return (
          <Balloon.Tooltip
            key={o.label}
            v2
            triggerType={'hover'}
            trigger={(
              <ClickableIcon
                type={o.icon}
                size={'small'}
                colorType={'normal'}
                onClick={o.onClick}
                className={anyResourceSelected ? '' : styles.disabled}
              />
            )}
          >
            {t(o.label)}
          </Balloon.Tooltip>
        );
      });
    }
    return;
  }, [selectionMode, selectedResourceIds]);

  return (
    <div className={`${styles.filterPanel} ${!panelVisible ? styles.folded : ''}`}>
      <ClickableIcon
        colorType={'normal'}
        size={'small'}
        type={panelVisible ? 'caret-up' : 'search'}
        className={styles.collapseSwitcher}
        onClick={() => {
          setPanelVisible(!panelVisible);
        }}
      />
      <div className={styles.line1}>
        <div className={styles.left}>
          <Input
            innerAfter={(
              <CustomIcon
                type={'search'}
                size={'small'}
                style={{ padding: '0 5px' }}
              />
            )}
            className={styles.searchInput}
            placeholder={t('Search everything')}
          />
          <Dropdown
            triggerType={'click'}
            trigger={(
              <Button
                className={styles.contentCenterAlignedBtn}
                type={'normal'}
                size={'small'}
                onClick={() => {

                }}
              >
                <CustomIcon
                  type={'playlistplay'}
                  // size={'small'}
                />
                {t('Playlist')}
              </Button>
            )}
          >
            <PlaylistCollection className={'resource-page'} />
          </Dropdown>
        </div>
        <div className={styles.right} />
      </div>
      <div className={styles.line2}>
        <div className={styles.line3}>
          <FilterGroupsPanel
            group={searchForm.group}
            onChange={v => {
            setSearchForm({
              group: v,
            });
          }}
          />
        </div>
      </div>
      <div className={styles.line3}>
        <div className={styles.left}>
          <Button
            type={'primary'}
            size={'small'}
            onClick={async () => {
              onSearch?.({
                ...searchForm,
                pageIndex: 1,
              });
            }}
          >
            {t('Search')}
          </Button>
        </div>
        <div className={styles.right}>
          <OrderSelector
            value={searchForm.orders}
            onChange={orders => {
              const nf = {
                ...searchForm,
                orders,
              };
              setSearchForm(nf);
              onSearch?.(nf);
            }}
          />
          <Balloon.Tooltip
            trigger={(
              <Checkbox
                onChange={(c) => {
                  BApi.options.patchUiOptions({
                    resource: {
                      ...(uiOptions?.resource || {}),
                      showBiggerCoverWhileHover: c,
                    },
                  }).then(r => {
                    if (!r.code) {
                      Notification.success({
                        title: t('Saved'),
                      });
                    }
                  });
                }}
                checked={uiOptions?.resource?.showBiggerCoverWhileHover}
              >
                {t('Larger cover')}
              </Checkbox>
            )}
            align={'l'}
            triggerType={'hover'}
          >
            {t('Show larger cover on mouse hover')}
          </Balloon.Tooltip>
          <Balloon.Tooltip
            trigger={(
              <Checkbox
                onChange={(c) => {
                  BApi.options.patchUiOptions({
                    resource: {
                      ...(uiOptions?.resource || {}),
                      disableMediaPreviewer: !c,
                    },
                  }).then(r => {
                    if (!r.code) {
                      Notification.success({
                        title: t('Saved'),
                      });
                    }
                  });
                }}
                checked={!uiOptions?.resource?.disableMediaPreviewer}
              >
                {t('快速预览')}
              </Checkbox>
            )}
            align={'l'}
            triggerType={'hover'}
          >
            {t('Preview files of a resource on mouse hover')}
          </Balloon.Tooltip>
          <Balloon.Tooltip
            trigger={(
              <Checkbox
                onChange={(c) => {
                  BApi.options.patchUiOptions({
                    resource: {
                      ...(uiOptions?.resource || {}),
                      disableCache: c,
                    },
                  }).then(r => {
                    if (!r.code) {
                      Notification.success({
                        title: t('Saved'),
                      });
                    }
                  });
                }}
                checked={uiOptions?.resource?.disableCache}
              >
                {t('Disable cache')}
              </Checkbox>
            )}
            triggerType={'hover'}
            align={'l'}
          >
            {t('Disabling cache may cause performance issues')}
          </Balloon.Tooltip>
          <div className={styles.columnCount}>
            {t('Column count')}
            &nbsp;
            <Popup
              v2
              trigger={(
                <Button
                  type={'primary'}
                  // size={'small'}
                  text
                >
                  {colCount}
                </Button>
              )}
              triggerType={'click'}
            >
              <div className={styles.columnCounts}>
                {colCountsDataSource.map((cc, i) => {
                  return (
                    <Button
                      type={'normal'}
                      size={'small'}
                      onClick={async () => {
                        const patches = {
                          resource: {
                            ...(uiOptions?.resource || {}),
                            colCount: cc.value,
                          },
                        };
                        await BApi.options.patchUiOptions(patches);
                      }}
                    >
                      {cc.label}
                    </Button>
                  );
                })}
              </div>
            </Popup>
          </div>
          <div className={styles.bulkOperations}>
            {renderBulkOperations()}
            <Balloon.Tooltip
              trigger={(
                <ClickableIcon
                  colorType={selectionMode == 'multiple' ? 'danger' : 'normal'}
                  type={selectionMode == 'multiple' ? 'exit' : 'Multiselect'}
                  onClick={() => {
                    setSelectionMode(selectionMode == 'multiple' ? 'single' : 'multiple');
                  }}
                />
              )}
              v2
              triggerType={'hover'}
            >
              {t('Bulk operations')}
            </Balloon.Tooltip>
          </div>
        </div>
      </div>
    </div>
  );
};
