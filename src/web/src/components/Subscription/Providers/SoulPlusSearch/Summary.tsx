import type { SubscriptionProviderSummaryProps } from "../types";
import type { SoulPlusSearchTarget } from "./types";

import React from "react";

const SoulPlusSearchSummary: React.FC<SubscriptionProviderSummaryProps<SoulPlusSearchTarget>> = ({
  target,
}) => {
  if (!target?.url) return null;

  return (
    <div className="flex items-center gap-2 min-w-0">
      <a
        className="text-default-500 text-xs truncate hover:text-primary"
        href={target.url}
        rel="noreferrer noopener"
        target="_blank"
        onClick={(e) => e.stopPropagation()}
      >
        {target.url}
      </a>
      {target.keywords?.length > 0 && (
        <span className="text-default-400 text-xs truncate">{target.keywords.join(", ")}</span>
      )}
    </div>
  );
};

export default SoulPlusSearchSummary;
