import { Button, Dialog, Input, Message } from '@alifd/next';
import React, { useReducer, useRef } from 'react';
import i18n from 'i18next';
import { horizontalListSortingStrategy, SortableContext, useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useUpdateEffect } from 'react-use';
import CustomIcon from '@/components/CustomIcon';
import SortableTagList from '@/pages/Tag/components/SortableTagList';
import { AddTags, RemoveTagGroup, UpdateTagGroup } from '@/sdk/apis';
import DragHandle from '@/components/DragHandle';
import ButtonsBalloon from '@/components/ButtonsBalloon';
import type { TagGroup as TagGroupDto } from '@/core/models/TagGroup';
import SortableTag from '@/pages/Tag/components/SortableTag';
import SimpleLabel from '@/components/SimpleLabel';

export default (({
                   group,
                   tags,
                   isTarget,
                   onTagSortEnd,
                   onDragEnterGroup,
                   loadAllTags,
                   onRemove = (g) => {
                   },
                   isBulkDeleting,
                   onSelectTag = (tag, selected) => {
                   },
                   selectedTagIds = [],
                   fakeTag,
                   isDragging,
                 }: { group: TagGroupDto }) => {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
  } = useSortable({ id: `g${group.id}` });

  const style = {
    transform: CSS.Translate.toString({
      ...transform,
      scaleY: 1,
    }),
    transition,
  };

  const [, forceUpdate] = useReducer((x) => x + 1, 0);

  const tagContainerIdsRef = useRef([]);
  useUpdateEffect(() => {
    tagContainerIdsRef.current = tags.map(a => `t${a.id}`);
    forceUpdate();
  }, [tags]);

  // console.log('rendering tag group');

  return (
    <div
      className={`tag-page-draggable-group ${isTarget ? 'target' : ''}`}
      ref={setNodeRef}
      style={style}
    >
      <div className="name">
        <DragHandle {...listeners} {...attributes} />
        {group.id > 0 ? (
          <ButtonsBalloon
            trigger={(
              <SimpleLabel
                style={{ cursor: 'pointer' }}
                onClick={() => {
                  let { name } = group;
                  Dialog.show({
                    title: i18n.t('Tag group detail'),
                    content: <Input defaultValue={name} onChange={(v) => name = v} />,
                    closeable: true,
                    onOk: () => new Promise(((resolve, reject) => {
                      if (name?.length > 0) {
                        UpdateTagGroup({
                          id: group.id,
                          model: {
                            name,
                          },
                        })
                          .invoke((t) => {
                            if (!t.code) {
                              group.name = name;
                              forceUpdate();
                              resolve();
                            } else {
                              reject();
                            }
                          });
                      } else {
                        Message.error(i18n.t('Bad name'));
                        reject();
                      }
                    })),
                  });
                }}
              >
                {group.displayName}
              </SimpleLabel>
            )}
            operations={[
              {
                confirmation: true,
                onClick: () => {
                  RemoveTagGroup({
                    id: group.id,
                  })
                    .invoke((t) => {
                      if (!t.code) {
                        onRemove(group);
                      }
                    });
                },
                warning: true,
                label: 'Delete',
              },
            ]}
          >
            {group.preferredAlias && group.preferredAlias != group.name && `${i18n.t('Raw name')}: ${group.name}`}
          </ButtonsBalloon>
        ) : <SimpleLabel>{group.name}</SimpleLabel>}
        <Button
          className={'add'}
          size={'small'}
          type={'normal'}
          onClick={() => {
            let value;
            const dialog = Dialog.show({
              footer: false,
              closeMode: ['mask', 'esc'],
              content: (
                <Input
                  style={{ width: 600 }}
                  size={'large'}
                  placeholder={i18n.t('Tag list, and space is the separator, and press entry to submit.')}
                  onChange={(v) => value = v}
                  onKeyDown={(e) => {
                    if (value && e.key == 'Enter') {
                      const ts = value.split(' ')
                        .map((a) => a.trim())
                        .filter((a) => a);
                      if (ts.length > 0) {
                        AddTags({
                          model: {
                            [group.id]: ts,
                          },
                        })
                          .invoke((a) => {
                            if (!a.code) {
                              dialog.hide();
                              loadAllTags();
                            }
                          });
                      }
                    }
                  }}
                />
              ),
            });
          }}
        >
          <CustomIcon type={'plus-circle'} />
        </Button>
      </div>

      {tags.length > 0 && (
        <div className={'tags'}>
          <SortableContext
            items={tagContainerIdsRef.current}
            strategy={horizontalListSortingStrategy}
          >
            {tags.map((tag, index) => (
              <SortableTag
                isFake={fakeTag?.id == tag.id}
                isBulkDeleting={isBulkDeleting}
                onSelect={(s) => onSelectTag(tag, s)}
                selected={selectedTagIds?.indexOf(tag.id) > -1}
                key={`t${tag.id}`}
                index={index}
                disabled={isDragging}
                tag={tag}
                onRemove={(t) => {
                  tags.splice(tags.indexOf(t), 1);
                  forceUpdate();
                }}
              />
            ))}
          </SortableContext>
        </div>
      )}
    </div>
  );
});
