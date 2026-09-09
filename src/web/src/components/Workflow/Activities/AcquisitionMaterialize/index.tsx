"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Switch } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";

interface Config {
  cleanWorkingDirectory: boolean;
  notify: boolean;
}

const DEFAULTS: Config = { cleanWorkingDirectory: true, notify: true };

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-2">
      <Switch
        isSelected={value.notify !== false}
        size="sm"
        onValueChange={(notify) => onChange({ ...value, notify })}
      >
        <span className="text-sm">{t<string>("workflow.acquisition.materialize.notify")}</span>
      </Switch>
      <Switch
        isSelected={value.cleanWorkingDirectory !== false}
        size="sm"
        onValueChange={(cleanWorkingDirectory) => onChange({ ...value, cleanWorkingDirectory })}
      >
        <span className="text-sm">{t<string>("workflow.acquisition.materialize.clean")}</span>
      </Switch>
    </div>
  );
};

export const AcquisitionMaterializeUI: WorkflowActivityUI<Config> = {
  kind: "acquisition.materialize",
  displayNameKey: "workflow.acquisition.step.materialize",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ ...DEFAULTS }),
  parseConfig: (json) => {
    try {
      return json ? { ...DEFAULTS, ...(JSON.parse(json) as Partial<Config>) } : { ...DEFAULTS };
    } catch {
      return { ...DEFAULTS };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: () => true,
  ConfigForm,
  Summary: () => null,
};
