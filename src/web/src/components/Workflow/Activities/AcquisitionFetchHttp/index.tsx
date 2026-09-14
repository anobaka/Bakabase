import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Input } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";

interface Config {
  timeoutMinutes: number;
  parallelConnections: number;
  maxRetries: number;
  speedLimitKiB: number;
}

const DEFAULT: Config = {
  timeoutMinutes: 240,
  parallelConnections: 4,
  maxRetries: 3,
  speedLimitKiB: 0,
};
const FIELDS = [
  { key: "parallelConnections", min: 1, max: 16, error: "httpConnectionsInvalid" },
  { key: "maxRetries", min: 0, max: 10, error: "httpRetriesInvalid" },
  { key: "speedLimitKiB", min: 0, max: 1048576, error: "httpSpeedLimitInvalid" },
  { key: "timeoutMinutes", min: 1, max: 43200, error: "timeoutInvalid" },
] as const;
const inRange = (value: number, min: number, max: number) =>
  Number.isInteger(value) && value >= min && value <= max;

const ConfigForm = ({ value, onChange }: { value: Config; onChange: (next: Config) => void }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <div className="grid grid-cols-1 gap-3">
        {FIELDS.map(({ key, min, max, error }) => (
          <Input
            key={key}
            description={t<string>(`workflow.acquisition.fetchHttp.${key}.description`)}
            errorMessage={t<string>(`workflow.validation.acquisition.${error}`)}
            isInvalid={!inRange(value[key], min, max)}
            label={t<string>(`workflow.acquisition.fetchHttp.${key}.label`)}
            max={max}
            min={min}
            size="sm"
            step={1}
            type="number"
            value={Number.isFinite(value[key]) ? String(value[key]) : ""}
            onValueChange={(raw) =>
              onChange({ ...value, [key]: raw.trim() === "" ? Number.NaN : Number(raw) })
            }
          />
        ))}
      </div>
      <p className="rounded-lg bg-default-100/60 p-3 text-xs leading-relaxed text-default-500">
        {t<string>("workflow.acquisition.fetchHttp.automaticTransferHelp")}
      </p>
    </div>
  );
};

export const AcquisitionFetchHttpUI: WorkflowActivityUI<Config> = {
  kind: "acquisition.fetchHttp",
  displayNameKey: "workflow.acquisition.step.fetchHttp",
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
  isValid: (value) => FIELDS.every(({ key, min, max }) => inRange(value[key], min, max)),
  ConfigForm,
  Summary: () => null,
};
