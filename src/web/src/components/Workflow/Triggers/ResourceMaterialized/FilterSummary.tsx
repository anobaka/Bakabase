import type { ResourceMaterializedFilter } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { ResourceSource } from "@/sdk/constants";

const FilterSummary: React.FC<{ filter: ResourceMaterializedFilter }> = ({ filter }) => {
  const { t } = useTranslation();

  if (filter.sources.length === 0) {
    return (
      <span className="text-xs text-default-500">
        {t<string>("workflow.trigger.resourceMaterialized.summary.matchAll")}
      </span>
    );
  }

  return (
    <span className="text-xs text-default-500">
      {t<string>("workflow.trigger.resourceMaterialized.summary.only")}&nbsp;
      {filter.sources.map((id) => ResourceSource[id]).join(", ")}
    </span>
  );
};

export default FilterSummary;
