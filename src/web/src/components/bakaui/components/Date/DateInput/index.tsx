"use client";

import type { DateInputProps as NextUIDateInputProps } from "@heroui/react";
import type { Dayjs } from "dayjs";

import { DateInput as HeroDateInput } from "@heroui/react";
import dayjs from "dayjs";
import { CalendarDateTime } from "@internationalized/date";
import { useEffect, useState } from "react";

import { buildLogger } from "@/components/utils";

interface DateInputProps
  extends Omit<NextUIDateInputProps, "value" | "onChange" | "defaultValue"> {
  value?: Dayjs;
  defaultValue?: Dayjs;
  onChange?: (value?: Dayjs) => void;
}

const log = buildLogger("DateInput");

const convertToCalendarDateTime = (
  value: Dayjs | undefined,
): CalendarDateTime | undefined => {
  if (!value) {
    return;
  }
  const date = value.toDate();

  return new CalendarDateTime(
    date.getFullYear(),
    date.getMonth() + 1,
    date.getDate(),
    date.getHours(),
    date.getMinutes(),
    date.getSeconds(),
    date.getMilliseconds(),
  );
};
const DateInput = ({
  value: propsValue,
  onChange,
  defaultValue,
  ...props
}: DateInputProps) => {
  const [value, setValue] = useState<CalendarDateTime>();

  log(propsValue);
  log(propsValue, propsValue?.toISOString(), value, propsValue?.toDate());

  useEffect(() => {
    setValue(convertToCalendarDateTime(propsValue));
  }, [propsValue]);

  const dv = convertToCalendarDateTime(defaultValue);

  return (
    <HeroDateInput
      aria-label={"Date Input"}
      defaultValue={dv}
      hourCycle={24}
      value={value}
      onChange={(v) => {
        onChange?.(v ? dayjs(v.toString()) : undefined);
      }}
      {...props}
    />
  );
};

DateInput.displayName = "DateInput";

export default DateInput;
