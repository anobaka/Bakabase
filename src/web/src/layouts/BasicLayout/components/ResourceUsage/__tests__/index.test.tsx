import type { ReactNode } from "react";

import { act, cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceUsage from "../index";
import { formatUsageBytes, formatUsageUpdatedAt } from "../formatUsage";

const state = vi.hoisted(() => ({ initialized: true, data: { showResourceUsage: true } }));
const getUsage = vi.hoisted(() => vi.fn());

vi.mock("@/stores/options", () => ({
  useUiOptionsStore: (selector: (s: typeof state) => unknown) => selector(state),
}));
vi.mock("@/sdk/BApi", () => ({ default: { app: { getResourceUsage: getUsage } } }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

beforeEach(() => {
  vi.useFakeTimers();
  state.initialized = true;
  state.data.showResourceUsage = true;
  Object.defineProperty(document, "hidden", { configurable: true, value: false });
  getUsage.mockReset().mockResolvedValue({
    code: 0,
    data: { cpuPercent: 12.5, memoryBytes: 1536, dataDirectoryBytes: 3 * 1024 ** 3 },
  });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("resource usage", () => {
  it("interprets the server's UTC measurement timestamp in the viewer's timezone", () => {
    expect(formatUsageUpdatedAt("2026-10-09 05:09:22.364")).toBe(
      new Date("2026-10-09T05:09:22.364Z").toLocaleTimeString(),
    );
    expect(formatUsageUpdatedAt("2026-10-09T13:09:22.364+08:00")).toBe(
      new Date("2026-10-09T05:09:22.364Z").toLocaleTimeString(),
    );
    expect(formatUsageUpdatedAt("unknown")).toBe("—");
  });
  it("formats zero, small, large and unavailable sizes without misleading units", () => {
    expect(formatUsageBytes(0)).toBe("0 B");
    expect(formatUsageBytes(1024)).toBe("1 KiB");
    expect(formatUsageBytes(1536)).toBe("1.5 KiB");
    expect(formatUsageBytes(3 * 1024 ** 4)).toBe("3 TiB");
    expect(formatUsageBytes(undefined)).toBe("—");
    expect(formatUsageBytes(NaN)).toBe("—");
  });

  it("does not poll before options load or when display is disabled", async () => {
    state.initialized = false;
    const view = render(<ResourceUsage />);

    await act(async () => {});
    expect(getUsage).not.toHaveBeenCalled();
    state.initialized = true;
    state.data.showResourceUsage = false;
    view.rerender(<ResourceUsage />);
    await act(async () => vi.advanceTimersByTimeAsync(15000));
    expect(getUsage).not.toHaveBeenCalled();
  });

  it("shows samples, pauses hidden pages, resumes immediately, and cancels when disabled", async () => {
    const view = render(<ResourceUsage />);

    await act(async () => {});
    expect(screen.getByText("12.5%")).toBeTruthy();
    expect(screen.getByText("1.5 KiB")).toBeTruthy();
    expect(screen.getByText("3 GiB")).toBeTruthy();
    Object.defineProperty(document, "hidden", { configurable: true, value: true });
    act(() => document.dispatchEvent(new Event("visibilitychange")));
    await act(async () => vi.advanceTimersByTimeAsync(20000));
    expect(getUsage).toHaveBeenCalledTimes(1);
    Object.defineProperty(document, "hidden", { configurable: true, value: false });
    await act(async () => document.dispatchEvent(new Event("visibilitychange")));
    expect(getUsage).toHaveBeenCalledTimes(2);
    const signal = getUsage.mock.calls.at(-1)![0].signal as AbortSignal;

    state.data.showResourceUsage = false;
    view.rerender(<ResourceUsage />);
    expect(signal.aborted).toBe(true);
    await act(async () => vi.advanceTimersByTimeAsync(10000));
    expect(getUsage).toHaveBeenCalledTimes(2);
  });

  it("does not overlap slow samples and replaces failed live values with an unavailable state", async () => {
    let reject!: (reason: Error) => void;

    getUsage.mockImplementationOnce(
      () =>
        new Promise((_, no) => {
          reject = no;
        }),
    );
    render(<ResourceUsage />);
    await act(async () => vi.advanceTimersByTimeAsync(20000));
    expect(getUsage).toHaveBeenCalledTimes(1);
    await act(async () => reject(new Error("offline")));
    expect(screen.getByText("resourceUsage.unavailable")).toBeTruthy();
    expect(getUsage.mock.calls[0][0].showErrorToast).toBe(false);
    await act(async () => vi.advanceTimersByTimeAsync(5000));
    expect(screen.getByText("12.5%")).toBeTruthy();
  });

  it("times out a stalled connection and retries instead of displaying stale live samples forever", async () => {
    getUsage.mockImplementationOnce(
      ({ signal }: { signal: AbortSignal }) =>
        new Promise((_, reject) =>
          signal.addEventListener("abort", () => reject(new Error("aborted"))),
        ),
    );
    render(<ResourceUsage />);
    await act(async () => vi.advanceTimersByTimeAsync(10000));
    expect(screen.getByText("resourceUsage.unavailable")).toBeTruthy();
    await act(async () => vi.advanceTimersByTimeAsync(5000));
    expect(getUsage).toHaveBeenCalledTimes(2);
    expect(screen.getByText("12.5%")).toBeTruthy();
  });
});
