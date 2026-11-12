import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useSourceRuns } from "../useSourceRuns";
import { querySourceRuns, sourceRefKey, type SourceRef, type SourceRuns } from "../sourceHistory";
import { WorkflowRunStatus } from "@/sdk/constants";

vi.mock("../sourceHistory", async () => ({
  ...(await vi.importActual("../sourceHistory")),
  querySourceRuns: vi.fn(),
}));
const a: SourceRef = { module: "source-a", entityKind: "item", entityId: "41" };
const b: SourceRef = { module: "source-b", entityKind: "item", entityId: "42" };
const row = (source: SourceRef): SourceRuns => ({
  source,
  runCount: 1,
  runs: [
    {
      run: {
        id: 3,
        workflowDefinitionId: 4,
        parameterSources: [],
        hasPlanSnapshot: false,
        planNodes: [],
        status: WorkflowRunStatus.Success,
        startedAt: "2026-10-20T00:00:00Z",
        inputCount: 1,
        outputCount: 1,
        outputPreviewTruncated: false,
        failedItemCount: 0,
        stepStats: [],
        nodeExecutions: [],
      },
      workflowName: "Acquire",
      workflowIsBuiltin: false,
      phase: "reused",
      reused: true,
      completedFileCount: 2,
      downloadTaskIds: [],
    },
  ],
});
beforeEach(() => vi.mocked(querySourceRuns).mockReset());
describe("page-level source history", () => {
  it("keeps visible history during a refresh of the same source", async () => {
    let finish!: (value: SourceRuns[]) => void;
    vi.mocked(querySourceRuns)
      .mockResolvedValueOnce([row(a)])
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finish = resolve;
          }),
      );
    const { result } = renderHook(() => useSourceRuns([a]));
    await waitFor(() => expect(result.current.rows.size).toBe(1));
    act(() => result.current.refresh());
    expect(result.current.rows.get(sourceRefKey(a))?.runs[0].run.id).toBe(3);
    await act(async () => finish([row(a)]));
    expect(result.current.loading).toBe(false);
  });
  it("continues polling when waiting runs exist beyond the first page", async () => {
    vi.useFakeTimers();
    try {
      vi.mocked(querySourceRuns).mockResolvedValue([
        { ...row(a), statusCounts: { success: 5, waiting: 2 } },
      ]);
      renderHook(() => useSourceRuns([a]));
      await act(async () => {});
      expect(querySourceRuns).toHaveBeenCalledTimes(1);
      await act(async () => vi.advanceTimersByTimeAsync(5000));
      expect(querySourceRuns).toHaveBeenCalledTimes(2);
    } finally {
      vi.useRealTimers();
    }
  });
  it("queries multiple sources together and preserves a completed reuse without a download task", async () => {
    vi.mocked(querySourceRuns).mockResolvedValue([row(a), row(b)]);
    const { result, rerender } = renderHook(({ sources }) => useSourceRuns(sources), {
      initialProps: { sources: [a, b] },
    });
    await waitFor(() => expect(result.current.rows.size).toBe(2));
    rerender({ sources: [{ ...a }, { ...b }] });
    expect(querySourceRuns).toHaveBeenCalledExactlyOnceWith([a, b]);
    expect(result.current.rows.get(sourceRefKey(a))?.runs[0]).toMatchObject({
      reused: true,
      downloadTaskIds: [],
    });
  });
  it("ignores an old page response after the source scope changes", async () => {
    let finishOld: (value: SourceRuns[]) => void = () => {};
    vi.mocked(querySourceRuns)
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finishOld = resolve;
          }),
      )
      .mockResolvedValueOnce([row(b)]);
    const { result, rerender } = renderHook(({ sources }) => useSourceRuns(sources), {
      initialProps: { sources: [a] },
    });
    rerender({ sources: [b] });
    await waitFor(() => expect(result.current.rows.has(sourceRefKey(b))).toBe(true));
    await act(async () => finishOld([row(a)]));
    expect(result.current.rows.has(sourceRefKey(a))).toBe(false);
  });
  it("refreshes completed history when source execution metadata changes without changing its scope", async () => {
    const next = row(a);
    next.runs[0].run.id = 5;
    vi.mocked(querySourceRuns)
      .mockResolvedValueOnce([row(a)])
      .mockResolvedValueOnce([next]);
    const { result, rerender } = renderHook(
      ({ refreshKey }) => useSourceRuns([{ ...a }], refreshKey),
      { initialProps: { refreshKey: "41:1:3:success" } },
    );
    await waitFor(() => expect(result.current.rows.get(sourceRefKey(a))?.runs[0].run.id).toBe(3));
    rerender({ refreshKey: "41:1:3:success" });
    expect(querySourceRuns).toHaveBeenCalledTimes(1);
    rerender({ refreshKey: "41:2:5:success" });
    await waitFor(() => expect(result.current.rows.get(sourceRefKey(a))?.runs[0].run.id).toBe(5));
    expect(querySourceRuns).toHaveBeenNthCalledWith(2, [a]);
  });
  it("ignores a stale response after the same source history is invalidated", async () => {
    let finishOld: (value: SourceRuns[]) => void = () => {};
    const next = row(a);
    next.runs[0].run.id = 5;
    vi.mocked(querySourceRuns)
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finishOld = resolve;
          }),
      )
      .mockResolvedValueOnce([next]);
    const { result, rerender } = renderHook(({ refreshKey }) => useSourceRuns([a], refreshKey), {
      initialProps: { refreshKey: "before-start" },
    });
    rerender({ refreshKey: "after-start" });
    await waitFor(() => expect(result.current.rows.get(sourceRefKey(a))?.runs[0].run.id).toBe(5));
    await act(async () => finishOld([row(a)]));
    expect(result.current.rows.get(sourceRefKey(a))?.runs[0].run.id).toBe(5);
  });
});
