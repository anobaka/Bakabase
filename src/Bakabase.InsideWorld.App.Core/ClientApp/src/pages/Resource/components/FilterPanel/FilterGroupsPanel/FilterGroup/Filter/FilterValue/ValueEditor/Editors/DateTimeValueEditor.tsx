import type { Dayjs } from 'dayjs';
import { useRef } from 'react';
import type { ValueEditorProps } from '../models';
import { DateInput } from '@/components/bakaui';

interface DateTimeValueEditorProps extends ValueEditorProps<Dayjs> {
  mode?: 'date' | 'datetime';
}

export default ({ mode = 'datetime', initValue, onChange, ...props }: DateTimeValueEditorProps) => {
  const valueRef = useRef(initValue);
    return (
      <DateInput
        size={'sm'}
        granularity={mode == 'datetime' ? 'day' : 'second'}
        defaultValue={initValue}
        onChange={v => {
          valueRef.current = v;
        }}
        onBlur={() => {
          onChange?.(valueRef.current);
        }}
      />
  );
};
