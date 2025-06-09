import { AiOutlineCheck, AiOutlineClose, AiOutlineEdit } from 'react-icons/ai';
import type { ReactNode } from 'react';
import React, { useState, FC, ReactElement } from 'react';
import EditableSnippetWithInput from './components/EditableSnippetWithInput';
import EditableChipWithNumberInput from './components/EditableChipWithNumberInput';
import { Button } from '@/components/bakaui';
import { isPromise } from '@/components/utils';
import IntrinsicAttributes = React.JSX.IntrinsicAttributes;

// export type SimpleEditableValueProps<TValue> = {
//   onSubmit?: (value?: TValue) => any | Promise<any>;
// };

type ComponentCommonProps<TValue> = {
  value?: TValue;
  label?: ReactNode;
  description?: ReactNode;
};

type ViewerProps<TValue> = ComponentCommonProps<TValue> & {
  isReadOnly?: boolean;
};

type EditorProps<TValue> = ComponentCommonProps<TValue> & {
  onValueChange?: (value?: TValue) => void;
};

type Props<TValue,
  TEditorProps extends EditorProps<TValue>,
  TViewerProps extends ViewerProps<TValue> = TEditorProps> =
  {
    Viewer: React.FC<TViewerProps>;
    Editor: React.FC<TEditorProps>;
    viewerProps?: Omit<TViewerProps, keyof ComponentCommonProps<TValue>>;
    editorProps?: Omit<TEditorProps, keyof ComponentCommonProps<TValue>>;
    onSubmit?: (value?: TValue) => any | Promise<any>;
  }
  & ComponentCommonProps<TValue>;

// & SimpleEditableValueProps<TValue> & EditorProps<TValue> & ViewProps<TValue>;

function EditableValue<TValue,
  TEditorProps extends EditorProps<TValue>,
  TViewerProps extends ViewerProps<TValue> = TEditorProps>(
  {
    onSubmit,
    Viewer,
    viewerProps,
    Editor,
    editorProps,
    ...commonProps
  }: Props<TValue, TEditorProps, TViewerProps>) {
  const [editing, setEditing] = useState(false);
  const [editingValue, setEditingValue] = useState<TValue>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  console.log(Viewer, viewerProps, commonProps);

  return (
    <div className={'flex items-center gap-2'}>
      {editing ? (
        <>
          <Editor
            {...editorProps as unknown as TEditorProps}
            {...commonProps}
            onValueChange={v => setEditingValue(v)}
          />
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
          <Viewer
            {...viewerProps as unknown as TViewerProps}
            {...commonProps}
            isReadOnly
          />
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

export {
  EditableValue,
  EditableSnippetWithInput,
  EditableChipWithNumberInput,
};
