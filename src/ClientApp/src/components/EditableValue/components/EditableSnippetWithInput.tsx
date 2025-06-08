import React from 'react';
import { EditableValue } from '../index';
import { Input, Snippet, Textarea } from '@/components/bakaui';
import type { SimpleEditableValueProps } from '@/components/EditableValue';

type Props = SimpleEditableValueProps<string>;

export default (props: Props) => {
  return (
    <EditableValue
      Viewer={({
                 value,
                 ...props
               }) => (<Snippet symbol={<>&nbsp;</>} {...props}>{value}</Snippet>)}
      Editor={(props) => (<Input {...props} />)}
      {...props}
    />
  );
};
