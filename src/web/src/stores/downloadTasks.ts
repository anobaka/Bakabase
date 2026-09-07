import type { DownloadTask } from "@/core/models/DownloadTask";

import { create } from "zustand";

interface DownloadTasksState {
  tasks: DownloadTask[];
  setTasks: (tasks: DownloadTask[]) => void;
  updateTask: (task: DownloadTask) => void;
  /**
   * Apply many pushed tasks in one store write.
   *
   * An active download reports progress and its current step several times per file, and each
   * report used to be its own store write — so a page showing hundreds of tasks re-rendered the
   * whole list several times a second, and the main thread had nothing left for the clicks the
   * user was making. See `flushDownloadTaskUpdates` in the hub connection for the batching.
   */
  updateTasks: (tasks: DownloadTask[]) => void;
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
  updateTasks: (incoming) =>
    set((state) => {
      if (incoming.length === 0) {
        return state;
      }

      // Index once instead of scanning the list per incoming task: a burst covering every row
      // would otherwise be quadratic in the number of tasks.
      const byId = new Map(incoming.map((t) => [t.id, t]));
      const known = new Set(state.tasks.map((t) => t.id));
      const nextTasks = state.tasks.map((t) => byId.get(t.id) ?? t);

      for (const t of incoming) {
        if (!known.has(t.id)) {
          nextTasks.push(t);
        }
      }

      return { tasks: nextTasks };
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
