import { afterEach, describe, expect, it } from "vitest";
import { selectResourceMovingTask, useBTasksStore } from "../bTasks";
import { BTaskResourceType, BTaskStatus, BTaskType } from "@/sdk/constants";
import type { BTask } from "@/core/models/BTask";
import { ActionsFilter, TaskAction } from "@/components/FloatingAssistant/constants";

const task: BTask = {
  id: "MoveResources:batch",
  name: "move",
  createdAt: "2026-09-29T00:00:00Z",
  isPersistent: false,
  status: BTaskStatus.NotStarted,
  type: BTaskType.MoveResources,
  resourceType: BTaskResourceType.Resource,
  resourceKeys: [1, "2"],
};

afterEach(() => useBTasksStore.getState().setTasks([]));

describe("resource move task locks", () => {
  it.each([
    BTaskStatus.NotStarted,
    BTaskStatus.Running,
    BTaskStatus.Cancelling,
    BTaskStatus.WaitingForInput,
  ])("keeps affected descendants locked in active state %s", (status) => {
    useBTasksStore.getState().setTasks([{ ...task, status }]);
    const state = useBTasksStore.getState();
    expect(selectResourceMovingTask(1)(state)?.id).toBe(task.id);
    expect(selectResourceMovingTask(2)(state)?.id).toBe(task.id);
    expect(selectResourceMovingTask(3)(state)).toBeUndefined();
  });

  it("releases the task lock only when cancellation has completed", () => {
    useBTasksStore.getState().setTasks([{ ...task, status: BTaskStatus.Cancelling }]);
    expect(selectResourceMovingTask(1)(useBTasksStore.getState())).toBeDefined();
    useBTasksStore.getState().updateTask({ ...task, status: BTaskStatus.Cancelled });
    expect(selectResourceMovingTask(1)(useBTasksStore.getState())).toBeUndefined();
  });

  it("allows queued move cancellation, and keeps waiting moves out of generic resume and cleanup", () => {
    expect(ActionsFilter[TaskAction.Stop](task)).toBe(true);
    const waiting = { ...task, status: BTaskStatus.WaitingForInput };
    expect(ActionsFilter[TaskAction.Stop](waiting)).toBe(true);
    expect(ActionsFilter[TaskAction.Start](waiting)).toBe(false);
    expect(ActionsFilter[TaskAction.Resume](waiting)).toBe(false);
    expect(ActionsFilter[TaskAction.Clean](waiting)).toBe(false);
  });
});
