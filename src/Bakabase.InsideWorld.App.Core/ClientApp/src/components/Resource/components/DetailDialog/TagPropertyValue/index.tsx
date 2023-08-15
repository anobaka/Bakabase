import React, { useEffect, useState } from 'react';
import './index.scss';
import i18n from 'i18next';
import { Balloon, Button, Dialog, Select } from '@alifd/next';
import { useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import { AddTagGroups, AddTags, GetAllTagGroups, PatchResource, SearchTags, UpdateResourceTags } from '@/sdk/apis';
import { TagGroupAdditionalItem } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import TagSelector from '@/components/TagSelector';
import Property from '@/components/Resource/components/DetailDialog/PropertyValue';
import EditableTree from '@/components/EditableTree';

export default ({
  reloadResource,
  resource,
  onSearch,
}: { resource: any; reloadResource: any; onSearch: (tagId: number, append: boolean) => any }) => {
  const { t } = useTranslation();

  const tagGroups: any[] = [];
  for (const tag of (resource.tags || [])) {
    let tagGroup = tagGroups.find((a) => a.name == tag.groupName || (tag.groupName == null && a.name == ''));
    if (!tagGroup) {
      tagGroup = {
        name: tag.groupName == null ? '' : tag.groupName,
        preferredAlias: tag.groupNamePreferredAlias,
        tags: [],
        id: tag.groupId ?? 0,
      };
      tagGroups.push(tagGroup);
    }
    tagGroup.tags.push(tag);
  }

  const renderAlias = (object) => {
    if (object.preferredAlias?.length > 0) {
      if (object.preferredAlias != object.name) {
        return (
          <Balloon.Tooltip
            align={'t'}
            triggerType={'hover'}
            trigger={object.preferredAlias}
          >
            {i18n.t('Raw name')}: {object.name}
          </Balloon.Tooltip>
        );
        return `${object.preferredAlias}(${object.name})`;
      }
    }
    if (object.name?.length > 0) {
      return object.name;
    } else {
      // Tags can not have a empty name.
      return i18n.t('Default');
    }
  };

  const tagIds = (resource.tags || []).map((t) => t.id);

  return (
    <div className={'property-value tags'}>
      {tagGroups.length > 0 && (
        <div className="value">
          <div className="tag-groups">
            {tagGroups.map((g) => {
              return (
                <>
                  <div className="name">
                    {renderAlias(g)}:
                  </div>
                  <div className="tags">
                    <div className="current-tags">
                      {g.tags.map((tag) => {
                        return (
                          <Balloon
                            trigger={(
                              <div className={'tag'}>{renderAlias(tag)}</div>
                            )}
                            triggerType={'click'}
                            align={'t'}
                            closable={false}
                            v2
                            autoFocus={false}
                          >
                            <Button
                              type={'normal'}
                              size={'small'}
                              onClick={() => {
                                if (onSearch) {
                                  onSearch(tag.id, false);
                                }
                              }}
                            >
                              {t('Replace tags in search')}
                            </Button>
                            &nbsp;
                            <Button
                              type={'normal'}
                              size={'small'}
                              onClick={() => {
                                if (onSearch) {
                                  onSearch(tag.id, true);
                                }
                              }}
                            >
                              {t('Append to tags in search')}
                            </Button>
                          </Balloon>
                        );
                      })}
                    </div>
                  </div>
                </>
              );
            })}
          </div>
        </div>
      )}
      <CustomIcon
        type={'edit-square'}
        size={'small'}
        onClick={() => {
          let newTagIds;
          Dialog.show({
            title: t('Edit tags'),
            content: (
              <TagSelector
                defaultValue={{ tagIds }}
                onChange={(v) => {
                  newTagIds = v.tagIds;
                }}
              />
            ),
            closeable: true,
            onOk: () => new Promise(((resolve, reject) => {
              UpdateResourceTags({
                model: {
                  resourceTagIds: {
                    [resource.id]: newTagIds || [],
                  },
                },
              })
                .invoke((a) => {
                  if (!a.code) {
                    resolve(undefined);
                    reloadResource();
                  } else {
                    reject();
                  }
                })
                .catch(() => {
                  reject();
                });
            })),
          });
        }}
      />
    </div>
  );
};
