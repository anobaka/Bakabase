import { useState } from "react";
import { Button } from "@/components/bakaui";
import DeliveryFileSelection from "./DeliveryFileSelection";
import IncompleteArchiveGroupRecovery, { isArchiveGroupWait } from "./IncompleteArchiveGroupRecovery";
import { useTranslation } from "react-i18next";
import { WorkflowActivityCategory } from "@/sdk/constants";
import type { WorkflowActivityUI } from "./types";
import { fileProcessingExecutionLabels } from "./executionLabels";
import { ProcessFileArtifactsUI } from "./CloudDownload/ProcessFiles";

type ProcessingResumeForm = NonNullable<WorkflowActivityUI<Record<string, unknown>>["ResumeForm"]>;

export const DeliveryReviewResumeForm: ProcessingResumeForm = ({ promptJson, submitting, onSubmit, fileTask, runId }) => {
  const { t } = useTranslation();
  const [choosing, setChoosing] = useState<string>();
  let prompt: { artifactId?: string; fingerprint?: string; reviewToken?: string; targetId?: string;
    reason?: string; allowedArchivePolicies?: string[] } = {};
  try { prompt = JSON.parse(promptJson || "{}"); } catch { /* Wait for a valid stored prompt. */ }
  if (!prompt || typeof prompt.artifactId !== "string" || typeof prompt.fingerprint !== "string" || typeof prompt.reviewToken !== "string")
    return <p className="text-sm text-warning-600">{t("cloudDownload.deliveryReview.reload")}</p>;
  const Form = ProcessFileArtifactsUI.ResumeForm!;
  const { artifactId, fingerprint, reviewToken, targetId } = prompt;
  const identity = JSON.stringify([runId, targetId, artifactId, fingerprint, reviewToken]);
  const canSelect = !!runId && !!targetId && prompt.allowedArchivePolicies?.includes("keep") &&
    (prompt.reason === "FILE_DELIVERY_RESULT_UNCERTAIN" || prompt.reason === "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED");
  if (canSelect && choosing === identity)
    return <DeliveryFileSelection key={identity} runId={runId} artifactId={artifactId} submitting={submitting}
      onCancel={() => setChoosing(undefined)}
      onConfirm={excludedFiles => onSubmit(JSON.stringify({ artifactId, fingerprint, reviewToken,
        processing: { targetId, archivePolicy: "keep" }, excludedFiles: excludedFiles.length ? excludedFiles : undefined }))} />;
  return <div className="space-y-2">
    {canSelect && <Button size="sm" variant="flat" isDisabled={submitting} onPress={() => setChoosing(identity)}>
      {t("cloudDownload.deliveryReview.selectFiles")}
    </Button>}
    <Form runId={runId} fileTask={fileTask} promptJson={promptJson} submitting={submitting}
      onSubmit={value => onSubmit(JSON.stringify({ artifactId, fingerprint, reviewToken, processing: JSON.parse(value) }))} />
  </div>;
};

/** Configuration is compiled from an acquisition plan; recovery uses the shared artifact contract. */
export const AcquisitionPlanProcessUI: WorkflowActivityUI<Record<string, unknown>> = {
  kind: "acquisition.plan.process",
  executionLabels: fileProcessingExecutionLabels,
  displayNameKey: "acquisition.plan.processingStep",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({}),
  parseConfig: (json) => {
    try {
      return JSON.parse(json);
    } catch {
      return {};
    }
  },
  serializeConfig: JSON.stringify,
  isValid: () => false,
  ConfigForm: () => {
    const { t } = useTranslation();
    return <p className="text-sm text-default-500">{t("acquisition.plan.generatedConfig")}</p>;
  },
  Summary: () => {
    const { t } = useTranslation();
    return <span className="text-xs text-default-500">{t("acquisition.plan.processingStep")}</span>;
  },
  ResumeForm: (props) => {
    let prompt;
    try { prompt = JSON.parse(props.promptJson || "{}"); } catch { /* Use the shared processing form. */ }
    if (!prompt?.deliveryReview && isArchiveGroupWait(prompt))
      return <IncompleteArchiveGroupRecovery key={props.promptJson} prompt={prompt}
        submitting={props.submitting} onSubmit={props.onSubmit} />;
    const Form = prompt?.deliveryReview === true ? DeliveryReviewResumeForm : ProcessFileArtifactsUI.ResumeForm!;
    return <Form {...props} />;
  },
};

export const AcquisitionPlanContinueFilesUI: WorkflowActivityUI<Record<string, unknown>> = {
  ...AcquisitionPlanProcessUI,
  kind: "acquisition.plan.continueFiles",
  displayNameKey: "acquisition.plan.continueFiles",
  category: WorkflowActivityCategory.Transform,
  ResumeForm: undefined,
  Summary: () => {
    const { t } = useTranslation();
    return <span className="text-xs text-default-500">{t("acquisition.plan.continueFiles")}</span>;
  },
};
