import type { TimeInputProps as NextUITimeInputProps } from '@nextui-org/react';
import { TimeInput } from '@nextui-org/react';
import dayjs from 'dayjs';
import { Time } from '@internationalized/date';
import type { Duration } from 'dayjs/plugin/duration';
import { convertDurationToTime } from '@/components/utils';

interface TimeInputProps extends Omit<NextUITimeInputProps, 'value' | 'onChange' | 'defaultValue'> {
  value?: Duration;
  defaultValue?: Duration;
  onChange?: (value: Duration) => void;
}

export default ({
                  value,
                  onChange,
                  defaultValue,
                  ...props
                }: TimeInputProps) => {
  const dv = defaultValue ? convertDurationToTime(defaultValue) : undefined;
  const v = value ? convertDurationToTime(value) : undefined;

  new Time();

  return (
    <TimeInput
      defaultValue={dv}
      value={v}
      onChange={v => {
        onChange?.(dayjs.duration(v.toString()));
      }}
      hourCycle={24}
      {...props}
    />
  );
};
