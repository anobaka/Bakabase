import { useTranslation } from "react-i18next";
import { WorkflowActivityCategory } from "@/sdk/constants";
import type { WorkflowActivityUI } from "./types";
import { fileProcessingExecutionLabels } from "./executionLabels";
import { ProcessFileArtifactsUI } from "./CloudDownload/ProcessFiles";

type ProcessingResumeForm = NonNullable<WorkflowActivityUI<Record<string, unknown>>["ResumeForm"]>;

export const DeliveryReviewResumeForm: ProcessingResumeForm = ({ promptJson, submitting, onSubmit, fileTask }) => {
  const { t } = useTranslation();
  let prompt: { artifactId?: string; fingerprint?: string; reviewToken?: string } = {};
  try { prompt = JSON.parse(promptJson || "{}"); } catch { /* Wait for a valid stored prompt. */ }
  if (!prompt || typeof prompt.artifactId !== "string" || typeof prompt.fingerprint !== "string" || typeof prompt.reviewToken !== "string")
    return <p className="text-sm text-warning-600">{t("cloudDownload.deliveryReview.reload")}</p>;
  const Form = ProcessFileArtifactsUI.ResumeForm!;
  return <Form fileTask={fileTask} promptJson={promptJson} submitting={submitting} onSubmit={(value) => onSubmit(JSON.stringify({
    artifactId: prompt.artifactId, fingerprint: prompt.fingerprint, reviewToken: prompt.reviewToken, processing: JSON.parse(value),
  }))} />;
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
    let deliveryReview = false;
    try { deliveryReview = JSON.parse(props.promptJson || "{}")?.deliveryReview === true; } catch { /* Use shared processing form. */ }
    const Form = deliveryReview ? DeliveryReviewResumeForm : ProcessFileArtifactsUI.ResumeForm!;
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
