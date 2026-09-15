import type { SubscriptionProviderSummaryProps } from "../types";
import type { BangumiSubjectRelationsTarget } from "./types";

import React from "react";

const BangumiSubjectRelationsSummary: React.FC<
  SubscriptionProviderSummaryProps<BangumiSubjectRelationsTarget>
> = ({ target }) => {
  if (!target?.subject) return null;

  return (
    <div className="flex items-center gap-2 min-w-0">
      <span className="text-default-500 text-xs truncate">{target.subject}</span>
      {target.relations?.length > 0 && (
        <span className="text-default-400 text-xs truncate">{target.relations.join(", ")}</span>
      )}
    </div>
  );
};

export default BangumiSubjectRelationsSummary;
