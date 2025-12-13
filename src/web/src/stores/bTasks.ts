import type { BTask } from "@/core/models/BTask";
import { BTaskStatus } from "@/sdk/constants";

import { create } from "zustand";
import _ from "lodash";

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
