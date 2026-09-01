"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Switch } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";

export interface TrimConfig {
  collapseSpaces: boolean;
  trimEnds: boolean;
  removeEmptyWrappers: boolean;
}

const DEFAULTS: TrimConfig = { collapseSpaces: true, trimEnds: true, removeEmptyWrappers: true };

const SWITCHES: Array<keyof TrimConfig> = ["collapseSpaces", "trimEnds", "removeEmptyWrappers"];

const ConfigForm: React.FC<{ value: TrimConfig; onChange: (v: TrimConfig) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-2">
      {SWITCHES.map((key) => (
        <Switch
          key={key}
          isSelected={value[key]}
          size="sm"
          onValueChange={(b) => onChange({ ...value, [key]: b })}
        >
          {t<string>(`workflow.activity.textTrim.${key}.label`)}
        </Switch>
      ))}
    </div>
  );
};

const Summary: React.FC<{ config: TrimConfig }> = ({ config }) => {
  const { t } = useTranslation();
  const enabled = SWITCHES.filter((key) => config[key]);

  if (enabled.length === 0) {
    return <span>{t<string>("workflow.activity.textTrim.summary.noop")}</span>;
  }

  return (
    <span>
      {enabled.map((key) => t<string>(`workflow.activity.textTrim.${key}.short`)).join(" · ")}
    </span>
  );
};

export const TextTrimUI: WorkflowActivityUI<TrimConfig> = {
  kind: "transform.text.trim",
  displayNameKey: "workflow.activity.textTrim.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({ ...DEFAULTS }),
  parseConfig: (json) => {
    if (!json) return { ...DEFAULTS };
    try {
      const parsed = JSON.parse(json) as Partial<TrimConfig>;

      return {
        collapseSpaces: parsed.collapseSpaces ?? true,
        trimEnds: parsed.trimEnds ?? true,
        removeEmptyWrappers: parsed.removeEmptyWrappers ?? true,
      };
    } catch {
      return { ...DEFAULTS };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  // All-off is a legal (if pointless) configuration; the summary calls it out instead.
  isValid: () => true,
  ConfigForm,
  Summary,
};
