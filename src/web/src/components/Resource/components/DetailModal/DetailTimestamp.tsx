"use client";

import type { ReactNode } from "react";

import dayjs from "dayjs";

import DateTimeValueRenderer from "@/components/StandardValue/ValueRenderer/Renderers/DateTimeValueRenderer";

interface Props {
  label: string;
  value?: string;
  unavailableText?: string;
  action?: ReactNode;
}

/** One presentation for file, resource and playback timestamps inside resource details. */
const DetailTimestamp = ({ label, value, unavailableText, action }: Props) => {
  const parsed = value ? dayjs(value) : undefined;
  const date = parsed?.isValid() ? parsed : undefined;

  return (
    <div className="min-w-0 rounded-xl border border-default-200 bg-default-50/60 p-3">
      <div className="flex min-h-5 items-center justify-between gap-2">
        <span className="text-xs font-medium text-default-500">{label}</span>
        {action}
      </div>
      <div className="mt-1 break-words text-sm leading-relaxed tabular-nums text-default-700">
        {unavailableText ? (
          <span className="text-default-400">{unavailableText}</span>
        ) : date ? (
          <DateTimeValueRenderer isReadonly as="datetime" size="sm" value={date} />
        ) : (
          <span className="text-default-400">—</span>
        )}
      </div>
    </div>
  );
};

export default DetailTimestamp;
