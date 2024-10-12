import { Overlay } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import React, { useCallback, useEffect, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import { EyeOutlined, OrderedListOutlined, SearchOutlined, SnippetsOutlined } from '@ant-design/icons';
import styles from './index.module.scss';
import FilterGroupsPanel from './FilterGroupsPanel';
import OrderSelector from './OrderSelector';
import BApi from '@/sdk/BApi';
import ResourceTagBinderDialog from '@/components/Resource/components/ResourceTagBinderDialog';
import store from '@/store';
import ClickableIcon from '@/components/ClickableIcon';
import { PlaylistCollection } from '@/components/Playlist';
import type { ISearchForm } from '@/pages/Resource/models';
import { Button, Chip, Icon, Input, Popover, Tooltip, DropdownItem, DropdownMenu, DropdownTrigger, Dropdown } from '@/components/bakaui';
import CustomIcon from '@/components/CustomIcon';
import DeleteUnknownResources from '@/components/DeleteUnknownResources';
import { ResourceDisplayContent } from '@/sdk/constants';
import { CoverFit, resourceDisplayContents } from '@/sdk/constants';

const { Popup } = Overlay;

interface IProps {
  onBulkOperationModeChange?: (bulkOperationMode: boolean) => any;
  selectedResourceIds?: number[];
  maxResourceColCount?: number;
  searchForm?: Partial<ISearchForm>;
  onSearch?: (form: Partial<ISearchForm>) => Promise<any>;
  reloadResources: (ids: number[]) => any;
  multiSelection?: boolean;
  rearrangeResources?: () => any;
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
                  multiSelection = false,
                  rearrangeResources,
                }: IProps) => {
  const { t } = useTranslation();

  const [bulkOperationMode, setBulkOperationMode] = useState<boolean>(false);

  const [panelVisible, setPanelVisible] = useState(true);
  const uiOptions = store.useModelState('uiOptions');

  const [colCountsDataSource, setColCountsDataSource] = useState<{ label: any; value: number }[]>([]);
  const colCount = uiOptions.resource?.colCount ?? DefaultResourceColCount;

  const [searchForm, setSearchForm] = useState<Partial<ISearchForm>>(propsSearchForm || {});
  const [searching, setSearching] = useState(false);

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

  const search = async (patches: Partial<ISearchForm>) => {
    if (onSearch) {
      setSearching(true);

      try {
        await onSearch(patches);
      } catch (e) {
        console.error(e);
      } finally {
        setSearching(false);
      }
    }
  };

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

  const currentResourceDisplayContents = uiOptions.resource?.displayContents ?? ResourceDisplayContent.All;
  const selectableResourceDisplayContents = resourceDisplayContents.filter(d => d.value != ResourceDisplayContent.All).map(x => ({ ...x, value: x.value.toString() }));

  return (
    <div className={`${styles.filterPanel}`}>
      <div className={'flex items-center gap-4'}>
        <Input
          startContent={(
            <SearchOutlined className={'text-xl'} />
          )}
          className={'w-1/4'}
          placeholder={t('Search everything')}
          onValueChange={v => {
            setSearchForm({
              ...searchForm,
              keyword: v,
            });
          }}
          value={searchForm.keyword}
          onKeyDown={e => {
            if (e.key == 'Enter') {
              search({
                ...searchForm,
                pageIndex: 1,
              });
            }
          }}
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
              ...searchForm,
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
              search({
                ...searchForm,
                pageIndex: 1,
              });
            }}
            isLoading={searching}
          >
            {t('Search')}
          </Button>
        </div>
        <div className={'flex items-center gap-2'}>
          {multiSelection && (
            <Chip variant={'light'} color={'success'}>
              <SnippetsOutlined className={'text-base'} />
            </Chip>
          )}
          <DeleteUnknownResources onDeleted={() => search({})} />
          <OrderSelector
            className={'mr-2'}
            value={searchForm.orders}
            onChange={orders => {
              const nf = {
                ...searchForm,
                orders,
              };
              setSearchForm(nf);
              search(nf);
            }}
          />
          <Popover trigger={(
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
          </Popover>
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
          <Tooltip content={t('封面占满空白部分')}>
            <Icon
              type={'FullscreenOutlined'}
              className={`${styles.switch} ${uiOptions?.resource?.coverFit == CoverFit.Cover ? styles.on : ''} !text-xl`}
              onClick={() => {
                const current = uiOptions?.resource?.coverFit ?? CoverFit.Contain;
                const n = current == CoverFit.Contain ? CoverFit.Cover : CoverFit.Contain;
                BApi.options.patchUiOptions({
                  resource: {
                    ...(uiOptions?.resource || {}),
                    coverFit: n,
                  },
                });
              }}
            />
          </Tooltip>
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
              className={`${styles.switch} ${uiOptions?.resource?.disableMediaPreviewer ? '' : styles.on} !text-xl`}
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
          <Dropdown>
            <DropdownTrigger>
              <EyeOutlined className={`${styles.switch} !text-xl ${currentResourceDisplayContents ? styles.on : ''}`} />
            </DropdownTrigger>
            <DropdownMenu
              aria-label="Multiple selection example"
              variant="flat"
              closeOnSelect={false}
              selectionMode="multiple"
              selectedKeys={selectableResourceDisplayContents.filter(c => currentResourceDisplayContents & c.value).map(c => c.value.toString())}
              onSelectionChange={keys => {
                let dc: ResourceDisplayContent = 0;
                for (const key of keys) {
                  dc |= parseInt(key as string, 10);
                }
                // console.log(123212321321312, keys);
                BApi.options.patchUiOptions({
                  resource: {
                    ...(uiOptions?.resource || {}),
                    displayContents: dc,
                  },
                }).then(r => {
                  if ((currentResourceDisplayContents & ResourceDisplayContent.Tags) != (dc & ResourceDisplayContent.Tags)) {
                    rearrangeResources?.();
                  }
                });
              }}
            >
              {selectableResourceDisplayContents.map(d => {
                return (
                  <DropdownItem key={d.value.toString()}>{t(d.label)}</DropdownItem>
                );
              })}
            </DropdownMenu>
          </Dropdown>
          {/* <div className={'flex gap-2 items-center'}> */}
          {/*   {renderBulkOperations()} */}
          {/*   <Tooltip */}
          {/*     content={t(bulkOperationMode ? 'Exit bulk operations mode' : 'Bulk operations mode')} */}
          {/*     placement={'left'} */}
          {/*   > */}
          {/*     <ClickableIcon */}
          {/*       className={'text-xl'} */}
          {/*       colorType={bulkOperationMode ? 'danger' : 'normal'} */}
          {/*       type={bulkOperationMode ? 'exit' : 'Multiselect'} */}
          {/*       onClick={() => { */}
          {/*         setBulkOperationMode(!bulkOperationMode); */}
          {/*       }} */}
          {/*     /> */}
          {/*   </Tooltip> */}
          {/* </div> */}
        </div>
      </div>
    </div>
  );
};
