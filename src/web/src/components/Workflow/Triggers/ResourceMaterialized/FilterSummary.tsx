import type { ResourceMaterializedFilter } from "./types";
import type { ResourceSource } from "@/sdk/constants";

import React from "react";
import { useTranslation } from "react-i18next";

import { ResourceSourceLabel } from "@/sdk/constants";

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
      {filter.sources.map((id) => ResourceSourceLabel[id as ResourceSource]).join(", ")}
    </span>
  );
};

export default FilterSummary;
