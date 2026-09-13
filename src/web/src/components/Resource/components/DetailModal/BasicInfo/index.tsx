"use client";

import type { Resource } from "@/core/models/Resource";

import React from "react";
import { useTranslation } from "react-i18next";

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
  const { t } = useTranslation();

  return (
    <div
      className="grid gap-2"
      style={{ gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 8rem), 1fr))" }}
    >
      {dateTimes.map((dateTime) => {
        const label = t<string>(dateTime.label);
        const raw = resource[dateTime.key] as string | undefined;
        const unavailable = dateTime.fromFiles && !resource.hasLocalPath;

        return (
          <DetailTimestamp
            key={dateTime.key}
            label={label}
            unavailableText={unavailable ? t<string>("resource.label.notMaterialized") : undefined}
            value={raw}
          />
        );
      })}
    </div>
  );
};

BasicInfo.displayName = "BasicInfo";

export default BasicInfo;
