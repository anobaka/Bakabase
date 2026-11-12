import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import StorageCapacitySummary from "../StorageCapacitySummary";
import en from "@/locales/en/components/fileSystemSelector.json";

const api = vi.hoisted(() => ({ capacity: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { file: { getPathStorageCapacity: api.capacity } } }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({
  i18n: { language: "en" },
  t: (key: string, params: Record<string, string> = {}) => Object.entries(params).reduce(
    (text, [name, value]) => text.replace(`{{${name}}}`, String(value)), (en as Record<string, string>)[key] ?? key,
  ),
}) }));
vi.mock("@/components/bakaui", () => ({
  Tooltip: ({children, content}: any) => <span title={content}>{children}</span>,
  Spinner: ({"aria-label": label}: any) => <span role="status" aria-label={label} />,
  Progress: ({value, "aria-label": label}: any) => <div role="progressbar" aria-label={label} aria-valuenow={value} />,
  Button: ({children, onPress, isDisabled, "aria-label": label}: any) =>
    <button aria-label={label} disabled={isDisabled} onClick={onPress}>{children}</button>,
}));
const gib = 1024 ** 3;
const available = (free = 2 * gib, total = 4 * gib) => ({code: 0, data: {
  path: "/server/path", status: "available", availableBytes: free, totalBytes: total,
}});
beforeEach(() => { vi.useFakeTimers(); api.capacity.mockReset().mockResolvedValue(available()); });
afterEach(() => { cleanup(); vi.useRealTimers(); });
const debounce = () => act(async () => { await vi.advanceTimersByTimeAsync(300); });

it("debounces path drafts and shows the selected server volume capacity with remaining percentage", async () => {
  const view = render(<StorageCapacitySummary path="/first" />);
  view.rerender(<StorageCapacitySummary path=" /server/new-path " />);
  expect(api.capacity).not.toHaveBeenCalled();
  await debounce();
  expect(api.capacity).toHaveBeenCalledExactlyOnceWith({path: "/server/new-path"}, expect.objectContaining({showErrorToast: false, signal: expect.any(AbortSignal)}));
  expect(screen.getByText("2 GiB free / 4 GiB total (50% remaining)")).toBeVisible();
  expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "50");
  expect(screen.getByTitle(en["fileSystemSelector.capacity.scope"])).toBeVisible();
});

it("ignores an older request that completes after a different directory was selected", async () => {
  let old!: (value: unknown) => void;
  api.capacity.mockImplementationOnce(() => new Promise(resolve => {old = resolve;}));
  const view = render(<StorageCapacitySummary path="/old" />);
  await debounce();
  const signal = api.capacity.mock.calls[0][1].signal;
  view.rerender(<StorageCapacitySummary path="/new" />);
  expect(signal.aborted).toBe(true);
  await debounce();
  expect(screen.getByText("2 GiB free / 4 GiB total (50% remaining)")).toBeVisible();
  await act(async () => old(available(0, gib)));
  expect(screen.getByText("2 GiB free / 4 GiB total (50% remaining)")).toBeVisible();
  expect(screen.queryByText(/0 GiB free/)).toBeNull();
});

it("shows an explicit unknown path without querying the browser disk or an assumed filesystem root", async () => {
  const view = render(<StorageCapacitySummary path=" " emptyHint="Resolved by the acquisition method" />);
  await debounce();
  expect(screen.getByText("Resolved by the acquisition method")).toBeVisible();
  expect(api.capacity).not.toHaveBeenCalled();
  view.rerender(<StorageCapacitySummary />);
  expect(screen.getByText(en["fileSystemSelector.capacity.unknown"])).toBeVisible();
});

it.each(["unavailable", "unknown"])("keeps %s distinct from zero free space", async (status) => {
  api.capacity.mockResolvedValue({code: 0, data: {status}});
  render(<StorageCapacitySummary path="/nas" />);
  await debounce();
  expect(screen.getByText(en[`fileSystemSelector.capacity.${status}` as keyof typeof en])).toBeVisible();
  expect(screen.queryByRole("progressbar")).toBeNull();
  expect(screen.queryByText(/0 GiB/)).toBeNull();
});

it("displays a genuine zero reading and formats terabyte totals without inventing available capacity", async () => {
  api.capacity.mockResolvedValue(available(0, 2 * 1024 ** 4));
  render(<StorageCapacitySummary path="/full" />);
  await debounce();
  expect(screen.getByText("0 GiB free / 2 TiB total (0% remaining)")).toBeVisible();
  expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "0");
});

it("bounds a stalled request and ignores late data after its deadline", async () => {
  let finish!: (value: unknown) => void;
  api.capacity.mockImplementationOnce(() => new Promise(resolve => {finish = resolve;}));
  render(<StorageCapacitySummary path="/stalled" />);
  await debounce();
  const signal = api.capacity.mock.calls[0][1].signal;
  await act(async () => { await vi.advanceTimersByTimeAsync(5000); });
  expect(signal.aborted).toBe(true);
  expect(screen.getByText(en["fileSystemSelector.capacity.unknown"])).toBeVisible();
  await act(async () => finish(available()));
  expect(screen.queryByRole("progressbar")).toBeNull();
});

it("allows explicit refresh without saving settings or reusing old capacity", async () => {
  render(<StorageCapacitySummary path="/server/path" />);
  await debounce();
  api.capacity.mockResolvedValue(available(gib, 4 * gib));
  fireEvent.click(screen.getByRole("button", {name: en["fileSystemSelector.capacity.refresh"]}));
  await debounce();
  expect(api.capacity).toHaveBeenCalledTimes(2);
  expect(screen.getByText("1 GiB free / 4 GiB total (25% remaining)")).toBeVisible();
});

it("turns a transport failure into unknown capacity rather than an inaccessible-path claim", async () => {
  api.capacity.mockRejectedValue(new Error("network unavailable"));
  render(<StorageCapacitySummary path="/server/path" />);
  await debounce();
  expect(screen.getByText(en["fileSystemSelector.capacity.unknown"])).toBeVisible();
  expect(screen.queryByText(en["fileSystemSelector.capacity.unavailable"])).toBeNull();
});
