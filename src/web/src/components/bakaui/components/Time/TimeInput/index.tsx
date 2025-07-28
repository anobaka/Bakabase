"use client";

import type { TimeInputProps as NextUITimeInputProps } from "@heroui/react";
import type { Duration } from "dayjs/plugin/duration";
import type { Time } from "@internationalized/date";

import { TimeInput as HeroTimeInput } from "@heroui/react";

import {
  convertDurationToTime,
  convertTimeToDuration,
} from "@/components/utils";

interface TimeInputProps
  extends Omit<NextUITimeInputProps, "value" | "onChange" | "defaultValue"> {
  value?: Duration;
  defaultValue?: Duration;
  onChange?: (value: Duration) => void;
}
const TimeInput = ({
  value,
  onChange,
  defaultValue,
  ...props
}: TimeInputProps) => {
  const dv = defaultValue ? convertDurationToTime(defaultValue) : undefined;
  const v = value ? convertDurationToTime(value) : undefined;

  return (
    <HeroTimeInput
      defaultValue={dv}
      hourCycle={24}
      value={v}
      onChange={(v) => {
        onChange?.(convertTimeToDuration(v as Time));
      }}
      {...props}
    />
  );
};

TimeInput.displayName = "TimeInput";

export default TimeInput;
