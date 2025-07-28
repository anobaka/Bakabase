import type { BTask } from "@/core/models/BTask";

import { create } from "zustand";
import _ from "lodash";

interface BTasksState {
  tasks: BTask[];
  setTasks: (tasks: BTask[]) => void;
  removeTask: (id: number) => void;
  updateTask: (task: BTask) => void;
}

export const useBTasksStore = create<BTasksState>((set, get) => ({
  tasks: [],
  setTasks: (tasks) => set({ tasks: _.sortBy(tasks, (x) => x.createdAt) }),
  removeTask: (id) =>
    set((state) => ({ tasks: state.tasks.filter((t) => t.id != id) })),
  updateTask: (task) =>
    set((state) => {
      const idx = state.tasks.findIndex((t) => t.id == task.id);
      let newState = state.tasks.slice();

      if (idx > -1) {
        newState[idx] = task;
      } else {
        newState.push(task);
        newState = _.sortBy(newState, (x) => x.createdAt);
      }

      return { tasks: newState };
    }),
}));
