import React, { useEffect, useState } from 'react';
import type { UniqueIdentifier } from '@dnd-kit/core';
import { closestCenter, DndContext, KeyboardSensor, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { Button, Input, Overlay } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import type { IChoice } from '../../../../models';
import { SortableChoice } from './components/SortableChoice';
import CustomIcon from '@/components/CustomIcon';
import { uuidv4 } from '@/components/utils';

const { Popup } = Overlay;

interface IProps {
  choices?: IChoice[];
  onChange?: (choices: IChoice[]) => void;
}

interface ISortableChoice extends IChoice {
  id: UniqueIdentifier;
}

export default function ChoiceList({ choices: propsChoices, onChange }: IProps) {
  const { t } = useTranslation();
  const [choices, setChoices] = useState<IChoice[]>(propsChoices || []);
  const [sortableChoices, setSortableChoices] = useState<ISortableChoice[]>([]);
  const [addInBulkPopupVisible, setAddInBulkPopupVisible] = useState(false);
  const [addInBulkText, setAddInBulkText] = useState('');
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  useEffect(() => {
    setSortableChoices(choices.map((c, index) => ({
      id: uuidv4(),
      ...c,
    })));
    onChange?.(choices);
  }, [choices]);

  useEffect(() => {
    console.log(6666, sortableChoices);
  }, [sortableChoices]);

  return (
    <div className={'choice-list'}>
      <div className="other-opts">
        <Button
          size={'small'}
          text
          type={'normal'}
          className={'sort'}
          onClick={() => {
            choices.sort((a, b) => (a.value || '').localeCompare(b.value || ''));
            console.log(123, choices);
            setChoices([...choices]);
          }}
        >
          <CustomIcon type={'sorting'} size={'small'} />
          {t('Sort by alphabet')}
        </Button>
      </div>
      <div className="sortable-choices">
        <DndContext
          sensors={sensors}
          collisionDetection={closestCenter}
          onDragEnd={handleDragEnd}
        >
          <SortableContext
            items={sortableChoices}
            strategy={verticalListSortingStrategy}
          >
            {sortableChoices?.map((sc, index) => (
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
      <div className="add-opts">
        <Button
          className="add"
          size={'small'}
          text
          onClick={() => {
            setChoices([...choices, {}]);
          }}
        >
          <CustomIcon
            type={'plus-circle'}
            size={'small'}
          />
          {t('Add choice')}
        </Button>
        <Popup
          v2
          animation={false}
          trigger={(
            <Button
              className="add-in-bulk"
              size={'small'}
              text
            >
              {t('Add in bulk')}
            </Button>
          )}
          triggerType="click"
          placement={'rt'}
          visible={addInBulkPopupVisible}
          onVisibleChange={v => {
            setAddInBulkPopupVisible(v);
          }}
        >
          <div className={'add-choice-in-bulk-popup'}>
            <div className="title">{t('Add choices in bulk')}</div>
            <Input.TextArea
              value={addInBulkText}
              onChange={v => setAddInBulkText(v)}
              placeholder={t('Please enter the choices, one per line')}
              rows={20}
            />
            <div className="opts">
              <Button
                type={'primary'}
                size={'small'}
                onClick={() => {
                  const newChoices = addInBulkText.split('\n').map(c => ({ value: c }));
                  setChoices([...choices, ...newChoices]);
                  setAddInBulkPopupVisible(false);
                }}
              >
                {t('Add')}
              </Button>
              <Button
                type={'normal'}
                size={'small'}
                onClick={() => {
                  setAddInBulkPopupVisible(false);
                }}
              >
                {t('Cancel')}
              </Button>
            </div>
          </div>
        </Popup>
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
