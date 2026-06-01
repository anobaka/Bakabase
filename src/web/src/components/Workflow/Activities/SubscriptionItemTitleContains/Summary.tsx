import type { SubscriptionItemTitleContainsConfig } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

const Summary: React.FC<{ config: SubscriptionItemTitleContainsConfig }> = ({ config }) => {
  const { t } = useTranslation();
  if (config.keywords.length === 0) {
    return (
      <span className="text-xs text-warning-600">
        {t<string>("workflow.activity.subscriptionItemTitleContains.summary.noKeywords")}
      </span>
    );
  }
  const key = config.exclude
    ? "workflow.activity.subscriptionItemTitleContains.summary.exclude"
    : "workflow.activity.subscriptionItemTitleContains.summary.include";
  return (
    <span className="text-xs text-default-500">
      {t<string>(key, { keywords: config.keywords.join(", ") })}
    </span>
  );
};

export default Summary;
