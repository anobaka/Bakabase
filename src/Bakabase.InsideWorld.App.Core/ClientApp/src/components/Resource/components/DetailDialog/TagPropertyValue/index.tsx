import React, { useEffect, useState } from 'react';
import './index.scss';
import i18n from 'i18next';
import { Balloon, Button, Dialog, Select } from '@alifd/next';
import { useUpdateEffect } from 'react-use';
import { AddTagGroups, AddTags, GetAllTagGroups, PatchResource, SearchTags, UpdateResourceTags } from '@/sdk/apis';
import { TagGroupAdditionalItem } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import TagSelector from '@/components/TagSelector';
import Property from '@/components/Resource/components/DetailDialog/PropertyValue';
import EditableTree from '@/components/EditableTree';

export default ({
  reloadResource,
  resource,
}: { resource: any; reloadResource: any }) => {
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
                      {g.tags.map((t) => {
                        return (
                          <div className={'tag'}>{renderAlias(t)}</div>
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
            title: i18n.t('Edit tags'),
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
                    resolve();
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
