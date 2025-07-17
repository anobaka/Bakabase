import type { BTask } from "@/core/models/BTask";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { create } from "zustand";
import _ from "lodash";

interface PostParserTasksState {
  tasks: PostParserTask[];
  setTasks: (tasks: BTask[]) => void;
  updateTask: (task: PostParserTask) => void;
  deleteTask: (taskId: number) => void;
  deleteAll: () => void;
}

export const usePostParserTasksStore = create<PostParserTasksState>(
  (set, get) => ({
    tasks: [],
    setTasks: (tasks) => set({ tasks: _.sortBy(tasks, (x) => x.createdAt) }),
    updateTask: (task) =>
      set((state) => {
        const idx = state.tasks.findIndex((t) => t.id == task.id);
        let newState = state.tasks.slice();

        if (idx > -1) {
          newState[idx] = task;
        } else {
          newState.push(task);
          newState = _.sortBy(newState, (x) => x.id);
        }

        return { tasks: newState };
      }),
    deleteTask: (taskId) =>
      set((state) => ({ tasks: state.tasks.filter((t) => t.id !== taskId) })),
    deleteAll: () => set({ tasks: [] }),
  }),
);
