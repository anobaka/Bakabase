"use client";

"use strict";
import type { ResourceSearchFilterGroup } from "./models";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";

import { GroupCombinator } from "./models";
import FilterGroup from "./FilterGroup";

import { buildLogger } from "@/components/utils";

interface IProps {
  group?: ResourceSearchFilterGroup;
  onChange?: (group: ResourceSearchFilterGroup) => any;
}

const log = buildLogger("FilterGroupsPanel");

export default FilterGroupsPanel;

function FilterGroupsPanel({ group: propsGroup, onChange }: IProps) {
  const { t } = useTranslation();

  const [group, setGroup] = useState<ResourceSearchFilterGroup>(
    propsGroup ?? {
      combinator: GroupCombinator.And,
      disabled: false,
    },
  );

  useEffect(() => {}, []);

  useUpdateEffect(() => {
    setGroup(
      propsGroup ?? {
        combinator: GroupCombinator.And,
        disabled: false,
      },
    );
  }, [propsGroup]);

  return (
    <div className={"group flex flex-wrap gap-2 item-center"}>
      <FilterGroup
        isRoot
        group={group}
        onChange={(group) => {
          setGroup(group);
          onChange?.(group);
        }}
      />
    </div>
  );
}
