import type { ReactNode } from "react";

import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DownloadResultsPanel from "../DownloadResultsPanel";

import { WorkflowRunStatus } from "@/sdk/constants";

const { getDownloadResults, retryDownloadResultWorkflow } = vi.hoisted(() => ({
  getDownloadResults: vi.fn(),
  retryDownloadResultWorkflow: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { downloader: { getDownloadResults, retryDownloadResultWorkflow } },
}));
vi.mock("@/components/Workflow/WorkflowRunsDrawer", () => ({
  default: ({
    workflowDefinitionId,
    workflowName,
  }: {
    workflowDefinitionId: number;
    workflowName: string;
  }) => <div data-workflow={workflowDefinitionId}>{workflowName}</div>,
}));
vi.mock("@/components/bakaui", () => ({
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Spinner: () => <span>Loading</span>,
  Button: ({
    as,
    href,
    children,
    onPress,
    isDisabled,
    "aria-label": label,
  }: {
    as?: string;
    href?: string;
    children: ReactNode;
    onPress?: () => void;
    isDisabled?: boolean;
    "aria-label"?: string;
  }) =>
    as === "a" ? (
      <a href={href}>{children}</a>
    ) : (
      <button aria-label={label} disabled={isDisabled} onClick={onPress}>
        {children}
      </button>
    ),
}));

const result = (overrides = {}) => ({
  id: 7,
  downloadTaskId: 3,
  name: "Gallery example",
  kind: 1,
  contentsReady: false,
  filterDidNotMatch: false,
  canRetry: false,
  ...overrides,
});
let container: HTMLDivElement;
let root: Root;
const button = (label: string) =>
  Array.from(container.querySelectorAll("button")).find((element) => element.textContent === label);

beforeEach(() => {
  vi.resetAllMocks();
  vi.useFakeTimers();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("per-gallery download results", () => {
  it("distinguishes a saved torrent from downloaded files and refreshes the completed directory", async () => {
    getDownloadResults.mockResolvedValueOnce({ data: [result()] }).mockResolvedValue({
      data: [
        result({
          contentsReady: true,
          contentsDirectory: "/library/gallery",
          workflowDefinitionId: 4,
          workflowRunId: 12,
          workflowStatus: WorkflowRunStatus.Success,
        }),
      ],
    });

    await act(async () => root.render(<DownloadResultsPanel taskId={3} />));
    expect(getDownloadResults).toHaveBeenCalledWith({ taskId: 3 });
    expect(container).toHaveTextContent("downloader.results.torrentReady");
    expect(container).not.toHaveTextContent("downloader.results.contentsReady");
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(container).toHaveTextContent("downloader.results.contentsReady");
    expect(container).toHaveTextContent("/library/gallery");
    expect(container).toHaveTextContent("workflow.runs.status.Success");
  });

  it("retries only the requested result and reloads its workflow state", async () => {
    getDownloadResults.mockResolvedValue({
      data: [
        result({
          workflowDefinitionId: 4,
          workflowRunId: 12,
          workflowStatus: WorkflowRunStatus.Failed,
          error: "Peer disconnected",
          canRetry: true,
        }),
      ],
    });
    retryDownloadResultWorkflow.mockResolvedValue({ code: 0 });
    await act(async () => root.render(<DownloadResultsPanel taskId={3} />));
    expect(container).toHaveTextContent("Peer disconnected");
    await act(async () => button("downloader.results.retry")!.click());
    expect(retryDownloadResultWorkflow).toHaveBeenCalledWith(7);
    expect(getDownloadResults).toHaveBeenCalledTimes(2);
  });

  it("links acquisition-owned results to the original run without offering standalone retry", async () => {
    getDownloadResults.mockResolvedValue({
      data: [
        result({
          acquisitionTaskId: 22,
          workflowDefinitionId: 4,
          workflowRunId: 12,
          workflowName: "Original acquisition",
          workflowStatus: WorkflowRunStatus.Running,
        }),
      ],
    });
    await act(async () => root.render(<DownloadResultsPanel taskId={3} />));
    expect(container).toHaveTextContent("downloader.results.acquisitionOwned");
    expect(button("downloader.results.retry")).toBeUndefined();
    expect(container.querySelector("a")).toHaveAttribute("href", "#/acquisitions?tab=all");
    await act(async () => button("downloader.results.viewRun")!.click());
    expect(container.querySelector('[data-workflow="4"]')).toHaveTextContent(
      "Original acquisition · #12",
    );
  });

  it("shows retry errors without pretending a new run started", async () => {
    getDownloadResults.mockResolvedValue({ data: [result({ canRetry: true })] });
    retryDownloadResultWorkflow.mockResolvedValue({
      code: 400,
      message: "Cannot retry a running result",
    });
    await act(async () => root.render(<DownloadResultsPanel taskId={3} />));
    await act(async () => button("downloader.results.retry")!.click());
    expect(container.querySelector('[role="alert"]')).toHaveTextContent(
      "downloader.results.retryFailed",
    );
    expect(getDownloadResults).toHaveBeenCalledTimes(1);
  });
});
