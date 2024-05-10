import type { Duration } from 'dayjs/plugin/duration';
import { useRef, useState } from 'react';
import type { ValueEditorProps } from '../models';
import { TimeInput } from '@/components/bakaui';

type TimeValueEditorProps = ValueEditorProps<Duration>;

export default ({ initValue, onChange, ...props }: TimeValueEditorProps) => {
  const valueRef = useRef(initValue);

    return (
      <TimeInput
        size={'sm'}
        defaultValue={initValue}
        onChange={v => valueRef.current = v}
        onBlur={() => {
          onChange?.(valueRef.current);
        }}
      />
  );
};
