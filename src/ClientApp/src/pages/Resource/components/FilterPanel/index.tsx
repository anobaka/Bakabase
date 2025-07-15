import { Overlay } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import React, { useEffect, useRef, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import {
  OrderedListOutlined,
  QuestionCircleOutlined,
  SaveOutlined,
  SearchOutlined,
  SnippetsOutlined,
} from '@ant-design/icons';
import toast from 'react-hot-toast';
import { AiOutlineFilter, AiOutlineSearch } from 'react-icons/ai';
import styles from './index.module.scss';
import FilterGroupsPanel from './FilterGroupsPanel';
import OrderSelector from './OrderSelector';
import BApi from '@/sdk/BApi';
import store from '@/store';
import { PlaylistCollection } from '@/components/Playlist';
import type { SearchForm } from '@/pages/Resource/models';
import { Autocomplete, AutocompleteItem, Button, Checkbox, Chip, Input, Modal, Popover, Spinner, Tooltip } from '@/components/bakaui';
import CustomIcon from '@/components/CustomIcon';
import type { SavedSearchRef } from '@/pages/Resource/components/FilterPanel/SavedSearches';
import SavedSearches from '@/pages/Resource/components/FilterPanel/SavedSearches';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import MiscellaneousOptions from '@/pages/Resource/components/FilterPanel/MiscellaneousOptions';
import { ResourceTag } from '@/sdk/constants';
import HandleUnknownResources from '@/components/HandleUnknownResources';

const { Popup } = Overlay;

interface IProps {
  selectedResourceIds?: number[];
  maxResourceColCount?: number;
  searchForm?: SearchForm;
  onSearch?: (form: Partial<SearchForm>) => Promise<any>;
  reloadResources: (ids: number[]) => any;
  multiSelection?: boolean;
  rearrangeResources?: () => any;
  onSelectAllChange: (selected: boolean, includeNotLoaded?: boolean) => any;
  resourceCount?: number;
  totalFilteredResourceCount?: number;
}

const MinResourceColCount = 3;
const DefaultResourceColCount = 6;
const DefaultMaxResourceColCount = 10;

const defaultSearchForm = (): SearchForm => ({
  page: 1,
  pageSize: 0,
});

export default ({
  maxResourceColCount = DefaultMaxResourceColCount,
  selectedResourceIds,
  onSearch,
  searchForm: propsSearchForm,
  multiSelection = false,
  rearrangeResources,
  onSelectAllChange,
  resourceCount,
  totalFilteredResourceCount,
}: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const uiOptions = store.useModelState('uiOptions');

  const [colCountsDataSource, setColCountsDataSource] = useState<{ label: any; value: number }[]>([]);
  const colCount = uiOptions.resource?.colCount ?? DefaultResourceColCount;

  const [selectedAll, setSelectedAll] = useState(false);

  const [searchForm, setSearchForm] = useState<SearchForm>(propsSearchForm || defaultSearchForm());
  const [searching, setSearching] = useState(false);

  const [selectingAllFilteredResources, setSelectingAllFilteredResources] = useState(false);

  const savedSearchesRef = useRef<SavedSearchRef>(null);

  const [isLoadingKeywords, setIsLoadingKeywords] = React.useState(false);
  const [keywordCandidates, setKeywordCandidates] = React.useState<string[]>([]);
  const debounceTimer = useRef<NodeJS.Timeout | null>(null);

  const filterGroupPortalRef = React.useRef<HTMLButtonElement>(null);

  useUpdateEffect(() => {
    setSearchForm(propsSearchForm || defaultSearchForm());
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
    console.log('Search form changed', searchForm);
  }, [searchForm]);

  const search = async (patches: Partial<SearchForm>) => {
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

  console.log('resource page filter panel rerender');

  return (
    <div className={`${styles.filterPanel}`}>
      <div className={'flex items-center gap-4'}>
        <Autocomplete
          isLoading={isLoadingKeywords}
          startContent={(
            <SearchOutlined className={'text-xl'} />
          )}
          className={'w-1/4 min-w-[200px]'}
          placeholder={t('Search everything')}
          onValueChange={v => {
            setSearchForm({
              ...searchForm,
              keyword: v,
            });
          }}
          isClearable
          inputValue={searchForm.keyword}
          onKeyDown={e => {
            if (e.key == 'Enter') {
              search({
                ...searchForm,
                page: 1,
              });
            }
          }}
          selectedKey={null}
          onSelectionChange={k => {
            console.log('onSelectionChange', k);
            if (k) {
              const nf = {
                keyword: k as string,
                page: 1,
              };
              search(nf)
            }
          }
          }
          items={keywordCandidates.map(k => ({ label: k, value: k }))}
          onInputChange={v => {
            if (debounceTimer.current) {
              clearTimeout(debounceTimer.current);
            }
            if (v != undefined && v.length > 0) {
              setIsLoadingKeywords(true);
              debounceTimer.current = setTimeout(() => {
                BApi.resource.getResourceSearchKeywordRecommendation({
                  keyword: v,
                }).then(ret => {
                  setKeywordCandidates(ret.data ?? []);
                }).finally(() => {
                  setIsLoadingKeywords(false);
                });
              }, 300);
            }
          }}
        >
          {(item) => <AutocompleteItem title={item.label}
            key={item.value}>{item.label}</AutocompleteItem>}
        </Autocomplete>
        <Button
          ref={filterGroupPortalRef}
          isIconOnly
        />
        <SavedSearches
          ref={savedSearchesRef}
          onSelect={nf => {
            search(nf);
          }}
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
          tags={searchForm.tags}
          onTagsChange={tags => {
            setSearchForm({
              ...searchForm,
              tags,
            });
          }}
        />
      )}
      {searchForm.tags && searchForm.tags.length > 0 && (
        <div className={'flex flex-wrap gap-1 mb-2'}>
          {searchForm.tags.map((tag, i) => {
            return (
              <Chip
                key={i}
                size={'sm'}
                isCloseable
                onClose={() => {
                  setSearchForm({
                    ...searchForm,
                    tags: searchForm.tags?.filter(t => t != tag),
                  });
                }}
              >
                {t(`ResourceTag.${ResourceTag[tag]}`)}
              </Chip>
            );
          })}
        </div>
      )}
      <div className={'flex items-center justify-between'}>
        <div className={'flex items-center gap-4'}>
          <Button
            color={'primary'}
            size={'sm'}
            onPress={async () => {
              await search({
                ...searchForm,
                page: 1,
              });
            }}
            isLoading={searching}
          >
            <AiOutlineSearch className={'text-medium'} />
            {t('Search')}
          </Button>
          <Tooltip
            content={t('Save current search to quick search')}
            placement={'right'}
          >
            <Button
              size={'sm'}
              isIconOnly
              onPress={() => {
                let name = `${t('Untitled search')}1`;
                createPortal(Modal, {
                  defaultVisible: true,
                  size: 'lg',
                  title: t('Save current search'),
                  children: (
                    <Input
                      label={t('Name')}
                      onValueChange={v => name = v?.trim()}
                      defaultValue={name}
                      placeholder={t('Please set a name for current search')}
                      isRequired
                    />
                  ),
                  onOk: async () => {
                    if (name != undefined && name.length > 0) {
                      // @ts-ignore
                      await BApi.resource.saveNewResourceSearch({
                        search: searchForm,
                        name,
                      });
                      savedSearchesRef.current?.reload();
                    } else {
                      toast.error(t('Name is required'));
                      throw new Error('Name is required');
                    }
                  },
                });
              }}
            >
              <SaveOutlined className={'text-base'} />
            </Button>
          </Tooltip>
        </div>
        <div className={'flex items-center gap-2'}>
          {multiSelection && (
            <Chip variant={'light'} color={'success'}>
              <SnippetsOutlined className={'text-base'} />
            </Chip>
          )}
          <Popover
            trigger={<QuestionCircleOutlined className={'text-base'} />}
            color={'success'}
          >
            <div className={'flex flex-col gap-1'}>
              <div>{t('Hold down Ctrl to select multiple resources.')}</div>
              <div>{t('You can perform more actions by right-clicking on the resource.')}</div>
            </div>
          </Popover>
          <Tooltip
            content={(
              <div className={'flex items-center gap-1'}>
                {t('Resources loaded in current page')}
                {selectingAllFilteredResources ? (
                  <Spinner size={'sm'} />
                ) : (totalFilteredResourceCount != resourceCount) && (
                  <Button
                    size={'sm'}
                    variant={'light'}
                    color={'primary'}
                    onPress={async () => {
                      setSelectedAll(true);
                      setSelectingAllFilteredResources(true);
                      try {
                        const ret = onSelectAllChange(true, true);
                        if (!!ret && typeof ret.then === 'function') {
                          await ret;
                        }
                      } finally {
                        setSelectingAllFilteredResources(false);
                      }
                    }}
                  >{t('Select all {{count}} filtered resources (including those not currently loaded).', { count: totalFilteredResourceCount })}</Button>
                )}
              </div>
            )}
          >
            <Checkbox
              isSelected={selectedAll && selectedResourceIds && selectedResourceIds?.length > 0}
              onValueChange={isSelected => {
                onSelectAllChange(isSelected);
                setSelectedAll(isSelected);
              }}
              size={'sm'}
            >{selectedAll ? t('{{count}} items selected', { count: selectedResourceIds?.length }) : t('Select all')}</Checkbox>
          </Tooltip>
          <HandleUnknownResources onHandled={() => search({})} />
          {(totalFilteredResourceCount && totalFilteredResourceCount > 0) ? (
            <div className={'flex items-center gap-1'}>
              <Tooltip content={t('Loaded resources')}>
                <span className={'text-success'} >{resourceCount}</span>
              </Tooltip>
              /
              <Tooltip content={t('All filtered resources')}>
                <span className={'text-secondary'} >{totalFilteredResourceCount}</span>
              </Tooltip>
            </div>
          ) : null}
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
          <MiscellaneousOptions rearrangeResources={rearrangeResources} />
        </div>
      </div>
    </div>
  );
};
