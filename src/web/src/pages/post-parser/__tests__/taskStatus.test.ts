import type { BTask } from "@/core/models/BTask";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { describe, expect, it } from "vitest";

import { getTaskDisplayStatus, summarizeTaskStatuses } from "../taskStatus";

import { BTaskStatus, PostParserSource, PostParseTarget, WorkflowRunStatus } from "@/sdk/constants";

const task: PostParserTask = {
  id: 1,
  source: PostParserSource.SoulPlus,
  link: "https://example.test/post/1",
  targets: [PostParseTarget.DownloadInfo],
};
const live = (status: BTaskStatus, stage?: string) => ({ status, data: { stage } }) as BTask;
const successful: PostParserTask = {
  ...task,
  workflowRunId: 50,
  workflowStatus: WorkflowRunStatus.Success,
  parsingState: "complete",
  results: { DownloadInfo: { isComplete: true, resources: [] } },
};

describe("post parser status overview classification", () => {
  it("assigns exactly one group to every task and keeps active work ahead of saved results", () => {
    const statuses = [
      getTaskDisplayStatus(successful, live(BTaskStatus.Running, "extracting")),
      getTaskDisplayStatus(task, live(BTaskStatus.Running, "waitingForAi")),
      getTaskDisplayStatus(successful, live(BTaskStatus.NotStarted)),
      getTaskDisplayStatus(successful, live(BTaskStatus.Paused)),
      getTaskDisplayStatus(task),
      getTaskDisplayStatus({
        ...task,
        workflowRunId: 50,
        workflowStatus: WorkflowRunStatus.Interrupted,
      }),
      getTaskDisplayStatus({ ...task, parsingState: "awaitingAi" }),
      getTaskDisplayStatus({ ...task, parsingState: "possiblyExpired" }),
      getTaskDisplayStatus(successful),
      getTaskDisplayStatus({ ...successful, workflowStatus: WorkflowRunStatus.Failed }),
    ];
    const summary = summarizeTaskStatuses(statuses);

    expect(summary.total).toBe(10);
    expect(summary.counts).toEqual({
      running: 2,
      queued: 1,
      paused: 1,
      ready: 2,
      attention: 2,
      success: 1,
      failed: 1,
    });
    expect([...summary.stages]).toEqual([
      ["postParser.stage.extracting", 1],
      ["postParser.stage.waitingForAi", 1],
    ]);
    expect(Object.values(summary.counts).reduce((sum, count) => sum + count, 0)).toBe(
      summary.total,
    );
  });

  it("waits for the live execution to end before adopting persisted success or failure", () => {
    const persistedFailure = {
      ...successful,
      workflowStatus: WorkflowRunStatus.Failed,
      error: "AI failed",
    };

    expect(getTaskDisplayStatus(successful, live(BTaskStatus.Running, "checkingLinks")).group).toBe(
      "running",
    );
    expect(getTaskDisplayStatus(successful, live(BTaskStatus.Completed)).group).toBe("success");
    expect(getTaskDisplayStatus(persistedFailure, live(BTaskStatus.Cancelling)).group).toBe(
      "running",
    );
    expect(getTaskDisplayStatus(persistedFailure, live(BTaskStatus.Cancelled)).group).toBe(
      "failed",
    );
  });

  it("does not call incomplete, waiting or not-yet-finished records successful", () => {
    expect(
      getTaskDisplayStatus({ ...successful, workflowStatus: WorkflowRunStatus.Running }).group,
    ).toBe("ready");
    expect(
      getTaskDisplayStatus({ ...successful, workflowStatus: WorkflowRunStatus.Waiting }).group,
    ).toBe("attention");
    expect(
      getTaskDisplayStatus({ ...successful, contentSnapshot: { locks: [{ isBought: false }] } })
        .group,
    ).toBe("attention");
    expect(
      getTaskDisplayStatus({ ...successful, results: { DownloadInfo: { isComplete: false } } })
        .group,
    ).toBe("attention");
    expect(
      getTaskDisplayStatus({ ...task, results: { DownloadInfo: { error: "Failed extraction" } } })
        .group,
    ).toBe("failed");
    expect(getTaskDisplayStatus({ ...successful, workflowStatus: undefined }).group).toBe("ready");
    expect(
      getTaskDisplayStatus({ ...successful, workflowRunId: undefined, workflowStatus: undefined })
        .group,
    ).toBe("success");
  });

  it.each([BTaskStatus.Running, BTaskStatus.Pausing, BTaskStatus.Resuming, BTaskStatus.Cancelling])(
    "allows locating admitted status %s",
    (status) => {
      expect(getTaskDisplayStatus(task, live(status, "waitingForAi"))).toMatchObject({
        group: "running",
        canLocate: true,
      });
    },
  );

  it("keeps queued, paused and locally starting tasks out of navigation", () => {
    for (const status of [
      BTaskStatus.NotStarted,
      BTaskStatus.Paused,
      BTaskStatus.Completed,
      BTaskStatus.Cancelled,
      BTaskStatus.Error,
    ])
      expect(getTaskDisplayStatus(task, live(status)).canLocate).toBe(false);
    expect(getTaskDisplayStatus(task, undefined, true)).toMatchObject({
      group: "running",
      canLocate: false,
    });
  });
});
