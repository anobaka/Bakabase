"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Button, Input } from "@/components/bakaui";
import { AcquisitionWaitReason, WorkflowActivityCategory } from "@/sdk/constants";

/** No configuration: the whole step is the question it asks. */
const Nothing: React.FC = () => null;

const ResumeForm: WorkflowActivityUI<Record<string, never>>["ResumeForm"] = ({
  submitting,
  onSubmit,
}) => {
  const { t } = useTranslation();
  const [directory, setDirectory] = React.useState("");

  return (
    <div className="flex items-end gap-2">
      <Input
        className="flex-1"
        description={t<string>("workflow.acquisition.pickDirectory.description")}
        label={t<string>("workflow.acquisition.pickDirectory.label")}
        size="sm"
        value={directory}
        onValueChange={setDirectory}
      />
      <Button
        color="primary"
        isDisabled={submitting || directory.trim().length === 0}
        size="sm"
        onPress={() =>
          onSubmit(
            JSON.stringify({
              reason: AcquisitionWaitReason.PickDirectory,
              payloadJson: JSON.stringify({ directory: directory.trim() }),
            }),
          )
        }
      >
        {t<string>("workflow.acquisition.pickDirectory.use")}
      </Button>
    </div>
  );
};

export const AcquisitionPickDirectoryUI: WorkflowActivityUI<Record<string, never>> = {
  kind: "acquisition.pickLocalDirectory",
  displayNameKey: "workflow.acquisition.step.pickLocalDirectory",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({}),
  parseConfig: () => ({}),
  serializeConfig: () => "{}",
  isValid: () => true,
  ConfigForm: Nothing,
  Summary: Nothing,
  ResumeForm,
};
