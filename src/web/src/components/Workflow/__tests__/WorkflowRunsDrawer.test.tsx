import type * as ReactI18next from "react-i18next";
import React from "react";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import WorkflowRunsDrawer, { type WorkflowRunLoader } from "../WorkflowRunsDrawer";
import SourceExecutionHistory from "@/components/Acquire/SourceExecutionHistory";

import { WorkflowRunStatus } from "@/sdk/constants";

const api = vi.hoisted(() => ({
  search: vi.fn(),
  sourceQuery: vi.fn(),
  portal: vi.fn(),
  restore: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    workflow: { searchWorkflowRuns: api.search },
    workflowSources: { queryWorkflowSourceRuns: api.sourceQuery },
  },
}));
vi.mock("react-i18next", async (importOriginal) => ({
  ...await importOriginal<typeof ReactI18next>(),
  useTranslation: () => ({
    t: (key: string, options?: { count?: number }) =>
      key === "acquire.history.files" ? `${key}:${options?.count}` : key,
  }),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("../WorkflowStepCounts", () => ({
  default: ({ inputCount, outputCount }: any) => (
    <span>
      input {inputCount}, output {outputCount}
    </span>
  ),
}));
vi.mock("../WorkflowFileLocation", () => ({ default: ({ path }: any) => <span title={path}>{path}</span> }));
vi.mock("../displayNames", () => ({ activityDisplayName: (_: unknown, kind: string) => kind }));
vi.mock("../ResumeRunModal", () => ({ default: () => null }));
vi.mock("../RenamePlanPanel", () => ({ default: () => null }));
vi.mock("@/components/CloudDownload/artifactCleanupApi", () => ({
  restoreCleanupReceipt: api.restore,
}));
vi.mock("@heroui/react", () => {
  const Frame = ({ children }: { children: React.ReactNode }) => <div>{children}</div>;

  return {
    Drawer: ({ isOpen, children }: { isOpen: boolean; children: React.ReactNode }) =>
      isOpen ? <div>{children}</div> : null,
    DrawerBody: Frame,
    DrawerContent: Frame,
    DrawerHeader: Frame,
    DrawerFooter: Frame,
  };
});
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: { children: React.ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
  Chip: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
  Spinner: () => <span>Loading</span>,
  Pagination: ({ page, onChange }: { page: number; onChange: (page: number) => void }) => (
    <button onClick={() => onChange(page + 1)}>Page {page}: next</button>
  ),
  Accordion: ({ children, selectedKeys = new Set(), onSelectionChange }: any) => (
    <div>
      {React.Children.map(
        children,
        (child: any) =>
          child && (
            <div>
              <button
                aria-expanded={selectedKeys.has(child.key)}
                onClick={() => {
                  const next = new Set(selectedKeys);
                  if (next.has(child.key)) next.delete(child.key);
                  else next.add(child.key);
                  onSelectionChange?.(next);
                }}
              >
                {child.props.title}
              </button>
              {selectedKeys.has(child.key) && child.props.children}
            </div>
          ),
      )}
    </div>
  ),
  AccordionItem: ({ children }: any) => <div>{children}</div>,
}));

type Run = NonNullable<Awaited<ReturnType<WorkflowRunLoader>>["data"]>[number];
const run = (id: number, message: string, definitionId = 9): Run => ({
  id,
  workflowDefinitionId: definitionId,
  parameterSources: [],
  hasPlanSnapshot: false,
  planNodes: [],
  status: WorkflowRunStatus.Failed,
  startedAt: "2026-10-05T10:00:00Z",
  inputCount: 1,
  outputCount: 0,
  outputPreviewTruncated: false,
  failedItemCount: 1,
  stepStats: [],
  nodeExecutions: [],
  errorMessage: message,
});

beforeEach(() => vi.clearAllMocks());
afterEach(() => vi.restoreAllMocks());

describe("workflow run history sources", () => {
  it("propagates read-only history to run actions and nested cleanup receipts", async () => {
    const source = { module: "acquisition", entityKind: "task", entityId: "42" };
    api.sourceQuery.mockResolvedValue({
      code: 0,
      data: [
        {
          source,
          runCount: 1,
          runs: [
            {
              run: {
                ...run(75, "Retained execution"),
                status: WorkflowRunStatus.Waiting,
                outputItemsJson: JSON.stringify([
                  {
                    targets: [
                      {
                        id: "files",
                        title: "Retained files",
                        cleanupReceiptId: "retained-receipt",
                        cleanupStatus: "quarantined",
                        processedArtifactId: "retained-artifact",
                      },
                    ],
                  },
                ]),
              },
              workflowName: "Retained workflow",
              downloadTaskIds: [],
              completedFileCount: 1,
            },
          ],
        },
      ],
    });
    render(<SourceExecutionHistory source={source} onClose={vi.fn()} readOnly />);
    await screen.findByText("Retained execution");
    expect(screen.queryByRole("button", { name: "workflow.resume.respond" })).toBeNull();
    expect(screen.queryByRole("button", { name: "cloudDownload.cleanup.restore" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "workflow.details.expandPage" }));
    expect(screen.getByText("retained-artifact")).toBeVisible();
    expect(screen.getByText("Retained files")).toBeVisible();
    expect(api.restore).not.toHaveBeenCalled();
    expect(api.portal).not.toHaveBeenCalled();
  });
  it("retains source context and an actionable explanation when historical execution rows are missing", async () => {
    render(
      <WorkflowRunsDrawer
        isOpen
        workflowName="Parsing history"
        onClose={vi.fn()}
        loadRuns={async () => ({ data: [], totalCount: 0 })}
        headerContent={<p>Saved parsing results remain available</p>}
        emptyContent={<p>The old execution log is unavailable; use the saved results.</p>}
      />,
    );
    expect(
      await screen.findByText("The old execution log is unavailable; use the saved results."),
    ).toBeVisible();
    expect(screen.getByText("Saved parsing results remain available")).toBeVisible();
    expect(screen.queryByText("workflow.runs.empty")).toBeNull();
  });
  it("renders delivery metadata for a source run whose output is a resource rather than a workset", async () => {
    const source = { module: "anySource", entityKind: "item", entityId: "41" };
    api.sourceQuery.mockResolvedValue({
      code: 0,
      data: [
        {
          source,
          runCount: 1,
          runs: [
            {
              run: {
                ...run(675, ""),
                status: WorkflowRunStatus.Success,
                failedItemCount: 0,
                outputItemsJson: JSON.stringify([{ resourceId: 321, acquisitionTaskId: 67 }]),
              },
              workflowName: "Acquire supported sources (v1)",
              phase: "delivered",
              completedFileCount: 10,
              reused: true,
              deliveredDirectory: "/library/delivered/game",
              downloadTaskIds: [],
            },
          ],
        },
      ],
    });
    render(<SourceExecutionHistory source={source} onClose={vi.fn()} />);
    await screen.findByText("Acquire supported sources (v1)");
    expect(screen.getByText("acquire.phase.delivered")).toBeInTheDocument();
    expect(screen.getByText("acquire.history.files:10")).toBeInTheDocument();
    expect(screen.getByText("acquire.history.reused")).toBeInTheDocument();
    expect(screen.getByTitle("/library/delivered/game")).toHaveTextContent(
      "/library/delivered/game",
    );
    expect(screen.queryByText(/"resourceId": 321/)).toBeNull();
    expect(screen.getByText("workflow.metadata.legacyHint")).toBeVisible();
  });

  it("sends a SourceRef object through the real source-history loader and keeps its scope stable", async () => {
    const source = { module: "postParser", entityKind: "task", entityId: "41", revision: "3" };
    api.sourceQuery.mockImplementation(async (body) => {
      expect(body).toEqual({ sources: [source], pageIndex: 1, pageSize: 20 });
      expect(Array.isArray(body.sources[0])).toBe(false);
      return {
        code: 0,
        data: [
          {
            source,
            runCount: 1,
            runs: [{ run: run(673, "Source run"), workflowName: "Acquire files" }],
          },
        ],
      };
    });
    const view = render(<SourceExecutionHistory source={source} onClose={vi.fn()} />);
    await screen.findByText("Source run");
    expect(screen.getByText("Acquire files")).toBeInTheDocument();
    view.rerender(<SourceExecutionHistory source={{ ...source }} onClose={vi.fn()} />);
    expect(api.sourceQuery).toHaveBeenCalledTimes(1);
    expect(api.search).not.toHaveBeenCalled();
  });

  it("shows source-history failures instead of an empty history and retries the request", async () => {
    const source = { module: "postParser", entityKind: "task", entityId: "41" };
    api.sourceQuery
      .mockResolvedValueOnce({ code: 400, message: "Invalid source reference" })
      .mockResolvedValueOnce({
        code: 0,
        data: [{ source, runCount: 1, runs: [{ run: run(674, "Recovered history") }] }],
      });
    render(<SourceExecutionHistory source={source} onClose={vi.fn()} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Invalid source reference");
    expect(screen.queryByText("workflow.runs.empty")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "common.action.retry" }));
    await screen.findByText("Recovered history");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(api.sourceQuery).toHaveBeenCalledTimes(2);
  });

  it("polls an open active run through completion without a loading flash and stops when closed", async () => {
    vi.useFakeTimers();
    try {
      const pending = { ...run(1, ""), status: WorkflowRunStatus.Pending };
      const completed = { ...pending, status: WorkflowRunStatus.Success };
      let finish!: (response: { data: Run[]; totalCount: number }) => void;
      const refreshing = new Promise<{ data: Run[]; totalCount: number }>((resolve) => {
        finish = resolve;
      });
      api.search
        .mockResolvedValueOnce({ data: [pending], totalCount: 1 })
        .mockReturnValueOnce(refreshing)
        .mockResolvedValue({ data: [pending], totalCount: 1 });
      const props = { workflowDefinitionId: 9, workflowName: "Polling", onClose: vi.fn() };
      const view = render(<WorkflowRunsDrawer {...props} isOpen />);
      await act(async () => {});
      expect(screen.getByText("workflow.runs.status.Pending")).toBeInTheDocument();
      await act(async () => {
        await vi.advanceTimersByTimeAsync(2000);
      });
      expect(api.search).toHaveBeenCalledTimes(2);
      expect(screen.queryByText("Loading")).not.toBeInTheDocument();
      await act(async () => {
        await vi.advanceTimersByTimeAsync(4000);
      });
      expect(api.search).toHaveBeenCalledTimes(2);
      await act(async () => {
        finish({ data: [completed], totalCount: 1 });
        await refreshing;
      });
      expect(screen.getByText("workflow.runs.status.Success")).toBeInTheDocument();
      view.rerender(<WorkflowRunsDrawer {...props} isOpen={false} />);
      view.rerender(<WorkflowRunsDrawer {...props} isOpen />);
      await act(async () => {});
      expect(screen.getByText("workflow.runs.status.Pending")).toBeInTheDocument();
      view.rerender(<WorkflowRunsDrawer {...props} isOpen={false} />);
      const callsAtClose = api.search.mock.calls.length;
      await act(async () => {
        await vi.advanceTimersByTimeAsync(6000);
      });
      expect(api.search).toHaveBeenCalledTimes(callsAtClose);
    } finally {
      vi.useRealTimers();
    }
  });

  it("renders historical artifact results and recovery receipts without exposing arbitrary output payloads", async () => {
    api.restore.mockResolvedValue("Restored");
    api.search.mockResolvedValue({
      data: [
        {
          ...run(1, "", 9),
          status: WorkflowRunStatus.Success,
          outputItemsJson: JSON.stringify([
            {
              targets: [
                {
                  id: "historical-content",
                  title: "Historical artifact",
                  executionStatus: "reused",
                  processedArtifactId: "artifact-history",
                  cleanupStatus: "quarantined",
                  cleanupReceiptId: "receipt-history",
                },
              ],
            },
          ]),
        },
        {
          ...run(2, "", 9),
          status: WorkflowRunStatus.Success,
          outputItemsJson: JSON.stringify([{ text: "unrelated-value" }]),
        },
      ],
      totalCount: 2,
    });
    render(
      <WorkflowRunsDrawer
        isOpen
        workflowDefinitionId={9}
        workflowName="Artifacts"
        onClose={vi.fn()}
      />,
    );

    await screen.findByText("Historical artifact");
    expect(screen.getByText("cloudDownload.execution.reused")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "workflow.details.expandPage" }));
    expect(screen.getByText("artifact-history")).toBeVisible();
    expect(screen.queryByText(/unrelated-value/)).toBeNull();
    expect(screen.getAllByText("workflow.metadata.legacyHint")).toHaveLength(2);
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.cleanup.restore" }));
    await screen.findByRole("status");
    expect(api.restore).toHaveBeenCalledWith("receipt-history");
  });

  it("preserves the existing definition-scoped loader and pagination", async () => {
    api.search.mockResolvedValue({ data: [run(1, "Generic history")], totalCount: 21 });
    render(
      <WorkflowRunsDrawer
        isOpen
        workflowDefinitionId={9}
        workflowName="Generic"
        onClose={vi.fn()}
      />,
    );

    await screen.findByText("Generic history");
    expect(api.search).toHaveBeenCalledWith(9, {
      workflowDefinitionId: 9,
      pageIndex: 1,
      pageSize: 20,
    });
    fireEvent.click(screen.getByText("Page 1: next"));
    await waitFor(() =>
      expect(api.search).toHaveBeenLastCalledWith(9, {
        workflowDefinitionId: 9,
        pageIndex: 2,
        pageSize: 20,
      }),
    );
  });

  it("uses a source loader without fetching a shared definition and resumes the row's definition", async () => {
    const waiting = { ...run(2, "Historical run", 4), status: WorkflowRunStatus.Waiting };
    const loadRuns = vi
      .fn<WorkflowRunLoader>()
      .mockResolvedValue({ data: [waiting], totalCount: 1 });

    render(
      <WorkflowRunsDrawer
        isOpen
        loadRuns={loadRuns}
        runSourceKey="postParser:task:1"
        workflowName="Post 1"
        onClose={vi.fn()}
      />,
    );

    await screen.findByText("Historical run");
    expect(loadRuns).toHaveBeenCalledWith({ pageIndex: 1, pageSize: 20 });
    expect(api.search).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("workflow.resume.respond"));
    expect(api.portal).toHaveBeenCalledWith(
      expect.any(Function),
      expect.objectContaining({ runId: 2, workflowDefinitionId: 4 }),
    );
  });

  it("clears rows and pagination when changing scope and ignores the previous scope's late response", async () => {
    let finishOldPage!: (value: Awaited<ReturnType<WorkflowRunLoader>>) => void;
    const oldPage = new Promise<Awaited<ReturnType<WorkflowRunLoader>>>((resolve) => {
      finishOldPage = resolve;
    });
    const taskLoader = vi
      .fn<WorkflowRunLoader>()
      .mockResolvedValueOnce({ data: [run(1, "Task 1 history")], totalCount: 21 })
      .mockReturnValueOnce(oldPage);
    const allLoader = vi
      .fn<WorkflowRunLoader>()
      .mockResolvedValue({ data: [run(3, "All post parser history")], totalCount: 21 });
    const { rerender } = render(
      <WorkflowRunsDrawer
        isOpen
        loadRuns={taskLoader}
        runSourceKey="postParser:task:1"
        workflowName="Post 1"
        onClose={vi.fn()}
      />,
    );

    await screen.findByText("Task 1 history");
    fireEvent.click(screen.getByText("Page 1: next"));
    await waitFor(() =>
      expect(taskLoader).toHaveBeenLastCalledWith({ pageIndex: 2, pageSize: 20 }),
    );
    rerender(
      <WorkflowRunsDrawer
        isOpen
        loadRuns={allLoader}
        runSourceKey="postParser:all"
        workflowName="All posts"
        onClose={vi.fn()}
      />,
    );

    await screen.findByText("All post parser history");
    expect(allLoader).toHaveBeenLastCalledWith({ pageIndex: 1, pageSize: 20 });
    expect(screen.queryByText("Task 1 history")).toBeNull();
    expect(screen.getByText("Page 1: next")).toBeTruthy();
    finishOldPage({ data: [run(4, "Stale task page")], totalCount: 21 });
    await oldPage;
    expect(screen.queryByText("Stale task page")).toBeNull();
    expect(screen.getByText("All post parser history")).toBeTruthy();
  });
});

