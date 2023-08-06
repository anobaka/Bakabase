import React, { useEffect, useState } from 'react';
import './index.scss';
import i18n from 'i18next';
import { Balloon, Button, Divider, Input, Rating, Tag } from '@alifd/next';
import dayjs from 'dayjs';
import IceLabel from '@icedesign/label';
import { useUpdateEffect } from 'react-use';
import CustomIcon from '@/components/CustomIcon';
import { ResourceCategoryAdditionalItem, ResourceLanguage, ResourceSearchOrder, TagAdditionalItem } from '@/sdk/constants';
import { GetAllFavorites, GetAllMediaLibraries, GetAllResourceCategories, GetAllTags, PatchResourceOptions } from '@/sdk/apis';
import { useTraceUpdate } from '@/components/utils';
import type { Tag as TagDto } from '@/core/models/Tag';
import store from '@/store';
import BApi from '@/sdk/BApi';

const QuicksearchSlots = (props) => {
  const { currentSearchForm, onSelect = (form) => {} } = props;

  const storeResourceOptions = store.useModelState('resourceOptions');
  const [categories, setCategories] = useState([]);
  const [mediaLibraries, setMediaLibraries] = useState([]);
  const [tags, setTags] = useState<TagDto[]>([]);
  const [favorites, setFavorites] = useState([]);

  useTraceUpdate(props, 'QuicksearchSlots');

  const [resourceOptions, setResourceOptions] = useState(storeResourceOptions);

  useUpdateEffect(() => {
    setResourceOptions(JSON.parse(JSON.stringify(storeResourceOptions)));
  }, [storeResourceOptions]);


  useEffect(() => {
    console.log('currentSearchForm causes QuicksearchSlots rendering');
  }, [currentSearchForm]);

  useEffect(() => {
    console.log('onSelect causes QuicksearchSlots rendering');
  }, [onSelect]);

  useEffect(() => {
    console.log('Some states cause rerendering');
  }, [storeResourceOptions, categories, mediaLibraries, tags, favorites]);

  useEffect(() => {
    BApi.resourceCategory.getAllResourceCategories({ additionalItems: ResourceCategoryAdditionalItem.Validation }).then(a => {
      setCategories(a.data);
    });
    GetAllMediaLibraries().invoke((t) => {
      setMediaLibraries(t.data);
    });
    GetAllTags(
      { additionalItems: TagAdditionalItem.PreferredAlias },
    ).invoke((a) => {
      setTags(a.data.sort((x, y) => x.order - y.order).map((d) => new Tag(d)));
    });

    GetAllFavorites().invoke((t) => {
      setFavorites(t.data);
    });
  }, []);

  const patchResourceOptions = (patches = {}) => {
    PatchResourceOptions({ model: patches }).invoke();
  };

  const renderSearchSlotsItem = (item) => {
    let { name } = item || {};
    const model = item?.model || currentSearchForm;
    // console.log(model);
    const viewData = Object.keys(model).filter((a) => {
      const v = model[a];
      return v && (!Array.isArray(v) || v.length > 0);
    }).reduce((s, k) => {
      const value = model[k];
      switch (k) {
        case 'releaseStartDt':
        case 'releaseEndDt':
        case 'addStartDt':
        case 'addEndDt':
        case 'fileCreateStartDt':
        case 'fileCreateEndDt':
        case 'fileModifyStartDt':
        case 'fileModifyEndDt': {
          const isStartDt = k.endsWith('StartDt');
          const useless = isStartDt ? 'StartDt' : 'EndDt';
          const key = k.replace(useless, 'Dt');
          if (!(key in s)) {
            s[key] = [undefined, undefined];
          }
          const arr = s[key];
          const idx = isStartDt ? 0 : 1;
          arr[idx] = dayjs(value).format('YYYY-MM-DD');
          break;
        }
        case 'minRate':
          s[k] = (
            <Rating
              allowHalf
              defaultValue={value}
              disabled
            />
          );
          break;
        case 'languages':
          s[k] = (
            <div className={'languages'}>
              {value.map((a) => <IceLabel key={a} status={'info'} inverse={false}>{i18n.t(ResourceLanguage[a])}</IceLabel>)}
            </div>
          );
          break;
        case 'mediaLibraryIds':
          s[k] = categories.map((a) => {
            const cm = mediaLibraries.filter((b) => b.categoryId == a.id);
            return ({
              category: a.name,
              mediaLibraries: cm.filter((c) => value.indexOf(c.id) > -1).map((c) => <IceLabel key={c.id} status={'info'} inverse={false}>{c.name}</IceLabel>),
            });
          }).filter((a) => a.mediaLibraries.length > 0);
          break;
        case 'tagIds':
          s.tags = tags.filter((t) => value.indexOf(t.id) > -1).map((c) => <IceLabel key={c.id} status={'info'} inverse={false}>{c.displayName}</IceLabel>);
          break;
        case 'excludedTagIds':
          s.excludedTags = tags.filter((t) => value.indexOf(t.id) > -1).map((c) => <IceLabel key={c.id} status={'info'} inverse={false}>{c.displayName}</IceLabel>);
          break;
        case 'orders':
          s[k] = value.map((t) => <IceLabel key={`${t.order}${t.asc}`} status={'info'} inverse={false}>{i18n.t(ResourceSearchOrder[t.order])}{t.asc ? '↑' : '↓'}</IceLabel>);
          break;
        case 'customPropertyKeys':
          s[k] = value?.join(', ');
          break;
        case 'customProperties':
          if (value) {
            Object.keys(value).forEach((t) => {
              s[t] = value[t];
            });
          }
          break;
        case 'favoritesIds':
          if (value) {
            s[k] = favorites.filter((f) => value.indexOf(f.id) > -1).map((f) => f.name).join(', ');
          }
          break;
        default:
          s[k] = i18n.t(value);
          break;
      }
      return s;
    }, {});

    return (
      <div className={'resource-page-search-slot-item'}>
        <div className="opt">
          <Button
            type={'primary'}
            size={'small'}
            onClick={() => {
              const n = name ?? i18n.t('unnamed');
              if (item == null) {
                if (!resourceOptions.searchSlots) {
                  resourceOptions.searchSlots = [];
                }
                resourceOptions.searchSlots.push({
                  name: n,
                  model: currentSearchForm,
                });
              } else {
                item.name = n;
              }
              patchResourceOptions({
                searchSlots: resourceOptions.searchSlots,
              });
            }}
            title={item ? undefined : i18n.t('Add current search form to slot')}
          >
            {i18n.t(item ? 'Update name' : 'Add')}
          </Button>
          <Input defaultValue={name} placeholder={i18n.t('name')} size={'small'} onChange={(v) => { name = v; }} />
          {item && (
            <Button warning size={'small'} type={'primary'}>
              <CustomIcon
                type={'delete'}
                onClick={() => {
                  patchResourceOptions({
                    searchSlots: resourceOptions.searchSlots.filter((x) => x != item),
                  });
                }}
              />
            </Button>
          )}
        </div>
        <Divider />
        <div className={'model'}>
          {Object.keys(viewData).map((a) => {
            let value = viewData[a];
            if (a.endsWith('Dt') && Array.isArray(value)) {
              value = `${value[0]} ~ ${value[1]}`;
            } else if (a == 'mediaLibraryIds') {
              value = (
                <div className={'media-libraries'}>
                  {value.map((v) => (
                    <div className={'category'} key={v.category}>
                      <div className={'name'}>{v.category}</div>
                      <div>{v.mediaLibraries}</div>
                    </div>
                  ))}
                </div>
              );
            }
            // console.log(a);
            return (
              <div className={'item'} key={a}>
                <div className="label">
                  {a.startsWith('p:') ? a : i18n.t(a)}：
                </div>
                <div className="value">
                  {value}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    );
  };

  console.log('QuicksearchSlots rendering');

  return (
    <div className="quick-search-slot">
      <div>{i18n.t('Quick search slot')}</div>
      <div className={'items'}>
        <Tag.Group>
          {(resourceOptions.searchSlots || []).concat([undefined]).map((item, i) => {
            return (
              <Balloon
                key={i}
                closable={false}
                trigger={
                  (
                    <Tag
                      type="normal"
                      className={'quick-search-slot-item'}
                      size={'small'}
                      onClick={() => {
                        if (item) {
                          const newForm = JSON.parse(JSON.stringify(item.model));
                          if (onSelect) {
                            onSelect(newForm);
                          }
                        }
                      }}
                    >
                      {item ? item.name : <CustomIcon type={'plus-circle'} size={'small'} />}
                    </Tag>
                  )
                }
                triggerType={'hover'}
                align={'b'}
              >
                {renderSearchSlotsItem(item)}
              </Balloon>
            );
          })}
        </Tag.Group>
      </div>
    </div>
  );
};

export default React.memo(QuicksearchSlots);
