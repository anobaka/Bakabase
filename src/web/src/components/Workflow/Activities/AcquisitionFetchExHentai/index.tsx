import type { WorkflowActivityUI } from "../types";

import { useTranslation } from "react-i18next";

import { WorkflowActivityCategory } from "@/sdk/constants";

const Explanation = () => {
  const { t } = useTranslation();

  return (
    <p className="text-xs leading-relaxed text-default-500">
      {t("workflow.acquisition.fetchExHentai.description")}
    </p>
  );
};

export const AcquisitionFetchExHentaiUI: WorkflowActivityUI<Record<string, never>> = {
  kind: "acquisition.fetchExHentai",
  displayNameKey: "workflow.acquisition.step.fetchExHentai",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({}),
  parseConfig: () => ({}),
  serializeConfig: () => "{}",
  isValid: () => true,
  ConfigForm: Explanation,
  Summary: () => null,
};
