"use client";

import type { DateInputProps as NextUIDateInputProps } from "@heroui/react";
import type { Dayjs } from "dayjs";

import { DatePicker as HeroDatePicker } from "@heroui/react";
import dayjs from "dayjs";
import { parseDateTime } from "@internationalized/date";

interface DatePickerProps
  extends Omit<NextUIDateInputProps, "value" | "onChange" | "defaultValue"> {
  value?: Dayjs;
  defaultValue?: Dayjs;
  onChange?: (value: Dayjs) => void;
}
const DatePicker = ({
  value,
  onChange,
  defaultValue,
  ...props
}: DatePickerProps) => {
  const dv = defaultValue
    ? parseDateTime(defaultValue.toISOString())
    : undefined;
  const v = value ? parseDateTime(value.toISOString()) : undefined;

  return (
    <HeroDatePicker
      defaultValue={dv}
      value={v}
      onChange={(v) => {
        onChange?.(dayjs(v.toString()));
      }}
      {...props}
    />
  );
};

DatePicker.displayName = "DatePicker";

export default DatePicker;
