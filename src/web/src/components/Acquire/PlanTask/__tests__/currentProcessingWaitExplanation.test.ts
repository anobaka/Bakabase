import { expect, it } from "vitest";
import { AcquisitionStatus } from "@/sdk/constants";
import { currentProcessingWaitExplanation, type RuntimeOverview } from "../runtimeOverview";
import type { PlanTaskDetail } from "../models";

const message = "The requested release and the downloaded archive have different identifiers. Please verify the file.";
const prompt = { targetId: "part", adaptiveAssessment: true,
  reason: "FILE_DELIVERY_RESULT_UNCERTAIN", message };
const detail = () => ({ task: { id: 865, status: AcquisitionStatus.Waiting, workflowRunId: 3939,
  currentStepIndex: 2, waitPromptJson: JSON.stringify(prompt) },
  plan: { parts: [{ id: "part" }] },
  steps: [{ kind: "download.enqueueSupported" }, { kind: "download.awaitCompletion" },
    { kind: "fs.assessDelivery", stage: "processing" }] } as PlanTaskDetail);
const overview = () => ({ taskId: 865, workflowRunId: 3939, workflowStatus: "waiting", currentStepIndex: 2,
  currentNodeKind: "fs.assessDelivery", problemCode: prompt.reason } as RuntimeOverview);

it.each([prompt.reason, "SOURCE_ATTENTION_REQUIRED"])("uses the bound current model explanation when the runtime code is %s", code => {
  expect(currentProcessingWaitExplanation(detail(), { ...overview(), problemCode: code })).toBe(message);
});

it.each([
  { workflowRunId: 4000 }, { taskId: 7 }, { currentStepIndex: 1 }, { workflowStatus: "failed" },
  { workflowStatus: "running" }, { currentNodeKind: "fs.publishArtifacts" },
  { problemReasonCode: "LOCAL_STORAGE_FULL" },
])("does not revive a mismatched wait explanation for %j", patch => {
  expect(currentProcessingWaitExplanation(detail(), { ...overview(), ...patch })).toBeUndefined();
});

it.each([AcquisitionStatus.Failed, AcquisitionStatus.Running, AcquisitionStatus.Completed])(
  "does not use the old prompt after the task leaves waiting (%s)", status => {
    const item = detail(); item.task.status = status;
    expect(currentProcessingWaitExplanation(item, overview())).toBeUndefined();
  });

it.each([
  "{", "null", "[]", JSON.stringify({ ...prompt, targetId: "another-part" }),
  JSON.stringify({ ...prompt, message: { text: message } }),
  JSON.stringify({ ...prompt, message: prompt.reason }),
  JSON.stringify({ ...prompt, message: "LLM_OUTPUT_TRUNCATED: private diagnostic" }),
  JSON.stringify({ ...prompt, adaptiveAssessment: false }),
])("rejects malformed, code-only, non-model or unrelated prompt %s", waitPromptJson => {
  const item = detail(); item.task.waitPromptJson = waitPromptJson;
  expect(currentProcessingWaitExplanation(item, overview())).toBeUndefined();
});

it("requires the actual task step to match the current runtime node", () => {
  const item = detail(); item.steps[2].kind = "download.awaitCompletion";
  expect(currentProcessingWaitExplanation(item, overview())).toBeUndefined();
});
