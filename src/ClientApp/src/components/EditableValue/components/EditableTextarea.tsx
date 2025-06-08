import React from 'react';
import { EditableValue } from '../index';
import type { TextareaProps } from '@/components/bakaui';
import { Input, Snippet, Textarea } from '@/components/bakaui';
import type { SimpleEditableValueProps } from '@/components/EditableValue';

type Props = TextareaProps & SimpleEditableValueProps<string>;

export default (props: Props) => {
  return (
    <EditableValue
      Viewer={Textarea}
      Editor={Textarea}
      {...props}
    />
  );
};
