import type { BTask } from "@/core/models/BTask";

import { create } from "zustand";
import _ from "lodash";

interface BackgroundTasksState {
  tasks: BTask[];
  setTasks: (tasks: BTask[]) => void;
  updateTask: (task: BTask) => void;
}

export const useBackgroundTasksStore = create<BackgroundTasksState>(
  (set, get) => ({
    tasks: [],
    setTasks: (tasks) => {
      console.log("background tasks changed", tasks);
      set({ tasks: _.sortBy(tasks, (x) => x.startedAt ?? "2099") });
    },
    updateTask: (task) =>
      set((state) => {
        const idx = state.tasks.findIndex((t) => t.id == task.id);
        let newTasks = state.tasks.slice();

        if (idx > -1) {
          newTasks[idx] = task;
        } else {
          newTasks.push(task);
        }

        return { tasks: _.sortBy(newTasks, (x) => x.startedAt ?? "2099") };
      }),
  }),
);
