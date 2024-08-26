import type { DateInputProps as NextUIDateInputProps } from '@nextui-org/react';
import { CalendarDate } from '@nextui-org/react';
import { DateInput } from '@nextui-org/react';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import type { CalendarDateTime } from '@internationalized/date';
import { parseDateTime } from '@internationalized/date';
import { useEffect, useState } from 'react';

interface DateInputProps extends Omit<NextUIDateInputProps, 'value' | 'onChange' | 'defaultValue'> {
  value?: Dayjs;
  defaultValue?: Dayjs;
  onChange?: (value?: Dayjs) => void;
}

export default ({ value: propsValue, onChange, defaultValue, ...props }: DateInputProps) => {
  const [value, setValue] = useState<CalendarDateTime>();

  console.log('1234567', propsValue, propsValue?.toISOString(), value);

  useEffect(() => {
    try {
      if (propsValue) {
        setValue(parseDateTime(propsValue.toISOString()));
      }
    } catch (e) {
      console.error(e);
    }
  }, [propsValue]);

  let dv: CalendarDateTime | undefined;
  try {
    if (defaultValue) {
      dv = parseDateTime(defaultValue.toISOString());
    }
  } catch (e) {
    console.error(e);
  }

  return (
    <DateInput
      defaultValue={dv}
      value={value}
      onChange={v => {
        onChange?.(v ? dayjs(v.toString()) : undefined);
      }}
      {...props}
    />
  );
};
