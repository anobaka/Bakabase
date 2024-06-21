import { Balloon, Dropdown, Overlay } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import React, { useCallback, useEffect, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import { OrderedListOutlined, SearchOutlined } from '@ant-design/icons';
import { PopoverContent, PopoverTrigger } from '@nextui-org/react';
import styles from './index.module.scss';
import FilterGroupsPanel from './FilterGroupsPanel';
import OrderSelector from './OrderSelector';
import BApi from '@/sdk/BApi';
import ResourceTagBinderDialog from '@/components/Resource/components/ResourceTagBinderDialog';
import MediaLibraryPathSelector from '@/components/MediaLibraryPathSelector';
import FavoritesSelector from '@/pages/Resource/components/FavoritesSelector';
import store from '@/store';
import ClickableIcon from '@/components/ClickableIcon';
import { PlaylistCollection } from '@/components/Playlist';
import type { ISearchForm } from '@/pages/Resource/models';
import { Button, Icon, Input, Popover, Tooltip } from '@/components/bakaui';
import CustomIcon from '@/components/CustomIcon';

const { Popup } = Overlay;

interface IProps {
  onBulkOperationModeChange?: (bulkOperationMode: boolean) => any;
  selectedResourceIds?: number[];
  maxResourceColCount?: number;
  searchForm?: Partial<ISearchForm>;
  onSearch?: (form: Partial<ISearchForm>) => any;
  reloadResources: (ids: number[]) => any;
}

const MinResourceColCount = 3;
const DefaultResourceColCount = 6;
const DefaultMaxResourceColCount = 10;

export default ({
                  selectedResourceIds,
                  onBulkOperationModeChange,
                  maxResourceColCount = DefaultMaxResourceColCount,
                  onSearch,
                  searchForm: propsSearchForm,
                  reloadResources,
                }: IProps) => {
  const { t } = useTranslation();

  const [bulkOperationMode, setBulkOperationMode] = useState<boolean>(false);

  const [panelVisible, setPanelVisible] = useState(true);
  const uiOptions = store.useModelState('uiOptions');

  const [colCountsDataSource, setColCountsDataSource] = useState<{ label: any; value: number }[]>([]);
  const colCount = uiOptions.resource?.colCount ?? DefaultResourceColCount;

  const [searchForm, setSearchForm] = useState<Partial<ISearchForm>>(propsSearchForm || {});

  const filterGroupPortalRef = React.useRef<HTMLButtonElement>(null);

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
    onBulkOperationModeChange?.(bulkOperationMode);
  }, [bulkOperationMode]);

  useUpdateEffect(() => {
    console.log('Search form changed', searchForm);
  }, [searchForm]);

  const renderBulkOperations = useCallback(() => {
    if (bulkOperationMode) {
      const operations: { label: string; onClick: () => any; icon: string }[] = [
        {
          label: 'Set tags',
          icon: 'tags',
          onClick: () => {
            ResourceTagBinderDialog.show({
              resourceIds: selectedResourceIds!,
              onSaved: () => reloadResources?.(selectedResourceIds!),
            });
          },
        },
        // {
        //   label: 'Move',
        //   icon: 'move',
        //   onClick: () => {
        //     MediaLibraryPathSelector.show({
        //       onSelect: path => BApi.resource.moveResources({
        //         ids: selectedResourceIds!,
        //         path,
        //       }),
        //     });
        //   },
        // },
        // {
        //   label: 'Add to favorites',
        //   icon: 'star',
        //   onClick: () => {
        //     FavoritesSelector.show({
        //       resourceIds: selectedResourceIds!,
        //     });
        //   },
        // },
      ];

      const anyResourceSelected = selectedResourceIds && selectedResourceIds.length > 0;

      return operations.map(o => {
        return (
          <Tooltip
            content={t(o.label)}
          >
            <ClickableIcon
              type={o.icon}
              colorType={'normal'}
              onClick={() => {
                if (anyResourceSelected) {
                  o.onClick();
                }
              }}
              className={`text-xl ${anyResourceSelected ? '' : '!cursor-not-allowed opacity-50'}`}
            />
          </Tooltip>
        );
      });
    }
    return;
  }, [bulkOperationMode, selectedResourceIds]);

  return (
    <div className={`${styles.filterPanel}`}>
      <div className={'flex items-center gap-4'}>
        <Input
          startContent={(
            <SearchOutlined className={'text-xl'} />
          )}
          className={'w-1/4'}
          placeholder={t('Search everything')}
        />
        <Button
          ref={filterGroupPortalRef}
          isIconOnly
        />
      </div>
      {filterGroupPortalRef.current && (
        <FilterGroupsPanel
          portalContainer={filterGroupPortalRef.current}
          group={searchForm.group}
          onChange={v => {
            setSearchForm({
              group: v,
            });
          }}
        />
      )}
      <div className={'flex items-center justify-between'}>
        <div className={'flex items-center gap-4'}>
          <Button
            color={'primary'}
            size={'sm'}
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
        <div className={'flex items-center gap-2'}>
          <OrderSelector
            className={'mr-2'}
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
          <Dropdown
            triggerType={'click'}
            trigger={(
              <Button
                color={'default'}
                size={'sm'}
                startContent={<CustomIcon
                  type={'playlistplay'}
                  className={'text-xl'}
                />}
              >
                {t('Playlist')}
              </Button>
            )}
          >
            <PlaylistCollection className={'resource-page'} />
          </Dropdown>
          <Popover
            placement={'bottom-end'}
            trigger={(
              <Button
                startContent={<OrderedListOutlined />}
                color={'default'}
                size={'sm'}
              >
                {t('Column count')}
                &nbsp;
                {colCount}
              </Button>
            )}
          >
            <div className={'grid grid-cols-4 gap-1 p-1 rounded'}>
              {colCountsDataSource.map((cc, i) => {
                return (
                  <Button
                    key={i}
                    color={'default'}
                    size={'sm'}
                    className={'min-w-0 pl-2 pr-2'}
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
          </Popover>
          <Tooltip content={t('Show larger cover on mouse hover')}>
            <Icon
              type={'ZoomInOutlined'}
              className={`${styles.switch} ${uiOptions?.resource?.showBiggerCoverWhileHover ? styles.on : ''} !text-xl`}
              onClick={() => {
                BApi.options.patchUiOptions({
                  resource: {
                    ...(uiOptions?.resource || {}),
                    showBiggerCoverWhileHover: !uiOptions?.resource?.showBiggerCoverWhileHover,
                  },
                });
              }}
            />
          </Tooltip>
          <Tooltip content={t('Preview files of a resource on mouse hover')}>
            <Icon
              type={'PlayCircleOutlined'}
              className={`${styles.switch} ${uiOptions?.resource?.disableMediaPreviewer ? styles.on : ''} !text-xl`}
              onClick={() => {
                BApi.options.patchUiOptions({
                  resource: {
                    ...(uiOptions?.resource || {}),
                    disableMediaPreviewer: !uiOptions?.resource?.disableMediaPreviewer,
                  },
                });
              }}
            />
          </Tooltip>
          <Tooltip content={t('Enabling caching can improve loading speed')}>
            <Icon
              type={'DashboardOutlined'}
              className={`${styles.switch} ${uiOptions?.resource?.disableCache ? '' : styles.on} !text-xl`}
              onClick={() => {
                BApi.options.patchUiOptions({
                  resource: {
                    ...(uiOptions?.resource || {}),
                    disableCache: !uiOptions?.resource?.disableCache,
                  },
                });
              }}
            />
          </Tooltip>
          <div className={'flex gap-2 items-center'}>
            {renderBulkOperations()}
            <Tooltip
              content={t(bulkOperationMode ? 'Exit bulk operations mode' : 'Bulk operations mode')}
              placement={'left'}
            >
              <ClickableIcon
                className={'text-xl'}
                colorType={bulkOperationMode ? 'danger' : 'normal'}
                type={bulkOperationMode ? 'exit' : 'Multiselect'}
                onClick={() => {
                  setBulkOperationMode(!bulkOperationMode);
                }}
              />
            </Tooltip>
          </div>
        </div>
      </div>
    </div>
  );
};
