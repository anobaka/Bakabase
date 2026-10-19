import type { ReactNode } from "react";

import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import WorkflowRunDetailModal from "../WorkflowRunDetailModal";

import { WorkflowRunStatus } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ get: vi.fn(), portal: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { workflow: { getWorkflowRun: mocks.get } } }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("../displayNames", () => ({ activityDisplayName: (_t: unknown, kind: string) => kind }));
vi.mock("../ResumeRunModal", () => ({ default: () => null }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, title }: { children: ReactNode; title: string }) => (
    <div>
      <h1>{title}</h1>
      {children}
    </div>
  ),
  Spinner: () => <span>loading</span>,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Button: ({ children, onPress }: { children: ReactNode; onPress?: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
  Progress: ({ value }: { value: number }) => <progress max={100} value={value} />,
}));

beforeEach(() => {
  vi.clearAllMocks();
});

describe("workflow instance details", () => {
  it("requests and displays precisely the selected run", async () => {
    mocks.get.mockResolvedValue({
      code: 0,
      data: {
        id: 42,
        workflowDefinitionId: 9,
        status: WorkflowRunStatus.Running,
        startedAt: "2026-10-19T01:00:00Z",
        progress: 37,
        process: "Downloading archive",
        inputCount: 1,
        outputCount: 0,
        failedItemCount: 0,
      },
    });
    render(<WorkflowRunDetailModal runId={42} />);
    expect(await screen.findByText("Downloading archive", { exact: false })).toBeInTheDocument();
    expect(mocks.get).toHaveBeenCalledWith(42);
    expect(screen.getByRole("progressbar")).toHaveAttribute("value", "37");
  });

  it("offers the existing response dialog for the selected waiting instance", async () => {
    mocks.get.mockResolvedValue({
      code: 0,
      data: {
        id: 42,
        workflowDefinitionId: 9,
        status: WorkflowRunStatus.Waiting,
        startedAt: "2026-10-19T01:00:00Z",
        currentStepIndex: 2,
        waitReason: "approval",
        waitPromptJson: "{}",
        inputCount: 1,
        outputCount: 0,
        failedItemCount: 0,
      },
    });
    render(<WorkflowRunDetailModal runId={42} />);
    fireEvent.click(await screen.findByText("workflow.resume.respond"));
    expect(mocks.portal).toHaveBeenCalledWith(
      expect.any(Function),
      expect.objectContaining({ runId: 42, workflowDefinitionId: 9, currentStepIndex: 2 }),
    );
  });

  it("shows a missing run as a load error rather than an empty unrelated history", async () => {
    mocks.get.mockResolvedValue({ code: 404 });
    render(<WorkflowRunDetailModal runId={42} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("workflow.runDetail.loadFailed");
    fireEvent.click(screen.getByText("common.action.retry"));
    await waitFor(() => expect(mocks.get).toHaveBeenCalledTimes(2));
  });
});
