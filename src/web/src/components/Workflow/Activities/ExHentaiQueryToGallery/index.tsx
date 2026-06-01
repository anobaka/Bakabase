import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { WorkflowActivityCategory } from "@/sdk/constants";

// No user-configurable options — the node just runs the incoming query and takes the first
// gallery. A tiny note keeps the expanded card from looking broken.
const ConfigForm: React.FC = () => {
  const { t } = useTranslation();
  return (
    <span className="text-xs text-default-500">
      {t<string>("workflow.activity.exhentaiQueryToGallery.noOptions")}
    </span>
  );
};

const Summary: React.FC = () => {
  const { t } = useTranslation();
  return (
    <span className="text-xs text-default-500">
      {t<string>("workflow.activity.exhentaiQueryToGallery.summary")}
    </span>
  );
};

export const ExHentaiQueryToGalleryUI: WorkflowActivityUI<Record<string, never>> = {
  kind: "transform.exhentai.queryToGallery",
  displayNameKey: "workflow.activity.exhentaiQueryToGallery.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({}),
  parseConfig: () => ({}),
  serializeConfig: () => "{}",
  isValid: () => true,
  ConfigForm,
  Summary,
};