it("expands and collapses every run's nested metadata on the current history page", async () => {
  api.search.mockResolvedValue({
    data: [1, 2].map((id) => ({
      ...run(id, ""),
      nodeExecutions: [
        {
          id,
          nodeId: "node",
          stepIndex: 0,
          kind: "download.awaitCompletion",
          attempt: 1,
          status: "succeeded",
          resumeKind: "initial",
          startedAt: "2026-10-25T08:00:00Z",
          inputCount: 1,
          outputCount: 1,
          failedCount: 0,
          outputsTruncated: false,
          outputs: [{ key: "result", kind: "scalar", value: `Saved output ${id}` }],
        },
      ],
    })),
    totalCount: 2,
  });
  render(
    <WorkflowRunsDrawer isOpen workflowDefinitionId={9} workflowName="History" onClose={vi.fn()} />,
  );
  await screen.findByRole("button", { name: "workflow.details.expandPage" });
  expect(screen.queryByText("Saved output 1")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "workflow.details.expandPage" }));
  expect(screen.getByText("Saved output 1")).toBeVisible();
  expect(screen.getByText("Saved output 2")).toBeVisible();
  fireEvent.click(screen.getAllByRole("button", { name: "workflow.details.collapseAll" })[0]);
  expect(screen.queryByText("Saved output 1")).toBeNull();
  expect(screen.getByText("Saved output 2")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "workflow.details.expandPage" }));
  expect(screen.getByText("Saved output 1")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "workflow.details.collapsePage" }));
  expect(screen.queryByText("Saved output 1")).toBeNull();
  expect(screen.queryByText("Saved output 2")).toBeNull();
  expect(api.search).toHaveBeenCalledTimes(1);
});

