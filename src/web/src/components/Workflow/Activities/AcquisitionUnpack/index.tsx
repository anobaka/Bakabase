"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Button, Chip, Input, NumberInput, Switch } from "@/components/bakaui";
import { AcquisitionWaitReason, WorkflowActivityCategory } from "@/sdk/constants";

interface Config {
  deleteArchive: boolean;
  maxDepth: number;
  tryRecentPasswords: boolean | null;
}

interface Prompt {
  archiveName: string;
  tried: string[];
}

const DEFAULTS: Config = { deleteArchive: true, maxDepth: 2, tryRecentPasswords: null };

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <Switch
        isSelected={value.deleteArchive !== false}
        size="sm"
        onValueChange={(deleteArchive) => onChange({ ...value, deleteArchive })}
      >
        <span className="text-sm">{t<string>("workflow.acquisition.unpack.deleteArchive")}</span>
      </Switch>
      <NumberInput
        description={t<string>("workflow.acquisition.unpack.maxDepth.description")}
        label={t<string>("workflow.acquisition.unpack.maxDepth.label")}
        minValue={1}
        size="sm"
        value={value.maxDepth ?? 2}
        onValueChange={(maxDepth) => onChange({ ...value, maxDepth })}
      />
    </div>
  );
};

/** Nothing known opened it. What is left is to be told the password. */
const ResumeForm: WorkflowActivityUI<Config>["ResumeForm"] = ({
  promptJson,
  submitting,
  onSubmit,
}) => {
  const { t } = useTranslation();
  const [password, setPassword] = React.useState("");

  let prompt: Prompt | null = null;

  try {
    prompt = promptJson ? (JSON.parse(promptJson) as Prompt) : null;
  } catch {
    prompt = null;
  }

  return (
    <div className="flex flex-col gap-3">
      {prompt?.archiveName && (
        <div className="text-sm">
          {t<string>("workflow.acquisition.unpack.needPassword", { name: prompt.archiveName })}
        </div>
      )}

      {(prompt?.tried?.length ?? 0) > 0 && (
        <div className="flex flex-wrap items-center gap-1">
          <span className="text-xs text-default-400">
            {t<string>("workflow.acquisition.unpack.alreadyTried")}
          </span>
          {prompt!.tried.map((p) => (
            <Chip key={p} size="sm" variant="flat">
              {p}
            </Chip>
          ))}
        </div>
      )}

      <div className="flex items-end gap-2">
        <Input
          className="flex-1"
          label={t<string>("workflow.acquisition.unpack.password")}
          size="sm"
          value={password}
          onValueChange={setPassword}
        />
        <Button
          color="primary"
          isDisabled={submitting || password.length === 0}
          size="sm"
          onPress={() =>
            onSubmit(
              JSON.stringify({
                reason: AcquisitionWaitReason.PasswordUnknown,
                payloadJson: JSON.stringify({ password }),
              }),
            )
          }
        >
          {t<string>("workflow.acquisition.unpack.tryIt")}
        </Button>
      </div>
    </div>
  );
};

export const AcquisitionUnpackUI: WorkflowActivityUI<Config> = {
  kind: "acquisition.unpack",
  displayNameKey: "workflow.acquisition.step.unpack",
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
  ResumeForm,
};
