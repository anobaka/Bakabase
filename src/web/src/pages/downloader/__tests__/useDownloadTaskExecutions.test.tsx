import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import useDownloadTaskExecutions, { executionDisplayStatus } from "../useDownloadTaskExecutions";

import { DownloadTaskStatus } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: { downloadTask: { getDownloadTaskExecutions: mocks.get } },
}));

beforeEach(() => vi.resetAllMocks());
afterEach(() => vi.useRealTimers());

type Response = {
  code: number;
  data?: { downloadTaskId: number; status: string; progress?: number }[];
};

function deferredResponse() {
  let resolve!: (value: Response) => void;
  const promise = new Promise<Response>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

describe("download task associated executions", () => {
  it("batches task IDs and does not refetch for a new array with the same IDs", async () => {
    mocks.get.mockResolvedValue({
      code: 0,
      data: [{ downloadTaskId: 7, status: "running", progress: 25 }],
    });
    const { result, rerender } = renderHook(({ ids }) => useDownloadTaskExecutions(ids), {
      initialProps: { ids: [7, 8] },
    });
    await waitFor(() => expect(result.current.get(7)?.progress).toBe(25));
    expect(mocks.get).toHaveBeenCalledWith({ ids: [7, 8] });
    rerender({ ids: [7, 8] });
    expect(mocks.get).toHaveBeenCalledTimes(1);
  });

  it.each([0, 500, 501, 698, 1001])(
    "loads %i task IDs in batches of at most 500 and merges the complete snapshot",
    async (count) => {
      const ids = Array.from({ length: count }, (_, index) => index + 1);
      mocks.get.mockImplementation(async ({ ids: batch }: { ids: number[] }) => ({
        code: 0,
        data: batch.map((id) => ({ downloadTaskId: id, status: "running" })),
      }));
      const { result } = renderHook(() => useDownloadTaskExecutions(ids));
      await waitFor(() => expect(result.current.size).toBe(count));

      const batches = mocks.get.mock.calls.map(([query]) => query.ids as number[]);
      expect(batches).toHaveLength(Math.ceil(count / 500));
      expect(batches.every((batch) => batch.length <= 500)).toBe(true);
      expect(batches.flat()).toEqual(ids);
      expect([...result.current.keys()]).toEqual(ids);
    },
  );

  it("publishes only complete snapshots and skips polls while any batch is pending", async () => {
    vi.useFakeTimers();
    const first = deferredResponse();
    const second = deferredResponse();
    mocks.get.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    const ids = Array.from({ length: 698 }, (_, index) => index + 1);
    const { result } = renderHook(() => useDownloadTaskExecutions(ids));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(9000);
    });
    expect(mocks.get).toHaveBeenCalledTimes(1);
    await act(async () => {
      first.resolve({ code: 0, data: [{ downloadTaskId: 1, status: "running" }] });
    });
    expect(mocks.get).toHaveBeenCalledTimes(2);
    expect(result.current.size).toBe(0);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(9000);
    });
    expect(mocks.get).toHaveBeenCalledTimes(2);
    await act(async () => {
      second.resolve({ code: 0, data: [{ downloadTaskId: 698, status: "completed" }] });
    });
    expect([...result.current.keys()]).toEqual([1, 698]);
  });

  it.each(["business", "network"])(
    "keeps the previous complete snapshot when a later batch has a %s error, then retries",
    async (failure) => {
      vi.useFakeTimers();
      const ids = Array.from({ length: 501 }, (_, index) => index + 1);
      mocks.get
        .mockResolvedValueOnce({ code: 0, data: [{ downloadTaskId: 1, status: "running" }] })
        .mockResolvedValueOnce({ code: 0, data: [{ downloadTaskId: 501, status: "running" }] });
      const { result } = renderHook(() => useDownloadTaskExecutions(ids));
      await act(async () => {});
      const previous = result.current;
      expect(previous.size).toBe(2);

      mocks.get.mockResolvedValueOnce({
        code: 0,
        data: [{ downloadTaskId: 1, status: "completed" }],
      });
      if (failure === "business") mocks.get.mockResolvedValueOnce({ code: 400 });
      else mocks.get.mockRejectedValueOnce(new Error("offline"));
      await act(async () => {
        await vi.advanceTimersByTimeAsync(3000);
      });
      expect(mocks.get).toHaveBeenCalledTimes(4);
      expect(result.current).toBe(previous);

      mocks.get
        .mockResolvedValueOnce({ code: 0, data: [{ downloadTaskId: 1, status: "completed" }] })
        .mockResolvedValueOnce({ code: 0, data: [] });
      await act(async () => {
        await vi.advanceTimersByTimeAsync(3000);
      });
      expect(mocks.get).toHaveBeenCalledTimes(6);
      expect(result.current.get(1)?.status).toBe("completed");
      expect(result.current.has(501)).toBe(false);
    },
  );

  it("does not issue further batches for an obsolete task set", async () => {
    const old = deferredResponse();
    mocks.get
      .mockReturnValueOnce(old.promise)
      .mockResolvedValueOnce({ code: 0, data: [{ downloadTaskId: 900, status: "completed" }] });
    const { result, rerender } = renderHook(({ ids }) => useDownloadTaskExecutions(ids), {
      initialProps: { ids: Array.from({ length: 698 }, (_, index) => index + 1) },
    });
    rerender({ ids: [900] });
    await waitFor(() => expect(result.current.has(900)).toBe(true));
    await act(async () => {
      old.resolve({ code: 0, data: [{ downloadTaskId: 1, status: "running" }] });
    });
    expect(mocks.get).toHaveBeenCalledTimes(2);
    expect([...result.current.keys()]).toEqual([900]);
  });

  it("ignores a late final batch from an obsolete task set", async () => {
    const oldFinal = deferredResponse();
    mocks.get
      .mockResolvedValueOnce({ code: 0, data: [{ downloadTaskId: 1, status: "running" }] })
      .mockReturnValueOnce(oldFinal.promise)
      .mockResolvedValueOnce({ code: 0, data: [{ downloadTaskId: 900, status: "completed" }] });
    const { result, rerender } = renderHook(({ ids }) => useDownloadTaskExecutions(ids), {
      initialProps: { ids: Array.from({ length: 698 }, (_, index) => index + 1) },
    });
    await waitFor(() => expect(mocks.get).toHaveBeenCalledTimes(2));
    rerender({ ids: [900] });
    await waitFor(() => expect(result.current.has(900)).toBe(true));
    await act(async () => {
      oldFinal.resolve({ code: 0, data: [{ downloadTaskId: 698, status: "running" }] });
    });
    expect([...result.current.keys()]).toEqual([900]);
  });

  it.each(["empty", "unmount"])(
    "stops requesting further batches after %s cleanup",
    async (cleanup) => {
      vi.useFakeTimers();
      const pending = deferredResponse();
      mocks.get.mockReturnValueOnce(pending.promise);
      const { result, rerender, unmount } = renderHook(
        ({ ids }) => useDownloadTaskExecutions(ids),
        {
          initialProps: { ids: Array.from({ length: 698 }, (_, index) => index + 1) },
        },
      );
      if (cleanup === "empty") rerender({ ids: [] });
      else unmount();
      await act(async () => {
        pending.resolve({ code: 0, data: [{ downloadTaskId: 1, status: "running" }] });
        await vi.advanceTimersByTimeAsync(6000);
      });
      expect(mocks.get).toHaveBeenCalledTimes(1);
      expect(result.current.size).toBe(0);
    },
  );

  it("ignores an older task set when its response arrives late", async () => {
    let resolveOld: (value: unknown) => void = () => undefined;
    mocks.get
      .mockReturnValueOnce(
        new Promise((resolve) => {
          resolveOld = resolve;
        }),
      )
      .mockResolvedValueOnce({ code: 0, data: [{ downloadTaskId: 8, status: "completed" }] });
    const { result, rerender } = renderHook(({ ids }) => useDownloadTaskExecutions(ids), {
      initialProps: { ids: [7] },
    });
    rerender({ ids: [8] });
    await waitFor(() => expect(result.current.has(8)).toBe(true));
    await act(async () => {
      resolveOld({ code: 0, data: [{ downloadTaskId: 7, status: "running" }] });
    });
    expect(result.current.has(7)).toBe(false);
    expect(result.current.has(8)).toBe(true);
  });

  it("does not present a finished seed fetch as an overall completed BT download", () => {
    expect(
      executionDisplayStatus(DownloadTaskStatus.Complete, { status: "running" } as never),
    ).toBe(DownloadTaskStatus.Downloading);
    expect(
      executionDisplayStatus(DownloadTaskStatus.Complete, { status: "waiting" } as never),
    ).toBe(DownloadTaskStatus.Idle);
    expect(
      executionDisplayStatus(DownloadTaskStatus.Complete, { status: "processingFailed" } as never),
    ).toBe(DownloadTaskStatus.Failed);
    expect(executionDisplayStatus(DownloadTaskStatus.Complete)).toBe(DownloadTaskStatus.Complete);
  });
});
