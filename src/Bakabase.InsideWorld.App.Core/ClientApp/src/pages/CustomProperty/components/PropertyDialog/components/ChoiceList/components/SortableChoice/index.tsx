import React, { useEffect, useState } from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import { toast } from 'react-toastify';
import type { IChoice } from '../../../../../../models';
import DragHandle from '@/components/DragHandle';
import ClickableIcon from '@/components/ClickableIcon';
import { Input } from '@/components/bakaui';

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
    <div ref={setNodeRef} style={style} className={'flex gap-1 items-center'}>
      <DragHandle {...listeners} {...attributes} />
      <div className={'border-1 w-[14px] h-[14px]'} style={{ background: choice?.color ?? 'transparent' }} />
      <Input
        size={'sm'}
        value={choice?.value}
        onValueChange={value => {
          setChoice({
            ...choice,
            value,
          });
        }}
      />
      <ClickableIcon
        colorType={'normal'}
        type={choice.hide == true ? 'eye-close' : 'eye'}
        className={'text-medium'}
        title={t('Hide in view')}
        onClick={() => {
          setChoice({
            ...choice,
            hide: !choice.hide,
          });
        }}
      />
      <ClickableIcon
        className={'text-medium'}
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
