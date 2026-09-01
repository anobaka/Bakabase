"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { MatchModeSelect, TextTypeSelect, typeName, useTextTypes } from "./shared";

import { Spinner } from "@/components/bakaui";
import { TextMatchMode, TextMatchModeLabel, WorkflowActivityCategory } from "@/sdk/constants";

export interface RemoveTextsConfig {
  setTypeId: number;
  mode: TextMatchMode;
}

const EMPTY: RemoveTextsConfig = { setTypeId: 0, mode: TextMatchMode.EqualsAny };

const ConfigForm: React.FC<{
  value: RemoveTextsConfig;
  onChange: (v: RemoveTextsConfig) => void;
}> = ({ value, onChange }) => {
  const { t } = useTranslation();
  const types = useTextTypes();

  if (types === null) return <Spinner size="sm" />;

  return (
    <div className="flex flex-col gap-3">
      <TextTypeSelect
        description={t<string>("workflow.activity.textRemoveTexts.set.description")}
        label={t<string>("workflow.activity.textRemoveTexts.set.label")}
        types={types}
        value={value.setTypeId}
        onChange={(id) => onChange({ ...value, setTypeId: id })}
      />
      <MatchModeSelect value={value.mode} onChange={(mode) => onChange({ ...value, mode })} />
    </div>
  );
};

const Summary: React.FC<{ config: RemoveTextsConfig }> = ({ config }) => {
  const { t } = useTranslation();
  const types = useTextTypes();

  if (!(config.setTypeId > 0)) {
    return <span>{t<string>("workflow.activity.textRemoveTexts.summary.unconfigured")}</span>;
  }

  return (
    <span>
      {t<string>("workflow.activity.textRemoveTexts.summary.configured", {
        set: typeName(types, config.setTypeId),
        mode: TextMatchModeLabel[config.mode],
      })}
    </span>
  );
};

export const TextRemoveTextsUI: WorkflowActivityUI<RemoveTextsConfig> = {
  kind: "transform.text.removeTexts",
  displayNameKey: "workflow.activity.textRemoveTexts.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({ ...EMPTY }),
  parseConfig: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<RemoveTextsConfig>;

      return {
        setTypeId: parsed.setTypeId ?? 0,
        mode: parsed.mode ?? TextMatchMode.EqualsAny,
      };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: (config) => config.setTypeId > 0,
  ConfigForm,
  Summary,
};
