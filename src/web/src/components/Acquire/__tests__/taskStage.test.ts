import { expect, it } from "vitest";
import { AcquisitionStatus as Status } from "@/sdk/constants";
import { acquisitionTaskStage, acquisitionStageLabel, acquisitionDownloadActivity, filterAcquisitionTasks } from "../taskStage";
import type { PlanTask } from "../PlanTask/models";
import type { TFunction } from "i18next";

it.each([
  [{ builderId: "draft", phase: "draft", status: Status.Waiting }, "preparing"],
  [{ builderId: "search", phase: "selection", status: Status.Waiting }, "preparing"],
  [{ builderId: "prepared", phase: "ready", status: Status.Waiting }, "preparing"],
  [
    { builderId: "prepared", phase: "ready", targetDirectory: "/result", status: Status.Waiting },
    "ready",
  ],
  [{ builderId: "search", phase: "attention", status: Status.Waiting }, "attention"],
  [{ workflowRunId: 3, status: Status.Waiting }, "attention"],
  [{ workflowRunId: 3, status: Status.Pending }, "running"],
  [{ status: Status.Pending }, "running"],
  [{ status: Status.Running }, "running"],
  [{ status: Status.Failed, phase: "executing" }, "attention"],
  [{ status: Status.Cancelled, phase: "ready" }, "cancelled"],
  [{ status: Status.Completed, phase: "executing" }, "completed"],
  [{ status: Status.Waiting, stage: "ready" }, "ready"],
])(
  "maps execution and preparation semantics without treating every wait as an error: %j",
  (task, expected) => {
    expect(acquisitionTaskStage(task as PlanTask)).toBe(expected);
  },
);

it("searches task names and exact IDs, retaining cancelled history only in its matching filter or all", () => {
  const tasks = [
    { id: 4, title: "My Draft", status: Status.Waiting, builderId: "draft" },
    { id: 5, resourceName: "Legacy", status: Status.Cancelled },
  ] as PlanTask[];
  expect(filterAcquisitionTasks(tasks, "preparing", "my").map((task) => task.id)).toEqual([4]);
  expect(filterAcquisitionTasks(tasks, "all", "#5").map((task) => task.id)).toEqual([5]);
  expect(filterAcquisitionTasks(tasks, "cancelled", "legacy").map((task) => task.id)).toEqual([5]);
  expect(filterAcquisitionTasks(tasks, "attention", "")).toEqual([]);
});

it("matches hash-prefixed IDs and preserves literal hash text in task names", () => {
  const tasks = [
    { id: 3, title: "Exact task", status: Status.Waiting, builderId: "draft" },
    { id: 13, title: "Chapter 3", status: Status.Waiting, builderId: "draft" },
    { id: 7, resourceName: "Legacy 3", status: Status.Cancelled },
    { id: 9, title: "Volume #3", status: Status.Waiting },
    { id: 10, resourceName: "Legacy #3", status: Status.Waiting },
  ] as PlanTask[];
  expect(filterAcquisitionTasks(tasks, "all", " #3 ").map((task) => task.id)).toEqual([10, 9, 3]);
  expect(filterAcquisitionTasks(tasks, "all", "#33")).toEqual([]);
  expect(filterAcquisitionTasks(tasks, "all", "3").map((task) => task.id)).toEqual([
    13, 10, 9, 7, 3,
  ]);
});

it.each(["running", "queued"])("presents a waiting workflow with %s child activity as acquiring", (activity) => {
  const task = { id: 8, workflowRunId: 30, status: Status.Waiting, stage: "attention", downloadActivity: activity } as PlanTask;
  expect(acquisitionTaskStage(task)).toBe("running");
  expect(acquisitionStageLabel(((key: string) => key) as TFunction, task)).toBe(`acquisition.downloadActivity.${activity}`);
  expect(filterAcquisitionTasks([task], "running", "")).toEqual([task]);
  expect(task.status).toBe(Status.Waiting);
});

it("clears stale list activity with the current run observation and ignores another run or task", () => {
  const task = { id: 8, workflowRunId: 30, status: Status.Waiting, stage: "running", downloadActivity: "queued" } as PlanTask;
  const current = { taskId: 8, workflowRunId: 30, downloadActivity: "running" };
  expect(acquisitionDownloadActivity(task, current)).toBe("running");
  expect(acquisitionDownloadActivity(task, { ...current, workflowRunId: 29 })).toBe("queued");
  expect(acquisitionDownloadActivity(task, { ...current, taskId: 9 })).toBe("queued");
  expect(acquisitionDownloadActivity(task, { ...current, downloadActivity: null })).toBeUndefined();
  expect(acquisitionTaskStage(task, { ...current, downloadActivity: null })).toBe("attention");
});

it.each([Status.Completed, Status.Failed, Status.Cancelled])("does not revive terminal status %s from child activity", (status) => {
  const task = { id: 8, workflowRunId: 30, status, downloadActivity: "running" } as PlanTask;
  expect(acquisitionDownloadActivity(task)).toBeUndefined();
  expect(acquisitionTaskStage(task)).not.toBe("running");
});

it("does not infer active downloads from unknown activity or a manual processing wait", () => {
  const task = { id: 8, workflowRunId: 30, status: Status.Waiting, downloadActivity: "stopped" } as PlanTask;
  expect(acquisitionTaskStage(task)).toBe("attention");
  expect(acquisitionDownloadActivity({ ...task, workflowRunId: undefined, downloadActivity: "running" })).toBeUndefined();
});
