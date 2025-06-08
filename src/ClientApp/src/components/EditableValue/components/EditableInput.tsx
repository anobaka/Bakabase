import React from 'react';
import { EditableValue } from '../index';
import type { InputProps } from '@/components/bakaui';
import { Input, Snippet, Textarea } from '@/components/bakaui';
import type { SimpleEditableValueProps } from '@/components/EditableValue';

type Props = InputProps & SimpleEditableValueProps<string>;

export default (props: Props) => {
  return (
    <EditableValue
      Viewer={Input}
      Editor={Input}
      {...props}
    />
  );
};
