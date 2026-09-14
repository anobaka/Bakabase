import type { WorkflowActivityUI } from "../types";

import { useTranslation } from "react-i18next";

import { WorkflowActivityCategory } from "@/sdk/constants";

const Explanation = () => {
  const { t } = useTranslation();

  return (
    <p className="text-xs leading-relaxed text-default-500">
      {t("workflow.activity.downloaderPrepareResource.description")}
    </p>
  );
};

export const DownloaderPrepareResourceUI: WorkflowActivityUI<Record<string, never>> = {
  kind: "transform.downloader.prepareResource",
  displayNameKey: "workflow.activity.downloaderPrepareResource.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({}),
  parseConfig: () => ({}),
  serializeConfig: () => "{}",
  isValid: () => true,
  ConfigForm: Explanation,
  Summary: () => null,
};
