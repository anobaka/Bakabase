"use client";

import type { UpstreamActivityRef, WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import {
  FS_SYSTEM_VARIABLES,
  referencedVariables,
  upstreamCapturedVariables,
} from "../../variables";

import { Chip, Input } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";

export interface TemplateConfig {
  template: string;
  requiredVars: string[];
}

const EMPTY: TemplateConfig = { template: "", requiredVars: [] };

const ConfigForm: React.FC<{
  value: TemplateConfig;
  onChange: (v: TemplateConfig) => void;
  upstream?: UpstreamActivityRef[];
}> = ({ value, onChange, upstream }) => {
  const { t } = useTranslation();
  const captured = upstreamCapturedVariables(upstream);
  // The soft contract (capability map E4): captures upstream + the domain's system variables.
  // System vars are listed for fs items; for other domains they simply won't resolve.
  const available = [...captured, ...FS_SYSTEM_VARIABLES.filter((v) => !captured.includes(v))];
  const referenced = referencedVariables(value.template);
  const unknown = referenced.filter((v) => !available.includes(v));

  const insert = (token: string) => onChange({ ...value, template: value.template + token });

  return (
    <div className="flex flex-col gap-3">
      <Input
        className="font-mono"
        description={t<string>("workflow.activity.textTemplate.template.description")}
        label={t<string>("workflow.activity.textTemplate.template.label")}
        value={value.template}
        onValueChange={(s) => onChange({ ...value, template: s })}
      />
      <div className="flex items-center gap-1 flex-wrap text-xs">
        <span className="text-default-500">
          {t<string>("workflow.activity.textTemplate.available.label")}:
        </span>
        {available.map((v) => (
          <Chip
            key={v}
            className="cursor-pointer"
            radius="sm"
            size="sm"
            variant="flat"
            onClick={() => insert(`{var:${v}}`)}
          >
            {v}
          </Chip>
        ))}
        <Chip
          className="cursor-pointer"
          radius="sm"
          size="sm"
          variant="flat"
          onClick={() => insert("{originalText}")}
        >
          originalText
        </Chip>
      </div>
      {unknown.length > 0 && (
        <div className="text-xs text-warning-600">
          {t<string>("workflow.activity.textTemplate.lint.unknown", {
            vars: unknown.join(", "),
          })}
        </div>
      )}
      <Input
        description={t<string>("workflow.activity.textTemplate.requiredVars.description")}
        label={t<string>("workflow.activity.textTemplate.requiredVars.label")}
        value={value.requiredVars.join(", ")}
        onValueChange={(s) =>
          onChange({
            ...value,
            requiredVars: s
              .split(",")
              .map((v) => v.trim())
              .filter((v) => v.length > 0),
          })
        }
      />
    </div>
  );
};

const Summary: React.FC<{ config: TemplateConfig }> = ({ config }) => {
  const { t } = useTranslation();

  if (!config.template) {
    return <span>{t<string>("workflow.activity.textTemplate.summary.unconfigured")}</span>;
  }

  return <span className="font-mono">{config.template}</span>;
};

export const TextTemplateUI: WorkflowActivityUI<TemplateConfig> = {
  kind: "transform.text.template",
  displayNameKey: "workflow.activity.textTemplate.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({ ...EMPTY }),
  parseConfig: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<TemplateConfig>;

      return {
        template: parsed.template ?? "",
        requiredVars: parsed.requiredVars ?? [],
      };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: (config) => config.template.length > 0,
  ConfigForm,
  Summary,
};
