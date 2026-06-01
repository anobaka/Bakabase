import type { SubscriptionUpdatedFilter } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

const FilterSummary: React.FC<{ filter: SubscriptionUpdatedFilter }> = ({ filter }) => {
  const { t } = useTranslation();
  const idCount = filter.subscriptionIds.length;
  const kindCount = filter.kinds.length;

  if (idCount === 0 && kindCount === 0) {
    return (
      <span className="text-xs text-default-500">
        {t<string>("workflow.trigger.subscriptionUpdated.summary.matchAll")}
      </span>
    );
  }

  const parts: string[] = [];
  if (idCount > 0) {
    parts.push(t<string>("workflow.trigger.subscriptionUpdated.summary.subscriptionIds", { count: idCount }));
  }
  if (kindCount > 0) {
    parts.push(t<string>("workflow.trigger.subscriptionUpdated.summary.kinds", { count: kindCount }));
  }

  return <span className="text-xs text-default-500">{parts.join(" · ")}</span>;
};

export default FilterSummary;
