import type { AiTransformConfig } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

const Summary: React.FC<{ config: AiTransformConfig }> = ({ config }) => {
  const { t } = useTranslation();
  const targetPart = config.targetItemType
    ? t<string>("workflow.activity.aiTransform.summary.targetPinned", {
        targetItemType: config.targetItemType,
      })
    : t<string>("workflow.activity.aiTransform.summary.targetAuto");
  const customPart = config.extraInstructions
    ? "  ·  " + t<string>("workflow.activity.aiTransform.summary.custom")
    : "";
  return <span className="text-xs text-default-500">{targetPart + customPart}</span>;
};

export default Summary;
