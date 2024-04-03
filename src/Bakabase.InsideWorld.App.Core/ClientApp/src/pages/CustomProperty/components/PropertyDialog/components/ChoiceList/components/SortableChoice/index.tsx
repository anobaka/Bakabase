import React, { useEffect, useState } from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { Input } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import { toast } from 'react-toastify';
import type { IChoice } from '../../../../../../models';
import DragHandle from '@/components/DragHandle';
import ClickableIcon from '@/components/ClickableIcon';

interface IProps {
  id: string;
  choice: IChoice;
  onRemove?: (choice: IChoice) => any;
  onChange?: (choice: IChoice) => any;
}

export function SortableChoice({ id, choice: propsChoice, onRemove, onChange }: IProps) {
  const { t } = useTranslation();
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
  } = useSortable({ id: id });

  const [choice, setChoice] = useState(propsChoice);

  useEffect(() => {
    console.log(9999, 'new rendering');
  }, []);

  useUpdateEffect(() => {
    onChange?.(choice);
  }, [choice]);

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <div ref={setNodeRef} style={style} className={'sortable-choice'}>
      <DragHandle {...listeners} {...attributes} />
      <div className="color" style={{ background: choice?.color ?? 'transparent' }} />
      <Input
        size={'small'}
        value={choice?.value}
        hasClear
        trim
        onChange={value => {
          setChoice({
            ...choice,
            value,
          });
        }}
      />
      <ClickableIcon
        colorType={'normal'}
        type={choice.hide == true ? 'eye-close' : 'eye'}
        size={'small'}
        title={t('Hide in view')}
        onClick={() => {
          setChoice({
            ...choice,
            hide: !choice.hide,
          });
        }}
      />
      <ClickableIcon
        className={'remove'}
        colorType={'danger'}
        type={'delete'}
        size={'small'}
        onClick={() => {
          onRemove?.(choice);
        }}
      />
    </div>
  );
}
