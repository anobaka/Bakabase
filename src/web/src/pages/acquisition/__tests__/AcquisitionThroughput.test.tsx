import type { ReactNode } from "react";
import {
  act,
  cleanup,
  fireEvent,
  render,
  renderHook,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import AcquisitionThroughput from "../components/throughput";
import ThroughputHistory from "../components/throughput/ThroughputHistory";
import useAcquisitionThroughput from "../components/throughput/useAcquisitionThroughput";
import {
  formatRate,
  formatReportedRate,
  partial,
  rate,
  sampleTime,
  taskTrends,
} from "../components/throughput/model";

const getThroughput = vi.hoisted(() => vi.fn());
vi.mock("@/sdk/BApi", () => ({
  default: { acquisition: { getAcquisitionThroughput: getThroughput } },
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { id?: number }) => key + (options?.id ?? ""),
    i18n: { language: "en" },
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: { children: ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
  Modal: ({
    title,
    children,
    onClose,
  }: {
    title: string;
    children: ReactNode;
    onClose: () => void;
  }) => (
    <div role="dialog" aria-label={title}>
      {children}
      <button onClick={onClose}>Close</button>
    </div>
  ),
  Spinner: () => <span>Loading</span>,
}));
vi.mock("react-chartjs-2", () => ({
  Line: ({
    data,
    "aria-label": label,
  }: {
    data: { datasets: { data: { x: number; y: number | null }[] }[] };
    "aria-label": string;
  }) => (
    <div
      role="img"
      aria-label={label}
      data-values={JSON.stringify(data.datasets.map((d) => d.data.map((p) => p.y)))}
    />
  ),
}));

const rates = {
  downloadBytesPerSecond: 1024,
  readBytesPerSecond: undefined,
  writeBytesPerSecond: 0,
  downloadPartial: true,
  fileIoPartial: false,
};
const end = Date.UTC(2026, 10, 24, 2, 3, 20);
const sample = (seconds: number, id?: number) => ({
  sampledAt: new Date(end + 1000 * seconds).toISOString(),
  totals: rates,
  tasks: id ? [{ taskId: id, title: `Task ${id}`, rates }] : [],
});
const latest = sample(0, 710);
const history = [sample(-20, 90), sample(-15, 710), latest];
beforeEach(() => {
  Object.defineProperty(document, "hidden", { configurable: true, value: false });
  getThroughput.mockReset().mockImplementation(async ({ includeHistory }) => ({
    code: 0,
    data: {
      latest,
      history: includeHistory ? history : [],
      intervalSeconds: 5,
      windowSeconds: 300,
    },
  }));
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

it("distinguishes unavailable, partial and measured zero rates", () => {
  expect(formatRate(rate(rates, "read"))).toBe("—");
  expect(formatRate(rate(rates, "write"))).toBe("0 B/s");
  expect(formatRate(rate(rates, "download"))).toBe("1.0 KiB/s");
  expect(formatRate(0.2)).toBe("0.2 B/s");
  expect(formatReportedRate("read", 1024, true)).toBe("1.0 KiB/s*");
  expect(formatReportedRate("write", null, true)).toBe("—");
  expect(rate(undefined, "write")).toBeNull();
  expect(partial(rates, "download")).toBe(true);
  expect(partial(rates, "read")).toBe(false);
  expect(sampleTime({ ...latest, sampledAt: "2026-11-24 02:03:20.000" })).toBe(end);
});

it("breaks the trend at missing buckets and absent tasks and preserves every observed task", () => {
  const { titles, ticks } = taskTrends([latest, history[0], history[1], latest], 5);
  expect([...titles.keys()]).toEqual([90, 710]);
  expect(ticks.map((tick) => (tick.time - end) / 1000)).toEqual([-20, -15, -10, 0]);
  expect(ticks.map((tick) => rate(tick.tasks.get(710), "write"))).toEqual([null, 0, null, 0]);
});

it("opens the server-wide history and shows historical contributors outside the latest snapshot", async () => {
  render(<AcquisitionThroughput />);
  const button = await screen.findByRole("button", { name: /1\.0 KiB\/s/ });
  expect(button.textContent).toContain("≥ ");
  expect(getThroughput.mock.calls[0][0]).toEqual({ includeHistory: false });
  fireEvent.click(button);
  const dialog = await screen.findByRole("dialog");
  expect(await within(dialog).findByText("#90 Task 90")).toBeTruthy();
  expect(within(dialog).getByText("#710 Task 710")).toBeTruthy();
  const chart = within(dialog).getByRole("img", { name: /taskChart710/ });
  expect(JSON.parse(chart.getAttribute("data-values")!)).toEqual([
    [null, 1024, null, 1024],
    [null, null, null, null],
    [null, 0, null, 0],
  ]);
  expect(getThroughput.mock.calls[1][0]).toEqual({ includeHistory: true });
  fireEvent.click(within(dialog).getByText("Close"));
  await waitFor(() => expect(getThroughput.mock.calls[2][0]).toEqual({ includeHistory: false }));
});

it("distinguishes an empty measured window from the first sample still pending", async () => {
  const idle = {
    ...latest,
    tasks: [],
    totals: { ...rates, downloadBytesPerSecond: 0, readBytesPerSecond: 0, downloadPartial: false },
  };
  getThroughput.mockResolvedValue({ code: 0, data: { latest: idle, history: [idle] } });
  const page = render(<AcquisitionThroughput />);
  const button = await screen.findByRole("button", { name: /0 B\/s/ });
  fireEvent.click(button);
  expect(
    await within(screen.getByRole("dialog")).findByText("acquisition.throughput.empty"),
  ).toBeTruthy();
  page.unmount();
  getThroughput.mockResolvedValue({ code: 0, data: { latest: null, history: [] } });
  render(<AcquisitionThroughput />);
  const awaiting = await screen.findByRole("button", { name: /noSamples/ });
  expect(awaiting.textContent).not.toContain("0 B/s");
});

it("keeps the last measured window through disconnect and retries without overlapping requests", async () => {
  vi.useFakeTimers();
  const { result, unmount } = renderHook(() => useAcquisitionThroughput(true));
  await act(async () => {});
  expect(result.current.snapshot?.history).toEqual(history);
  getThroughput.mockRejectedValueOnce(new Error("offline"));
  await act(async () => {
    await vi.advanceTimersByTimeAsync(5000);
  });
  expect(result.current.disconnected).toBe(true);
  expect(result.current.snapshot?.history).toEqual(history);
  await act(async () => {
    await vi.advanceTimersByTimeAsync(5000);
  });
  expect(result.current.disconnected).toBe(false);
  await act(async () => {
    await vi.advanceTimersByTimeAsync(5000);
  });
  expect(result.current.delayed).toBe(true);
  unmount();
  expect(getThroughput.mock.calls[getThroughput.mock.calls.length - 1][1].signal.aborted).toBe(
    true,
  );
});

it("distinguishes missing telemetry from idle and covers mixed and skipped sampling intervals", () => {
  const missing = { ...sample(0), totals: { downloadPartial: true, fileIoPartial: true } };
  const idle = {
    ...sample(-5),
    totals: {
      downloadBytesPerSecond: 0,
      readBytesPerSecond: 0,
      writeBytesPerSecond: 0,
      downloadPartial: false,
      fileIoPartial: false,
    },
  };
  const snapshot = { latest: missing, history: [missing], intervalSeconds: 5, windowSeconds: 300 };
  const page = render(<ThroughputHistory snapshot={snapshot} current />);
  expect(screen.getByText("acquisition.throughput.unobservedWindow")).toBeTruthy();
  expect(screen.queryByText("acquisition.throughput.empty")).toBeNull();
  page.rerender(<ThroughputHistory snapshot={{ ...snapshot, history: [idle, missing] }} current />);
  expect(screen.getByText("acquisition.throughput.incompleteWindow")).toBeTruthy();
  const later = { ...idle, sampledAt: sample(10).sampledAt };
  page.rerender(
    <ThroughputHistory snapshot={{ ...snapshot, latest: later, history: [idle, later] }} current />,
  );
  expect(screen.getByText("acquisition.throughput.incompleteWindow")).toBeTruthy();
  page.rerender(
    <ThroughputHistory snapshot={{ ...snapshot, latest: idle, history: [idle] }} current />,
  );
  expect(screen.getByText("acquisition.throughput.empty")).toBeTruthy();
});
