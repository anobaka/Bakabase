"use client";

import type { Resource } from "@/core/models/Resource";

import dayjs from "dayjs";
import React from "react";
import { useTranslation } from "react-i18next";

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
      className={"grid justify-evenly gap-y-1"}
      style={{ gridTemplateColumns: "repeat(2, auto)" }}
    >
      {dateTimes.map((dateTime, i) => {
        const label = t<string>(dateTime.label);
        const raw = resource[dateTime.key] as string | undefined;
        const unavailable = dateTime.fromFiles && !resource.hasLocalPath;

        return (
          <div key={i} className={"flex flex-col"}>
            <div className={"text-xs opacity-60"}>{label}</div>
            {unavailable ? (
              <div className={"opacity-60"}>{t<string>("resource.label.notMaterialized")}</div>
            ) : (
              <div>{raw ? dayjs(raw).format("YYYY-MM-DD HH:mm:ss") : "-"}</div>
            )}
          </div>
        );
      })}
    </div>
  );
};

BasicInfo.displayName = "BasicInfo";

export default BasicInfo;
