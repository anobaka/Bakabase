import { AiOutlineCheck, AiOutlineClose, AiOutlineEdit } from 'react-icons/ai';
import type { FC, ReactElement } from 'react';
import React, { useState } from 'react';
import { Button } from '@/components/bakaui';
import { isPromise } from '@/components/utils';

type CommonProps<TValue> = {
  value?: TValue;
  onValueChange?: (value: TValue) => void;
  className?: string | undefined;
  size?: 'sm' | 'md' | 'lg' | undefined;
};

type Props<TValue> = {
  onSubmit?: (value?: TValue) => any | Promise<any>;
  Viewer: (props: CommonProps<TValue>) => ReactElement;
  Editor: (props: CommonProps<TValue>) => ReactElement;
} & CommonProps<TValue>;

export default function EditableValue<TValue>(
  {
    onSubmit,
    Viewer,
    Editor,
    ...commonProps
  }: Props<TValue>) {
  const [editing, setEditing] = useState(false);
  const [editingValue, setEditingValue] = useState<TValue>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  return (
    <div className={'flex items-center gap-2'}>
      {editing ? (
        <>
          <Editor {...commonProps} />
          <Button
            variant={'light'}
            size={'sm'}
            isLoading={isSubmitting}
            isIconOnly
            onPress={() => {
              const func = onSubmit?.(editingValue);
              if (isPromise(func)) {
                setIsSubmitting(true);
                func.finally(() => {
                  setIsSubmitting(false);
                  setEditing(false);
                });
              }
            }}
            color={'success'}
          >
            <AiOutlineCheck className={'text-medium'} />
          </Button>
          <Button
            variant={'light'}
            size={'sm'}
            isIconOnly
            onPress={() => {
              setEditing(false);
            }}
            color={'danger'}
          >
            <AiOutlineClose className={'text-medium'} />
          </Button>
        </>
      ) : (
        <>
          <Viewer {...commonProps} />
          <Button
            variant={'light'}
            size={'sm'}
            isIconOnly
            onPress={() => {
              setEditingValue(commonProps.value);
              setEditing(true);
            }}
          >
            <AiOutlineEdit className={'text-medium'} />
          </Button>
        </>
      )}
    </div>
  );
}
