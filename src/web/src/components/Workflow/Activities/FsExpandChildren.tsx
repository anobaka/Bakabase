"use client";

import type { WorkflowActivityUI } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Input, Select, Switch } from "@/components/bakaui";
import { FsScanTarget, WorkflowActivityCategory } from "@/sdk/constants";

export interface FsExpandChildrenConfig {
  target: FsScanTarget;
  extensionFilter: string[];
  includeSelf: boolean;
}

const EMPTY: FsExpandChildrenConfig = {
  target: FsScanTarget.Files,
  extensionFilter: [],
  includeSelf: false,
};

const TARGETS = [FsScanTarget.Files, FsScanTarget.Directories, FsScanTarget.Both];

const ConfigForm: React.FC<{
  value: FsExpandChildrenConfig;
  onChange: (v: FsExpandChildrenConfig) => void;
}> = ({ value, onChange }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <Select
        dataSource={TARGETS.map((v) => ({
          value: String(v),
          label: t<string>(`workflow.trigger.fsManualScan.target.${FsScanTarget[v]}`),
        }))}
        label={t<string>("workflow.activity.fsExpandChildren.target.label")}
        selectedKeys={[String(value.target)]}
        onSelectionChange={(keys) => {
          const next = Array.from(keys)[0] as string | undefined;

          if (next) onChange({ ...value, target: parseInt(next, 10) as FsScanTarget });
        }}
      />
      <Input
        description={t<string>("workflow.trigger.fsManualScan.extensions.description")}
        label={t<string>("workflow.trigger.fsManualScan.extensions.label")}
        value={value.extensionFilter.join(", ")}
        onValueChange={(s) =>
          onChange({
            ...value,
            extensionFilter: s
              .split(/[\s,]+/)
              .map((e) => e.trim().replace(/^\./, ""))
              .filter((e) => e.length > 0),
          })
        }
      />
      <Switch
        isSelected={value.includeSelf}
        size="sm"
        onValueChange={(b) => onChange({ ...value, includeSelf: b })}
      >
        {t<string>("workflow.activity.fsExpandChildren.includeSelf.label")}
      </Switch>
    </div>
  );
};

const Summary: React.FC<{ config: FsExpandChildrenConfig }> = ({ config }) => {
  const { t } = useTranslation();
  const parts = [
    t<string>(`workflow.trigger.fsManualScan.target.${FsScanTarget[config.target]}`),
    config.extensionFilter.length > 0 ? config.extensionFilter.join(", ") : null,
    config.includeSelf
      ? t<string>("workflow.activity.fsExpandChildren.summary.includeSelf")
      : null,
  ].filter((p): p is string => !!p);

  return (
    <span>
      {t<string>("workflow.activity.fsExpandChildren.summary.text", {
        detail: parts.join(" · "),
      })}
    </span>
  );
};

export const FsExpandChildrenUI: WorkflowActivityUI<FsExpandChildrenConfig> = {
  kind: "transform.fs.expandChildren",
  displayNameKey: "workflow.activity.fsExpandChildren.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({ ...EMPTY }),
  parseConfig: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<FsExpandChildrenConfig>;

      return {
        target: parsed.target ?? FsScanTarget.Files,
        extensionFilter: parsed.extensionFilter ?? [],
        includeSelf: parsed.includeSelf ?? false,
      };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: () => true,
  ConfigForm,
  Summary,
};
