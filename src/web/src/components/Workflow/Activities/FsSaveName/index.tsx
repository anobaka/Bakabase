import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { WorkflowActivityCategory } from "@/sdk/constants";

/**
 * No configuration in the preview batch — the activity always records a plan. The apply/undo
 * batch will add its mode here.
 */
export type FsSaveNameConfig = Record<string, never>;

const Hint: React.FC = () => {
  const { t } = useTranslation();

  return (
    <span className="text-xs text-default-500">
      {t<string>("workflow.activity.fsSaveName.hint")}
    </span>
  );
};

export const FsSaveNameUI: WorkflowActivityUI<FsSaveNameConfig> = {
  kind: "action.fs.saveName",
  displayNameKey: "workflow.activity.fsSaveName.displayName",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({}),
  parseConfig: () => ({}),
  serializeConfig: () => "{}",
  isValid: () => true,
  ConfigForm: Hint,
  Summary: Hint,
};
