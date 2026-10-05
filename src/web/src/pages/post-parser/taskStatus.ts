import type { BTask } from "@/core/models/BTask";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { getDownloadInfo, getTargetResult } from "./results";

import { BTaskStatus, WorkflowRunStatus } from "@/sdk/constants";

export const taskStatusGroups = [
  "running",
  "queued",
  "ready",
  "attention",
  "success",
  "failed",
  "paused",
] as const;
export type TaskStatusGroup = (typeof taskStatusGroups)[number];
export interface TaskDisplayStatus {
  group: TaskStatusGroup;
  label: string;
  stage?: string;
  processing: boolean;
  canLocate: boolean;
}

const activeStatuses = new Set([
  BTaskStatus.Running,
  BTaskStatus.Pausing,
  BTaskStatus.Resuming,
  BTaskStatus.Cancelling,
]);
const knownStages = new Set([
  "fetching",
  "waitingForAi",
  "checkingAvailability",
  "purchasing",
  "extracting",
  "checkingLinks",
]);
const attentionStates = new Set(["awaitingAi", "awaitingPurchase", "possiblyExpired", "partial"]);

/** One classification for the row and the global overview; persisted results may precede completion. */
export function getTaskDisplayStatus(
  task: PostParserTask,
  liveTask?: BTask,
  locallyStarting = false,
): TaskDisplayStatus {
  const idle = (group: TaskStatusGroup, label: string): TaskDisplayStatus => ({
    group,
    label,
    processing: false,
    canLocate: false,
  });

  if (liveTask?.status === BTaskStatus.NotStarted)
    return { ...idle("queued", "postParser.stage.queued"), processing: true };
  if (liveTask?.status === BTaskStatus.Paused)
    return { ...idle("paused", "postParser.stage.paused"), processing: true };
  if ((liveTask && activeStatuses.has(liveTask.status)) || locallyStarting) {
    const value =
      liveTask?.data && typeof liveTask.data === "object" && "stage" in liveTask.data
        ? liveTask.data.stage
        : undefined;
    const stage = typeof value === "string" && knownStages.has(value) ? value : "running";

    return {
      group: "running",
      label: stage === "running" ? "postParser.label.processing" : `postParser.stage.${stage}`,
      stage,
      processing: true,
      canLocate: !!liveTask && activeStatuses.has(liveTask.status),
    };
  }

  if (
    task.workflowStatus === WorkflowRunStatus.Interrupted ||
    task.workflowStatus === WorkflowRunStatus.Pending ||
    task.workflowStatus === WorkflowRunStatus.Running
  )
    return idle("ready", "postParser.stage.interrupted");

  if (
    task.error ||
    task.workflowStatus === WorkflowRunStatus.Failed ||
    task.workflowStatus === WorkflowRunStatus.Cancelled ||
    task.targets.some((target) => !!getTargetResult(task, target)?.error)
  )
    return idle("failed", "postParser.state.error");

  if (task.parsingState && attentionStates.has(task.parsingState))
    return idle("attention", `postParser.state.${task.parsingState}`);
  const downloadInfo = getDownloadInfo(task);

  if (
    task.contentSnapshot?.locks.some((lock) => !lock.isBought) ||
    downloadInfo?.isComplete === false
  )
    return idle("attention", "postParser.state.partial");
  if (task.workflowStatus === WorkflowRunStatus.Waiting)
    return idle("attention", "postParser.state.awaitingAction");
  if (task.parsingState === "snapshotSaved") return idle("ready", "postParser.state.snapshotSaved");
  if (
    task.workflowStatus === WorkflowRunStatus.Success ||
    (!task.workflowRunId &&
      (task.parsingState === "complete" ||
        (task.targets.length > 0 &&
          task.targets.every((target) => !!getTargetResult(task, target)?.data))))
  )
    return idle("success", "postParser.state.complete");

  return idle(
    "ready",
    task.workflowRunId ? "postParser.stage.interrupted" : "postParser.label.pending",
  );
}

export function summarizeTaskStatuses(statuses: Iterable<TaskDisplayStatus>) {
  const counts = Object.fromEntries(taskStatusGroups.map((group) => [group, 0])) as Record<
    TaskStatusGroup,
    number
  >;
  const stages = new Map<string, number>();
  let total = 0;

  for (const status of statuses) {
    counts[status.group]++;
    total++;
    if (status.group === "running") stages.set(status.label, (stages.get(status.label) ?? 0) + 1);
  }

  return { total, counts, stages };
}
