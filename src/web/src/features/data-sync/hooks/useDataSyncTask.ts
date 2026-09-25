import type { BTask } from "@/core/models/BTask";

import { BTaskStatus } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";

/** Where a data sync task is, as the task list says it. */
export type DataSyncTaskPhase = "waiting" | "running" | "completed" | "failed" | "cancelled";

export interface DataSyncTaskView {
  phase: DataSyncTaskPhase;
  percentage?: number;
  process?: string;
  error?: string;
  /** While it has not started: what it waits for (another task, by name). */
  waitingReason?: string;
  task?: BTask;
}

export const taskPhase = (task?: BTask): DataSyncTaskPhase => {
  switch (task?.status) {
    case BTaskStatus.Running:
    case BTaskStatus.Pausing:
    case BTaskStatus.Resuming:
    case BTaskStatus.Paused:
    case BTaskStatus.Cancelling:
      return "running";
    case BTaskStatus.Completed:
      return "completed";
    case BTaskStatus.Error:
      return "failed";
    case BTaskStatus.Cancelled:
      return "cancelled";
    default:
      // Not pushed yet, or not started: both wait.
      return "waiting";
  }
};

export const isTaskOver = (phase: DataSyncTaskPhase) =>
  phase === "completed" || phase === "failed" || phase === "cancelled";

/**
 * One data sync task (an apply, a resolution, an undo, a restore), as the task list pushes it
 * over the UI hub (v3.1 `useDataSyncTask`). Undefined id: nothing to watch.
 */
export function useDataSyncTask(taskId?: string | null): DataSyncTaskView | undefined {
  const task = useBTasksStore((state) =>
    taskId ? state.tasks.find((item) => item.id === taskId) : undefined,
  );

  if (!taskId) return undefined;
  const phase = taskPhase(task);

  return {
    phase,
    percentage: task?.percentage,
    process: task?.process,
    error: task?.briefError || task?.error,
    waitingReason: phase === "waiting" ? task?.reasonForUnableToStart : undefined,
    task,
  };
}
