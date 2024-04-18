import React, { useCallback, useEffect, useState } from 'react';
import { Balloon, Button, Dialog, Icon, Input, Select, Tag } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { Accordion, AccordionItem, Checkbox } from '@/components/bakaui';
import { AddTagGroups, AddTags } from '@/sdk/apis';
import { TagGroupAdditionalItem } from '@/sdk/constants';
import { TagGroup as TagGroupDto } from '@/core/models/TagGroup';
import { Tag as TagDto } from '@/core/models/Tag';
import CustomIcon from '@/components/CustomIcon';
import BApi from '@/sdk/BApi';
import AliasAppliedChip from '@/components/Chips/AliasAppliedChip';


export enum TagSelection {
  Selected = 1,
  NotSelected,
  Indeterminate,
}

export type TagSelectorValue = Record<number, TagSelection>;

interface IProps {
  onChange?: (value: TagSelectorValue, tagMap: { [tagId: number]: TagDto }) => any;
  enableCreation?: boolean;
  defaultValue?: TagSelectorValue;
  value?: TagSelectorValue;
}

const TagSelector = (props: IProps) => {
  const {
    enableCreation = false,
    defaultValue,
    value: propsValue,
    onChange = (value, tags) => {
    },
    ...otherProps
  } = props;
  const { t } = useTranslation();
  const [tagGroups, setTagGroups] = useState<TagGroupDto[]>([]);
  const [keyword, setKeyword] = useState<string>('');
  const [value, setValue] = useState<TagSelectorValue>(propsValue ?? defaultValue ?? { });

  const changeValue = (value: TagSelectorValue) => {
    setValue(value);
    if (onChange) {
      const tags = tagGroups.reduce<{ [tagId: number]: TagDto }>((s, t) => {
        for (const tag of t.tags.filter((a) => value[a.id] != TagSelection.NotSelected)) {
          s[tag.id] = tag;
        }
        return s;
      }, {});
      onChange(value, tags);
    }
  };

  useEffect(() => {
    if (propsValue) {
      setValue(propsValue);
    }
  }, [propsValue]);

  const loadAllGroups = useCallback(() => {
    const additionalItems = TagGroupAdditionalItem.PreferredAlias | TagGroupAdditionalItem.Tags | TagGroupAdditionalItem.TagNamePreferredAlias;
    // @ts-ignore
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
    <div className={'min-w-[600px] min-h-[100px]'} {...otherProps}>
      <div className="mb-2 flex items-center justify-between">
        <div className="flex items-center gap-2">
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
          <AliasAppliedChip />
          {t('{{count}} selected', { count: Object.keys(value).length })}
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
      {filteredTagGroups.length > 0 && (
        <Accordion
          className="pt-5 pb-5"
          variant="splitted"
          defaultExpandedKeys={filteredTagGroups.map(g => g.id.toString())}
          selectionMode="multiple"
        >
          {filteredTagGroups.map((g) => {
            const selectedTagsCount = g.tags.filter((t) => value[t.id] != TagSelection.NotSelected).length;
            return (
              <AccordionItem
                key={g.id}
                subtitle={(
                  <div>
                    {enableCreation && (
                      <CustomIcon
                        type={'plus-circle'}
                        className={'text-small'}
                        onClick={() => {
                          renderCreationDialog('Tag name list, and press enter to add a new tag.', AddTags, (arr) => ({
                            [g.id]: arr,
                          }));
                        }}
                      />
                    )}
                    <div style={{ color: 'var(--bakaui-primary)' }} className={'text-small'}>
                      {t('{{count}} selected', { count: selectedTagsCount })}
                    </div>
                  </div>
                )}
                title={(
                  g.displayName?.length > 0 ? g.displayName : t('Default')
                )}
              >
                {g.tags?.length > 0 && (
                  <div className={'flex flex-wrap gap-1 col-span-7'}>
                    {g.tags.map((tag) => {
                        let tagComp;
                        const selection = value[tag.id];
                        tagComp = (
                          <Checkbox
                            key={tag.id}
                            isSelected={selection == TagSelection.Selected}
                            isIndeterminate={selection == TagSelection.Indeterminate}
                            onValueChange={(checked) => {
                              const newStatus = selection == TagSelection.Selected ? TagSelection.NotSelected : TagSelection.Selected;
                              changeValue({ ...value, [tag.id]: newStatus });
                            }}
                          >
                            {tag.displayName}
                          </Checkbox>
                        );
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

              </AccordionItem>
            );
          })}
        </Accordion>
      )}
    </div>
  );
};

export default TagSelector;
