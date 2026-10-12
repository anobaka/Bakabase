import type { BTask } from "@/core/models/BTask";

import { describe, expect, it } from "vitest";

import {
  didPathMarkSyncTaskComplete,
  getPathMarkSyncProgress,
  getPathMarkSyncTask,
  isPathMarkSyncTaskActive,
  PATH_MARK_SYNC_TASK_ID,
  RESOURCE_SYNC_TASK_ID,
  getSyncTaskForMark,
  didPathMarkSyncTasksSettle,
  isNewPathMarkSyncTaskRun,
} from "../pathMarkSyncTask";

import { BTaskStatus, PathMarkType } from "@/sdk/constants";

const task = (id: string, status: BTaskStatus, percentage?: number) =>
  ({ id, status, percentage }) as BTask;

describe("path-mark synchronization task", () => {
  it("uses only the global task and ignores legacy per-mark task ids", () => {
    const legacyTask = task("SyncPathMark_42", BTaskStatus.Running, 75);
    const globalTask = task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Running, 90);

    expect(getPathMarkSyncTask([legacyTask])).toBeUndefined();
    expect(getPathMarkSyncTask([legacyTask, globalTask])).toBe(globalTask);
  });

  it("treats every non-terminal global task status as active", () => {
    expect(isPathMarkSyncTaskActive(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.NotStarted))).toBe(
      true,
    );
    expect(isPathMarkSyncTaskActive(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Paused))).toBe(true);
    expect(isPathMarkSyncTaskActive(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Cancelling))).toBe(
      true,
    );
    expect(isPathMarkSyncTaskActive(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Completed))).toBe(
      false,
    );
  });

  it("uses indeterminate progress until the global task reports a positive percentage", () => {
    expect(
      getPathMarkSyncProgress(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Running)),
    ).toBeUndefined();
    expect(
      getPathMarkSyncProgress(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Running, 0)),
    ).toBeUndefined();
    expect(getPathMarkSyncProgress(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Running, 87))).toBe(87);
    expect(
      getPathMarkSyncProgress(task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Completed, 100)),
    ).toBeUndefined();
  });

  it("detects completion or cleanup only after an active task", () => {
    expect(didPathMarkSyncTaskComplete(BTaskStatus.Running, BTaskStatus.Completed)).toBe(true);
    expect(didPathMarkSyncTaskComplete(BTaskStatus.Paused, undefined)).toBe(true);
    expect(didPathMarkSyncTaskComplete(BTaskStatus.Completed, undefined)).toBe(false);
    expect(didPathMarkSyncTaskComplete(undefined, BTaskStatus.Completed)).toBe(false);
  });

  it("selects resource indexing progress for resource marks and property progress for the others", () => {
    const resources = task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Running, 25);
    const properties = task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Running, 75);

    expect(getSyncTaskForMark([resources, properties], PathMarkType.Resource)).toBe(resources);
    expect(getSyncTaskForMark([resources, properties], PathMarkType.Property)).toBe(properties);
    expect(getSyncTaskForMark([resources, properties], PathMarkType.MediaLibrary)).toBe(properties);
  });

  it("refreshes for a fast new persistent run even when its running event was not rendered", () => {
    const oldRun = { ...task(RESOURCE_SYNC_TASK_ID, BTaskStatus.Completed), startedAt: "old" };
    const newRun = { ...oldRun, startedAt: "new" };

    expect(didPathMarkSyncTasksSettle([oldRun], [newRun])).toBe(true);
    expect(didPathMarkSyncTasksSettle([newRun], [{ ...newRun, percentage: 100 }])).toBe(false);
  });

  it("does not own a previously queued job merely because it acquires a start time", () => {
    const queued = { ...task(RESOURCE_SYNC_TASK_ID, BTaskStatus.NotStarted), createdAt: "before" };

    expect(
      isNewPathMarkSyncTaskRun(
        { ...queued, status: BTaskStatus.Running, startedAt: "started" },
        queued,
      ),
    ).toBe(false);
    expect(isNewPathMarkSyncTaskRun({ ...queued, createdAt: "after" }, queued)).toBe(true);
    expect(
      didPathMarkSyncTasksSettle(
        [queued],
        [{ ...queued, status: BTaskStatus.Running, startedAt: "started" }],
      ),
    ).toBe(false);
    expect(
      didPathMarkSyncTasksSettle(
        [{ ...queued, status: BTaskStatus.Running, startedAt: "started" }],
        [{ ...queued, createdAt: "other", status: BTaskStatus.Running, startedAt: "later" }],
      ),
    ).toBe(true);
  });

  it("refreshes on failed/cancelled/removed active tasks without watching unrelated jobs", () => {
    const running = task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Running);

    expect(
      didPathMarkSyncTasksSettle([running], [task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Error)]),
    ).toBe(true);
    expect(
      didPathMarkSyncTasksSettle([running], [task(PATH_MARK_SYNC_TASK_ID, BTaskStatus.Cancelled)]),
    ).toBe(true);
    expect(didPathMarkSyncTasksSettle([running], [])).toBe(true);
    expect(didPathMarkSyncTasksSettle([], [task("Other", BTaskStatus.Completed)])).toBe(false);
  });
});
