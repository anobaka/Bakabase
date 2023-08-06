import React, { useCallback, useEffect, useImperativeHandle, useReducer, useRef, useState } from 'react';
import type { CollisionDetection, UniqueIdentifier } from '@dnd-kit/core';
import {
  closestCenter,
  closestCorners,
  DndContext, DragOverlay,
  getFirstCollision, MeasuringStrategy, MouseSensor,
  PointerSensor,
  pointerWithin,
  rectIntersection,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import { arrayMove, SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { useUpdateEffect } from 'react-use';
import i18n from 'i18next';
import SortableTagGroup from '@/pages/Tag/components/SortableTagGroup';
import SortableTag from '@/pages/Tag/components/SortableTag';
import { Tag as TagDto } from '@/core/models/Tag';
import { GetAllTagGroups, GetAllTags, MoveTag, SortTagGroups } from '@/sdk/apis.js';
import { TagAdditionalItem, TagGroupAdditionalItem } from '@/sdk/constants';
import { uuidv4 } from '@/components/utils';
import { TagGroup as TagGroupDto } from '@/core/models/TagGroup';
import { IEntryRef } from '@/core/models/FileExplorer/Entry';

export default React.forwardRef((({
                   isBulkDeleting,
                   onSelectTag = (tag, selected) => {
                   },
                   selectedTagIds = [],
                 }, ref) => {
  const [, forceUpdate] = useReducer((x) => x + 1, 0);
  const sensors = useSensors(
    useSensor(MouseSensor, {
      activationConstraint: {
        distance: 5,
      },
    }),
  );

  const [groups, setGroups] = useState<TagGroupDto[]>([]);
  const [tags, setTags] = useState<TagDto[]>([]);

  const [activeSortableId, setActiveSortableId] = useState<UniqueIdentifier>();
  const activeSortableIdRef = useRef<UniqueIdentifier>();

  const [isDragging, setIsDragging] = useState(false);

  const prevCollisionIdRef = useRef<UniqueIdentifier>();

  const groupSortableIdMapRef = useRef({});
  const [groupSortableIds, setGroupSortableIds] = useState<UniqueIdentifier[]>([]);
  const tagSortableIdMapRef = useRef({});
  const [fakeTagOnDragging, setFakeTagOnDragging] = useState<TagDto>();

  useImperativeHandle(ref, () => ({
    loadAllGroups,
    loadAllTags,
  }), []);

  useEffect(() => {
    loadAllGroups();
    loadAllTags();
  }, []);

  const loadAllTags = () => {
    GetAllTags({ additionalItems: TagAdditionalItem.PreferredAlias })
      .invoke((b) => {
        const ts = b.data.sort((x, y) => {
          return x.order - y.order;
        });
        ts.forEach((a) => {
          a.version = uuidv4();
        });
        setTags(ts.map((t) => new TagDto(t)));
      });
  };

  const loadAllGroups = (cb: any = undefined) => {
    GetAllTagGroups(
      { additionalItems: TagGroupAdditionalItem.PreferredAlias },
    )
      .invoke((a) => {
        if (!a.data.find(b => b.id == 0)) {
          a.data.push(
            {
              id: 0,
              name: i18n.t('Default'),
              order: -1,
            },
          );
        }
        const groups = a.data
          .sort((x, y) => x.order - y.order);
        setGroups(groups.map((g) => new TagGroupDto(g)));
        cb && cb(a);
      });
  };

  useUpdateEffect(() => {
    activeSortableIdRef.current = activeSortableId;
  }, [activeSortableId]);

  useUpdateEffect(() => {
    groupSortableIdMapRef.current = groups.reduce((t, g) => {
      t[`g${g.id}`] = true;
      return t;
    }, {});
    setGroupSortableIds(Object.keys(groupSortableIdMapRef.current));
  }, [groups]);

  useUpdateEffect(() => {
    tagSortableIdMapRef.current = tags.reduce((s, t) => {
      s[`t${t.id}`] = true;
      return s;
    }, {});
  }, [tags]);

  useUpdateEffect(() => {
    // console.log('fake tag changes', fakeTagOnDragging)
  }, [fakeTagOnDragging]);


  /**
   * Custom collision detection strategy optimized for multiple containers
   *
   * - First, find any droppable containers intersecting with the pointer.
   * - If there are none, find intersecting containers with the active draggable.
   * - If there are no intersecting containers, return the last matched intersection
   *
   */
  const collisionDetectionStrategy: CollisionDetection = useCallback(
    (args) => {
      console.log('collision detection', activeSortableIdRef.current);

      if (activeSortableIdRef.current?.startsWith('g') || activeSortableIdRef.current?.startsWith('t')) {
        const activeId = parseInt(activeSortableIdRef.current?.substring(1));
        if (activeSortableIdRef.current?.startsWith('g')) {
          // group intersection
          if (activeSortableIdRef.current in groupSortableIdMapRef.current) {
            const newArgs = {
              ...args,
              droppableContainers: args.droppableContainers.filter(
                (container) => container.id in groupSortableIdMapRef.current,
              ),
            };

            const pointerIntersections = pointerWithin(newArgs);
            // Start by finding any intersecting droppable
            const intersections =
              pointerIntersections.length > 0
                ? // If there are droppables intersecting with the pointer, return those
                pointerIntersections
                : rectIntersection(newArgs);

            return intersections.filter(a => a.id != 'g0');
          }
        } else {
          if (activeSortableIdRef.current in tagSortableIdMapRef.current) {
            const pointerIntersections = pointerWithin(args);
            // Start by finding any intersecting droppable
            const intersections =
              pointerIntersections.length > 0
                ? // If there are droppables intersecting with the pointer, return those
                pointerIntersections
                : rectIntersection(args);

            // console.log(intersections);

            const overTag = intersections.find(a => a.id.toString()
              .startsWith('t'));
            if (overTag) {
              return [overTag];
            } else {
              const overGroup = intersections.find(a => a.id.toString()
                .startsWith('g'));
              if (overGroup) {
                return [overGroup];
              }
            }
          }
        }
      }

      console.log('No collision');
      return [];
    },
    [],
  );

  let activeTag;
  if (activeSortableId?.startsWith('t')) {
    const tagId = parseInt(activeSortableId?.substring(1));
    activeTag = tags.find(a => a.id == tagId);
  }

  function handleDragOver(event) {
    const {
      active,
      over,
      draggingRect,
    } = event;

    // Find the containers
    const activeContainer = active?.id;
    const overContainer = over?.id;

    if (
      !activeContainer ||
      !overContainer ||
      activeContainer === overContainer
    ) {
      return;
    }

    console.log('active', activeContainer, 'over', overContainer);

    if (activeContainer.startsWith('t')) {
      const tagId = parseInt(activeContainer.substring(1));
      const tag = tags.find(a => a.id == tagId);
      const clone = JSON.parse(JSON.stringify(tag));
      let overTagId;
      let overGroupId;

      if (overContainer.startsWith('t')) {
        overTagId = parseInt(overContainer.substring(1));
        overGroupId = tags.find(a => a.id == overTagId).groupId;
      } else {
        if (overContainer.startsWith('g')) {
          overGroupId = parseInt(overContainer.substring(1));
        }
      }
      prevCollisionIdRef.current = overContainer;
      setFakeTagOnDragging(new TagDto({
        ...clone,
        groupId: overGroupId,
        nextTagId: overTagId,
      }));
    }
  }

  function handleDragEnd(e) {
    const activeSortableId = e.active?.id;
    const overSortableId = e.over?.id;
    // console.log(activeSortableId, overSortableId)
    if (activeSortableId && overSortableId) {
      if (activeSortableId.startsWith('g')) {
        if (overSortableId.startsWith('g')) {
          if (overSortableId != 'g0') {
            const activeId = parseInt(activeSortableId.substring(1));
            const overId = parseInt(overSortableId.substring(1));
            const oldIndex = groups.findIndex(g => g.id == activeId);
            const newIndex = groups.findIndex(g => g.id == overId);
            let newGroups = arrayMove(groups, oldIndex, newIndex);

            const defaultGroupIdx = newGroups.findIndex(g => g.id == 0);
            if (defaultGroupIdx > 0) {
              newGroups = arrayMove(newGroups, defaultGroupIdx, 0);
            }

            // for better experience
            for (let i = 1; i < newGroups.length; i++) {
              newGroups[i].order = i;
            }
            setGroups(newGroups);

            SortTagGroups({
              model: {
                ids: newGroups.map((t) => t.id),
              },
            })
              .invoke((a) => {
                if (!a.code) {

                }
              });
          }
        }
      } else {
        if (activeSortableId.startsWith('t')) {
          const activeId = parseInt(activeSortableId.substring(1));
          const model = {
            targetTagId: undefined,
            targetGroupId: fakeTagOnDragging?.groupId,
          };

          if (overSortableId.startsWith('t')) {
            if (overSortableId != activeSortableId) {
              model.targetTagId = parseInt(overSortableId.substring(1));
            } else {
              if (prevCollisionIdRef.current?.startsWith('t')) {
                model.targetTagId = parseInt(prevCollisionIdRef.current.substring(1));
              }
            }
          }

          // for better experience
          const tag = tags.find(t => t.id == activeId);
          const groupTags = tags.filter(t => t.groupId == model.targetGroupId && t.id != tag.id)
            .sort((a, b) => a.order - b.order);
          tag.groupId = model.targetGroupId;
          if (model.targetTagId != undefined) {
            const targetTagIdx = groupTags.findIndex(a => a.id == model.targetTagId);
            const targetTag = groupTags[targetTagIdx];
            console.log(`insert ${tag.name} to target:${targetTag.name} ${targetTagIdx}`);
            groupTags.splice(targetTagIdx, 0, tag);
            for (let i = targetTagIdx; i < groupTags.length; i++) {
              groupTags[i].order = targetTag.order + i - targetTagIdx;
              console.log(`change tag: ${groupTags[i].name} to ${groupTags[i].order}`);
            }
          } else {
            tag.order = groupTags.length > 0 ? (Math.max(...groupTags.map(t => t.order))) + 1 : 0;
            console.log(`change tag: ${tag.name} to ${tag.order}`);
          }

          // real change
          MoveTag({
            id: activeId,
            model,
          })
            .invoke(a => {
              if (a.data) {
                for (const changes of a.data) {
                  const tag = tags.find(a => a.id == changes.tagId);
                  if (changes.order != undefined) {
                    tag.order = changes.order;
                  }
                  if (changes.groupId != undefined) {
                    tag.groupId = changes.groupId;
                  }
                }
                forceUpdate();
              }
            });
        }
      }
    }
  }

  useUpdateEffect(() => {
    console.log(`drag started at ${activeSortableId}`);
  }, [activeSortableId]);

  return (
    <div className={'groups'}>
      {/* https://github.com/clauderic/dnd-kit/issues/718 */}
      <DndContext
        onDragStart={({ active }) => {
          if (active.id != 'g0') {
            setActiveSortableId(active.id);
          }
          setIsDragging(true);
        }}
        sensors={sensors}
        collisionDetection={collisionDetectionStrategy}
        onDragMove={e => {
          // console.log('drag move', e)
        }}
        onDragOver={e => {
          // console.log('drag over', e)
          handleDragOver(e);
        }}
        onDragEnd={(e) => {
          // console.log('drag end', e);
          handleDragEnd(e);
          setFakeTagOnDragging(undefined);
          setActiveSortableId(undefined);
          setIsDragging(false);
        }}
        onDragCancel={e => {
          setIsDragging(false);
        }}
      >
        <SortableContext
          items={groups.map(g => `g${g.id}`)!}
          strategy={verticalListSortingStrategy}
        >
          {groups.map((g, i) => {
            const groupTags = tags.filter((t) => t.groupId == g.id || (g.id == 0 && !(t.groupId > 0)))
              .sort((a, b) => a.order - b.order);

            if (fakeTagOnDragging) {
              const idx = groupTags.findIndex(a => a.id == fakeTagOnDragging.id);
              if (fakeTagOnDragging.groupId == g.id) {
                if (idx == -1) {
                  if (fakeTagOnDragging.nextTagId) {
                    const nextTagIndex = groupTags.findIndex(a => a.id == fakeTagOnDragging.nextTagId);
                    groupTags.splice(nextTagIndex, 0, fakeTagOnDragging);
                  } else {
                    groupTags.push(fakeTagOnDragging);
                  }
                }
              } else {
                if (idx > -1) {
                  groupTags.splice(idx, 1);
                }
              }
            }

            return (
              <SortableTagGroup
                selectedTagIds={selectedTagIds}
                onSelectTag={onSelectTag}
                isBulkDeleting={isBulkDeleting}
                loadAllTags={loadAllTags}
                fakeTag={fakeTagOnDragging}
                group={g}
                isDragging={isDragging}
                key={g.id}
                tags={groupTags}
                onRemove={(group) => {
                  groups.splice(groups.indexOf(group), 1);
                  forceUpdate();
                }}
              />
            );
          })}

          <DragOverlay>
            {activeTag ? (
              <SortableTag
                isBulkDeleting={isBulkDeleting}
                onSelect={(s) => onSelect(activeTag, s)}
                selected={selectedTagIds?.indexOf(activeTag.id) > -1}
                key={`t${activeTag.id}`}
                tag={activeTag}
                disable
                onRemove={(t) => {
                  tags.splice(tags.indexOf(t), 1);
                  forceUpdate();
                }}
              />) : null}
          </DragOverlay>
        </SortableContext>
      </DndContext>
    </div>
  );
}));
