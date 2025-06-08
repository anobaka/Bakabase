import React from 'react';
import { AiOutlineNumber } from 'react-icons/ai';
import { EditableValue } from '../index';
import { Chip, Input, NumberInput, Snippet } from '@/components/bakaui';
import type { SimpleEditableValueProps } from '@/components/EditableValue';

type Props = SimpleEditableValueProps<number> & {
  min?: number;
  max?: number;
};

export default (props: Props) => {
  return (
    <EditableValue
      Viewer={({
                 value,
                 ...props
               }) => (
                 <Chip
                   variant={'flat'}
                   radius={'sm'}
                   startContent={<AiOutlineNumber className={'text-medium'} />}
                   {...props}
                 >
                   {value}
                 </Chip>
      )}
      Editor={(props) => (<NumberInput
        formatOptions={{ useGrouping: false }}
        {...props}
      />)}
      {...props}
    />
  );
};
