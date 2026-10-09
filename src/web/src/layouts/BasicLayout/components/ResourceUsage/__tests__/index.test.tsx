import type { ComponentType, ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceUsage from "../index";
import { formatUsageBytes, formatUsageUpdatedAt } from "../formatUsage";

import { useUiOptionsStore } from "@/stores/options";

const { getUsage, patchOptions, updateOptions, createPortal } = vi.hoisted(() => ({
  getUsage: vi.fn(),
  patchOptions: vi.fn(),
  updateOptions: vi.fn(),
  createPortal: vi.fn(),
}));

vi.mock("@/stores/options", async () => {
  const { create } = await import("zustand");

  return {
    useUiOptionsStore: create<{
      initialized: boolean;
      data: { showResourceUsage: boolean };
      update: (payload: { showResourceUsage: boolean }) => void;
    }>((set) => ({
      initialized: true,
      data: { showResourceUsage: true },
      update: (payload) => {
        updateOptions(payload);
        set((state) => ({ data: { ...state.data, ...payload } }));
      },
    })),
  };
});
vi.mock("@/sdk/BApi", () => ({
  default: { app: { getResourceUsage: getUsage }, options: { patchUiOptions: patchOptions } },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Button: ({
    children,
    isDisabled,
    onPress,
    "aria-label": label,
  }: {
    children: ReactNode;
    isDisabled?: boolean;
    onPress: () => void;
    "aria-label"?: string;
  }) => (
    <button aria-label={label} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Modal: ({ title, children }: { title: string; children: ReactNode }) => (
    <div aria-label={title} role="dialog">
      {children}
    </div>
  ),
}));

const setEnabled = (showResourceUsage: boolean, initialized = true) =>
  useUiOptionsStore.setState((state) => ({
    initialized,
    data: { ...state.data, showResourceUsage },
  }));

beforeEach(() => {
  vi.useFakeTimers();
  setEnabled(true);
  updateOptions.mockReset();
  patchOptions.mockReset().mockResolvedValue({ code: 0 });
  createPortal
    .mockReset()
    .mockImplementation(
      (
        Component: ComponentType<{ title: string; children: ReactNode }>,
        props: { title: string; children: ReactNode },
      ) => {
        // Mirror the app provider's ownership: the notice is outside the ResourceUsage tree.
        const portal = render(<Component {...props} />);

        return { key: "notice", destroy: portal.unmount };
      },
    );
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
    setEnabled(true, false);
    render(<ResourceUsage />);

    await act(async () => {});
    expect(getUsage).not.toHaveBeenCalled();
    act(() => setEnabled(false));
    await act(async () => vi.advanceTimersByTimeAsync(15000));
    expect(getUsage).not.toHaveBeenCalled();
  });

  it("shows samples, pauses hidden pages, resumes immediately, and cancels when disabled", async () => {
    render(<ResourceUsage />);

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

    act(() => setEnabled(false));
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

  it.each([false, true])(
    "persists hiding, stops polling and preserves the notice (SignalR arrives early: %s)",
    async (signalRArrivesEarly) => {
      let finish!: (response: { code: number }) => void;

      patchOptions.mockReturnValueOnce(
        new Promise((resolve) => {
          finish = resolve;
        }),
      );
      const view = render(<ResourceUsage />);

      await act(async () => {});
      const signal = getUsage.mock.calls[0][0].signal as AbortSignal;
      const hide = screen.getByRole("button", { name: "resourceUsage.hide.label" });

      fireEvent.click(hide);
      fireEvent.click(hide);
      expect(hide).toBeDisabled();
      expect(patchOptions).toHaveBeenCalledExactlyOnceWith({ showResourceUsage: false });
      expect(updateOptions).not.toHaveBeenCalled();
      expect(createPortal).not.toHaveBeenCalled();
      if (signalRArrivesEarly) {
        act(() => setEnabled(false));
        expect(screen.queryByRole("region", { name: "resourceUsage.title" })).toBeNull();
      }

      await act(async () => finish({ code: 0 }));
      expect(updateOptions).toHaveBeenCalledExactlyOnceWith({ showResourceUsage: false });
      expect(useUiOptionsStore.getState().data.showResourceUsage).toBe(false);
      expect(screen.queryByRole("region", { name: "resourceUsage.title" })).toBeNull();
      expect(signal.aborted).toBe(true);
      expect(screen.getByRole("dialog", { name: "resourceUsage.hidden.title" })).toHaveTextContent(
        "resourceUsage.hidden.description",
      );
      expect(createPortal).toHaveBeenCalledTimes(1);
      await act(async () => vi.advanceTimersByTimeAsync(20000));
      expect(getUsage).toHaveBeenCalledTimes(1);

      view.unmount();
      expect(
        screen.getByRole("dialog", { name: "resourceUsage.hidden.title" }),
      ).toBeInTheDocument();
    },
  );

  it.each(["business", "network"])(
    "leaves the card enabled after a %s failure and allows retry",
    async (failure) => {
      if (failure === "business")
        patchOptions.mockResolvedValueOnce({ code: 500, message: "failed" });
      else patchOptions.mockRejectedValueOnce(new Error("offline"));
      render(<ResourceUsage />);
      await act(async () => {});

      await act(async () =>
        fireEvent.click(screen.getByRole("button", { name: "resourceUsage.hide.label" })),
      );
      expect(screen.getByRole("region", { name: "resourceUsage.title" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "resourceUsage.hide.label" })).toBeEnabled();
      expect(useUiOptionsStore.getState().data.showResourceUsage).toBe(true);
      expect(updateOptions).not.toHaveBeenCalled();
      expect(createPortal).not.toHaveBeenCalled();
      await act(async () => vi.advanceTimersByTimeAsync(5000));
      expect(getUsage).toHaveBeenCalledTimes(2);

      await act(async () =>
        fireEvent.click(screen.getByRole("button", { name: "resourceUsage.hide.label" })),
      );
      expect(patchOptions).toHaveBeenCalledTimes(2);
      expect(useUiOptionsStore.getState().data.showResourceUsage).toBe(false);
      expect(
        screen.getByRole("dialog", { name: "resourceUsage.hidden.title" }),
      ).toBeInTheDocument();
    },
  );
});
