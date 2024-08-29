import React, { useEffect, useRef, useState } from 'react';
import { closestCenter, DndContext, KeyboardSensor, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { Input, Overlay } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import {
  AutoSizer,
  CellMeasurer,
  CellMeasurerCache,
  createMasonryCellPositioner,
  Masonry,
  WindowScroller,
  List,
} from 'react-virtualized';
import type { Tag } from '../../../Property/models';
import { IChoice } from '../../../Property/models';
import { SortableTag } from './components/SortableTag';
import CustomIcon from '@/components/CustomIcon';
import { uuidv4 } from '@/components/utils';
import { Button, Popover, Textarea } from '@/components/bakaui';

interface IProps {
  tags?: Tag[];
  onChange?: (tags: Tag[]) => void;
  className?: string;
  checkUsage?: (value: string) => Promise<number>;
}

export default function TagList({
                                  tags: propsTags,
                                  onChange,
                                  className,
                                  checkUsage,
                                }: IProps) {
  const { t } = useTranslation();
  const [tags, setTags] = useState<Tag[]>(propsTags || []);
  const [addInBulkPopupVisible, setAddInBulkPopupVisible] = useState(false);
  const [addInBulkText, setAddInBulkText] = useState('');
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
            <div style={{ height: Math.min(tags.length, 6) * 30 }}>
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
                      rowHeight={30}
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
              {t('Add in bulk')}
            </Button>
          )}
          placement={'right'}
          visible={addInBulkPopupVisible}
          onVisibleChange={v => {
            setAddInBulkPopupVisible(v);
          }}
        >
          <div className={'flex flex-col gap-2 m-2 '}>
            <div className="text-medium">{t('Add tags in bulk')}</div>
            <Textarea
              value={addInBulkText}
              onValueChange={v => setAddInBulkText(v)}
              placeholder={t('Please enter the data you want to add, colon can be added between group and name, and you can separated tags by line breaks')}
            />
            <div className="flex justify-end items-center">
              <Button
                variant={'light'}
                size={'sm'}
                onClick={() => {
                  setAddInBulkPopupVisible(false);
                }}
              >
                {t('Cancel')}
              </Button>
              <Button
                color={'primary'}
                size={'sm'}
                onClick={() => {
                  const newTags: Tag[] = addInBulkText.split('\n').map(c => {
                    const segments = c.split(':');
                    const t: Tag = {
                      name: c,
                      value: uuidv4(),
                    };
                    if (segments.length == 1) {
                      t.name = segments[0];
                    } else {
                      t.group = segments[0];
                      t.name = segments[1];
                    }
                    return t;
                  });
                  setTags([...tags, ...newTags]);
                  setAddInBulkPopupVisible(false);
                }}
              >
                {t('Add')}
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
