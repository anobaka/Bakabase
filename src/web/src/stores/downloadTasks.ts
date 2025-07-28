import type { DownloadTask } from "@/core/models/DownloadTask";

import { create } from "zustand";

interface DownloadTasksState {
  tasks: DownloadTask[];
  setTasks: (tasks: DownloadTask[]) => void;
  updateTask: (task: DownloadTask) => void;
}

export const useDownloadTasksStore = create<DownloadTasksState>((set, get) => ({
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
}));
