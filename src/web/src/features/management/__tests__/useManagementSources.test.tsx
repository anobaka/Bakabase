import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useManagementSources } from "../useManagementSources";

const { load } = vi.hoisted(() => ({ load: vi.fn() }));

vi.mock("../api", () => ({ getManagementSources: load }));
beforeEach(() => {
  vi.useFakeTimers();
  load.mockReset();
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("local management sync status polling", () => {
  it("does not poll an ordinary all-in-one library", async () => {
    load.mockResolvedValue([{ serverId: "local", isLocal: true }]);
    const { result } = renderHook(useManagementSources);

    await act(async () => {
      await Promise.resolve();
    });
    expect(result.current.loading).toBe(false);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });
    expect(load).toHaveBeenCalledOnce();
  });
  it("polls managed status without clearing old sources or showing a full-page loading state", async () => {
    load.mockResolvedValue([{ serverId: "remote", isLocal: false }]);
    const { result, unmount } = renderHook(useManagementSources);

    await act(async () => {
      await Promise.resolve();
    });
    load.mockRejectedValueOnce(new Error("Local status unavailable"));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(result.current.sources).toEqual([{ serverId: "remote", isLocal: false }]);
    expect(result.current.error?.message).toBe("Local status unavailable");
    expect(result.current.loading).toBe(false);
    unmount();
    const count = load.mock.calls.length;

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });
    expect(load).toHaveBeenCalledTimes(count);
    expect(load.mock.calls[0][0].aborted).toBe(true);
  });
  it("ignores a stale response after a manual refresh", async () => {
    let old!: (value: unknown) => void;

    load
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            old = resolve;
          }),
      )
      .mockResolvedValue([{ serverId: "current", isLocal: true }]);
    const { result } = renderHook(useManagementSources);

    await act(async () => {
      result.current.refresh();
    });
    await act(async () => {
      old([{ serverId: "stale", isLocal: false }]);
    });
    expect(result.current.sources).toEqual([{ serverId: "current", isLocal: true }]);
  });
});
