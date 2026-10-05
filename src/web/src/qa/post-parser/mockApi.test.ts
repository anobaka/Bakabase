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
  await mockApi.options.patchSoulPlusOptions({ autoBuyThreshold: 5, minimumRemainingCoins: 50 });
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

  it("quotes a shared unlock once and distinguishes an excluded item from an all-eligible quote", () => {
    expect(task(3).contentSnapshot?.locks).toHaveLength(2);
    expect(task(3).purchaseQuote).toEqual({
      eligibleLockUrls: [task(3).contentSnapshot!.locks[0].url],
      eligibleTotal: 3,
      excludedTotal: 0,
      excludedCount: 0,
      unknownPriceCount: 0,
    });
    expect(task(9).purchaseQuote).toMatchObject({
      eligibleTotal: 3,
      excludedTotal: 8,
      excludedCount: 1,
      unknownPriceCount: 0,
    });
  });

  it("shows queued, purchasing and AI states while unlocking only the chosen post once", async () => {
    const unrelated = tasks().filter((item) => item.id !== 3);
    const request = {
      revision: task(3).revision!,
      lockUrls: task(3).purchaseQuote!.eligibleLockUrls,
      maxTotalCost: 3,
    };

    await Promise.all([
      mockApi.postParser.purchasePostParserTaskContent(3, request),
      mockApi.postParser.purchasePostParserTaskContent(3, request),
      mockApi.postParser.reParsePostParserTask(3),
    ]);
    expect(task(3).workflowStatus).toBe(WorkflowRunStatus.Pending);
    expect(task(3).contentSnapshot?.balance).toBe(120);
    await vi.advanceTimersByTimeAsync(300);
    expect(task(3).workflowStatus).toBe(WorkflowRunStatus.Running);
    expect(task(3).parsingMessage).toContain("正在购买");
    await vi.advanceTimersByTimeAsync(1200);
    expect(task(3).workflowStatus).toBe(WorkflowRunStatus.Running);
    expect(task(3).parsingMessage).toContain("AI");
    expect(task(3).contentSnapshot?.balance).toBe(117);
    expect(task(3).contentSnapshot?.locks.every((lock) => lock.isBought)).toBe(true);
    await vi.advanceTimersByTimeAsync(1500);
    expect(task(3).workflowStatus).toBe(WorkflowRunStatus.Success);
    expect(task(3).results?.[PostParseTarget.DownloadInfo]).toMatchObject({ isComplete: true });
    await mockApi.postParser.purchasePostParserTaskContent(3, request);
    await vi.advanceTimersByTimeAsync(3000);
    expect(task(3).contentSnapshot?.balance).toBe(117);
    expect(tasks().filter((item) => item.id !== 3)).toEqual(unrelated);
    expect((await history(3)).totalCount).toBe(1);
  });

  it("keeps a partially unlocked post waiting after extracting the newly readable content", async () => {
    await mockApi.postParser.purchasePostParserTaskContent(9, {
      revision: task(9).revision!,
      lockUrls: task(9).purchaseQuote!.eligibleLockUrls,
      maxTotalCost: 3,
    });
    await vi.advanceTimersByTimeAsync(3000);
    expect(task(9).workflowStatus).toBe(WorkflowRunStatus.Waiting);
    expect(task(9).contentSnapshot?.balance).toBe(117);
    expect(task(9).contentSnapshot?.locks.map((lock) => lock.isBought)).toEqual([true, false]);
    expect(task(9).purchaseQuote).toMatchObject({
      eligibleTotal: 0,
      eligibleLockUrls: [],
      excludedTotal: 8,
      excludedCount: 1,
    });
    expect(task(9).results?.[PostParseTarget.DownloadInfo]).toMatchObject({ isComplete: false });
    expect(task(9).completedAt).toBeUndefined();
  });

  it("applies the reserve to quotes and honors the confirmed total before spending", async () => {
    const request = {
      revision: task(3).revision!,
      lockUrls: task(3).purchaseQuote!.eligibleLockUrls,
      maxTotalCost: 2,
    };

    expect((await mockApi.postParser.purchasePostParserTaskContent(3, request)).code).toBe(409);
    expect(task(3).workflowStatus).toBe(WorkflowRunStatus.Waiting);
    expect(task(3).contentSnapshot?.balance).toBe(120);
    await mockApi.options.patchSoulPlusOptions({ minimumRemainingCoins: 118 });
    expect(task(3).purchaseQuote).toMatchObject({ eligibleTotal: 0, excludedTotal: 3 });
    expect(
      (await mockApi.postParser.purchasePostParserTaskContent(3, { ...request, maxTotalCost: 3 }))
        .code,
    ).toBe(400);
    await mockApi.options.patchSoulPlusOptions({ minimumRemainingCoins: 117 });
    expect(task(3).purchaseQuote).toMatchObject({ eligibleTotal: 3, excludedTotal: 0 });
  });

  it("automatically buys eligible restored content on reparse and never buys suspected expired content", async () => {
    await mockApi.options.patchSoulPlusOptions({ autoBuyThreshold: 8 });
    await Promise.all([
      mockApi.postParser.reParsePostParserTask(2),
      mockApi.postParser.reParsePostParserTask(3),
    ]);
    await vi.advanceTimersByTimeAsync(1000);
    expect(task(2).contentSnapshot?.balance).toBe(112);
    expect(task(2).contentSnapshot?.locks.map((lock) => lock.isBought)).toEqual([true, false]);
    expect(task(2).workflowStatus).toBe(WorkflowRunStatus.Waiting);
    expect(task(3).contentSnapshot?.balance).toBe(120);
    expect(task(3).contentSnapshot?.locks.every((lock) => !lock.isBought)).toBe(true);
    expect(task(3).workflowStatus).toBe(WorkflowRunStatus.Waiting);
  });
});
