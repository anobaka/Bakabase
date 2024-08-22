import type { DateInputProps as NextUIDateInputProps } from '@nextui-org/react';
import { CalendarDate } from '@nextui-org/react';
import { DateInput } from '@nextui-org/react';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import type { CalendarDateTime } from '@internationalized/date';
import { parseDateTime } from '@internationalized/date';

interface DateInputProps extends Omit<NextUIDateInputProps, 'value' | 'onChange' | 'defaultValue'> {
  value?: Dayjs;
  defaultValue?: Dayjs;
  onChange?: (value?: Dayjs) => void;
}

export default ({ value, onChange, defaultValue, ...props }: DateInputProps) => {
  let dv: CalendarDateTime | undefined;
  let v: CalendarDateTime | undefined;
  try {
    if (defaultValue) {
      dv = parseDateTime(defaultValue.toISOString());
    }
    if (value) {
      v = parseDateTime(value.toISOString());
    }
  } catch (e) {
    console.error(e);
  }

  return (
    <DateInput
      defaultValue={dv}
      value={v}
      onChange={v => {
        onChange?.(v ? dayjs(v.toString()) : undefined);
      }}
      {...props}
    />
  );
};
