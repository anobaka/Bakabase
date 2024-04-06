import React, { useEffect, useState } from 'react';
import { closestCenter, DndContext, KeyboardSensor, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { Input, Overlay } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import type { IChoice } from '../../../../models';
import { SortableChoice } from './components/SortableChoice';
import CustomIcon from '@/components/CustomIcon';
import { uuidv4 } from '@/components/utils';
import { Button, Popover, TextArea } from '@/components/bakaui';

const { Popup } = Overlay;

interface IProps {
  choices?: IChoice[];
  onChange?: (choices: IChoice[]) => void;
}

export default function ChoiceList({ choices: propsChoices, onChange }: IProps) {
  const { t } = useTranslation();
  const [choices, setChoices] = useState<IChoice[]>(propsChoices || []);
  const [addInBulkPopupVisible, setAddInBulkPopupVisible] = useState(false);
  const [addInBulkText, setAddInBulkText] = useState('');
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  useEffect(() => {
    onChange?.(choices);
  }, [choices]);

  return (
    <div className={''}>
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
            items={choices}
            strategy={verticalListSortingStrategy}
          >
            {choices?.map((sc, index) => (
              <SortableChoice
                key={sc.id}
                id={sc.id}
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
            ))}
          </SortableContext>
        </DndContext>
      </div>
      <div className="flex items-center justify-between">
        <Button
          size={'sm'}
          onClick={() => {
            setChoices([...choices, { id: uuidv4() }]);
          }}
        >
          <CustomIcon
            type={'plus-circle'}
            className={'text-medium'}
          />
          {t('Add choice')}
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
            <div className="text-medium">{t('Add choices in bulk')}</div>
            <TextArea
              value={addInBulkText}
              onValueChange={v => setAddInBulkText(v)}
              placeholder={t('Please enter the choices, one per line')}
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
                  const newChoices = addInBulkText.split('\n').map(c => ({ value: c, id: uuidv4() }));
                  setChoices([...choices, ...newChoices]);
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

    if (active.id !== over.id) {
      setChoices((items) => {
        const oldIndex = items.indexOf(active.id);
        const newIndex = items.indexOf(over.id);

        return arrayMove(items, oldIndex, newIndex);
      });
    }
  }
}
