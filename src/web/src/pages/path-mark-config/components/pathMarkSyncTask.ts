import type { BTask } from "@/core/models/BTask";

import { useEffect, useRef } from "react";

import { BTaskStatus, PathMarkType } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";

export const PATH_MARK_SYNC_TASK_ID = "SyncPathMarks";
export const RESOURCE_SYNC_TASK_ID = "SyncResources";

export const getPathMarkSyncTasks = (tasks: BTask[] = []): BTask[] =>
  tasks.filter((task) => task.id === PATH_MARK_SYNC_TASK_ID || task.id === RESOURCE_SYNC_TASK_ID);

const activeStatuses = new Set<BTaskStatus>([
  BTaskStatus.NotStarted,
  BTaskStatus.Running,
  BTaskStatus.Paused,
  BTaskStatus.Cancelling,
  BTaskStatus.Pausing,
  BTaskStatus.Resuming,
  BTaskStatus.WaitingForInput,
]);

export const getPathMarkSyncTask = (tasks?: BTask[]): BTask | undefined =>
  tasks?.find((task) => task.id === PATH_MARK_SYNC_TASK_ID);

export const getSyncTaskForMark = (
  tasks: BTask[] | undefined,
  markType?: number,
): BTask | undefined =>
  tasks?.find(
    (task) =>
      task.id ===
      (markType === PathMarkType.Resource ? RESOURCE_SYNC_TASK_ID : PATH_MARK_SYNC_TASK_ID),
  );

export const getSyncTaskRunKey = (task: BTask): string =>
  `${task.id}:${task.createdAt ?? ""}:${task.startedAt ?? ""}`;

/** A job already queued before submission may only acquire startedAt later; it is still old. */
export const isNewPathMarkSyncTaskRun = (task: BTask, baseline?: BTask): boolean =>
  !baseline ||
  task.createdAt !== baseline.createdAt ||
  (baseline.status !== BTaskStatus.NotStarted &&
    getSyncTaskRunKey(task) !== getSyncTaskRunKey(baseline));

export const isSamePathMarkSyncTaskRun = (current: BTask | undefined, captured: BTask): boolean =>
  current != null &&
  current.id === captured.id &&
  (getSyncTaskRunKey(current) === getSyncTaskRunKey(captured) ||
    (captured.status === BTaskStatus.NotStarted && current.createdAt === captured.createdAt));

export const waitForBTaskSnapshot = (signal: AbortSignal): Promise<boolean> => {
  if (signal.aborted) return Promise.resolve(false);
  if (useBTasksStore.getState().initialized) return Promise.resolve(true);

  return new Promise((resolve) => {
    const finish = (initialized: boolean) => {
      unsubscribe();
      signal.removeEventListener("abort", abort);
      resolve(initialized);
    };
    const abort = () => finish(false);
    const unsubscribe = useBTasksStore.subscribe((state) => {
      if (state.initialized) finish(true);
    });

    signal.addEventListener("abort", abort, { once: true });
  });
};

/** Persistent tasks reuse their id; startedAt distinguishes a fast new run from old history. */
export const didPathMarkSyncTasksSettle = (previous: BTask[], current: BTask[]): boolean => {
  const oldTasks = new Map(getPathMarkSyncTasks(previous).map((task) => [task.id, task]));
  const newTasks = new Map(getPathMarkSyncTasks(current).map((task) => [task.id, task]));

  for (const [id, oldTask] of oldTasks) {
    if (
      isPathMarkSyncTaskActive(oldTask) &&
      (!isPathMarkSyncTaskActive(newTasks.get(id)) ||
        !isSamePathMarkSyncTaskRun(newTasks.get(id), oldTask))
    )
      return true;
  }
  for (const [id, task] of newTasks) {
    const oldTask = oldTasks.get(id);

    if (
      !isPathMarkSyncTaskActive(task) &&
      (!oldTask || getSyncTaskRunKey(oldTask) !== getSyncTaskRunKey(task))
    )
      return true;
  }

  return false;
};

/** Refresh after either pipeline settles, including runs too fast to render an active state. */
export const usePathMarkSyncCompletion = (onSettled?: () => void) => {
  const tasks = useBTasksStore((state) => state.tasks);
  const previous = useRef(tasks);
  const callback = useRef(onSettled);

  callback.current = onSettled;
  useEffect(() => {
    const settled = didPathMarkSyncTasksSettle(previous.current, tasks);

    previous.current = tasks;
    if (settled) callback.current?.();
  }, [tasks]);
};

export const isPathMarkSyncTaskActive = (task?: BTask): boolean =>
  task != null && activeStatuses.has(task.status);

/**
 * Returns the real overall progress exposed by the single global path-mark sync task.
 * Zero/missing progress is represented as undefined so callers can render an indeterminate
 * indicator instead of implying that an individual mark has its own measurable task.
 */
export const getPathMarkSyncProgress = (task?: BTask): number | undefined => {
  if (!isPathMarkSyncTaskActive(task) || task?.percentage == null || task.percentage <= 0) {
    return undefined;
  }

  return Math.min(100, Math.max(0, task.percentage));
};

export const didPathMarkSyncTaskComplete = (
  previousStatus?: BTaskStatus,
  currentStatus?: BTaskStatus,
): boolean =>
  previousStatus != null &&
  activeStatuses.has(previousStatus) &&
  (currentStatus == null || currentStatus === BTaskStatus.Completed);
