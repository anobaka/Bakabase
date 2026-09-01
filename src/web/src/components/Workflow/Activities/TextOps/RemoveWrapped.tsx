"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { MatchModeSelect, TextTypeSelect, typeName, useTextTypes } from "./shared";

import { Spinner } from "@/components/bakaui";
import { TextMatchMode, TextMatchModeLabel, TextTypeShape, WorkflowActivityCategory } from "@/sdk/constants";

export interface RemoveWrappedConfig {
  wrappersTypeId: number;
  setTypeId: number;
  mode: TextMatchMode;
}

const EMPTY: RemoveWrappedConfig = { wrappersTypeId: 0, setTypeId: 0, mode: TextMatchMode.EqualsAny };

const ConfigForm: React.FC<{
  value: RemoveWrappedConfig;
  onChange: (v: RemoveWrappedConfig) => void;
}> = ({ value, onChange }) => {
  const { t } = useTranslation();
  const types = useTextTypes();

  if (types === null) return <Spinner size="sm" />;

  return (
    <div className="flex flex-col gap-3">
      <TextTypeSelect
        description={t<string>("workflow.activity.textRemoveWrapped.wrappers.description")}
        label={t<string>("workflow.activity.textRemoveWrapped.wrappers.label")}
        shape={TextTypeShape.DelimiterPair}
        types={types}
        value={value.wrappersTypeId}
        onChange={(id) => onChange({ ...value, wrappersTypeId: id })}
      />
      <TextTypeSelect
        description={t<string>("workflow.activity.textRemoveWrapped.set.description")}
        label={t<string>("workflow.activity.textRemoveWrapped.set.label")}
        types={types}
        value={value.setTypeId}
        onChange={(id) => onChange({ ...value, setTypeId: id })}
      />
      <MatchModeSelect value={value.mode} onChange={(mode) => onChange({ ...value, mode })} />
    </div>
  );
};

const Summary: React.FC<{ config: RemoveWrappedConfig }> = ({ config }) => {
  const { t } = useTranslation();
  const types = useTextTypes();

  if (!(config.wrappersTypeId > 0) || !(config.setTypeId > 0)) {
    return <span>{t<string>("workflow.activity.textRemoveWrapped.summary.unconfigured")}</span>;
  }

  return (
    <span>
      {t<string>("workflow.activity.textRemoveWrapped.summary.configured", {
        wrappers: typeName(types, config.wrappersTypeId),
        set: typeName(types, config.setTypeId),
        mode: TextMatchModeLabel[config.mode],
      })}
    </span>
  );
};

export const TextRemoveWrappedUI: WorkflowActivityUI<RemoveWrappedConfig> = {
  kind: "transform.text.removeWrapped",
  displayNameKey: "workflow.activity.textRemoveWrapped.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({ ...EMPTY }),
  parseConfig: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<RemoveWrappedConfig>;

      return {
        wrappersTypeId: parsed.wrappersTypeId ?? 0,
        setTypeId: parsed.setTypeId ?? 0,
        mode: parsed.mode ?? TextMatchMode.EqualsAny,
      };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: (config) => config.wrappersTypeId > 0 && config.setTypeId > 0,
  ConfigForm,
  Summary,
};
