import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import useDownloadCenter from "../useDownloadCenter";
const { get } = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { downloadCenter: { getDownloadCenterTasks: get } } }));
const deferred = () => {
  let resolve!: (value: unknown) => void;
  const promise = new Promise((resolveValue) => {
    resolve = resolveValue;
  });
  return { promise, resolve };
};
beforeEach(() => {
  vi.useFakeTimers();
  get.mockReset();
});
afterEach(() => vi.useRealTimers());
describe("unified download polling", () => {
  it("loads all 698 tasks in one request and never overlaps interval refreshes", async () => {
    const first = deferred();
    get.mockReturnValue(first.promise);
    const { result } = renderHook(useDownloadCenter);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(9000);
    });
    expect(get).toHaveBeenCalledTimes(1);
    await act(async () =>
      first.resolve({
        code: 0,
        data: Array.from({ length: 698 }, (_, i) => ({ id: `site:${i}` })),
      }),
    );
    expect(result.current.tasks).toHaveLength(698);
  });
  it("ignores an older request after a manual refresh and preserves unchanged task references", async () => {
    const first = deferred();
    const fresh = { id: "bt:1", status: "completed" };
    get.mockReturnValueOnce(first.promise).mockResolvedValue({ code: 0, data: [fresh] });
    const { result } = renderHook(useDownloadCenter);
    await act(async () => result.current.refresh());
    const confirmed = result.current.tasks[0];
    await act(async () => first.resolve({ code: 0, data: [{ id: "bt:1", status: "running" }] }));
    expect(result.current.tasks[0]).toBe(confirmed);
    await act(async () => result.current.refresh());
    expect(result.current.tasks[0]).toBe(confirmed);
  });
  it("keeps the confirmed snapshot and exposes a refresh failure", async () => {
    get
      .mockResolvedValueOnce({ code: 0, data: [{ id: "http:1" }] })
      .mockResolvedValue({ code: 500 });
    const { result } = renderHook(useDownloadCenter);
    await act(async () => {});
    await act(async () => result.current.refresh());
    expect(result.current.tasks[0].id).toBe("http:1");
    expect(result.current.loadFailed).toBe(true);
  });
  it("does not issue new requests after unmount", async () => {
    get.mockResolvedValue({ code: 0, data: [] });
    const { unmount } = renderHook(useDownloadCenter);
    await act(async () => {});
    unmount();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(9000);
    });
    expect(get).toHaveBeenCalledOnce();
  });
});
