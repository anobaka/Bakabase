"use client";

import type { Resource } from "@/core/models/Resource";

import React from "react";
import { useTranslation } from "react-i18next";
import { useMeasure } from "react-use";

import DetailTimestamp from "../DetailTimestamp";

type Props = {
  resource: Resource;
};

/**
 * The four timestamps, and which of them describe files rather than the row.
 *
 * `fromFiles` matters twice over: a resource with no local files has no file times — the columns
 * hold the moment its row was written — so showing them would be a lie, and the keys are the
 * model's own (`fileCreatedAt`, not the long-gone `fileCreateDt`, which read as `undefined` and
 * made every row render the current time).
 */
const dateTimes: { key: keyof Resource; label: string; fromFiles: boolean }[] = [
  {
    key: "fileCreatedAt",
    label: "resource.label.fileAddDate",
    fromFiles: true,
  },
  {
    key: "fileModifiedAt",
    label: "resource.label.fileModifyDate",
    fromFiles: true,
  },
  {
    key: "createdAt",
    label: "resource.label.resourceCreateDate",
    fromFiles: false,
  },
  {
    key: "updatedAt",
    label: "resource.label.resourceUpdateDate",
    fromFiles: false,
  },
];

const BasicInfo = ({ resource }: Props) => {
  const [containerRef, { width }] = useMeasure<HTMLDivElement>();
  // Keep the four timestamps in pairs until all four fit comfortably on one row.
  const columns = width >= 612 ? 4 : width > 0 && width < 236 ? 1 : 2;
  const { t } = useTranslation();

  return (
    <div
      ref={containerRef}
      className="grid gap-x-3 gap-y-2 rounded-xl bg-default-50/60 p-3"
      style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}
    >
      {dateTimes.map((dateTime) => {
        const label = t<string>(dateTime.label);
        const raw = resource[dateTime.key] as string | undefined;
        const unavailable = dateTime.fromFiles && !resource.hasLocalPath;

        return (
          <DetailTimestamp
            key={dateTime.key}
            label={label}
            unavailableText={unavailable ? t<string>("resource.detail.noFile") : undefined}
            value={raw}
          />
        );
      })}
    </div>
  );
};

BasicInfo.displayName = "BasicInfo";

export default BasicInfo;
