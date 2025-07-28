"use client";

import type { ResourceSearchFilterGroup } from "@/pages/resource/components/FilterPanel/FilterGroupsPanel/models";

import React, { useState } from "react";
import ReactJson from "react-json-view";

import { GroupCombinator } from "@/pages/resource/components/FilterPanel/FilterGroupsPanel/models";
import FilterGroupsPanel from "@/pages/resource/components/FilterPanel/FilterGroupsPanel";
const ResourceFilterPage = () => {
  const [group, setGroup] = useState<ResourceSearchFilterGroup>({
    combinator: GroupCombinator.And,
  });

  return (
    <div>
      <div>
        <FilterGroupsPanel group={group} onChange={(g) => setGroup(g)} />
      </div>
      <div>
        <ReactJson collapsed name={"Data"} src={group} theme={"monokai"} />
      </div>
    </div>
  );
};

ResourceFilterPage.displayName = "ResourceFilterPage";

export default ResourceFilterPage;
