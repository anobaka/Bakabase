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
import type { IChoice } from '../../../Property/models';
import { SortableChoice } from './components/SortableChoice';
import CustomIcon from '@/components/CustomIcon';
import { uuidv4 } from '@/components/utils';
import { Button, Chip, Popover, Textarea } from '@/components/bakaui';

const { Popup } = Overlay;

interface IProps {
  choices?: IChoice[];
  onChange?: (choices: IChoice[]) => void;
  className?: string;
  checkUsage?: (value: string) => Promise<number>;
}

const lineHeight = 35;

export default function ChoiceList({ choices: propsChoices, onChange, className, checkUsage }: IProps) {
  const { t } = useTranslation();
  const [choices, setChoices] = useState<IChoice[]>(propsChoices || []);
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
    onChange?.(choices);
  }, [choices]);

  const buildChoicesFromBulkText = (text: string): IChoice[] => {
    return text.split('\n').map<IChoice | null>(c => {
      const str = c.trim();
      if (str.length == 0) {
        return null;
      }

      const t = choices.find(x => x.label == str);

      if (t) {
        return t;
      }

      return {
        label: str,
        value: uuidv4(),
      };
    }).filter(x => x != null) as IChoice[];
  };

  const calculateBulkEditSummary = (text: string) => {
    const ctxChoices = buildChoicesFromBulkText(text);
    const addedChoicesCount = ctxChoices.filter(x => !choices.includes(x)).length;
    const sameChoicesCount = ctxChoices.filter(x => choices.includes(x)).length;

    const deletedChoicesCount = choices.length - sameChoicesCount;
    if (deletedChoicesCount > 0 || addedChoicesCount > 0) {
      const tips = [deletedChoicesCount > 0 ? t('{{count}} data will be deleted', { count: deletedChoicesCount }) : '', addedChoicesCount > 0 ? t('{{count}} data will be added', { count: addedChoicesCount }) : ''];
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
            choices.sort((a, b) => (a.value || '').localeCompare(b.value || ''));
            setChoices([...choices]);
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
            items={choices?.map(c => ({ ...c, id: c.value }))}
            strategy={verticalListSortingStrategy}
          >
            <div style={{ height: Math.min(choices.length, 6) * lineHeight }}>
              <AutoSizer>
                {({
                    width,
                    height,
                  }) => (
                    <List
                      ref={virtualListRef}
                    // className={styles.List}
                      height={height}
                      rowCount={choices.length}
                      rowHeight={lineHeight}
                      rowRenderer={(ctx) => {
                      const {
                        key,
                        index,
                        style,
                      } = ctx;
                      const sc = choices[index];
                      return (
                        <SortableChoice
                          key={sc.value}
                          style={style}
                          checkUsage={checkUsage}
                          // key={sc.value}
                          id={sc.value}
                          choice={sc}
                          onRemove={t => {
                            choices.splice(index, 1);
                            setChoices([...choices]);
                          }}
                          onChange={t => {
                            choices[index] = t;
                            setChoices([...choices]);
                          }}
                        />
                      );
                    }}
                      width={width}
                    />
                )}
              </AutoSizer>
            </div>
            {/* {choices?.map((sc, index) => ( */}
            {/*   <SortableChoice */}
            {/*     key={sc.value} */}
            {/*     id={sc.value} */}
            {/*     choice={sc} */}
            {/*     onRemove={t => { */}
            {/*       choices.splice(index, 1); */}
            {/*       setChoices([...choices]); */}
            {/*     }} */}
            {/*     onChange={t => { */}
            {/*       choices[index] = t; */}
            {/*       setChoices([...choices]); */}
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
            const newChoices = [...choices, { label: '', value: uuidv4() }];
            setChoices(newChoices);
            setTimeout(() => {
              virtualListRef.current?.scrollToRow(newChoices.length - 1);
            }, 100);
            // console.log(`scroll to ${newChoices.length}`, virtualListRef.current?.scrollToRow);
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
              const text = choices.map(t => t.label).join('\n');
              setEditInBulkText(text);
            }
            setEditInBulkPopupVisible(v);
          }}
        >
          <div className={'flex flex-col gap-2 m-2 '}>
            <div className="text-medium">{t('Add or delete choices in bulk')}</div>
            <div className={'text-sm opacity-70'}>
              <div>{t('Choices will be separated by line breaks.')}</div>
              <div>{t('Once you click the submit button, new choices will be added to the list, and missing choices will be deleted.')}</div>
              <div>{t('Be cautions: once you modify the text in one line, it will be treated as a new choice, and the original choice will be deleted.')}</div>
            </div>
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
                  setChoices(buildChoicesFromBulkText(editInBulkText));
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
      setChoices((items) => {
        const oldIndex = items.indexOf(active.value);
        const newIndex = items.indexOf(over.value);

        return arrayMove(items, oldIndex, newIndex);
      });
    }
  }
}
