import type { TFunction } from "i18next";
import { AcquisitionStatus } from "@/sdk/constants";
import type { PlanTask } from "./PlanTask/models";
import type { RuntimeOverview } from "./PlanTask/runtimeOverview";

export const acquisitionStages = [
  "preparing",
  "ready",
  "running",
  "attention",
  "completed",
] as const;
export type AcquisitionStage = (typeof acquisitionStages)[number] | "cancelled";
export type AcquisitionStageFilter = AcquisitionStage | "all";

type StageTask = Pick<PlanTask, "status" | "phase" | "workflowRunId" | "builderId" | "targetDirectory"> & {
  id?: number;
  stage?: string | null;
  downloadActivity?: string | null;
  isDeleted?: boolean;
  deletedAt?: string | null;
};

type DownloadOverview = Pick<RuntimeOverview, "taskId" | "workflowRunId"> & {
  downloadActivity?: string | null;
};

function currentDownloadOverview(task: StageTask, overview?: DownloadOverview) {
  return overview != null && task.workflowRunId != null && task.id === overview.taskId &&
    task.workflowRunId === overview.workflowRunId ? overview : undefined;
}

/** Live child activity changes presentation only; workflow status still controls mutations. */
export function acquisitionDownloadActivity(task: StageTask, overview?: DownloadOverview) {
  if (!task.workflowRunId || task.isDeleted || task.deletedAt ||
    ![AcquisitionStatus.Waiting, AcquisitionStatus.Running, AcquisitionStatus.Pending].includes(task.status))
    return undefined;
  const current = currentDownloadOverview(task, overview);
  // An authoritative current observation with no activity clears an older list snapshot.
  const activity = current ? current.downloadActivity : task.downloadActivity;
  return activity === "running" || activity === "queued" ? activity : undefined;
}

/** Preparation and a prepared plan both use Waiting internally; only an executing wait needs intervention. */
export function acquisitionTaskStage(
  task: StageTask,
  overview?: DownloadOverview,
): AcquisitionStage {
  if (task.status === AcquisitionStatus.Cancelled) return "cancelled";
  if (task.status === AcquisitionStatus.Completed) return "completed";
  if (task.status === AcquisitionStatus.Failed) return "attention";
  if (acquisitionDownloadActivity(task, overview)) return "running";
  const clearedDownloadActivity = currentDownloadOverview(task, overview) &&
    task.status === AcquisitionStatus.Waiting && task.stage === "running";
  if (!clearedDownloadActivity && [...acquisitionStages, "cancelled"].includes(task.stage ?? ""))
    return task.stage as AcquisitionStage;
  if (task.status === AcquisitionStatus.Running) return "running";
  if (task.workflowRunId)
    return task.status === AcquisitionStatus.Pending ? "running" : "attention";
  if (task.builderId) {
    if (task.phase === "attention") return "attention";
    return task.phase === "ready" && task.targetDirectory?.trim() ? "ready" : "preparing";
  }
  return task.status === AcquisitionStatus.Pending ? "running" : "attention";
}

export const acquisitionStageLabel = (
  t: TFunction,
  task: Parameters<typeof acquisitionTaskStage>[0],
  overview?: DownloadOverview,
) => {
  const activity = acquisitionDownloadActivity(task, overview);
  return t<string>(activity
    ? `acquisition.downloadActivity.${activity}`
    : `acquisition.stage.${acquisitionTaskStage(task, overview)}`);
};

export function filterAcquisitionTasks(
  tasks: PlanTask[],
  filter: AcquisitionStageFilter,
  keyword: string,
): PlanTask[] {
  const query = keyword.trim().toLocaleLowerCase();
  const exactId = /^#(\d+)$/.exec(query)?.[1];
  return tasks
    .filter(
      (task) =>
        (filter === "all" || acquisitionTaskStage(task) === filter) &&
        (!query ||
          String(task.id) === exactId ||
          String(task.id) === query ||
          [task.title, task.resourceName].some((name) =>
            name?.toLocaleLowerCase().includes(query),
          )),
    )
    .sort((a, b) => b.id - a.id);
}
