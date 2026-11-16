import type { TFunction } from "i18next";
import { downloadProblemSummary } from "@/components/CloudDownload/problemLabels";
import type { BakabaseServiceComponentsAcquisitionPlanningAcquisitionRuntimeOverview } from "@/sdk/Api";
import { AcquisitionStatus } from "@/sdk/constants";
import { assessmentExplanationFromWait } from "@/components/Workflow/adaptiveAssessmentDiagnostic";
import type { PlanTaskDetail } from "./models";

export type {
  BakabaseServiceComponentsAcquisitionPlanningAcquisitionRuntimeOverview as RuntimeOverview,
  BakabaseServiceComponentsAcquisitionPlanningAcquisitionRuntimeSourceObservation as RuntimeSource,
} from "@/sdk/Api";

/** Never revive a previous wait's model explanation after a retry or another phase has started. */
export function currentProcessingWaitExplanation(
  detail?: PlanTaskDetail,
  overview?: BakabaseServiceComponentsAcquisitionPlanningAcquisitionRuntimeOverview,
): string | undefined {
  const task = detail?.task;
  if (!task || !overview || task.status !== AcquisitionStatus.Waiting ||
      overview.workflowStatus !== "waiting" || overview.taskId !== task.id ||
      !task.workflowRunId || overview.workflowRunId !== task.workflowRunId ||
      task.currentStepIndex == null || task.currentStepIndex !== overview.currentStepIndex ||
      !["acquisition.plan.process", "fs.assessDelivery"].includes(overview.currentNodeKind ?? "") ||
      detail?.steps[task.currentStepIndex]?.kind !== overview.currentNodeKind ||
      !task.waitPromptJson || task.waitPromptJson.length > 1024 * 1024) return;
  try {
    const prompt = JSON.parse(task.waitPromptJson);
    if (!prompt || typeof prompt !== "object" || Array.isArray(prompt) ||
        typeof prompt.targetId !== "string" ||
        !detail.plan?.parts.some(part => part.id === prompt.targetId)) return;
    const code = overview.problemReasonCode ?? overview.problemCode;
    if (code && code !== "SOURCE_ATTENTION_REQUIRED" && code !== prompt.reason) return;
    return assessmentExplanationFromWait(prompt);
  } catch {
    return;
  }
}

export function hasCurrentRuntimeProblem(
  overview?: BakabaseServiceComponentsAcquisitionPlanningAcquisitionRuntimeOverview,
): boolean {
  return !!(overview?.problemCode || overview?.problemReasonCode || overview?.problem ||
    overview?.attentionTags?.length);
}

export function downloadContinuationLabel(
  t: TFunction,
  overview?: BakabaseServiceComponentsAcquisitionPlanningAcquisitionRuntimeOverview,
): string | undefined {
  if (!overview?.downloadsCompletedAwaitingWorkflow) return undefined;
  if (hasCurrentRuntimeProblem(overview))
    return t<string>("acquisition.runtime.downloadContinuation.blocked");
  const mode = ["automatic", "downloadsOnly", "paused"].includes(overview.downloadContinuation ?? "")
    ? overview.downloadContinuation
    : "manual";
  return t<string>(`acquisition.runtime.downloadContinuation.${mode}`);
}

/** Translate only known reason keys; retain a readable first line for unfamiliar diagnostics. */
export function runtimeProblemLabel(
  t: TFunction,
  code?: string | null,
  message?: string | null,
): string | undefined {
  if (code) {
    for (const prefix of [
      "acquisition.runtime.problem",
      "acquisition.plan.replan.error",
      "acquisition.directories.error",
      "acquisition.recovery.reason",
      "acquisition.plan.issue",
      "acquire.requirement",
      "sourceAvailability.reason",
      "cloudDownload.problemCode",
      "cloudDownload.problem",
      "rclone.problem",
      "workflow.metadata.warning",
    ]) {
      const key = `${prefix}.${code}`;
      const label = t<string>(key, { defaultValue: "" });
      if (label && label !== key) return label;
    }
  }
  if (message) return downloadProblemSummary(message, t) ?? message.split(/\r?\n/, 1)[0];
  return code || undefined;
}
