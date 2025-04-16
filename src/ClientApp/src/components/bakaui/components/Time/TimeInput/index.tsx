import type { TimeInputProps as NextUITimeInputProps } from "@heroui/react";
import { TimeInput } from "@heroui/react";
import dayjs from 'dayjs';
import { Time } from '@internationalized/date';
import type { Duration } from 'dayjs/plugin/duration';
import { convertDurationToTime, convertTimeToDuration } from '@/components/utils';

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
        onChange?.(convertTimeToDuration(v as Time));
      }}
      hourCycle={24}
      {...props}
    />
  );
};
