import type { ReactNode } from "react";

import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import BtDownloadsPage from "..";

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  stop: vi.fn(),
  resume: vi.fn(),
  retry: vi.fn(),
  remove: vi.fn(),
  portal: vi.fn(),
  query: new URLSearchParams(),
  setQuery: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    btDownloads: {
      getBtDownloads: mocks.get,
      stopBtDownload: mocks.stop,
      resumeBtDownload: mocks.resume,
      retryBtDownload: mocks.retry,
      deleteBtDownload: mocks.remove,
    },
  },
}));
vi.mock("react-router-dom", () => ({ useSearchParams: () => [mocks.query, mocks.setQuery] }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("../AddBtDownloadModal", () => ({ default: () => null }));
vi.mock("../BtSettingsPanel", () => ({ default: () => <div>bt-settings</div> }));
vi.mock("@/pages/downloader/components/TaskDetailModal", () => ({ default: () => null }));
vi.mock("@/components/Workflow/WorkflowRunDetailModal", () => ({ default: () => null }));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
    "aria-label": label,
  }: {
    children: ReactNode;
    onPress?: () => void;
    isDisabled?: boolean;
    "aria-label"?: string;
  }) => (
    <button aria-label={label} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Spinner: () => <span>loading</span>,
  Input: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: string;
    onValueChange: (v: string) => void;
  }) => <input aria-label={label} value={value} onChange={(e) => onValueChange(e.target.value)} />,
  Select: ({
    label,
    dataSource,
    selectedKeys,
    onSelectionChange,
  }: {
    label: string;
    dataSource: { value: string; label: string }[];
    selectedKeys: string[];
    onSelectionChange: (v: Set<string>) => void;
  }) => (
    <select
      aria-label={label}
      value={selectedKeys[0]}
      onChange={(e) => onSelectionChange(new Set([e.target.value]))}
    >
      {dataSource.map((x) => (
        <option key={x.value} value={x.value}>
          {x.label}
        </option>
      ))}
    </select>
  ),
  Progress: ({ value, "aria-label": label }: { value: number; "aria-label": string }) => (
    <progress aria-label={label} max={100} value={value} />
  ),
}));

const job = (overrides = {}) => ({
  id: "bt-1",
  name: "Gallery archive",
  status: 2,
  percentage: 50,
  completedBytes: 1024,
  totalBytes: 2048,
  speedBytesPerSecond: 128,
  peers: 3,
  outputDirectory: "/downloads/gallery",
  files: [],
  canStop: true,
  canResume: false,
  canRetry: false,
  canDelete: false,
  ...overrides,
});

beforeEach(() => {
  vi.clearAllMocks();
  mocks.query = new URLSearchParams();
  mocks.get.mockResolvedValue({ code: 0, data: [job()] });
});
afterEach(() => {
  vi.useRealTimers();
});

describe("BT downloads page", () => {
  it("shows an independently managed transfer and only its supported actions", async () => {
    mocks.stop.mockResolvedValue({ code: 0 });
    render(<BtDownloadsPage />);
    expect(await screen.findByText("Gallery archive")).toBeInTheDocument();
    expect(screen.getByText(/1.0 KiB/)).toHaveTextContent("2.0 KiB");
    expect(screen.queryByText("bt.action.delete")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("bt.action.stop"));
    await waitFor(() => expect(mocks.stop).toHaveBeenCalledWith("bt-1"));
    expect(mocks.get).toHaveBeenCalledTimes(2);
  });

  it("opens the exact workflow instance and source task of a linked job", async () => {
    mocks.get.mockResolvedValue({ code: 0, data: [job({ workflowRunId: 51, downloadTaskId: 9 })] });
    render(<BtDownloadsPage />);
    fireEvent.click(await screen.findByText("bt.action.workflow"));
    expect(mocks.portal).toHaveBeenLastCalledWith(expect.any(Function), { runId: 51 });
    fireEvent.click(screen.getByText("bt.action.source"));
    expect(mocks.portal).toHaveBeenLastCalledWith(expect.any(Function), { id: 9 });
  });

  it("filters a source link to its BT job and lets the user show all jobs", async () => {
    mocks.query = new URLSearchParams("id=bt-2");
    mocks.get.mockResolvedValue({
      code: 0,
      data: [job(), job({ id: "bt-2", name: "Selected gallery" })],
    });
    render(<BtDownloadsPage />);
    expect(await screen.findByText("Selected gallery")).toBeInTheDocument();
    expect(screen.queryByText("Gallery archive")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("bt.filter.showAll"));
    expect(mocks.setQuery).toHaveBeenCalledWith({});
  });

  it("keeps errors visible when a stop is rejected", async () => {
    mocks.stop.mockResolvedValue({ code: 409, message: "Still stopping" });
    render(<BtDownloadsPage />);
    fireEvent.click(await screen.findByText("bt.action.stop"));
    expect(await screen.findByRole("alert")).toHaveTextContent("Still stopping");
  });

  it("does not offer deletion or retry unless the server grants it", async () => {
    mocks.get.mockResolvedValue({
      code: 0,
      data: [job({ status: 6, canStop: false, canRetry: true })],
    });
    mocks.retry.mockResolvedValue({ code: 0 });
    render(<BtDownloadsPage />);
    fireEvent.click(await screen.findByText("bt.action.retry"));
    await waitFor(() => expect(mocks.retry).toHaveBeenCalledWith("bt-1"));
    expect(screen.queryByText("bt.action.delete")).not.toBeInTheDocument();
  });
});
