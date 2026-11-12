import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import usePostParserTaskPage, { type PostParserPageData } from "../usePostParserTaskPage";
import { usePostParserTasksStore } from "@/stores/postParserTasks";
import { taskStatusGroups } from "../taskStatus";

const api = vi.hoisted(() => ({ search: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { postParser: { searchPostParserTasks: api.search } } }));

const page = (updates: Partial<PostParserPageData> = {}): PostParserPageData => ({
  items: [],
  pageIndex: 1,
  pageSize: 20,
  totalCount: 0,
  globalTotal: 0,
  statusCounts: Object.fromEntries(
    taskStatusGroups.map((group) => [group, 0]),
  ) as PostParserPageData["statusCounts"],
  startableCount: 0,
  pendingHandoffCount: 0,
  runningStages: {},
  attentionReasonCounts: {},
  ...updates,
});
const response = (data = page()) => ({ code: 0, data });
const deferred = () => {
  let resolve!: (value: ReturnType<typeof response>) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<ReturnType<typeof response>>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
};
const initial = { keyword: "", status: "all" as const, pageIndex: 1, pageSize: 20 };
const advance = (milliseconds: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });

beforeEach(() => {
  vi.useFakeTimers();
  api.search.mockReset().mockResolvedValue(response());
  usePostParserTasksStore.getState().setTasks([]);
});
afterEach(() => vi.useRealTimers());

