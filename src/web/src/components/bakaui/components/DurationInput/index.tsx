"use client";

import { useCallback, useMemo } from "react";
import { useTranslation } from "react-i18next";

import { Input } from "@/components/bakaui";

export interface DurationInputProps {
  /** Value in seconds */
  value?: number;
  /** Default value in seconds */
  defaultValue?: number;
  /** Callback when value changes (value in seconds) */
  onChange?: (value: number) => void;
  /** Show days input */
  showDays?: boolean;
  /** Show hours input */
  showHours?: boolean;
  /** Show minutes input */
  showMinutes?: boolean;
  /** Show seconds input */
  showSeconds?: boolean;
  /** Minimum value in seconds */
  minValue?: number;
  /** Size of the input */
  size?: "sm" | "md" | "lg";
  /** Is disabled */
  isDisabled?: boolean;
  /** Class name */
  className?: string;
}

interface DurationParts {
  days: number;
  hours: number;
  minutes: number;
  seconds: number;
}

const secondsToParts = (totalSeconds: number): DurationParts => {
  const days = Math.floor(totalSeconds / 86400);
  const hours = Math.floor((totalSeconds % 86400) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return { days, hours, minutes, seconds };
};

const partsToSeconds = (parts: DurationParts): number => {
  return parts.days * 86400 + parts.hours * 3600 + parts.minutes * 60 + parts.seconds;
};

const DurationInput = ({
  value,
  defaultValue,
  onChange,
  showDays = true,
  showHours = true,
  showMinutes = true,
  showSeconds = true,
  minValue = 0,
  size = "sm",
  isDisabled = false,
  className,
}: DurationInputProps) => {
  const { t } = useTranslation();

  const currentValue = value ?? defaultValue ?? 0;
  const parts = useMemo(() => secondsToParts(currentValue), [currentValue]);

  const handlePartChange = useCallback(
    (part: keyof DurationParts, newValue: string) => {
      const numValue = parseInt(newValue, 10) || 0;
      const newParts = { ...parts, [part]: Math.max(0, numValue) };
      const totalSeconds = partsToSeconds(newParts);
      onChange?.(Math.max(minValue, totalSeconds));
    },
    [parts, onChange, minValue]
  );

  const inputWidth = size === "sm" ? "w-16" : size === "md" ? "w-20" : "w-24";

  return (
    <div className={`flex items-center gap-1 ${className || ""}`}>
      {showDays && (
        <div className="flex items-center gap-1">
          <Input
            type="number"
            size={size}
            value={String(parts.days)}
            min={0}
            className={inputWidth}
            isDisabled={isDisabled}
            onChange={(e) => handlePartChange("days", e.target.value)}
          />
          <span className="text-default-500 text-sm">{t("common.unit.days")}</span>
        </div>
      )}
      {showHours && (
        <div className="flex items-center gap-1">
          <Input
            type="number"
            size={size}
            value={String(parts.hours)}
            min={0}
            max={showDays ? 23 : undefined}
            className={inputWidth}
            isDisabled={isDisabled}
            onChange={(e) => handlePartChange("hours", e.target.value)}
          />
          <span className="text-default-500 text-sm">{t("common.unit.hours")}</span>
        </div>
      )}
      {showMinutes && (
        <div className="flex items-center gap-1">
          <Input
            type="number"
            size={size}
            value={String(parts.minutes)}
            min={0}
            max={59}
            className={inputWidth}
            isDisabled={isDisabled}
            onChange={(e) => handlePartChange("minutes", e.target.value)}
          />
          <span className="text-default-500 text-sm">{t("common.unit.minutes")}</span>
        </div>
      )}
      {showSeconds && (
        <div className="flex items-center gap-1">
          <Input
            type="number"
            size={size}
            value={String(parts.seconds)}
            min={0}
            max={59}
            className={inputWidth}
            isDisabled={isDisabled}
            onChange={(e) => handlePartChange("seconds", e.target.value)}
          />
          <span className="text-default-500 text-sm">{t("common.unit.seconds")}</span>
        </div>
      )}
    </div>
  );
};

DurationInput.displayName = "DurationInput";

export default DurationInput;

/**
 * Format seconds to human-readable duration string
 * @param seconds Total seconds
 * @returns Formatted string like "1d 2h 30m 15s" or "1 hour 30 minutes"
 */
export const formatDuration = (seconds: number, t: (key: string) => string): string => {
  if (seconds <= 0) return t("common.state.never");

  const parts = secondsToParts(seconds);
  const result: string[] = [];

  if (parts.days > 0) {
    result.push(`${parts.days}${t("common.unit.days")}`);
  }
  if (parts.hours > 0) {
    result.push(`${parts.hours}${t("common.unit.hours")}`);
  }
  if (parts.minutes > 0) {
    result.push(`${parts.minutes}${t("common.unit.minutes")}`);
  }
  if (parts.seconds > 0 || result.length === 0) {
    result.push(`${parts.seconds}${t("common.unit.seconds")}`);
  }

  return result.join(" ");
};
