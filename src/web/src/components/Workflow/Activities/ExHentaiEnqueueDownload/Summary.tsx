import type { ExHentaiEnqueueDownloadConfig } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

const Summary: React.FC<{ config: ExHentaiEnqueueDownloadConfig }> = ({ config }) => {
  const { t } = useTranslation();

  return (
    <span className="text-xs text-default-500">
      {t<string>("workflow.activity.exhentaiEnqueue.summary", {
        intervalMs: config.intervalMs,
        autoRetry: config.autoRetry
          ? t<string>("workflow.activity.exhentaiEnqueue.autoRetry.on")
          : t<string>("workflow.activity.exhentaiEnqueue.autoRetry.off"),
      })}
    </span>
  );
};

export default Summary;
