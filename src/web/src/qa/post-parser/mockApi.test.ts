import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import mockApi, { resetPreview, setPreviewScenario } from "./mockApi";

import { PostParseTarget, PostParserSource, WorkflowRunStatus } from "@/sdk/constants";
import { usePostParserTasksStore } from "@/stores/postParserTasks";

vi.mock("@/stores/options", () => ({
  useThirdPartyOptionsStore: { getState: () => ({ update: vi.fn() }) },
  useSoulPlusOptionsStore: { getState: () => ({ update: vi.fn() }) },
}));

const tasks = () => usePostParserTasksStore.getState().tasks;
const task = (id: number) => tasks().find((item) => item.id === id)!;
const history = (taskId?: number, pageIndex = 1, pageSize = 20) =>
  mockApi.postParser.searchPostParserWorkflowRuns({ taskId, pageIndex, pageSize });

beforeEach(async () => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date("2026-10-05T04:00:00Z"));
  await mockApi.options.patchThirdPartyOptions({ automaticallyParsingPosts: false });
  setPreviewScenario("all");
  resetPreview();
});

afterEach(() => {
  vi.clearAllTimers();
  vi.useRealTimers();
});

describe("post-parser preview action boundaries", () => {
  it("reparses only the chosen post and coalesces duplicate requests while it runs", async () => {
    const unrelated = tasks().filter((item) => item.id !== 4);
    const initialHistory = await history(4);

    await Promise.all([
      mockApi.postParser.reParsePostParserTask(4),
      mockApi.postParser.reParsePostParserTask(4),
      mockApi.postParser.retryPostParserTaskWorkflow(4),
    ]);

    expect(task(4).workflowStatus).toBe(WorkflowRunStatus.Running);
    expect(tasks().filter((item) => item.id !== 4)).toEqual(unrelated);
    expect((await history(4)).totalCount).toBe(initialHistory.totalCount! + 1);
    await vi.advanceTimersByTimeAsync(1000);
    expect(task(4).workflowStatus).toBe(WorkflowRunStatus.Success);
    expect(task(1).workflowRunId).toBeUndefined();
  });

  it("starts each pending post once across overlapping batch requests", async () => {
    await mockApi.postParser.addPostParserTasks({
      sourceLinksMap: {},
      targets: [PostParseTarget.DownloadInfo],
      links: ["https://example.test/a", "https://example.test/a", "https://example.test/b"],
    });
    const pending = tasks().filter((item) => item.workflowRunId == null);
    const settled = tasks().filter((item) => item.workflowRunId != null);

    expect(pending).toHaveLength(3);
    await Promise.all([
      mockApi.postParser.startAllPostParserTasks(),
      mockApi.postParser.startAllPostParserTasks(),
    ]);
    expect(tasks().filter((item) => settled.some((previous) => previous.id === item.id))).toEqual(
      settled,
    );
    for (const item of pending) {
      expect(task(item.id).workflowStatus).toBe(WorkflowRunStatus.Running);
      expect((await history(item.id)).totalCount).toBe(1);
    }
    await vi.advanceTimersByTimeAsync(1000);
    await mockApi.postParser.startAllPostParserTasks();
    for (const item of pending) expect((await history(item.id)).totalCount).toBe(1);
  });

  it("applies automatic parsing only to subsequently submitted posts", async () => {
    await mockApi.options.patchThirdPartyOptions({ automaticallyParsingPosts: true });
    expect(task(1).workflowRunId).toBeUndefined();
    await mockApi.postParser.addPostParserTasks({
      sourceLinksMap: {},
      targets: [PostParseTarget.DownloadInfo],
      links: ["https://example.test/new"],
    });
    const added = tasks().find((item) => item.link === "https://example.test/new")!;

    expect(added.workflowStatus).toBe(WorkflowRunStatus.Running);
    expect(task(1).workflowRunId).toBeUndefined();
    await vi.advanceTimersByTimeAsync(1000);
    expect(task(1).workflowRunId).toBeUndefined();

    // Explicitly submitting that older pending link makes it part of this input batch.
    await mockApi.postParser.addPostParserTasks({
      sourceLinksMap: { [PostParserSource.SoulPlus]: [task(1).link] },
      targets: [PostParseTarget.DownloadInfo],
      links: [],
    });
    expect(task(1).workflowStatus).toBe(WorkflowRunStatus.Running);
    expect(tasks().filter((item) => item.link === task(1).link)).toHaveLength(1);
  });

  it("filters and paginates a post's revisions across definitions without losing deleted history", async () => {
    const all = await history();
    const first = await history(4, 1, 1);
    const second = await history(4, 2, 1);

    expect(first.totalCount).toBe(2);
    expect(first.data?.map((run) => run.id)).toEqual([1004]);
    expect(second.data?.map((run) => run.id)).toEqual([904]);
    expect(first.data?.[0].workflowDefinitionId).not.toBe(second.data?.[0].workflowDefinitionId);
    expect(all.totalCount).toBeGreaterThan(first.totalCount!);
    expect((await history(1)).data).toEqual([]);

    await mockApi.postParser.reParsePostParserTask(4);
    await vi.advanceTimersByTimeAsync(1000);
    expect((await history(4)).totalCount).toBe(3);
    await mockApi.postParser.deletePostParserTask(4);
    expect(task(4)).toBeUndefined();
    expect((await history(4)).totalCount).toBe(3);
    expect((await history()).totalCount).toBe(all.totalCount! + 1);
  });
});
