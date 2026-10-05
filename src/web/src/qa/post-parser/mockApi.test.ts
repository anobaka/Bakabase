import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import mockApi, { resetPreview, setPreviewScenario } from "./mockApi";
import * as fixtures from "./fixtures";

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
  vi.restoreAllMocks();
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

  it("starts pending posts and resumes failed posts once across overlapping batch requests", async () => {
    await mockApi.postParser.addPostParserTasks({
      sourceLinksMap: {},
      targets: [PostParseTarget.DownloadInfo],
      links: ["https://example.test/a", "https://example.test/a", "https://example.test/b"],
    });
    const pending = tasks().filter((item) => item.workflowRunId == null);
    const settled = tasks().filter((item) => item.workflowRunId != null && item.id !== 7);
    const failedRunId = task(7).workflowRunId;

    expect(pending).toHaveLength(4);
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
    expect(task(7).workflowStatus).toBe(WorkflowRunStatus.Running);
    expect(task(7).workflowRunId).toBe(failedRunId);
    expect(task(7).error).toBeUndefined();
    await vi.advanceTimersByTimeAsync(1000);
    await mockApi.postParser.startAllPostParserTasks();
    for (const item of pending) {
      expect(task(item.id).workflowStatus).toBe(WorkflowRunStatus.Success);
      expect(task(item.id).results?.[PostParseTarget.DownloadInfo]).toMatchObject({
        isComplete: true,
      });
      expect((await history(item.id)).totalCount).toBe(1);
    }
    expect(task(7).workflowStatus).toBe(WorkflowRunStatus.Success);
    expect(task(7).results?.[PostParseTarget.DownloadInfo]).toMatchObject({ isComplete: true });
    expect(task(7).workflowRunId).toBe(failedRunId);
    expect((await history(7)).totalCount).toBe(1);
  });

  it("recovers all failed input kinds without restarting active, waiting or completed posts", async () => {
    const saved = fixtures.createFixtures();
    const legacy = saved.find((item) => item.id === 10)!;
    const extra = [
      { ...legacy, id: 11, error: "Legacy fetch error" },
      { ...legacy, id: 12, workflowRunId: 1012, workflowStatus: WorkflowRunStatus.Cancelled },
      { ...legacy, id: 13, workflowRunId: 1013, workflowStatus: WorkflowRunStatus.Interrupted },
      { ...legacy, id: 14, workflowRunId: 1014, workflowStatus: WorkflowRunStatus.Running },
      { ...legacy, id: 15, workflowRunId: 1015, error: "Workflow run no longer exists" },
    ];

    vi.spyOn(fixtures, "createFixtures").mockReturnValueOnce([...saved, ...extra]);
    resetPreview();
    const selectedIds = [1, 7, 10, 11, 12, 13, 15];
    const untouched = tasks().filter((item) => !selectedIds.includes(item.id));
    const resumed = [7, 12, 13].map((id) => [id, task(id).workflowRunId] as const);

    await Promise.all([
      mockApi.postParser.startAllPostParserTasks(),
      mockApi.postParser.startAllPostParserTasks(),
      mockApi.postParser.retryPostParserTaskWorkflow(7),
      mockApi.postParser.reParsePostParserTask(11),
    ]);
    for (const id of selectedIds) {
      expect(task(id).workflowStatus).toBe(WorkflowRunStatus.Running);
      expect(task(id).error).toBeUndefined();
    }
    for (const [id, previousRunId] of resumed) {
      expect(task(id).workflowRunId).toBe(previousRunId);
      expect((await history(id)).totalCount).toBe(1);
    }
    expect(task(15).workflowRunId).not.toBe(1015);
    expect((await history(15)).totalCount).toBe(1);
    expect(tasks().filter((item) => !selectedIds.includes(item.id))).toEqual(untouched);
    await vi.advanceTimersByTimeAsync(1000);
    const completedRunIds = selectedIds.map((id) => task(id).workflowRunId);

    for (const id of selectedIds) {
      expect(task(id).workflowStatus).toBe(WorkflowRunStatus.Success);
      expect(task(id).results?.[PostParseTarget.DownloadInfo]).toMatchObject({ isComplete: true });
    }
    await mockApi.postParser.startAllPostParserTasks();
    expect(selectedIds.map((id) => task(id).workflowRunId)).toEqual(completedRunIds);
    expect(tasks().filter((item) => !selectedIds.includes(item.id))).toEqual(untouched);
  });

  it.each([undefined, null, []])(
    "restores legacy targets %s and fetches that post once without starting another post",
    async (targets) => {
      const saved = fixtures
        .createFixtures()
        .map((item) => (item.id === 10 ? { ...item, targets } : item));

      vi.spyOn(fixtures, "createFixtures").mockReturnValueOnce(saved);
      resetPreview();
      const unrelated = tasks().filter((item) => item.id !== 10);

      expect(task(10).targets).toEqual([PostParseTarget.DownloadInfo]);
      await Promise.all([
        mockApi.postParser.reParsePostParserTask(10),
        mockApi.postParser.reParsePostParserTask(10),
      ]);
      expect(task(10).workflowStatus).toBe(WorkflowRunStatus.Running);
      expect((await history(10)).totalCount).toBe(1);
      await vi.advanceTimersByTimeAsync(1000);
      expect(task(10).workflowStatus).toBe(WorkflowRunStatus.Success);
      expect(task(10).results?.[PostParseTarget.DownloadInfo]).toMatchObject({
        isComplete: true,
        resources: [{ extraction: { steps: fixtures.samplePlan.steps } }],
      });
      expect(task(10).completedAt).toBeDefined();
      expect(tasks().filter((item) => item.id !== 10)).toEqual(unrelated);
    },
  );

  it.each([
    { key: PostParseTarget.DownloadInfo, result: fixtures.completeResult("Historical result") },
    { key: "DownloadInfo", result: fixtures.completeResult("Historical result") },
    { key: "DownloadInfo", result: null },
  ])(
    "starts legacy pending input without replacing saved $key results ($result)",
    async ({ key, result }) => {
      const saved = fixtures.createFixtures();
      const completed = {
        ...saved.find((item) => item.id === 10)!,
        id: 11,
        link: "https://www.north-plus.net/read.php?tid=900011",
        title: "历史帖子 · 已有完整结果",
        targets: null,
        results: { [key]: result },
      };

      vi.spyOn(fixtures, "createFixtures").mockReturnValueOnce([...saved, completed]);
      resetPreview();
      const before = structuredClone(task(11));

      expect(before.targets).toEqual([PostParseTarget.DownloadInfo]);
      expect(before.workflowRunId).toBeUndefined();
      await Promise.all([
        mockApi.postParser.startAllPostParserTasks(),
        mockApi.postParser.startAllPostParserTasks(),
        mockApi.postParser.reParsePostParserTask(10),
      ]);
      expect(task(10).workflowStatus).toBe(WorkflowRunStatus.Running);
      expect((await history(10)).totalCount).toBe(1);
      expect(task(11)).toEqual(before);
      expect((await history(11)).totalCount).toBe(0);
      await vi.advanceTimersByTimeAsync(1000);
      expect(task(10).workflowStatus).toBe(WorkflowRunStatus.Success);
      expect(task(10).results?.[PostParseTarget.DownloadInfo]).toMatchObject({ isComplete: true });
      await mockApi.postParser.startAllPostParserTasks();
      expect((await history(10)).totalCount).toBe(1);
      expect(task(11)).toEqual(before);
      expect((await history(11)).totalCount).toBe(0);
    },
  );

  it("preserves explicitly saved nonempty targets when reading old inputs", async () => {
    const targets = [99 as PostParseTarget];
    const saved = fixtures
      .createFixtures()
      .map((item) => (item.id === 10 ? { ...item, targets } : item));

    vi.spyOn(fixtures, "createFixtures").mockReturnValueOnce(saved);
    resetPreview();
    const response = await mockApi.postParser.getAllPostParserTasks();

    expect(response.data?.find((item) => item.id === 10)?.targets).toEqual(targets);
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
