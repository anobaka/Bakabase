"use client";

import type { SearchFilterGroup } from "@/components/ResourceFilter";

import React, { useState } from "react";
import ReactJson from "react-json-view";

import { ResourceSearchPanel } from "@/components/ResourceFilter";
import { GroupCombinator } from "@/components/ResourceFilter";

const ResourceFilterPage = () => {
  const [group, setGroup] = useState<SearchFilterGroup>({
    combinator: GroupCombinator.And,
    disabled: false,
  });

  return (
    <div>
      <div>
        <ResourceSearchPanel group={group} onChange={(g) => setGroup(g)} />
      </div>
      <div>
        <ReactJson collapsed name={"Data"} src={group} theme={"monokai"} />
      </div>
    </div>
  );
};

ResourceFilterPage.displayName = "ResourceFilterPage";

export default ResourceFilterPage;
