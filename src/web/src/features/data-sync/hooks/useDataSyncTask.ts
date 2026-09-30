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
 * When each task the task list shows now was created, by id. Taken just before starting a task
 * whose id may be listed already — a restore chosen again, an undo retried: until the hub says
 * otherwise, the list still shows the earlier run under that id, which is over.
 */
export const listedTasks = (): ReadonlyMap<string, string> =>
  new Map(useBTasksStore.getState().tasks.map((task) => [task.id, task.createdAt]));

/**
 * The task the list shows under `taskId`, unless it is the earlier run `earlier` names (its
 * creation time, from {@link listedTasks}).
 */
export const findTask = (tasks: readonly BTask[], taskId: string, earlier?: string) =>
  tasks.find((task) => task.id === taskId && (earlier === undefined || task.createdAt !== earlier));

/**
 * One data sync task (an apply, a resolution, an undo, a restore), as the task list pushes it
 * over the UI hub (v3.1 `useDataSyncTask`). Undefined id: nothing to watch. `earlier`: the
 * creation time of an earlier run under the same id, still listed when this one started.
 */
export function useDataSyncTask(
  taskId?: string | null,
  earlier?: string,
): DataSyncTaskView | undefined {
  const task = useBTasksStore((state) =>
    taskId ? findTask(state.tasks, taskId, earlier) : undefined,
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
