"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { WorkflowActivityCategory } from "@/sdk/constants";

interface Config {
  markIds?: number[];
}

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = () => {
  const { t } = useTranslation();

  // Nothing to configure yet: the chain does not know which marks cover the folder it wrote to,
  // so "whatever is pending" is both the default and the only useful answer today.
  return (
    <span className="text-xs text-default-500">
      {t<string>("workflow.pathmark.enqueueSync.description")}
    </span>
  );
};

export const PathMarkEnqueueSyncUI: WorkflowActivityUI<Config> = {
  kind: "action.pathmark.enqueueSync",
  displayNameKey: "workflow.pathmark.enqueueSync.displayName",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({}),
  parseConfig: (json) => {
    try {
      return json ? (JSON.parse(json) as Config) : {};
    } catch {
      return {};
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: () => true,
  ConfigForm,
  Summary: () => null,
};
