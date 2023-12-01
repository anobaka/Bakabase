import React, { useCallback, useEffect, useState } from 'react';
import { Balloon, Button, Checkbox, Dialog, Icon, Input, Select, Tag } from '@alifd/next';
import IceLabel from '@icedesign/label';
import { usePrevious, useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import { set } from 'immer/dist/utils/common';
import { AddTagGroups, AddTags } from '@/sdk/apis';
import { TagGroupAdditionalItem } from '@/sdk/constants';
import { TagGroup as TagGroupDto } from '@/core/models/TagGroup';
import { Tag as TagDto } from '@/core/models/Tag';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import BApi from '@/sdk/BApi';

interface IValue {
  tagIds: number[];
  tagIdExternalIdsMap?: Record<number, any[]>;
}

interface IProps {
  onChange?: (value: IValue, tagMap: { [tagId: number]: TagDto }) => any;
  enableCreation?: boolean;
  defaultValue?: IValue;
  value?: IValue;
  externalIds?: any[];
  multiple?: boolean;
}

const TagSelector = (props: IProps) => {
  const {
    enableCreation = false,
    defaultValue,
    value: propsValue,
    onChange = (value, tags) => {
    },
    externalIds,
    multiple = true,
    ...otherProps
  } = props;
  const { t } = useTranslation();
  const [tagGroups, setTagGroups] = useState<TagGroupDto[]>([]);
  const [keyword, setKeyword] = useState<string>('');
  const [internalValue, setInternalValue] = useState<IValue>(propsValue ?? defaultValue ?? { tagIds: [], tagIdExternalIdsMap: {} });

  const finalValue = propsValue ?? internalValue;

  useUpdateEffect(() => {
    if (onChange) {
      const tags = tagGroups.reduce<{ [tagId: number]: TagDto }>((s, t) => {
        for (const tag of t.tags.filter((a) => internalValue.tagIds.includes(a.id))) {
          s[tag.id] = tag;
        }
        return s;
      }, {});
      onChange(internalValue, tags);
    }
  }, [internalValue]);

  useEffect(() => {
    if (propsValue) {
      setInternalValue(propsValue);
    }
  }, [propsValue]);

  const loadAllGroups = useCallback(() => {
    const additionalItems = TagGroupAdditionalItem.PreferredAlias | TagGroupAdditionalItem.Tags | TagGroupAdditionalItem.TagNamePreferredAlias;
    BApi.tagGroup.getAllTagGroups({ additionalItems })
      .then((a) => {
        const data = a.data || [];
        const groups: TagGroupDto[] = [];
        for (let j = 0; j < data.length; j++) {
          // @ts-ignore
          const group = new TagGroupDto(data[j]);
          groups[j] = group;
          if (!group.tags) {
            group.tags = [];
          }
          for (let i = 0; i < group.tags.length; i++) {
            group.tags[i] = new TagDto(group.tags[i]);
          }
        }
        setTagGroups(groups.sort((c, b) => (c.order - b.order) * 100000 + (c.id - b.id)));
      });
  }, []);

  useEffect(() => {
    loadAllGroups();
  }, []);

  let filteredTagGroups = tagGroups.slice();

  if (keyword?.length > 0) {
    const lowerCaseKeyword = keyword!.toLowerCase();
    filteredTagGroups = tagGroups.filter((g) => {
      const ga = g.displayName?.toLowerCase()
        .indexOf(lowerCaseKeyword) > -1;
      const fts = g.tags?.filter((t) => t.displayName?.toLowerCase()
        .indexOf(lowerCaseKeyword) > -1);
      if (ga) {
        return true;
      } else if (fts?.length > 0) {
        g.tags = fts;
        return true;
      }
      return false;
    });
  }

  // console.log(filteredTagGroups);

  const renderCreationDialog = (placeholder, Api, buildModel) => {
    let v;
    const dialog = Dialog.show({
      footer: false,
      closeMode: ['mask', 'esc'],
      content: (
        <Input.Group
          addonAfter={(
            <Button
              size={'large'}
              onClick={() => {
                const ts = v.map((a) => a.trim())
                  .filter((a) => a);
                if (ts.length > 0) {
                  Api({
                    model: buildModel(ts),
                  })
                    .invoke((a) => {
                      if (!a.code) {
                        loadAllGroups();
                        dialog.hide();
                      }
                    });
                }
              }}
            >{t('Create')}
            </Button>
          )}
        >
          <Select
            mode={'tag'}
            style={{ width: 600 }}
            size={'large'}
            placeholder={t(placeholder)}
            onChange={(v) => {
              v = v;
            }}
            visible={false}
          />
        </Input.Group>
      ),
    });
    return dialog;
  };

  return (
    <div className={'tag-selector'} {...otherProps}>
      <div className="opt">
        <div className="left">
          <Input
            size={'small'}
            innerAfter={
              <Icon
                type="search"
                size="xs"
                style={{ margin: 4 }}
              />
            }
            value={keyword}
            onChange={(v) => setKeyword(v)}
          />
          <IceLabel inverse={false} status={'info'}>{t('Alias applied')}</IceLabel>
          {t('{{count}} selected', { count: finalValue.tagIds.length })}
        </div>
        <div className="right">
          {enableCreation && (
            <Button
              type={'normal'}
              onClick={() => {
                renderCreationDialog('Tag group name list, and press enter to add a new group.', AddTagGroups, (arr) => ({ names: arr }));
              }}
              size={'small'}
            >
              {t('Create tag group')}
            </Button>
          )}
        </div>
      </div>
      <div className="tag-groups">
        {filteredTagGroups.map((g) => {
          const selectedTagsCount = finalValue.tagIds.filter((id) => g.tags?.some((gt) => gt.id == id)).length ?? 0;
          return (
            <React.Fragment key={g.id}>
              <div className={'group-name'}>
                {g.displayName?.length > 0 ? g.displayName : t('Default')}
                {selectedTagsCount > 0 && (
                  <div className={'selected-tag-count'}>
                    ({t('{{count}} selected', { count: selectedTagsCount })})
                  </div>
                )}
                {enableCreation && (
                  <CustomIcon
                    type={'plus-circle'}
                    size={'small'}
                    onClick={() => {
                      renderCreationDialog('Tag name list, and press enter to add a new tag.', AddTags, (arr) => ({
                        [g.id]: arr,
                      }));
                    }}
                  />
                )}
              </div>
              {g.tags?.length > 0 && (
                <div className={'tag-group'}>
                  {g.tags.map((tag) => {
                      let tagComp;
                      const checked = finalValue.tagIds.includes(tag.id);
                      if (externalIds) {
                        const selectedEIds = finalValue.tagIdExternalIdsMap?.[tag.id] || [];
                        const intersection = selectedEIds.filter(eId => externalIds.includes(eId));
                        const indeterminate = intersection.length > 0 && intersection.length < selectedEIds.length;
                        tagComp = (
                          <Checkbox
                            key={tag.id}
                            checked={checked}
                            indeterminate={indeterminate}
                            onChange={() => {
                              if (indeterminate || !checked) {
                                setInternalValue({
                                  tagIds: finalValue.tagIds.filter(t => t != tag.id).concat(tag.id),
                                  tagIdExternalIdsMap: {
                                    ...finalValue.tagIdExternalIdsMap,
                                    [tag.id]: externalIds,
                                  },
                                });
                              } else {
                                let newMap: Record<number, any[]> | undefined;
                                if (finalValue.tagIdExternalIdsMap) {
                                  newMap = { ...finalValue.tagIdExternalIdsMap };
                                  delete newMap[tag.id];
                                }
                                setInternalValue({
                                  tagIdExternalIdsMap: newMap,
                                  tagIds: finalValue.tagIds.filter(t => t != tag.id),
                                });
                              }
                            }}
                            label={tag.displayName}
                          />
                        );
                      } else {
                        tagComp = (
                          <Tag.Selectable
                            // type={'primary'}
                            key={tag.id}
                            checked={checked}
                            onChange={(c) => {
                              const newIds = multiple ? checked
                                ? finalValue.tagIds.filter((a) => a != tag.id) : finalValue.tagIds.concat(tag.id) : [tag.id];
                              setInternalValue({
                                ...finalValue,
                                tagIds: newIds,
                              });
                            }}
                            size={'small'}
                          >
                            <span style={{ color: tag.color }}>{tag.displayName}</span>
                          </Tag.Selectable>
                        );
                      }
                      if (tag.displayName != tag.name
                      ) {
                        tagComp = (
                          <Balloon.Tooltip trigger={tagComp} align={'t'} key={tag.id}>
                            {t('Raw name')}: {tag.name}
                          </Balloon.Tooltip>
                        );
                      }
                      return tagComp;
                    },
                  )}
                </div>
              )}
            </React.Fragment>
          );
        })}
      </div>
    </div>
  );
};

export default TagSelector;
