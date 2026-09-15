"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Button, Input } from "@/components/bakaui";
import {
  AcquisitionWaitReason,
  PlacementConflictPolicy,
  WorkflowActivityCategory,
} from "@/sdk/constants";

interface Config {
  libraryRootDirectory: string | null;
  directoryTemplate: string | null;
  onConflict: PlacementConflictPolicy;
}

interface Prompt {
  targetDirectory: string;
  existingEntryCount: number;
}

const DEFAULTS: Config = {
  libraryRootDirectory: null,
  directoryTemplate: null,
  onConflict: PlacementConflictPolicy.Ask,
};

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <Input
        description={t<string>("workflow.acquisition.place.template.description")}
        label={t<string>("workflow.acquisition.place.template.label")}
        size="sm"
        value={value.directoryTemplate ?? ""}
        onValueChange={(v) => onChange({ ...value, directoryTemplate: v || null })}
      />
      <Input
        description={t<string>("workflow.acquisition.place.libraryRoot.description")}
        label={t<string>("workflow.acquisition.place.libraryRoot.label")}
        size="sm"
        value={value.libraryRootDirectory ?? ""}
        onValueChange={(v) => onChange({ ...value, libraryRootDirectory: v || null })}
      />
    </div>
  );
};

/** Something is already there. The three answers are the three things a person might mean. */
const ResumeForm: WorkflowActivityUI<Config>["ResumeForm"] = ({
  promptJson,
  submitting,
  onSubmit,
}) => {
  const { t } = useTranslation();
  const [renameTo, setRenameTo] = React.useState("");

  let prompt: Prompt | null = null;

  try {
    prompt = promptJson ? (JSON.parse(promptJson) as Prompt) : null;
  } catch {
    prompt = null;
  }

  const send = (policy: PlacementConflictPolicy, name?: string) =>
    onSubmit(
      JSON.stringify({
        reason: AcquisitionWaitReason.TargetExists,
        payloadJson: JSON.stringify({ policy, renameTo: name ?? null }),
      }),
    );

  return (
    <div className="flex flex-col gap-3">
      {prompt && (
        <div className="text-sm">
          {t<string>("workflow.acquisition.place.exists", {
            directory: prompt.targetDirectory,
            count: prompt.existingEntryCount,
          })}
        </div>
      )}

      <div className="flex gap-2">
        <Button
          color="primary"
          isDisabled={submitting}
          size="sm"
          onPress={() => send(PlacementConflictPolicy.Rename)}
        >
          {t<string>("workflow.acquisition.place.keepBoth")}
        </Button>
        <Button
          isDisabled={submitting}
          size="sm"
          variant="flat"
          onPress={() => send(PlacementConflictPolicy.Merge)}
        >
          {t<string>("workflow.acquisition.place.merge")}
        </Button>
      </div>

      <div className="flex items-end gap-2">
        <Input
          className="flex-1"
          label={t<string>("workflow.acquisition.place.renameTo")}
          size="sm"
          value={renameTo}
          onValueChange={setRenameTo}
        />
        <Button
          isDisabled={submitting || renameTo.trim().length === 0}
          size="sm"
          onPress={() => send(PlacementConflictPolicy.Merge, renameTo.trim())}
        >
          {t<string>("workflow.acquisition.place.useThisName")}
        </Button>
      </div>
    </div>
  );
};

export const AcquisitionPlaceUI: WorkflowActivityUI<Config> = {
  kind: "acquisition.place",
  displayNameKey: "workflow.acquisition.step.place",
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
  Summary: ({ config }) =>
    config.directoryTemplate ? (
      <span className="text-xs text-default-500">{config.directoryTemplate}</span>
    ) : null,
  ResumeForm,
};
