import type { BTask } from "@/core/models/BTask";

import { create } from "zustand";
import _ from "lodash";

import i18n from "@/i18n";
import { localizeBTask } from "@/core/bTaskLocalization";
import { BTaskResourceType, BTaskStatus, BTaskType } from "@/sdk/constants";

interface BTasksState {
  tasks: BTask[];
  sourceTasks: BTask[];
  setTasks: (tasks: BTask[]) => void;
  removeTask: (id: string) => void;
  updateTask: (task: BTask) => void;
}

const projectTasks = (tasks: BTask[]) => tasks.map((task) => localizeBTask(task, i18n.language));

export const useBTasksStore = create<BTasksState>((set) => ({
  tasks: [],
  sourceTasks: [],
  setTasks: (tasks) => {
    const sourceTasks = _.sortBy(tasks, (task) => task.createdAt);

    set({ sourceTasks, tasks: projectTasks(sourceTasks) });
  },
  removeTask: (id) =>
    set((state) => ({
      sourceTasks: state.sourceTasks.filter((task) => task.id !== id),
      tasks: state.tasks.filter((task) => task.id !== id),
    })),
  updateTask: (task) =>
    set((state) => {
      const sourceTasks = state.sourceTasks.slice();
      const index = sourceTasks.findIndex((item) => item.id === task.id);

      if (index >= 0) sourceTasks[index] = task;
      else sourceTasks.push(task);
      const sorted = index >= 0 ? sourceTasks : _.sortBy(sourceTasks, (item) => item.createdAt);

      const tasks = state.tasks.slice();
      const projected = localizeBTask(task, i18n.language);
      const visibleIndex = tasks.findIndex((item) => item.id === task.id);

      if (visibleIndex >= 0) tasks[visibleIndex] = projected;
      else tasks.push(projected);

      return {
        sourceTasks: sorted,
        tasks: visibleIndex >= 0 ? tasks : _.sortBy(tasks, (item) => item.createdAt),
      };
    }),
}));

const updateTaskLanguage = () =>
  useBTasksStore.setState((state) => ({
    tasks: projectTasks(state.sourceTasks),
  }));

i18n.on("languageChanged", updateTaskLanguage);
// Avoid retaining subscriptions when this module is replaced during development.
if (import.meta.hot) import.meta.hot.dispose(() => i18n.off("languageChanged", updateTaskLanguage));

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
  BTaskStatus.WaitingForInput,
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
