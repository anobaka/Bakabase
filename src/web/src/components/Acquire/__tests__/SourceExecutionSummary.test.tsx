import type { ReactNode } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import SourceExecutionSummary, { SourceRunSummary } from "../SourceExecutionSummary";
import { WorkflowRunStatus } from "@/sdk/constants";
import type { SourceRuns } from "../sourceHistory";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { count?: number }) =>
      options?.count == null ? key : key + ":" + options.count,
  }),
}));
vi.mock("@/components/Workflow/displayNames", () => ({
  activityDisplayName: (_: unknown, kind: string) => kind,
}));
vi.mock("@/components/Workflow/WorkflowFileLocation", () => ({
  default: ({
    path,
    available,
    label,
  }: {
    path: string;
    available: boolean;
    label?: ReactNode;
  }) => (
    <div data-testid="file-location" data-available={available}>
      {label}
      {path}
    </div>
  ),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: { children: ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
  Spinner: () => null,
}));
it("distinguishes the workflow name, bytes, speed and a waiting reason from a generic completed badge", () => {
  const value: SourceRuns = {
    source: { module: "any", entityKind: "item", entityId: "1" },
    runCount: 1,
    runs: [
      {
        run: {
          id: 1,
          workflowDefinitionId: 3,
          parameterSources: [],
          hasPlanSnapshot: false,
          planNodes: [],
          status: WorkflowRunStatus.Waiting,
          startedAt: "2026-10-20",
          inputCount: 1,
          outputCount: 0,
          outputPreviewTruncated: false,
          failedItemCount: 0,
          stepStats: [],
          nodeExecutions: [],
          waitReason: "PasswordUnknown",
        },
        workflowName: "Acquire and unpack",
        workflowIsBuiltin: false,
        phase: "waiting",
        downloadTaskIds: ["http:1"],
        completedFileCount: 0,
        reused: false,
        downloadedBytes: 1024,
        totalBytes: 4096,
        speedBytesPerSecond: 1024,
      },
    ],
  };
  const onOpenRun = vi.fn();
  render(
    <SourceExecutionSummary
      hideWhenEmpty
      value={value}
      onHistory={() => {}}
      onOpenRun={onOpenRun}
    />,
  );
  expect(screen.getByRole("button", { name: "Acquire and unpack" })).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Acquire and unpack" }));
  expect(onOpenRun).toHaveBeenCalledWith(1);
  expect(screen.getByText("1.0 KiB / 4.0 KiB")).toBeInTheDocument();
  expect(screen.getByText("1.0 KiB/s")).toBeInTheDocument();
  expect(screen.getByText("workflow.waitReason.PasswordUnknown")).toBeInTheDocument();
});

it("distinguishes loading, unavailable status, and a confirmed absence of subsequent runs", () => {
  const view = render(<SourceExecutionSummary loading onHistory={() => {}} />);
  expect(screen.getByText("acquire.history.loading")).toBeVisible();
  expect(screen.queryByText("acquire.history.notStarted")).toBeNull();
  view.rerender(<SourceExecutionSummary onHistory={() => {}} />);
  expect(screen.getByText("acquire.history.statusUnknown")).toBeVisible();
  view.rerender(
    <SourceExecutionSummary
      value={{
        source: { module: "any", entityKind: "item", entityId: "1" },
        runCount: 0,
        runs: [],
      }}
      onHistory={() => {}}
    />,
  );
  expect(screen.getByText("acquire.history.notStarted")).toBeVisible();
  expect(screen.getByText("acquire.history.notStartedHint")).toBeVisible();
});

it("lets an acquisition owner hide a confirmed empty history while retaining loading and errors", () => {
  const empty: SourceRuns = {
    source: { module: "any", entityKind: "item", entityId: "1" },
    runCount: 0,
    runs: [],
  };
  const view = render(<SourceExecutionSummary hideWhenEmpty value={empty} onHistory={() => {}} />);
  expect(view.container).toBeEmptyDOMElement();
  view.rerender(<SourceExecutionSummary hideWhenEmpty loading onHistory={() => {}} />);
  expect(screen.getByText("acquire.history.loading")).toBeVisible();
  view.rerender(
    <SourceExecutionSummary hideWhenEmpty value={empty} error="offline" onHistory={() => {}} />,
  );
  expect(screen.getByText("acquire.history.loadFailed")).toBeVisible();
});

it("shows aggregate waiting runs beyond the current page and labels planned paths without implying files exist", () => {
  const run = {
    id: 20,
    workflowDefinitionId: 3,
    parameterSources: [],
    hasPlanSnapshot: false,
    planNodes: [],
    status: WorkflowRunStatus.Waiting,
    startedAt: "2026-10-20",
    inputCount: 1,
    outputCount: 0,
    failedItemCount: 0,
    stepStats: [],
    nodeExecutions: [],
    outputPreviewTruncated: false,
    currentStepIndex: 3,
    activityKind: "download.awaitCompletion",
    waitPromptJson: '{"password":"private-canary"}',
  };
  const value: SourceRuns = {
    source: { module: "any", entityKind: "item", entityId: "1" },
    runCount: 20,
    statusCounts: { waiting: 12 },
    runs: [
      {
        run,
        workflowName: "Acquire",
        workflowIsBuiltin: false,
        phase: "waiting",
        downloadTaskIds: ["site:42"],
        completedFileCount: 0,
        reused: false,
        problemCodes: ["PIKPAK_ORIGINAL_FILE_UNAVAILABLE"],
        outputLocations: [{ kind: "working", directory: "/downloads/next", isPlanned: true }],
      },
    ],
  };
  render(<SourceExecutionSummary value={value} onHistory={() => {}} />);
  expect(screen.getByText("acquire.history.waitingCount:12")).toBeVisible();
  expect(screen.getByText("acquire.history.noFilesYet")).toBeVisible();
  expect(screen.getByText("acquire.history.location.planned")).toBeVisible();
  expect(screen.getByTitle("/downloads/next")).toHaveTextContent("/downloads/next");
  expect(screen.queryByText("0 B")).toBeNull();
  expect(document.body.textContent).not.toContain("private-canary");
  expect(screen.getByTestId("file-location")).toHaveAttribute("data-available", "false");
});

it("explains when separate attempts share a download task", () => {
  render(
    <SourceRunSummary
      value={{
        run: {
          id: 20,
          workflowDefinitionId: 3,
          parameterSources: [],
          hasPlanSnapshot: false,
          planNodes: [],
          status: WorkflowRunStatus.Waiting,
          startedAt: "2026-10-20",
          inputCount: 1,
          outputCount: 0,
          failedItemCount: 0,
          stepStats: [],
          nodeExecutions: [],
          outputPreviewTruncated: false,
        },
        workflowName: "Acquire",
        workflowIsBuiltin: false,
        phase: "waiting",
        downloadTaskIds: ["site:42"],
        completedFileCount: 0,
        reused: false,
      }}
      sharedRunIds={[19]}
    />,
  );
  expect(screen.getByText("acquire.history.sharedDownload")).toBeVisible();
});

const deliveredRun: SourceRuns["runs"][number] = {
  run: {
    id: 30,
    workflowDefinitionId: 3,
    parameterSources: [],
    hasPlanSnapshot: false,
    planNodes: [],
    status: WorkflowRunStatus.Success,
    startedAt: "2026-10-30",
    inputCount: 1,
    outputCount: 1,
    failedItemCount: 0,
    stepStats: [],
    nodeExecutions: [],
    outputPreviewTruncated: false,
  },
  workflowName: "Acquire",
  workflowIsBuiltin: false,
  phase: "delivered",
  downloadTaskIds: [],
  completedFileCount: 1,
  reused: false,
  deliveredDirectory: "/old/hash-directory",
};

it("shows live completion instead of an earlier download wait only for the matching run and step", () => {
  const value = { ...deliveredRun, run: { ...deliveredRun.run,
    status: WorkflowRunStatus.Waiting, currentStepIndex: 2,
    activityKind: "download.awaitCompletion", waitReason: "cloud.cloudAttention",
  }, problemCodes: ["DOWNLOAD_FAILED"], downloadedBytes: 1024, totalBytes: 4096 };
  const view = render(<SourceRunSummary value={value} completedDownloadWait={{
    runId: 30, stepIndex: 2, message: "Completed downloads; further processing paused",
  }} />);
  expect(screen.getByText("Completed downloads; further processing paused")).toBeVisible();
  expect(screen.queryByText("1.0 KiB / 4.0 KiB")).toBeNull();
  expect(screen.queryByText("acquire.history.problemUnknown")).toBeNull();
  view.rerender(<SourceRunSummary value={value} completedDownloadWait={{
    runId: 31, stepIndex: 2, message: "Other run completed",
  }} />);
  expect(screen.queryByText("Other run completed")).toBeNull();
  expect(screen.getByText("1.0 KiB / 4.0 KiB")).toBeVisible();
});

it("prefers resolved published artifact locations while retaining every distinct delivery", () => {
  render(
    <SourceRunSummary
      value={{
        ...deliveredRun,
        outputLocations: [
          {
            kind: "published",
            directory: "/Acquired/first",
            artifactId: "first",
            isPlanned: false,
            state: "available",
          },
          {
            kind: "published",
            directory: "/Acquired/second",
            artifactId: "second",
            isPlanned: false,
            state: "available",
          },
          { kind: "published", directory: "/other/recorded-delivery", isPlanned: true },
        ],
      }}
    />,
  );
  expect(screen.getByText("/Acquired/first")).toBeVisible();
  expect(screen.getByText("/Acquired/second")).toBeVisible();
  expect(screen.getByText("/other/recorded-delivery")).toBeVisible();
  expect(screen.queryByText("/old/hash-directory")).not.toBeInTheDocument();
  expect(screen.getAllByTestId("file-location")).toHaveLength(3);
});

it("labels an unmatched legacy delivery as an execution-time path without claiming availability", () => {
  render(<SourceRunSummary value={deliveredRun} />);
  expect(screen.getByTestId("file-location")).toHaveTextContent(
    "acquire.history.deliveredAtExecution",
  );
  expect(screen.getByTestId("file-location")).toHaveTextContent("/old/hash-directory");
  expect(screen.getByTestId("file-location")).toHaveAttribute("data-available", "false");
  expect(screen.getByText("acquire.history.recordedDirectoryHint")).toBeVisible();
});

it("does not use planned or processed locations to supersede a historical delivery", () => {
  render(
    <SourceRunSummary
      value={{
        ...deliveredRun,
        outputLocations: [
          {
            kind: "published",
            directory: "/planned/delivery",
            artifactId: "pending",
            isPlanned: true,
          },
          {
            kind: "processed",
            directory: "/processed/only",
            artifactId: "processed",
            isPlanned: false,
            state: "available",
          },
        ],
      }}
    />,
  );
  expect(screen.getByText(/acquire.history.deliveredAtExecution/)).toHaveTextContent(
    "/old/hash-directory",
  );
  expect(screen.getAllByTestId("file-location")).toHaveLength(3);
});
