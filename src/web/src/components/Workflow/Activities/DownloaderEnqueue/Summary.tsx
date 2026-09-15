import type { DownloaderEnqueueConfig } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

const Summary: React.FC<{ config: DownloaderEnqueueConfig }> = ({ config }) => {
  const { t } = useTranslation();

  return (
    <span className="text-xs text-default-500">
      {t<string>("workflow.activity.downloaderEnqueue.summary", {
        intervalMs: config.intervalMs,
        autoRetry: config.autoRetry
          ? t<string>("workflow.activity.downloaderEnqueue.autoRetry.on")
          : t<string>("workflow.activity.downloaderEnqueue.autoRetry.off"),
      })}
    </span>
  );
};

export default Summary;
