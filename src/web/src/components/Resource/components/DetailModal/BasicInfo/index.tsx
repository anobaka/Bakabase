"use client";

import type { Resource } from "@/core/models/Resource";

import dayjs from "dayjs";
import React from "react";
import { useTranslation } from "react-i18next";

type Props = {
  resource: Resource;
};

const dateTimes = [
  {
    key: "fileCreateDt",
    label: "resource.label.fileAddDate",
  },
  {
    key: "fileModifyDt",
    label: "resource.label.fileModifyDate",
  },
  {
    key: "createDt",
    label: "resource.label.resourceCreateDate",
  },
  {
    key: "updateDt",
    label: "resource.label.resourceUpdateDate",
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
        const value = dayjs(resource[dateTime.key]).format(
          "YYYY-MM-DD HH:mm:ss",
        );

        return (
          <div key={i} className={"flex flex-col"}>
            <div className={"text-xs opacity-60"}>{label}</div>
            <div>{value}</div>
          </div>
        );
      })}
    </div>
  );
};

BasicInfo.displayName = "BasicInfo";

export default BasicInfo;
