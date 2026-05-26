import type { BTask } from "@/core/models/BTask";

import { BTaskStatus } from "@/sdk/constants";

export const AssistantStatus = {
  Idle: 0,
  Working: 1,
  AllDone: 2,
  Failed: 3,
} as const;

export type AssistantStatusType = (typeof AssistantStatus)[keyof typeof AssistantStatus];

export enum TaskAction {
  Start = 1,
  Pause = 2,
  Resume = 3,
  Stop = 4,
  Clean = 5,
  Config = 6,
}

export const ActionsFilter: Record<TaskAction, (task: BTask) => boolean> = {
  [TaskAction.Start]: (task) =>
    task.isPersistent &&
    (task.status === BTaskStatus.Cancelled ||
      task.status === BTaskStatus.Error ||
      task.status === BTaskStatus.Completed ||
      task.status === BTaskStatus.NotStarted),
  // Pause/Resume actions are hidden in transitional states (Pausing/Resuming)
  // since the request is already in flight — re-clicking would be a no-op and
  // letting the user re-click looks like the first click was ignored.
  [TaskAction.Pause]: (task) => task.status === BTaskStatus.Running,
  [TaskAction.Resume]: (task) => task.status === BTaskStatus.Paused,
  [TaskAction.Stop]: (task) =>
    task.status === BTaskStatus.Running ||
    task.status === BTaskStatus.Paused ||
    task.status === BTaskStatus.Pausing ||
    task.status === BTaskStatus.Resuming,
  [TaskAction.Clean]: (task) =>
    !task.isPersistent &&
    (task.status === BTaskStatus.Completed ||
      task.status === BTaskStatus.Error ||
      task.status === BTaskStatus.Cancelled),
  [TaskAction.Config]: (task) => task.isPersistent,
};

export const POSITION_STORAGE_KEY = "floating-assistant-position";
export const DEFAULT_POSITION = {
  x: 10,
  y: typeof window !== "undefined" ? window.innerHeight - 68 : 100,
};
export const POLLING_INTERVAL_MS = 2000; // 2 seconds
