import type { WorkflowActivityUI } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Input } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";

type Config = { timeoutMinutes: number };
const DEFAULT: Config = { timeoutMinutes: 240 };

const ConfigForm = ({ value, onChange }: { value: Config; onChange: (next: Config) => void }) => {
  const { t } = useTranslation();

  return (
    <Input
      description={t<string>("workflow.acquisition.fetchMagnet.timeout.description")}
      label={t<string>("workflow.acquisition.fetchMagnet.timeout.label")}
      max={43200}
      min={1}
      size="sm"
      type="number"
      value={String(value.timeoutMinutes)}
      onValueChange={(raw) => {
        const next = Number(raw);

        if (Number.isFinite(next)) onChange({ timeoutMinutes: Math.min(43200, Math.max(1, next)) });
      }}
    />
  );
};

export const downloadTimeoutActivityUI = (
  kind: string,
  displayNameKey: string,
): WorkflowActivityUI<Config> => ({
  kind,
  displayNameKey,
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ ...DEFAULT }),
  parseConfig: (json) => {
    try {
      return { ...DEFAULT, ...JSON.parse(json || "{}") };
    } catch {
      return { ...DEFAULT };
    }
  },
  serializeConfig: JSON.stringify,
  isValid: (value) =>
    Number.isFinite(value.timeoutMinutes) &&
    value.timeoutMinutes >= 1 &&
    value.timeoutMinutes <= 43200,
  ConfigForm,
  Summary: () => null,
});
