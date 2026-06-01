import type { DownloaderCompletedFilter } from "./types";
import type { ThirdPartyId } from "@/sdk/constants";

import React from "react";
import { useTranslation } from "react-i18next";

import ThirdPartyLabel from "@/components/ThirdPartyLabel";

const FilterSummary: React.FC<{ filter: DownloaderCompletedFilter }> = ({ filter }) => {
  const { t } = useTranslation();
  if (filter.thirdPartyIds.length === 0) {
    return (
      <span className="text-xs text-default-500">
        {t<string>("workflow.trigger.downloaderCompleted.summary.matchAll")}
      </span>
    );
  }
  // Render each pinned source as ThirdPartyLabel for visual consistency with the picker.
  return (
    <div className="flex items-center gap-2 flex-wrap text-xs text-default-500">
      <span>{t<string>("workflow.trigger.downloaderCompleted.summary.only")}</span>
      {filter.thirdPartyIds.map((id) => (
        <ThirdPartyLabel key={id} thirdPartyId={id as ThirdPartyId} />
      ))}
    </div>
  );
};

export default FilterSummary;
