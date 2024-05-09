import type { DateInputProps as NextUIDateInputProps } from '@nextui-org/react';
import { DateInput } from '@nextui-org/react';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { parseDateTime } from '@internationalized/date';

interface DateInputProps extends Omit<NextUIDateInputProps, 'value' | 'onChange' | 'defaultValue'> {
  value?: Dayjs;
  defaultValue?: Dayjs;
  onChange?: (value: Dayjs) => void;
}

export default ({ value, onChange, defaultValue, ...props }: DateInputProps) => {
  const dv = defaultValue ? parseDateTime(defaultValue.toISOString()) : undefined;
  const v = value ? parseDateTime(value.toISOString()) : undefined;

  return (
    <DateInput
      defaultValue={dv}
      value={v}
      onChange={v => {
        onChange?.(dayjs(v.toString()));
      }}
      {...props}
    />
  );
};
