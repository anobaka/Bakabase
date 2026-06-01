import type { CreateNotificationConfig } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

const Summary: React.FC<{ config: CreateNotificationConfig }> = ({ config }) => {
  const { t } = useTranslation();
  if (!config.title) {
    return (
      <span className="text-xs text-warning-600">
        {t<string>("workflow.activity.createNotification.summary.noTitle")}
      </span>
    );
  }
  return (
    <span className="text-xs text-default-500 truncate">
      {t<string>("workflow.activity.createNotification.summary.with", { title: config.title })}
    </span>
  );
};

export default Summary;
