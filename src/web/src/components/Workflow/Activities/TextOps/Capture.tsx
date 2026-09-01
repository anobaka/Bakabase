"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { namedGroupsOf } from "../../variables";

import { Chip, Input, Select } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";

/** Mirrors TextCaptureActivity.CaptureMissBehavior. */
export enum CaptureMissBehavior {
  Ignore = 1,
  Fail = 2,
}

export interface CaptureConfig {
  pattern: string;
  onMiss: CaptureMissBehavior;
}

const EMPTY: CaptureConfig = { pattern: "", onMiss: CaptureMissBehavior.Ignore };

const ConfigForm: React.FC<{
  value: CaptureConfig;
  onChange: (v: CaptureConfig) => void;
}> = ({ value, onChange }) => {
  const { t } = useTranslation();
  const groups = namedGroupsOf(value.pattern);

  return (
    <div className="flex flex-col gap-3">
      <Input
        className="font-mono"
        description={t<string>("workflow.activity.textCapture.pattern.description")}
        label={t<string>("workflow.activity.textCapture.pattern.label")}
        value={value.pattern}
        onValueChange={(s) => onChange({ ...value, pattern: s })}
      />
      <div className="flex items-center gap-1 flex-wrap text-xs">
        <span className="text-default-500">
          {t<string>("workflow.activity.textCapture.groups.label")}:
        </span>
        {groups.length === 0 ? (
          <span className="text-warning-600">
            {t<string>("workflow.activity.textCapture.groups.none")}
          </span>
        ) : (
          groups.map((g) => (
            <Chip key={g} radius="sm" size="sm" variant="flat">
              {g}
            </Chip>
          ))
        )}
      </div>
      <Select
        dataSource={[
          {
            value: String(CaptureMissBehavior.Ignore),
            label: t<string>("workflow.activity.textCapture.onMiss.Ignore"),
          },
          {
            value: String(CaptureMissBehavior.Fail),
            label: t<string>("workflow.activity.textCapture.onMiss.Fail"),
          },
        ]}
        label={t<string>("workflow.activity.textCapture.onMiss.label")}
        selectedKeys={[String(value.onMiss)]}
        onSelectionChange={(keys) => {
          const next = Array.from(keys)[0] as string | undefined;

          if (next) onChange({ ...value, onMiss: parseInt(next, 10) as CaptureMissBehavior });
        }}
      />
    </div>
  );
};

const Summary: React.FC<{ config: CaptureConfig }> = ({ config }) => {
  const { t } = useTranslation();
  const groups = namedGroupsOf(config.pattern);

  if (!config.pattern) {
    return <span>{t<string>("workflow.activity.textCapture.summary.unconfigured")}</span>;
  }

  return (
    <span>
      {groups.length > 0
        ? t<string>("workflow.activity.textCapture.summary.configured", {
            vars: groups.join(", "),
          })
        : t<string>("workflow.activity.textCapture.summary.noGroups")}
    </span>
  );
};

export const TextCaptureUI: WorkflowActivityUI<CaptureConfig> = {
  kind: "transform.text.capture",
  displayNameKey: "workflow.activity.textCapture.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({ ...EMPTY }),
  parseConfig: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<CaptureConfig>;

      return {
        pattern: parsed.pattern ?? "",
        onMiss: parsed.onMiss ?? CaptureMissBehavior.Ignore,
      };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: (config) => config.pattern.length > 0 && namedGroupsOf(config.pattern).length > 0,
  ConfigForm,
  Summary,
};
