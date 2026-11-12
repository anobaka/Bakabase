import { expect, it } from "vitest";
import { AcquisitionStatus } from "@/sdk/constants";
import { hasSavedStorageProcessingCheckpoint } from "../savedProcessingCheckpoint";

function fixture() {
  const prompt: any = { targetId: "part", artifactId: "artifact", fingerprint: "fingerprint", reviewToken: "review",
    deliveryReview: true, reason: "StorageUnavailable", message: "LOCAL_STORAGE_RESERVE_LOW",
    extractionPlanJson: JSON.stringify({ requirement: "required", steps: [{ id: "extract", op: "extractArchive", input: "input", selector: "*.7z" }] }) };
  const detail: any = { task: { id: 42, workflowRunId: 75, status: AcquisitionStatus.Waiting },
    plan: { parts: [{ id: "part" }] }, steps: [{ stage: "processing" }] };
  const runtime: any = { taskId: 42, workflowRunId: 75, workflowStatus: "waiting", currentNodeKind: "fs.assessDelivery", currentStepIndex: 0 };
  return { prompt, detail, runtime, check: () => hasSavedStorageProcessingCheckpoint({ ...detail,
    task: { ...detail.task, waitPromptJson: JSON.stringify(prompt) } }, runtime) };
}
it.each(["reserve", "DiskSpaceLimit", "automaticOuter"])("prefers the saved %s processing checkpoint", mode => {
  const value = fixture();
  if (mode !== "reserve") value.prompt.reason = "DiskSpaceLimit";
  if (mode === "automaticOuter") Object.assign(value.prompt, { automaticOuterPackaging: true,
    extractionPlanJson: '{"requirement":"notRequired","steps":[]}' });
  expect(value.check()).toBe(true);
});
it.each(["otherRun", "otherTask", "running", "processingNode", "downloadStage", "missingTarget", "missingArtifact",
  "missingFingerprint", "missingReview", "notReview", "unknownReason", "untypedSpace", "badPlan", "emptySteps", "keepPlan"])("does not prefer a saved checkpoint for %s", mode => {
  const value = fixture();
  if (mode === "otherRun") value.runtime.workflowRunId = 76;
  if (mode === "otherTask") value.runtime.taskId = 43;
  if (mode === "running") value.runtime.workflowStatus = "running";
  if (mode === "processingNode") value.runtime.currentNodeKind = "acquisition.plan.process";
  if (mode === "downloadStage") value.detail.steps[0].stage = "download";
  if (mode === "missingTarget") value.prompt.targetId = "other";
  if (mode === "missingArtifact") delete value.prompt.artifactId;
  if (mode === "missingFingerprint") delete value.prompt.fingerprint;
  if (mode === "missingReview") delete value.prompt.reviewToken;
  if (mode === "notReview") value.prompt.deliveryReview = false;
  if (mode === "unknownReason") value.prompt.reason = "ExtractionPlanUnknown";
  if (mode === "untypedSpace") value.prompt.message = "Some text mentions LOCAL_STORAGE_RESERVE_LOW";
  if (mode === "badPlan") value.prompt.extractionPlanJson = "not-json";
  if (mode === "emptySteps") value.prompt.extractionPlanJson = '{"requirement":"required","steps":[]}';
  if (mode === "keepPlan") value.prompt.extractionPlanJson = '{"requirement":"notRequired","steps":[]}';
  expect(value.check()).toBe(false);
});
