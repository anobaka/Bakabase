import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  Button,
  Checkbox,
  DatePicker2,
  Dropdown,
  Icon,
  Input,
  Menu,
  Message,
  Overlay,
  Rating,
  Select,
} from '@alifd/next';
import dayjs from 'dayjs';
import IceLabel from '@icedesign/label';
import { useTranslation } from 'react-i18next';
import { usePrevious, useUpdate, useUpdateEffect } from 'react-use';
import { get } from 'immer/dist/utils/common';
import CustomIcon from '@/components/CustomIcon';
import type { ResourceLanguage } from '@/sdk/constants';
import { ResourceCategoryAdditionalItem, resourceLanguages, TagAdditionalItem, TagGroupAdditionalItem } from '@/sdk/constants';
import {
  GetAllCustomPropertiesAndCandidates,
  GetAllFavorites,
  GetAllMediaLibraries,
  GetAllReservedPropertiesAndCandidates,
  GetAllResourceCategories,
  GetAllTagGroups,
  GetAllTags,
} from '@/sdk/apis';
import { PlaylistCollection } from '@/components/Playlist';
import './index.scss';

import type { Tag as TagDto } from '@/core/models/Tag';
import type { TagGroup as TagGroupDto } from '@/core/models/TagGroup';
import TagSelector from '@/components/TagSelector';
import BApi from '@/sdk/BApi';
import type IOption from '@/core/models/Common/IOption';
import { buildLogger, getValue, setValue, useTraceUpdate } from '@/components/utils';

const { Popup } = Overlay;

const RangePresets = {
  today: [dayjs(), dayjs()],
  yesterday: [dayjs()
    .add(-1, 'day'), dayjs()
    .add(-1, 'day')],
  'past 3 days': [dayjs()
    .add(-3, 'day'), dayjs()],
  'past 7 days': [dayjs()
    .add(-7, 'day'), dayjs()],
  'this week': [dayjs()
    .startOf('week'), dayjs()
    .endOf('week')],
  'this month': [dayjs()
    .startOf('month'), dayjs()
    .endOf('month')],
};

enum FilterType {
  Select = 1,
  Search = 2,
  DateRange = 3,
  Rating = 4,
  MediaLibrary = 5,
  Tag = 6,
}

interface IFilter {
  name: string;
  key: string;
  type: FilterType;
  dataSource?: IOption<any>[];
  multiple?: boolean;
  onPopupOpen?: () => any;
}

interface ISearchForm {
  mediaLibraryIds?: number[];
  tagIds?: number[];
  excludedTagIds?: number[];
  customProperties?: Record<string, string>;
  name?: string;
  releaseStartDt?: Date | null;
  releaseEndDt?: Date | null;
  addStartDt?: Date | null;
  addEndDt?: Date | null;
  fileCreateStartDt?: Date | null;
  fileCreateEndDt?: Date | null;
  fileModifyStartDt?: Date | null;
  fileModifyEndDt?: Date | null;
  favoritesIds?: number[] | null;
  categoryId?: number | null;
  publisher?: string | null;
  original?: string | null;
  minRate?: number | null;
  languages?: ResourceLanguage[] | null;
  everything?: string;
  // orders: ResourceSearchOptions.OrderModel[] | null;
  customPropertyKeys?: string[];
  parentId?: number | null;
  pageSize?: number;
  hideChildren?: boolean;
  save?: boolean;
  pageIndex?: number;
}

interface IProps {
  search: (f: ISearchForm) => any;
  searchForm?: ISearchForm;
  renderAdditionalOptions?: () => any;
}

const log = buildLogger('ResourcePageFilterPanel');

