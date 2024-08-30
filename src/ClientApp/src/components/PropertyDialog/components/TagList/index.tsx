import React, { useEffect, useRef, useState } from 'react';
import { closestCenter, DndContext, KeyboardSensor, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { useTranslation } from 'react-i18next';
import { AutoSizer, List } from 'react-virtualized';
import type { Tag } from '../../../Property/models';
import { SortableTag } from './components/SortableTag';
import CustomIcon from '@/components/CustomIcon';
import { uuidv4 } from '@/components/utils';
import { Button, Chip, Modal, Popover, Textarea } from '@/components/bakaui';
import { createPortal } from '@/components/ContextProvider/helpers';

interface IProps {
  tags?: Tag[];
  onChange?: (tags: Tag[]) => void;
  className?: string;
  checkUsage?: (value: string) => Promise<number>;
}

const LineHeight = 35;
const GroupAndNameSeparator = ':';

export default function TagList({
                                  tags: propsTags,
                                  onChange,
                                  className,
                                  checkUsage,
                                }: IProps) {
  const { t } = useTranslation();
  const [tags, setTags] = useState<Tag[]>(propsTags || []);
  const [editInBulkPopupVisible, setEditInBulkPopupVisible] = useState(false);
  const [editInBulkText, setEditInBulkText] = useState('');
  const [bulkEditSummaries, setBulkEditSummaries] = useState<string[]>([]);
  const calculateBulkEditSummaryTimeoutRef = useRef<any>();
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  const virtualListRef = useRef<any>();

  useEffect(() => {
    onChange?.(tags);
  }, [tags]);

  const buildTagsFromBulkText = (text: string): Tag[] => {
    const newTags: Tag[] = editInBulkText.split('\n').map<Tag>(c => {
      const segments = c.split(':');
      let name = '';
      let group: string | undefined;
      if (segments.length == 1) {
        name = segments[0];
      } else {
        group = segments[0];
        name = segments[1];
      }

      const t = tags.find(x => x.name == name && x.group == group);

      if (t) {
        return t;
      }

      return {
        group,
        name,
        value: uuidv4(),
      };
    });
    return newTags;
  };

  const calculateBulkEditSummary = (text: string) => {
    const ctxTags = buildTagsFromBulkText(text);
    const addedTagsCount = ctxTags.filter(x => !tags.includes(x)).length;
    const sameTagsCount = ctxTags.filter(x => tags.includes(x)).length;

    const deletedTagsCount = tags.length - sameTagsCount;
    if (deletedTagsCount > 0 || addedTagsCount > 0) {
      const tips = [deletedTagsCount > 0 ? t('{{count}} data will be deleted', { count: deletedTagsCount }) : '', addedTagsCount > 0 ? t('{{count}} data will be added', { count: addedTagsCount }) : ''];
      setBulkEditSummaries(tips.filter(t => t));
    } else {
      setBulkEditSummaries([]);
    }
  };

  return (
    <div className={className}>
      <div className="flex justify-between items-center">
        <Button
          size={'sm'}
          variant={'light'}
          onClick={() => {
            tags.sort((a, b) => (a.value || '').localeCompare(b.value || ''));
            setTags([...tags]);
          }}
        >
          <CustomIcon type={'sorting'} className={'text-medium'} />
          {t('Sort by alphabet')}
        </Button>
      </div>
      <div className="mt-2 mb-2 flex flex-col gap-1">
        <DndContext
          sensors={sensors}
          collisionDetection={closestCenter}
          onDragEnd={handleDragEnd}
        >
          <SortableContext
            items={tags?.map(c => ({
              ...c,
              id: c.value,
            }))}
            strategy={verticalListSortingStrategy}
          >
            <div style={{ height: Math.min(tags.length, 6) * LineHeight }}>
              <AutoSizer>
                {({
                    width,
                    height,
                  }) => (
                    <List
                      ref={virtualListRef}
                    // className={styles.List}
                      height={height}
                    // height={Math.min(tags.length, 6) * 30}
                      rowCount={tags.length}
                      rowHeight={LineHeight}
                      rowRenderer={(ctx) => {
                      const {
                        key,
                        index,
                        style,
                      } = ctx;
                      const sc = tags[index];
                      return (
                        <SortableTag
                          checkUsage={checkUsage}
                          key={sc.value}
                          id={sc.value}
                          tag={sc}
                          onRemove={t => {
                            tags.splice(index, 1);
                            setTags([...tags]);
                          }}
                          onChange={t => {
                            tags[index] = t;
                            setTags([...tags]);
                          }}
                          style={style}
                        />
                      );
                    }}
                      width={width}
                    />
                )}
              </AutoSizer>
            </div>
            {/* {tags?.map((sc, index) => ( */}
            {/*   <SortableTag */}
            {/*     key={sc.value} */}
            {/*     id={sc.value} */}
            {/*     tag={sc} */}
            {/*     onRemove={t => { */}
            {/*       tags.splice(index, 1); */}
            {/*       setTags([...tags]); */}
            {/*     }} */}
            {/*     onChange={t => { */}
            {/*       tags[index] = t; */}
            {/*       setTags([...tags]); */}
            {/*     }} */}
            {/*   /> */}
            {/* ))} */}
          </SortableContext>
        </DndContext>
      </div>
      <div className="flex items-center justify-between">
        <Button
          size={'sm'}
          onClick={() => {
            const newTags = [...tags, { value: uuidv4() }];
            setTags(newTags);
            setTimeout(() => {
              virtualListRef.current?.scrollToRow(newTags.length - 1);
            }, 100);
          }}
        >
          <CustomIcon
            type={'plus-circle'}
            className={'text-medium'}
          />
          {t('Add a choice')}
        </Button>
        <Popover
          trigger={(
            <Button
              variant={'light'}
              size={'sm'}
            >
              {t('Add or delete in bulk')}
            </Button>
          )}
          style={{ zIndex: 100 }}
          size={'lg'}
          placement={'right'}
          visible={editInBulkPopupVisible}
          onVisibleChange={v => {
            if (v) {
              const text = tags.map(t => {
                let s = '';
                if (t.group != undefined && t.group.length > 0) {
                  s += t.group + GroupAndNameSeparator;
                }
                s += t.name;
                return s;
              }).join('\n');
              setEditInBulkText(text);
            }
            setEditInBulkPopupVisible(v);
          }}
        >
          <div className={'flex flex-col gap-2 m-2 '}>
            <div className="text-medium">{t('Add or delete tags in bulk')}</div>
            <div className={'text-sm opacity-70'}>
              <div>{t('Colon can be added between group and name, and tags will be separated by line breaks.')}</div>
              <div>{t('Once you click the submit button, new tags will be added to the list, and missing tags will be deleted.')}</div>
              <div>{t('Be cautions: once you modify the text in one line, it will be treated as a new tag, and the original tag will be deleted.')}</div>
            </div>
            <Textarea
              value={editInBulkText}
              // onValueChange={v => setEditInBulkText(v)}
              maxRows={16}
              onValueChange={v => {
                setEditInBulkText(v);
                calculateBulkEditSummaryTimeoutRef.current && clearTimeout(calculateBulkEditSummaryTimeoutRef.current);
                calculateBulkEditSummaryTimeoutRef.current = setTimeout(() => {
                  calculateBulkEditSummary(v);
                  calculateBulkEditSummaryTimeoutRef.current = undefined;
                }, 1000);
              }}
            />
            {bulkEditSummaries.length > 0 && (
              <div className={'flex items-center gap-2 text-sm'}>
                {bulkEditSummaries.map(s => (
                  <Chip
                    size={'sm'}
                    variant={'light'}
                    color={'success'}
                  >{s}</Chip>
                ))}
              </div>
            )}
            <div className="flex justify-end items-center">
              <Button
                variant={'light'}
                size={'sm'}
                onClick={() => {
                  setEditInBulkPopupVisible(false);
                }}
              >
                {t('Cancel')}
              </Button>
              <Button
                color={'primary'}
                size={'sm'}
                isLoading={!!calculateBulkEditSummaryTimeoutRef.current}
                onClick={() => {
                  setTags(buildTagsFromBulkText(editInBulkText));
                  setEditInBulkPopupVisible(false);
                }}
              >
                {t('Submit')}
              </Button>
            </div>
          </div>
        </Popover>
      </div>
    </div>
  );

  function handleDragEnd(event) {
    const {
      active,
      over,
    } = event;

    if (active.value !== over.value) {
      setTags((items) => {
        const oldIndex = items.indexOf(active.value);
        const newIndex = items.indexOf(over.value);

        return arrayMove(items, oldIndex, newIndex);
      });
    }
  }
}
