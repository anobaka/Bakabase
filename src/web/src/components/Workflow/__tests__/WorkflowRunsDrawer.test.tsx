import React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import WorkflowRunsDrawer, { type WorkflowRunLoader } from "../WorkflowRunsDrawer";

import { WorkflowRunStatus } from "@/sdk/constants";

const api = vi.hoisted(() => ({ search: vi.fn(), portal: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { workflow: { searchWorkflowRuns: api.search } },
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("../displayNames", () => ({ activityDisplayName: (_: unknown, kind: string) => kind }));
vi.mock("../ResumeRunModal", () => ({ default: () => null }));
vi.mock("../RenamePlanPanel", () => ({ default: () => null }));
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
}));

type Run = NonNullable<Awaited<ReturnType<WorkflowRunLoader>>["data"]>[number];
const run = (id: number, message: string, definitionId = 9): Run => ({
  id,
  workflowDefinitionId: definitionId,
  status: WorkflowRunStatus.Failed,
  startedAt: "2026-10-05T10:00:00Z",
  inputCount: 1,
  outputCount: 0,
  outputPreviewTruncated: false,
  failedItemCount: 1,
  stepStats: [],
  errorMessage: message,
});

beforeEach(() => vi.clearAllMocks());
afterEach(() => vi.restoreAllMocks());

describe("workflow run history sources", () => {
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
