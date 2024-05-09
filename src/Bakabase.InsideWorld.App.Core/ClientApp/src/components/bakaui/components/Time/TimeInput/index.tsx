import type { TimeInputProps as NextUITimeInputProps } from '@nextui-org/react';
import { TimeInput } from '@nextui-org/react';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { parseTime } from '@internationalized/date';
import type { Duration } from 'dayjs/plugin/duration';

interface TimeInputProps extends Omit<NextUITimeInputProps, 'value' | 'onChange' | 'defaultValue'> {
  value?: Duration;
  defaultValue?: Duration;
  onChange?: (value: Duration) => void;
}

export default ({ value, onChange, defaultValue, ...props }: TimeInputProps) => {
  const dv = defaultValue ? parseTime(defaultValue.toISOString()) : undefined;
  const v = value ? parseTime(value.toISOString()) : undefined;

  return (
    <TimeInput
      defaultValue={dv}
      value={v}
      onChange={v => {
        onChange?.(dayjs.duration(v.toString()));
      }}
      {...props}
    />
  );
};
