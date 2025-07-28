"use client";

import type { OnDeleteMatcherValue } from "../models";
import type { IPscPropertyMatcherValue } from "../models/PscPropertyMatcherValue";
import type { SimpleGlobalMatchResult } from "../models/PscContext";

import React from "react";
import { useTranslation } from "react-i18next";
import { DeleteOutlined } from "@ant-design/icons";

import { PscMatcherValue } from "../models/PscMatcherValue";

import { Button, Chip } from "@/components/bakaui";

type Props = {
  matches: SimpleGlobalMatchResult[];
  value: IPscPropertyMatcherValue[];
  onDeleteMatcherValue: OnDeleteMatcherValue;
};
const GlobalMatches = ({ matches, value, onDeleteMatcherValue }: Props) => {
  const { t } = useTranslation();

  if (matches.length > 0) {
    return (
      <div className={"flex flex-col mt-2"}>
        <div className="font-bold mb-1">{t<string>("Global matches")}</div>
        {matches.map((gm) => {
          const v = value?.filter((v) => v.property.equals(gm.property))?.[
            gm.valueIndex ?? 0
          ]?.value;

          return (
            <div className={"flex gap-2 items-center"}>
              <Chip radius={"sm"} size={"sm"}>
                {gm.property.toString(t, gm.valueIndex)}
              </Chip>
              {v && <span>{PscMatcherValue.ToString(t, v)}</span>}
              {t<string>("Matched {{count}} results", {
                count: gm.matches.length,
              })}
              {gm.matches.map((m) => (
                <Chip radius={"sm"} size={"sm"}>
                  {m}
                </Chip>
              ))}
              <Button
                isIconOnly
                color={"danger"}
                size={"sm"}
                variant={"light"}
                onClick={() => {
                  onDeleteMatcherValue(gm.property, gm.valueIndex);
                }}
              >
                <DeleteOutlined className={"text-base"} />
              </Button>
            </div>
          );
        })}
      </div>
    );
  }

  return null;
};

GlobalMatches.displayName = "GlobalMatches";

export default GlobalMatches;
