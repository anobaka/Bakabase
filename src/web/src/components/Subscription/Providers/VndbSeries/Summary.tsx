import type { SubscriptionProviderSummaryProps } from "../types";
import type { VndbSeriesTarget } from "./types";

import React from "react";

const VndbSeriesSummary: React.FC<SubscriptionProviderSummaryProps<VndbSeriesTarget>> = ({
  target,
}) => {
  if (!target?.visualNovel) return null;

  return (
    <div className="flex items-center gap-2 min-w-0">
      <span className="text-default-500 text-xs truncate">{target.visualNovel}</span>
      {target.relations?.length > 0 && (
        <span className="text-default-400 text-xs truncate">{target.relations.join(", ")}</span>
      )}
    </div>
  );
};

export default VndbSeriesSummary;
