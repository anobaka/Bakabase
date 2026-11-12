import { AcquisitionStatus } from "@/sdk/constants";
import { isCompleteProcessingPlan } from "@/components/Workflow/ProcessingPlanField";
import type { PlanTaskDetail } from "./models";
import type { RuntimeOverview } from "./runtimeOverview";

/** Presentation only: the resume endpoint revalidates the artifact, review and saved operation. */
export function hasSavedStorageProcessingCheckpoint(detail?: PlanTaskDetail, runtime?: RuntimeOverview): boolean {
  const task = detail?.task;
  if (!detail || !task || task.isDeleted || task.deletedAt || task.status !== AcquisitionStatus.Waiting ||
    !task.workflowRunId || runtime?.taskId !== task.id || runtime.workflowRunId !== task.workflowRunId ||
    runtime.workflowStatus !== "waiting" || runtime.currentNodeKind !== "fs.assessDelivery" ||
    !task.waitPromptJson || task.waitPromptJson.length > 1024 * 1024) return false;
  if (runtime.currentStepIndex != null && detail.steps[runtime.currentStepIndex]?.stage !== "processing")
    return false;
  try {
    const prompt = JSON.parse(task.waitPromptJson);
    if (prompt.deliveryReview !== true ||
      !(prompt.reason === "DiskSpaceLimit" ||
        prompt.reason === "StorageUnavailable" && prompt.message === "LOCAL_STORAGE_RESERVE_LOW") ||
      ![prompt.targetId, prompt.artifactId, prompt.fingerprint, prompt.reviewToken]
        .every(value => typeof value === "string" && value.trim().length > 0) ||
      !detail.plan?.parts.some(part => part.id === prompt.targetId) ||
      typeof prompt.extractionPlanJson !== "string" || !isCompleteProcessingPlan(prompt.extractionPlanJson)) return false;
    const plan = JSON.parse(prompt.extractionPlanJson);
    return plan.requirement === "required" ||
      prompt.reason === "DiskSpaceLimit" && prompt.automaticOuterPackaging === true && plan.requirement === "notRequired";
  } catch { return false; }
}
