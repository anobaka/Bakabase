import type { BTask } from "@/core/models/BTask";

import { create } from "zustand";
import _ from "lodash";

import { BTaskResourceType, BTaskStatus, BTaskType } from "@/sdk/constants";

interface BTasksState {
  tasks: BTask[];
  setTasks: (tasks: BTask[]) => void;
  removeTask: (id: string) => void;
  updateTask: (task: BTask) => void;
}

export const useBTasksStore = create<BTasksState>((set) => ({
  tasks: [],
  setTasks: (tasks) => set({ tasks: _.sortBy(tasks, (x) => x.createdAt) }),
  removeTask: (id) => set((state) => ({ tasks: state.tasks.filter((t) => t.id !== id) })),
  updateTask: (task) =>
    set((state) => {
      const idx = state.tasks.findIndex((t) => t.id === task.id);
      const newState = state.tasks.slice();

      if (idx > -1) {
        newState[idx] = task;
      } else {
        newState.push(task);

        return { tasks: _.sortBy(newState, (x) => x.createdAt) };
      }

      return { tasks: newState };
    }),
}));

// Memoized selectors
export const selectTasks = (state: BTasksState) => state.tasks;

export const selectRunningTasks = (state: BTasksState) =>
  state.tasks.filter((t) => t.status === BTaskStatus.Running);

export const selectFailedTasks = (state: BTasksState) =>
  state.tasks.filter((t) => t.status === BTaskStatus.Error);

export const selectCompletedTasks = (state: BTasksState) =>
  state.tasks.filter((t) => t.status === BTaskStatus.Completed);

export const selectClearableTasks = (state: BTasksState) =>
  state.tasks.filter(
    (t) =>
      !t.isPersistent &&
      (t.status === BTaskStatus.Completed ||
        t.status === BTaskStatus.Error ||
        t.status === BTaskStatus.Cancelled),
  );

// Create a hook with shallow comparison for array selectors
export const useBTasksWithShallow = <T>(selector: (state: BTasksState) => T) =>
  useBTasksStore((state) => selector(state));

// A move task locks its resources until it reaches a terminal status.
const activeMoveStatuses = new Set([
  BTaskStatus.NotStarted,
  BTaskStatus.Running,
  BTaskStatus.Paused,
  BTaskStatus.Cancelling,
  BTaskStatus.Pausing,
  BTaskStatus.Resuming,
]);

/**
 * The active MoveResources task covering a resource, or undefined. Backed by the BTask
 * SignalR feed (resourceKeys carries every affected resource id, descendants included), so
 * cards learn about the moving state with no extra push channel; cards not covered keep
 * getting a stable undefined and skip re-rendering.
 */
export const selectResourceMovingTask =
  (resourceId: number) =>
  (state: BTasksState): BTask | undefined =>
    state.tasks.find(
      (t) =>
        t.type === BTaskType.MoveResources &&
        t.resourceType === BTaskResourceType.Resource &&
        activeMoveStatuses.has(t.status) &&
        (t.resourceKeys ?? []).some((k) => Number(k) === resourceId),
    );
