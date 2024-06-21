import React, { useEffect, useState } from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import { DeleteOutlined, EyeInvisibleOutlined, EyeOutlined } from '@ant-design/icons';
import DragHandle from '@/components/DragHandle';
import { Button, ColorPicker, Input } from '@/components/bakaui';
import type { IChoice } from '@/components/Property/models';

interface IProps {
  id: string;
  choice: IChoice;
  onRemove?: (choice: IChoice) => any;
  onChange?: (choice: IChoice) => any;
}

export function SortableChoice({
                                 id,
                                 choice: propsChoice,
                                 onRemove,
                                 onChange,
                               }: IProps) {
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
    // console.log(9999, 'new rendering');
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
      <ColorPicker />
      <Input
        size={'sm'}
        value={choice?.label}
        onValueChange={label => {
          setChoice({
            ...choice,
            label,
          });
        }}
      />
      <div className={'flex items-center'}>
        <Button
          size={'sm'}
          radius={'sm'}
          isIconOnly
          title={t('Hide in view')}
          onClick={() => {
            setChoice({
              ...choice,
              hide: !choice.hide,
            });
          }}
          variant={'light'}
        >
          {choice.hide ? <EyeInvisibleOutlined className={'text-base'} /> : <EyeOutlined className={'text-base'} />}
        </Button>
        <Button
          size={'sm'}
          radius={'sm'}
          isIconOnly
          onClick={() => {
            onRemove?.(choice);
          }}
          variant={'light'}
          color={'danger'}
        >
          <DeleteOutlined className={'text-base'} />
        </Button>
      </div>
    </div>
  );
}
