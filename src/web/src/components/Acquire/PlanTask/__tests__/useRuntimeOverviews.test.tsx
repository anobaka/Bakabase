import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import useRuntimeOverviews from "../useRuntimeOverviews";
const api = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("../api", () => ({ planTaskApi: { runtimeOverviews: api.get } }));
const snapshot = (taskId = 1, problem = "old") => ({ taskId, problem, sources: [] });
const flush = () => act(async () => {});
beforeEach(() => {
  vi.useFakeTimers();
  api.get.mockReset().mockResolvedValue([snapshot()]);
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.restoreAllMocks();
});
it("deduplicates the visible page into one request and does not refetch for equivalent rerenders", async () => {
  const view = renderHook(({ tasks }) => useRuntimeOverviews(tasks), {
    initialProps: { tasks: [{ id: 2 }, { id: 1 }, { id: 2 }] },
  });
  await flush();
  expect(api.get).toHaveBeenCalledTimes(1);
  expect(api.get.mock.calls[0][0]).toEqual([1, 2]);
  view.rerender({ tasks: [{ id: 1 }, { id: 2 }] });
  await flush();
  expect(api.get).toHaveBeenCalledTimes(1);
});
it("retains the current snapshot across pending and failed refreshes without overlapping requests", async () => {
  const view = renderHook(() => useRuntimeOverviews([{ id: 1 }]));
  await flush();
  let reject!: (error: Error) => void;
  api.get.mockImplementationOnce(
    () =>
      new Promise((_, failed) => {
        reject = failed;
      }),
  );
  await act(async () => {
    vi.advanceTimersByTime(15000);
  });
  expect(api.get).toHaveBeenCalledTimes(2);
  expect(view.result.current.overviews[1].problem).toBe("old");
  await act(async () => reject(new Error("offline")));
  expect(view.result.current.failed).toBe(true);
  expect(view.result.current.overviews[1].problem).toBe("old");
  await act(async () => vi.advanceTimersByTime(5000));
  expect(view.result.current.failed).toBe(false);
});
it("discards a response from a departed page and aborts it", async () => {
  let finish!: (data: unknown) => void;
  api.get.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const view = renderHook(({ id }) => useRuntimeOverviews([{ id }]), { initialProps: { id: 1 } });
  const oldSignal = api.get.mock.calls[0][1];
  api.get.mockResolvedValue([snapshot(2, "new")]);
  view.rerender({ id: 2 });
  await flush();
  await act(async () => finish([snapshot(1)]));
  expect(oldSignal.aborted).toBe(true);
  expect(view.result.current.overviews[1]).toBeUndefined();
  expect(view.result.current.overviews[2].problem).toBe("new");
});
it("hides cached evidence immediately when the execution or plan revision changes", async () => {
  const view = renderHook(
    ({ run, revision }) =>
      useRuntimeOverviews([{ id: 1, workflowRunId: run, planRevision: revision }]),
    { initialProps: { run: 2, revision: 1 } },
  );
  await flush();
  api.get.mockImplementation(() => new Promise(() => {}));
  view.rerender({ run: 2, revision: 2 });
  expect(view.result.current.overviews[1]).toBeUndefined();
  view.rerender({ run: 3, revision: 2 });
  expect(view.result.current.overviews[1]).toBeUndefined();
  expect(api.get).toHaveBeenCalledTimes(3);
});
it("skips background polling and refreshes on visibility restoration", async () => {
  const hidden = vi.spyOn(document, "hidden", "get").mockReturnValue(true);
  const view = renderHook(() => useRuntimeOverviews([{ id: 1 }]));
  await act(async () => vi.advanceTimersByTime(10000));
  expect(api.get).not.toHaveBeenCalled();
  hidden.mockReturnValue(false);
  await act(async () => document.dispatchEvent(new Event("visibilitychange")));
  expect(api.get).toHaveBeenCalledTimes(1);
  expect(view.result.current.overviews[1]).toBeDefined();
});
