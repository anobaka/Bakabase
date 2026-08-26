import type { DownloadTask } from "@/core/models/DownloadTask";

import { create } from "zustand";

interface DownloadTasksState {
  tasks: DownloadTask[];
  setTasks: (tasks: DownloadTask[]) => void;
  updateTask: (task: DownloadTask) => void;
  /**
   * Merge a partial change into the given tasks without waiting for the server.
   *
   * Used to reflect a click immediately: the authoritative state arrives over SignalR
   * only after the backend has persisted and batched it, which is long enough that
   * the row looks unresponsive. Whatever is pushed next overwrites this.
   */
  patchTasks: (ids: number[], patch: Partial<DownloadTask>) => void;
}

export const useDownloadTasksStore = create<DownloadTasksState>((set) => ({
  tasks: [],
  setTasks: (tasks) => set({ tasks: tasks.slice() }),
  updateTask: (task) =>
    set((state) => {
      const idx = state.tasks.findIndex((t) => t.id == task.id);

      if (idx > -1) {
        const newTasks = state.tasks.slice();

        newTasks[idx] = task;

        return { tasks: newTasks };
      } else {
        return { tasks: [...state.tasks, task] };
      }
    }),
  patchTasks: (ids, patch) =>
    set((state) => {
      const targets = new Set(ids);

      if (targets.size === 0) {
        return state;
      }

      return {
        tasks: state.tasks.map((t) => (targets.has(t.id) ? { ...t, ...patch } : t)),
      };
    }),
}));