export default React.memo((props: IProps) => {
  const { t } = useTranslation();
  const {
    search = (f) => {},
    searchForm: propSearchForm = {},
    renderAdditionalOptions,
  } = props;

  useTraceUpdate(props, 'ResourcePageFilterPanel');

  const forceUpdate = useUpdate();
  const [searchForm, setSearchForm] = useState<ISearchForm>(JSON.parse(JSON.stringify(propSearchForm)));
  const searchFormRef = useRef<ISearchForm>(searchForm);

  const categoryLibrariesRef = useRef<{id: number; name: string; libraries: {id: number; name: string}[]}[]>([]);
  const tagIdDisplayNameMapRef = useRef<Record<number, string>>({});
  const customPropertiesAndCandidatesRef = useRef<Record<string, IOption<string>[]>>({});
  const favoritesRef = useRef<IOption<number>[]>([]);
  const reservedPropertiesAndCandidatesRef = useRef<Record<string, IOption<string>[]>>({});

  const [tmpLibraryIds, setTmpLibraryIds] = useState([...(searchForm.mediaLibraryIds || [])]);
  const [tmpTagIds, setTmpTagIds] = useState([...(searchForm.tagIds || [])]);
  const [tmpExcludedTagIds, setTmpExcludedTagIds] = useState([...(searchForm.excludedTagIds || [])]);

  useUpdateEffect(() => {
    searchFormRef.current = searchForm;

    setTmpLibraryIds(searchForm.mediaLibraryIds || []);
    setTmpTagIds(searchForm.tagIds || []);
    setTmpExcludedTagIds(searchForm.excludedTagIds || []);
  }, [searchForm]);

  useUpdateEffect(() => {
    if (JSON.stringify(searchFormRef.current) != JSON.stringify(propSearchForm)) {
      log('searchForm causes rendering.', propSearchForm);
      setSearchForm(propSearchForm);
    }
  }, [propSearchForm]);

  useUpdateEffect(() => {
     log('search causes rendering', search);
  }, [search]);

  useEffect(() => {
    let categories: {id: number; name: string}[] = [];
    let libraries: {id: number; name: string; categoryId: number; order: number}[] = [];
    const tasks: Promise<any>[] = [
      BApi.resourceCategory.getAllResourceCategories()
        .then((t) => {
          t.data?.sort((a, b) => a.order! - b.order!);
          // @ts-ignore
          categories = t.data || [];
        }),
      BApi.mediaLibrary.getAllMediaLibraries()
        .then((t) => {
          t.data?.sort((a, b) => a.order! - b.order!);
          // @ts-ignore
          libraries = t.data || [];
        }),
      BApi.tagGroup.getAllTagGroups({
        additionalItems: TagGroupAdditionalItem.PreferredAlias | TagGroupAdditionalItem.Tags | TagGroupAdditionalItem.TagNamePreferredAlias,
      }).then(a => {
        const groups = a.data || [];
        for (const group of groups) {
          const tags = group.tags || [];
          for (const tag of tags) {
            tagIdDisplayNameMapRef.current[tag.id!] = tag.displayName!;
          }
        }
      }),
      BApi.resource.getAllCustomPropertiesAndCandidates()
        .then((t) => {
          const data = t.data || {};
          const value: Record<string, IOption<string>[]> = {};
          Object.keys(data)
            .forEach((a) => {
              value[a] = data[a]!.map((x) => ({
                label: x,
                value: x,
              }));
            });
          customPropertiesAndCandidatesRef.current = value;
        }),
      BApi.resource.getAllReservedPropertiesAndCandidates()
        .then((t) => {
          const data = t.data || {};
          const value: Record<string, IOption<string>[]> = {};
          Object.keys(data)
            .forEach((a) => {
              value[a.toLowerCase()] = data[a]!.map((x) => ({
                label: x,
                value: x,
              }));
            });
          reservedPropertiesAndCandidatesRef.current = value;
        }),
      BApi.favorites.getAllFavorites()
        .then(t => {
          favoritesRef.current = (t.data || []).map(t => ({
            label: t.name,
            value: t.id!,
          }));
        }),
    ];

    Promise.all(tasks).then(t => {
      for (const c of categories) {
        const tLibraries: any[] = [];
        for (const l of libraries) {
          if (l.categoryId == c.id) {
            tLibraries.push(l);
          }
        }
        tLibraries.sort((a, b) => a.order - b.order);
        categoryLibrariesRef.current!.push({
          id: c.id,
          name: c.name,
          libraries: tLibraries,
        });
      }
      forceUpdate();
    });
  }, []);

  const filters: IFilter[] = [
    {
      name: 'Name',
      key: 'name',
      type: FilterType.Search,
    },
    {
      name: 'Publisher',
      key: 'publisher',
      type: FilterType.Search,
    },
    {
      name: 'Series',
      key: 'series',
      type: FilterType.Search,
    },
    {
      name: 'Original',
      key: 'original',
      type: FilterType.Search,
    },
    {
      name: 'Language',
      key: 'languages',
      dataSource: resourceLanguages.map((x) => ({
        label: t(x.label),
        value: x.value,
      })),
      multiple: true,
      type: FilterType.Select,
    },
    {
      name: 'Add Date',
      key: 'addDts',
      multiple: true,
      type: FilterType.DateRange,
    },
    {
      name: 'Release Date',
      key: 'releaseDts',
      multiple: true,
      type: FilterType.DateRange,
    },
    {
      name: 'File Add Date',
      key: 'fileCreateDts',
      multiple: true,
      type: FilterType.DateRange,
    },
    {
      name: 'File Modify Date',
      key: 'fileModifyDts',
      multiple: true,
      type: FilterType.DateRange,
    },
    {
      name: 'Min Rate',
      key: 'minRate',
      type: FilterType.Rating,
    },
  ];

  for (const f of filters) {
    if (f.type == FilterType.Search && !f.dataSource) {
      f.dataSource = reservedPropertiesAndCandidatesRef.current[f.key.toLowerCase()];
    }
  }

  const customPropertyFilters: IFilter[] = [];

  if (searchForm.customPropertyKeys) {
    searchForm.customPropertyKeys.forEach((p) => {
      customPropertyFilters.push({
        name: p,
        key: `customProperties.${p}`,
        type: FilterType.Search,
        dataSource: customPropertiesAndCandidatesRef.current[p] || [],
      });
    });
  }

  const filterGroups: IFilter[][] = [
    // [{
    //   name: 'Keyword',
    //   key: 'keyword',
    //   type: FilterType.Search,
    // }],
    filters,
    customPropertyFilters,
    [
      {
        name: 'Media Library',
        key: 'mediaLibraryIds',
        multiple: true,
        type: FilterType.MediaLibrary,
        onPopupOpen: () => {
          setTmpLibraryIds(searchForm.mediaLibraryIds || []);
        },
      },
      {
        name: 'Favorites',
        key: 'favoritesIds',
        multiple: true,
        type: FilterType.Select,
        dataSource: favoritesRef.current,
      },
      {
        name: 'Tag',
        key: 'tagIds',
        type: FilterType.Tag,
      },
      {
        name: 'Excluded tags',
        key: 'excludedTagIds',
        type: FilterType.Tag,
      },
      {
        name: 'Custom Properties',
        key: 'customPropertyKeys',
        type: FilterType.Select,
        dataSource: Object.keys(customPropertiesAndCandidatesRef.current)
          .map((c) => ({
            label: c,
            value: c,
          })),
        multiple: true,
      },
    ],
  ].filter(a => a?.length > 0);

  const patchSearchForm = useCallback((patches: Partial<ISearchForm>, thenSearch: boolean = false) => {
    const newForm = {
      ...searchForm,
      ...patches,
      pageIndex: 1,
    };
    setSearchForm(newForm);
    if (thenSearch) {
      search(newForm);
    }
  }, [searchForm]);

  const patchSearchFormByKey = useCallback((key: string, value: any, thenSearch: boolean = false) => {
    setValue(searchForm, key, value);
    const newForm = { ...searchForm, pageIndex: 1 };
    setSearchForm(newForm);
    if (thenSearch) {
      search(newForm);
    }
  }, [searchForm]);

  log('Rendering', props);

  return (
    <div className="new-search-panel">
      {filterGroups.map((nf, i) => {
        return (
          <div className={'group'} key={i}>
            {i == 0 && (
              <div className="filter">
                <Input.Group
                  className={'keyword'}
                  addonAfter={(
                    <Button
                      type={'normal'}
                      onClick={() => {
                        search(searchForm);
                      }}
                    >
                      <Icon type="search" />
                    </Button>
                  )}
                >
                  <Input
                    placeholder={t('Search everything')}
                    value={searchForm.everything}
                    onChange={(v) => patchSearchForm({
                      everything: v,
                    })}
                    onKeyDown={(e) => {
                      if (e.key == 'Enter') {
                        search(searchForm);
                      }
                    }}
                    hasClear
                  />
                </Input.Group>
              </div>
            )}
            {nf.map((f, j) => {
              let filtering;
              let value;
              if (f.type == FilterType.DateRange) {
                const prefix = f.key.replace(/Dts$/, '');
                const keys = [`${prefix}StartDt`, `${prefix}EndDt`];
                value = [];
                for (const key of keys) {
                  value.push(getValue(searchForm, key)?.substring(0, 10));
                }
                filtering = value.some((v) => v);
              } else {
                filtering = f.multiple ? getValue(searchForm, f.key)?.length > 0 : !(!getValue(searchForm, f.key));
                value = getValue(searchForm, f.key);
              }
              // log(f.key, getValue(searchForm, f.key));
              let label;
              if (filtering) {
                switch (f.type) {
                  case FilterType.Search:
                    label = f.multiple ? value.join(', ') : value;
                    break;
                  case FilterType.Select:
                    label = f.multiple ? value.map((k) => f.dataSource!.find((x) => x.value == k))
                      .map((x) => x.label)
                      .join(', ') : f.dataSource!.find((x) => x.value == value)!.label;
                    break;
                  case FilterType.DateRange:
                    label = `${value[0] || ''}~${value[1] || ''}`;
                    break;
                  // case FilterType.Rating:
                  //   label = <>{f.name}: <Rating disabled allowHalf value={value} /> </>;
                  //   break;
                  case FilterType.MediaLibrary: {
                    if (value.length > 0) {
                      label = (
                        <div className={'selected-media-libraries'}>
                          {value.map((id, i) => {
                            const cl = categoryLibrariesRef.current.find(a => a.libraries.some(b => b.id == id));
                            const ml = cl?.libraries.find(l => l.id == id);
                            if (ml) {
                              return (
                                <div key={ml.id}>
                                  <span className={'name'}>{ml.name}</span>
                                  <span className={'category'}>[{cl!.name}]</span>
                                  {i < value.length - 1 && ','}
                                </div>
                              );
                            }
                            return;
                          }).filter(t => t)}
                        </div>
                      );
                    } else {
                      filtering = false;
                    }
                    break;
                  }
                  case FilterType.Tag: {
                    if (value.length > 0) {
                      label = (
                        <div className="selected-tags">
                          {value.map((tagId, x) => {
                            return (
                              <div key={tagId.id}>
                                <span style={{ color: tagId.color }}>{tagIdDisplayNameMapRef.current[tagId] ?? t('Invalid tag')}</span>
                                {x < value.length - 1 && ','}
                              </div>
                            );
                          })}
                        </div>
                      );
                    } else {
                      filtering = false;
                    }
                    break;
                  }
                  default:
                    label = f.multiple ? value.join(', ') : value;
                    break;
                }
              }
              let valueContainer;
              switch (f.type) {
                case FilterType.Search:
                  if (f.dataSource) {
                    valueContainer = (
                      <div>
                        <Select.AutoComplete
                          dataSource={f.dataSource}
                          hasClear
                          onChange={(v, actionType) => {
                            if (actionType == 'itemClick') {
                              patchSearchFormByKey(f.key, v, true);
                            } else {
                              value = v;
                            }
                          }}
                          // popupProps={{ v2: true }}
                          // useVirtual
                          // autoWidth
                          style={{ width: 300 }}
                        />
                        <Button
                          type={'normal'}
                          onClick={() => {
                            patchSearchFormByKey(f.key, value, true);
                          }}
                        >
                          {t('Search')}
                        </Button>
                      </div>
                    );
                  } else {
                    valueContainer = (
                      <div>
                        <Input.Group addonAfter={(
                          <Button
                            type={'normal'}
                            onClick={() => {
                              patchSearchFormByKey(f.key, value, true);
                            }}
                          >Search
                          </Button>
                        )}
                        >
                          <Input
                            hasClear
                            onChange={(v) => {
                              value = v;
                            }}
                          />
                        </Input.Group>
                      </div>
                    );
                  }
                  break;
                case FilterType.Select:
                {
                  const selectedKeys = getValue(searchForm, f.key) || (f.multiple ? [] : undefined);
                  // ice.Menu.selectedKeys does not recognize number value
                  if (f.multiple) {
                    for (let i = 0; i < selectedKeys.length; i++) {
                      selectedKeys[i] = `${selectedKeys[i]}`;
                    }
                  }
                  valueContainer = (
                    <Menu
                      hasSelectedIcon
                      selectMode={f.multiple ? 'multiple' : 'single'}
                      selectedKeys={selectedKeys}
                      onSelect={(keys) => {
                        let value: any;
                        if (f.multiple) {
                          value = keys.filter((v, i) => keys.findIndex(x => x == v) == i);
                        } else {
                          value = keys;
                        }
                        console.log(value, keys, f);
                        patchSearchFormByKey(f.key, value, true);
                      }}
                    >
                      {f.dataSource!.map((d) => <Menu.Item key={d.value}>{d.label}</Menu.Item>)}
                    </Menu>
                  );
                  break;
                }
                case FilterType.DateRange:
                  valueContainer = (
                    <DatePicker2.RangePicker
                      popupProps={{ v2: true }}
                      hasClear
                      value={value}
                      onOk={(vs) => {
                        const dateStrs = vs.map((t) => (t ? t.format('YYYY-MM-DD') : undefined));
                        const prefix = f.key.replace(/Dts$/g, '');
                        const keys = [`${prefix}StartDt`, `${prefix}EndDt`];
                        for (let j = 0; j < keys.length; j++) {
                          const v = dateStrs[j];
                          if (v) {
                            patchSearchFormByKey(keys[j], v, false);
                          }
                        }
                        patchSearchForm({ pageIndex: 1 }, true);
                      }}
                      showOk
                      preset={RangePresets}
                    />
                  );
                  break;
                case FilterType.Rating:
                  valueContainer = (
                    <Rating
                      allowHalf
                      onChange={(v) => {
                        patchSearchFormByKey(f.key, v, true);
                      }}
                    />
                  );
                  break;
                case FilterType.MediaLibrary: {
                  valueContainer = (
                    <div className={'media-libraries'}>
                      <div className="categories">
                        {
                          Object.keys(categoryLibrariesRef.current).map((k) => {
                            const category = categoryLibrariesRef.current[k];
                            return (
                              <div key={category.id} className={'category'}>
                                <Button
                                  text
                                  className={'name'}
                                  title={category.name}
                                  type={'primary'}
                                  onClick={() => {
                                    const allIds = category.libraries.map((a) => a.id);
                                    const notAllIdSelected = allIds.some(id => !tmpLibraryIds.includes(id));
                                    const newIds = notAllIdSelected ? tmpLibraryIds.concat(allIds) : tmpLibraryIds.filter((a) => !allIds.includes(a));
                                    setTmpLibraryIds(newIds);
                                  }}
                                >{category.name}
                                </Button>
                                <div className="libraries">
                                  {category.libraries.map((ml) => {
                                    const checked = tmpLibraryIds.includes(ml.id);
                                    return (
                                      <Checkbox
                                        key={ml.id}
                                        checked={checked}
                                        onChange={() => {
                                          const newIds = checked ? tmpLibraryIds.filter((a) => a != ml.id) : tmpLibraryIds.concat(ml.id);
                                          setTmpLibraryIds(newIds);
                                        }}
                                      >
                                        <span className={'name'}>{ml.name}</span>
                                        <span className={'count'}>[{ml.resourceCount}]</span>
                                      </Checkbox>
                                    );
                                  })}
                                </div>
                              </div>
                            );
                          })
                        }
                      </div>
                      <div className="submit">
                        <Button
                          type={'primary'}
                          size={'small'}
                          onClick={() => {
                            patchSearchFormByKey(f.key, tmpLibraryIds, true);
                          }}
                        >{t('Search')}
                        </Button>
                      </div>
                    </div>
                  );
                  break;
                }
                case FilterType.Tag: {
                  const state = f.key == 'tagIds' ? tmpTagIds : tmpExcludedTagIds;
                  const setState = f.key == 'tagIds' ? setTmpTagIds : setTmpExcludedTagIds;
                  valueContainer = (
                    // <div style={{ display: 'flex', flexDirection: 'column' }}>
                    <>
                      <TagSelector
                        value={{
                          tagIds: state,
                        }}
                        // secondarySelection
                        onChange={(value) => {
                          setState(value.tagIds);
                        }}
                      />
                      <div className={'filter-tag-selector-operations'}>
                        <div className="left" />
                        <div className="right">
                          <Button
                            type={'primary'}
                            size={'small'}
                            onClick={() => {
                              patchSearchFormByKey(f.key, state, true);
                            }}
                          >
                            {t('Search')}
                          </Button>
                        </div>
                      </div>
                    </>
                    // </div>
                  );
                  break;
                }
              }

              return (
                <div className={`filter ${filtering ? 'filtering' : ''}`} key={j}>
                  <Icon
                    className={'clear'}
                    type="delete-filling"
                    size={'small'}
                    onClick={() => {
                      if (f.type == FilterType.DateRange) {
                        const prefix = f.key.replace(/Dts$/g, '');
                        const keys = [`${prefix}StartDt`, `${prefix}EndDt`];
                        for (let j = 0; j < keys.length; j++) {
                          patchSearchFormByKey(keys[j], undefined, false);
                        }
                        patchSearchForm({ pageIndex: 1 }, true);
                      } else {
                        patchSearchFormByKey(f.key, undefined, true);
                      }
                    }}
                  />
                  <Popup
                    v2
                    trigger={
                      <div className="hover-area">
                        <div className={`label ${label ? 'has-value' : ''}`}>
                          <div className="name">{t(f.name)}</div>
                          {label && (
                            <div className="value">
                              {label}
                            </div>
                          )}
                        </div>
                        <CustomIcon type={'caret-down'} size={'xs'} />
                      </div>
                    }
                    triggerType="click"
                  >
                    <div className="resource-filter-popup">
                      {valueContainer}
                    </div>
                  </Popup>
                </div>
              );
            })}
            {i == filterGroups.length - 1 && (
              <>
                <Dropdown
                  triggerType={'click'}
                  trigger={(
                    <div className={'filter'}>
                      <div className="hover-area">
                        {t('Playlist')}
                        &nbsp;
                        <CustomIcon type={'caret-down'} size={'xs'} />
                      </div>
                    </div>
                  )}
                >
                  <PlaylistCollection className={'resource-page'} />
                </Dropdown>
              </>
            )}
          </div>
        );
      })}
      <div className="group last">
        <div className="left">
          <Button
            type={'normal'}
            size={'small'}
            onClick={() => {
              setSearchForm({});
              search({});
            }}
          >
            {t('Reset')}
          </Button>
        </div>
        <div className="right">

          <Dropdown
            autoClose={false}
            trigger={(
              <Button
                size={'small'}
                className={'other-options'}
              >
                <CustomIcon
                  type={'setting'}
                  size={'small'}
                />
              </Button>
            )}
            align={'tr tl'}
            triggerType={['click']}
          >
            <div className={'other-options-popup'}>
              <div className="filter hide-children item">
                <Checkbox
                  label={t('Hide children')}
                  checked={searchForm.hideChildren}
                  onChange={(c) => {
                    patchSearchForm({
                      hideChildren: c,
                    }, true);
                  }}
                />
              </div>
              {renderAdditionalOptions?.()}
            </div>
          </Dropdown>
        </div>
      </div>
    </div>
  );
});