describe("server paged post parser query", () => {
  it("keeps initial counts unknown while the first bounded page is loading", async () => {
    const pending = deferred();
    api.search.mockReturnValue(pending.promise);
    const view = renderHook(() => usePostParserTaskPage(initial));
    expect(view.result.current.loading).toBe(true);
    expect(view.result.current.data).toBeUndefined();
    expect(api.search).toHaveBeenCalledExactlyOnceWith(
      { status: "all", pageIndex: 1, pageSize: 20 },
      expect.objectContaining({ sensitiveResponse: true, signal: expect.any(AbortSignal) }),
    );
    await act(async () => pending.resolve(response(page({ globalTotal: 971, totalCount: 971 }))));
    expect(view.result.current.loading).toBe(false);
    expect(view.result.current.data?.globalTotal).toBe(971);
  });

  it("debounces server search, retains the accepted page and blocks stale-page interaction", async () => {
    const view = renderHook((props) => usePostParserTaskPage(props), { initialProps: initial });
    await act(async () => {});
    const accepted = view.result.current.data;
    view.rerender({ ...initial, keyword: "  secret " });
    expect(view.result.current.data).toBe(accepted);
    expect(view.result.current.changing).toBe(true);
    await advance(100);
    view.rerender({ ...initial, keyword: "  secret code  " });
    await advance(179);
    expect(api.search).toHaveBeenCalledTimes(1);
    await advance(1);
    expect(api.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: "secret code" }),
      expect.anything(),
    );
    expect(api.search).toHaveBeenCalledTimes(2);
    expect(view.result.current.changing).toBe(false);
  });

  it("rejects a late old-page response without remounting the accepted scope during polling", async () => {
    const old = deferred();
    api.search.mockReturnValueOnce(old.promise).mockResolvedValue(response(page({ pageIndex: 2 })));
    const view = renderHook((props) => usePostParserTaskPage(props), { initialProps: initial });
    const firstSignal = api.search.mock.calls[0][1].signal;
    view.rerender({ ...initial, pageIndex: 2 });
    await act(async () => {});
    expect(firstSignal.aborted).toBe(true);
    const scope = view.result.current.scope;
    expect(view.result.current.data?.pageIndex).toBe(2);
    await act(async () => old.resolve(response(page({ globalTotal: 999 }))));
    expect(view.result.current.data?.pageIndex).toBe(2);
    await act(async () => view.result.current.refresh());
    expect(view.result.current.scope).toBe(scope);
  });

  it("ignores an old page failure after a newer page has loaded", async () => {
    const old = deferred();
    api.search
      .mockReturnValueOnce(old.promise)
      .mockResolvedValue(response(page({ pageIndex: 2, totalCount: 40 })));
    const view = renderHook((props) => usePostParserTaskPage(props), { initialProps: initial });
    view.rerender({ ...initial, pageIndex: 2 });
    await act(async () => {});
    const accepted = view.result.current.data;
    await act(async () => old.reject(new Error("old request failed after abort")));
    expect(view.result.current.data).toBe(accepted);
    expect(view.result.current.data?.pageIndex).toBe(2);
    expect(view.result.current.loading).toBe(false);
    expect(view.result.current.error).toBe(false);
    expect(api.search).toHaveBeenCalledTimes(2);
  });

  it("refreshes once after invalidation arrives during an in-flight snapshot, even if that snapshot is idle", async () => {
    const old = deferred();
    api.search
      .mockReturnValueOnce(old.promise)
      .mockResolvedValue(response(page({ globalTotal: 2 })));
    const view = renderHook(() => usePostParserTaskPage(initial));
    act(() => usePostParserTasksStore.getState().notifyChanged());
    await advance(350);
    expect(api.search).toHaveBeenCalledTimes(1);
    await act(async () => old.resolve(response(page({ globalTotal: 1 }))));
    expect(api.search).toHaveBeenCalledTimes(2);
    expect(view.result.current.data?.globalTotal).toBe(2);
  });

  it("coalesces continuous events within a fixed window and never overlaps ordinary refreshes", async () => {
    const pending = deferred();
    api.search.mockResolvedValueOnce(response()).mockReturnValue(pending.promise);
    renderHook(() => usePostParserTaskPage(initial));
    await act(async () => {});
    for (let index = 0; index < 5; index++) {
      act(() => usePostParserTasksStore.getState().notifyChanged());
      await advance(100);
    }
    expect(api.search).toHaveBeenCalledTimes(2);
    await advance(350);
    expect(api.search).toHaveBeenCalledTimes(2);
    await act(async () => pending.resolve(response()));
    expect(api.search).toHaveBeenCalledTimes(3);
  });

  it("does not run a queued follow-up after unmount", async () => {
    const pending = deferred();
    api.search.mockReturnValue(pending.promise);
    const view = renderHook(() => usePostParserTaskPage(initial));
    act(() => usePostParserTasksStore.getState().notifyChanged());
    await advance(350);
    view.unmount();
    await act(async () => pending.resolve(response()));
    await advance(5000);
    expect(api.search).toHaveBeenCalledTimes(1);
  });

  it("removes pending invalidation, active polling and visibility refreshes on unmount", async () => {
    api.search.mockResolvedValue(response(page({ pendingHandoffCount: 1 })));
    const view = renderHook(() => usePostParserTaskPage(initial));
    await act(async () => {});
    act(() => usePostParserTasksStore.getState().notifyChanged());
    view.unmount();
    act(() => {
      window.dispatchEvent(new Event("focus"));
      document.dispatchEvent(new Event("visibilitychange"));
      usePostParserTasksStore.getState().notifyChanged();
    });
    await advance(6000);
    expect(api.search).toHaveBeenCalledTimes(1);
  });

  it("shows an initial load failure without inventing empty counts and can retry the same page", async () => {
    const retry = deferred();
    api.search
      .mockResolvedValueOnce({ code: 1, data: undefined })
      .mockReturnValueOnce(retry.promise);
    const view = renderHook(() => usePostParserTaskPage({ ...initial, pageIndex: 3 }));
    await act(async () => {});
    expect(view.result.current.error).toBe(true);
    expect(view.result.current.loading).toBe(false);
    expect(view.result.current.data).toBeUndefined();
    act(() => {
      void view.result.current.refresh();
    });
    expect(view.result.current.loading).toBe(true);
    expect(api.search).toHaveBeenLastCalledWith(
      expect.objectContaining({ pageIndex: 3 }),
      expect.anything(),
    );
    await act(async () =>
      retry.resolve(response(page({ pageIndex: 3, globalTotal: 100, totalCount: 100 }))),
    );
    expect(view.result.current.loading).toBe(false);
    expect(view.result.current.error).toBe(false);
    expect(view.result.current.data?.globalTotal).toBe(100);
  });

  it("retains prior rows on refresh errors and supports an explicit retry", async () => {
    api.search
      .mockResolvedValueOnce(response(page({ globalTotal: 5 })))
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValue(response(page({ globalTotal: 6 })));
    const view = renderHook(() => usePostParserTaskPage(initial));
    await act(async () => {});
    await act(async () => view.result.current.refresh());
    expect(view.result.current.error).toBe(true);
    expect(view.result.current.data?.globalTotal).toBe(5);
    await act(async () => view.result.current.refresh());
    expect(view.result.current.error).toBe(false);
    expect(view.result.current.data?.globalTotal).toBe(6);
  });

  it("polls pending handoffs and stops when the whole server summary is idle", async () => {
    api.search
      .mockResolvedValueOnce(response(page({ pendingHandoffCount: 1 })))
      .mockResolvedValue(response());
    renderHook(() => usePostParserTaskPage(initial));
    await act(async () => {});
    await advance(2000);
    expect(api.search).toHaveBeenCalledTimes(2);
    await advance(6000);
    expect(api.search).toHaveBeenCalledTimes(2);
  });

  it("does not queue an extra refresh when polling overlaps a slow response", async () => {
    const pending = deferred();
    const active = page({ pendingHandoffCount: 1 });
    api.search
      .mockResolvedValueOnce(response(active))
      .mockReturnValueOnce(pending.promise)
      .mockResolvedValue(response(active));
    renderHook(() => usePostParserTaskPage(initial));
    await act(async () => {});
    await advance(2000);
    expect(api.search).toHaveBeenCalledTimes(2);
    await advance(4000);
    expect(api.search).toHaveBeenCalledTimes(2);
    await act(async () => pending.resolve(response(active)));
    expect(api.search).toHaveBeenCalledTimes(2);
    await advance(2000);
    expect(api.search).toHaveBeenCalledTimes(3);
  });

  it("keeps the row-action refresh callback stable across search changes and inert after unmount", async () => {
    const view = renderHook((props) => usePostParserTaskPage(props), { initialProps: initial });
    await act(async () => {});
    const refresh = view.result.current.refresh;
    view.rerender({ ...initial, keyword: "new keyword" });
    expect(view.result.current.refresh).toBe(refresh);
    await advance(180);
    expect(view.result.current.refresh).toBe(refresh);
    view.unmount();
    const requests = api.search.mock.calls.length;
    await act(async () => refresh());
    expect(api.search).toHaveBeenCalledTimes(requests);
  });
  it("includes the selected server-projected reason in the request and accepted navigation scope", async () => {
    const view = renderHook(
      (props: typeof initial & { reasonCode?: string }) => usePostParserTaskPage(props),
      { initialProps: { ...initial, reasonCode: undefined as string | undefined } },
    );
    await act(async () => {});
    const previous = view.result.current.scope;
    view.rerender({ ...initial, reasonCode: "priceUnknown" });
    await act(async () => {});
    expect(api.search.mock.lastCall?.[0]).toMatchObject({ reasonCode: "priceUnknown" });
    expect(view.result.current.scope).not.toBe(previous);
    expect(view.result.current.changing).toBe(false);
  });
});