it("keeps expansion scoped to the displayed page without fetching other history pages", async () => {
  const historyRun = (id: number) => ({
    ...run(id, ""),
    nodeExecutions: [
      {
        id,
        nodeId: "node",
        stepIndex: 0,
        kind: "download.awaitCompletion",
        attempt: 1,
        status: "succeeded",
        resumeKind: "initial",
        startedAt: "2026-10-25T08:00:00Z",
        inputCount: 1,
        outputCount: 1,
        failedCount: 0,
        outputsTruncated: false,
        outputs: [{ key: "result", kind: "scalar", value: `Page output ${id}` }],
      },
    ],
  });
  api.search
    .mockResolvedValueOnce({ data: [historyRun(1)], totalCount: 21 })
    .mockResolvedValueOnce({ data: [historyRun(21)], totalCount: 21 });
  render(
    <WorkflowRunsDrawer isOpen workflowDefinitionId={9} workflowName="History" onClose={vi.fn()} />,
  );
  fireEvent.click(await screen.findByRole("button", { name: "workflow.details.expandPage" }));
  expect(screen.getByText("Page output 1")).toBeVisible();
  expect(api.search).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByText("Page 1: next"));
  await waitFor(() => expect(api.search).toHaveBeenCalledTimes(2));
  expect(screen.queryByText("Page output 21")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "workflow.details.expandPage" }));
  expect(await screen.findByText("Page output 21")).toBeVisible();
});

it("returns a selected waiting history run to the host panel instead of stacking another modal", async () => {
  const onOpenRun = vi.fn();
  api.search.mockResolvedValue({
    code: 0,
    data: [{ ...run(75, "Needs an answer"), status: WorkflowRunStatus.Waiting }],
    totalCount: 1,
  });
  render(
    <WorkflowRunsDrawer
      isOpen
      workflowDefinitionId={9}
      workflowName="History"
      onClose={vi.fn()}
      onOpenRun={onOpenRun}
    />,
  );
  fireEvent.click(await screen.findByRole("button", { name: "workflow.resume.respond" }));
  expect(onOpenRun).toHaveBeenCalledExactlyOnceWith(75, true);
  expect(api.portal).not.toHaveBeenCalled();
});
