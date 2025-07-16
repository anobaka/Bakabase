"use client";

import type { Duration } from "dayjs/plugin/duration";
import type { NumberInputProps } from "@heroui/react";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import dayjs from "dayjs";

import { NumberInput } from "../bakaui";

type DurationUnit = "d" | "h" | "m" | "s";

const TimeUnits = [
  {
    label: "Day",
    value: "d",
    seconds: 60 * 60 * 24,
  },
  {
    label: "Hour",
    value: "h",
    seconds: 60 * 60,
  },
  {
    label: "Minute",
    value: "m",
    seconds: 60,
  },
  {
    label: "Second",
    value: "s",
    seconds: 1,
  },
].map((a) => ({
  ...a,
  label: a.label,
  value: a.value as DurationUnit,
}));

type Props = {
  duration?: Duration;
  onDurationChange?: (duration: Duration) => void;
  unit?: DurationUnit;
} & Omit<NumberInputProps, "value" | "onValueChange">;

const convertToDuration = (value: number, unit: DurationUnit): Duration => {
  const unitConfig = TimeUnits.find((tu) => tu.value === unit);

  if (!unitConfig) {
    throw new Error(`Invalid duration unit: ${unit}`);
  }

  return dayjs.duration({ seconds: value * unitConfig.seconds });
};

const convertFromDuration = (
  duration: Duration,
  unit: DurationUnit,
): number => {
  const unitConfig = TimeUnits.find((tu) => tu.value === unit);

  if (!unitConfig) {
    throw new Error(`Invalid duration unit: ${unit}`);
  }

  return duration.asSeconds() / unitConfig.seconds;
};

export default ({
  duration,
  onDurationChange,
  unit = "h",
  ...niProps
}: Props) => {
  const { t } = useTranslation();

  const [number, setNumber] = useState<number>(
    duration ? duration.asSeconds() : undefined,
  );

  useEffect(() => {
    setNumber(duration ? convertFromDuration(duration, unit) : undefined);
  }, [duration, unit]);

  return (
    <NumberInput
      endContent={
        <div className="flex items-center">
          <label className="sr-only" htmlFor="currency">
            Currency
          </label>
          <select
            className="outline-none border-0 bg-transparent text-default-400 text-small"
            defaultValue="USD"
          >
            {TimeUnits.map(tu => {
              return (
                <option aria-label="US Dollar" value={tu.value}>
                  {t<string>(`DurationUnit.${tu.label}`)}
                </option>
              );
            })}
          </select>
        </div>
      }
      placeholder="1"
      value={number}
      onValueChange={v => {
        setNumber(v);
        const nd = convertToDuration(v, unit);
        onDurationChange?.(nd);
      }}
      size={'sm'}
      // label={t<string>('Duration')}
      step={1}
      {...niProps}
    />
  );
};
