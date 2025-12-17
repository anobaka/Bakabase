"use client";

"use strict";

import type { SearchFilterGroup } from "@/components/ResourceFilter";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";

import {
  GroupCombinator,
  FilterProvider,
  FilterGroup,
  createDefaultFilterConfig,
} from "@/components/ResourceFilter";
import { buildLogger } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface IProps {
  group?: SearchFilterGroup;
  onChange?: (group: SearchFilterGroup) => any;
}

const log = buildLogger("FilterGroupsPanel");

export default FilterGroupsPanel;

function FilterGroupsPanel({ group: propsGroup, onChange }: IProps) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [group, setGroup] = useState<SearchFilterGroup>(
    propsGroup ?? {
      combinator: GroupCombinator.And,
      disabled: false,
    },
  );

  const filterConfig = useMemo(() => createDefaultFilterConfig(createPortal), [createPortal]);

  useUpdateEffect(() => {
    setGroup(
      propsGroup ?? {
        combinator: GroupCombinator.And,
        disabled: false,
      },
    );
  }, [propsGroup]);

  return (
    <FilterProvider config={filterConfig}>
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
    </FilterProvider>
  );
}
