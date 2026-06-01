import React from "react";

import type { SubscriptionProviderSummaryProps } from "../types";
import type { ExHentaiSearchTarget } from "./types";

const ExHentaiSearchSummary: React.FC<SubscriptionProviderSummaryProps<ExHentaiSearchTarget>> = ({
  target,
}) => {
  if (!target?.url) return null;

  return (
    <a
      className="text-default-500 text-xs truncate hover:text-primary"
      href={target.url}
      rel="noreferrer noopener"
      target="_blank"
      onClick={(e) => e.stopPropagation()}
    >
      {target.url}
    </a>
  );
};

export default ExHentaiSearchSummary;
