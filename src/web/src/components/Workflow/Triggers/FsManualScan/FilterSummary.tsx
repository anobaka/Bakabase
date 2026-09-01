import type { FsManualScanFilter } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { FsScanTarget } from "@/sdk/constants";

const FilterSummary: React.FC<{ filter: FsManualScanFilter }> = ({ filter }) => {
  const { t } = useTranslation();

  const parts = [
    t<string>("workflow.trigger.fsManualScan.summary.roots", { count: filter.roots.length }),
    t<string>(`workflow.trigger.fsManualScan.target.${FsScanTarget[filter.target]}`),
    t<string>("workflow.trigger.fsManualScan.summary.depth", { depth: filter.depth }),
  ];

  if (filter.extensionFilter.length > 0) {
    parts.push(filter.extensionFilter.join(", "));
  }

  return (
    <div className="text-xs text-default-500 flex flex-wrap gap-x-2">
      <span className="break-all">{filter.roots.join(" ｜ ")}</span>
      <span>·</span>
      <span>{parts.join(" · ")}</span>
    </div>
  );
};

export default FilterSummary;
