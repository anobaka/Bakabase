import type { ReactNode } from "react";
import { Children, useState } from "react";

import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import WorkflowRunDetailModal, { WorkflowRunDetailContent } from "../WorkflowRunDetailModal";

import { WorkflowRunStatus } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ get: vi.fn(), portal: vi.fn(), form: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { workflow: { getWorkflowRun: mocks.get } } }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("../CurrentProcessingFiles", () => ({ default: () => <section aria-label="current files" /> }));
vi.mock("../WorkflowStepCounts", () => ({
  default: ({ inputCount, outputCount }: any) => (
    <span>
      input {inputCount}, output {outputCount}
    </span>
  ),
}));
vi.mock("../WorkflowFileLocation", () => ({ default: ({ path }: any) => <span>{path}</span> }));
vi.mock("@/components/CloudDownload/DownloadProblemMessage", () => ({
  default: ({ message }: { message: string }) => <p>{message}</p>,
}));
vi.mock("../displayNames", () => ({ activityDisplayName: (_t: unknown, kind: string) => kind }));
vi.mock("../ResumeRunModal", () => ({
  default: () => null,
  ResumeRunContent: (props: any) => {
    mocks.form(props);
    return <div>inline response form</div>;
  },
}));
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
  Accordion: ({ children, selectedKeys, onSelectionChange }: any) => {
    const [open, setOpen] = useState<string[]>([]);
    const current = selectedKeys == null ? open : [...selectedKeys].map(String);
    return <div>{Children.map(children, (child: any) => <div>
      <button aria-expanded={current.includes(child.key)} onClick={() => {
        const next = current.includes(child.key) ? current.filter(key => key !== child.key) : [...current, child.key];
        setOpen(next); onSelectionChange?.(new Set(next));
      }}>{child.props.title}</button>
      {current.includes(child.key) && child.props.children}
    </div>)}</div>;
  },
  AccordionItem: ({ children }: any) => <div>{children}</div>,
}));

beforeEach(() => {
  vi.clearAllMocks();
});

describe("workflow instance details", () => {
  it("renders one step list when frozen plan, statistics and multiple attempts describe the same node", async () => {
    const execution = { id: 7, nodeId: "process", stepIndex: 0, kind: "fs.processArtifacts", attempt: 1,
      status: "waiting", startedAt: "2026-10-21T00:00:00Z", inputCount: 1, outputCount: 0, failedCount: 0,
      resumeKind: "initial", outputs: [{ kind: "scalar", key: "result", value: "First attempt" }] };
    mocks.get.mockResolvedValue({ code: 0, data: { id: 42, workflowDefinitionId: 0,
      status: WorkflowRunStatus.Success, startedAt: execution.startedAt,
      inputCount: 1, outputCount: 1, failedItemCount: 0, hasPlanSnapshot: true,
      planNodes: [{ nodeId: "process", order: 0, kind: execution.kind }],
      stepStats: [{ stepIndex: 0, kind: execution.kind, inputCount: 2, outputCount: 1, failedCount: 0 }],
      nodeExecutions: [execution, { ...execution, id: 8, attempt: 2, status: "succeeded", resumeKind: "signal",
        outputCount: 1, outputs: [{ kind: "scalar", key: "result", value: "Second attempt" }] }],
    } });
    render(<WorkflowRunDetailContent runId={42} readOnly />);
    const node = await screen.findByRole("button", { name: /fs.processArtifacts/ });
    expect(screen.getAllByText(/fs.processArtifacts/)).toHaveLength(1);
    expect(screen.queryByText("acquisition.plan.frozenSteps")).toBeNull();
    expect(screen.queryByText("First attempt")).toBeNull();
    fireEvent.click(node);
    const attempts = screen.getAllByRole("button", { name: /workflow.metadata.attempt(?:\s|$)/ });
    expect(attempts).toHaveLength(2);
    for (const attempt of attempts) fireEvent.click(attempt);
    expect(screen.getByText("First attempt")).toBeVisible();
    expect(screen.getByText("Second attempt")).toBeVisible();
  });
  it("keeps a read-only waiting run inspectable without a resume or edit entry", async () => {
    mocks.get.mockResolvedValue({
      code: 0,
      data: {
        id: 42,
        workflowDefinitionId: 9,
        status: WorkflowRunStatus.Waiting,
        startedAt: "2026-10-25T12:00:00Z",
        waitReason: "approval",
        inputCount: 1,
        outputCount: 0,
        failedItemCount: 0,
      },
    });
    render(<WorkflowRunDetailModal runId={42} readOnly />);
    expect(await screen.findByText("workflow.resume.waitingFor")).toBeVisible();
    expect(screen.queryByRole("button", { name: "workflow.resume.respond" })).toBeNull();
    expect(screen.queryByRole("button", { name: "workflow.runDetail.definition" })).toBeNull();
    expect(screen.getByText("workflow.metadata.title")).toBeVisible();
    expect(mocks.portal).not.toHaveBeenCalled();
  });
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

  it("shows the node response inside the existing run dialog", async () => {
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
    expect(screen.getByText("inline response form")).toBeVisible();
    expect(mocks.form).toHaveBeenLastCalledWith(
      expect.objectContaining({ runId: 42, workflowDefinitionId: 9, currentStepIndex: 2 }),
    );
    expect(mocks.portal).not.toHaveBeenCalled();
  });

  it("shows a missing run as a load error rather than an empty unrelated history", async () => {
    mocks.get.mockResolvedValue({ code: 404 });
    render(<WorkflowRunDetailModal runId={42} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("workflow.runDetail.loadFailed");
    fireEvent.click(screen.getByText("common.action.retry"));
    await waitFor(() => expect(mocks.get).toHaveBeenCalledTimes(2));
  });

  it("shows a frozen dynamic plan and resumes its recorded kind without linking template zero", async () => {
    mocks.get.mockResolvedValue({
      code: 0,
      data: {
        id: 42,
        workflowDefinitionId: 0,
        status: WorkflowRunStatus.Waiting,
        startedAt: "2026-10-21T00:00:00Z",
        currentStepIndex: 1,
        waitReason: "ExtractionPlanUnknown",
        waitPromptJson: "{}",
        hasPlanSnapshot: true,
        planName: "My acquisition",
        planNodes: [
          { nodeId: "fetch", kind: "download.awaitCompletion", order: 0 },
          {
            nodeId: "process",
            kind: "acquisition.plan.process",
            order: 1,
            notes: "Confirm processing",
          },
        ],
        inputCount: 1,
        outputCount: 0,
        failedItemCount: 0,
      },
    });
    render(<WorkflowRunDetailModal runId={42} />);
    expect(await screen.findByText("Confirm processing")).toBeVisible();
    expect(screen.queryByText("workflow.runDetail.definition")).toBeNull();
    fireEvent.click(screen.getByText("workflow.resume.respond"));
    expect(mocks.form).toHaveBeenLastCalledWith(
      expect.objectContaining({
        runId: 42,
        workflowDefinitionId: 0,
        activityKind: "acquisition.plan.process",
      }),
    );
  });
});

it("embeds the waiting form directly without opening a dialog or mutating the run", async () => {
  mocks.get.mockResolvedValue({
    code: 0,
    data: {
      id: 42,
      workflowDefinitionId: 0,
      status: WorkflowRunStatus.Waiting,
      startedAt: "2026-10-29T00:00:00Z",
      currentStepIndex: 3,
      activityKind: "fs.processArtifacts",
      waitPromptJson: "{}",
      inputCount: 1,
      outputCount: 0,
      failedItemCount: 0,
    },
  });
  render(<WorkflowRunDetailContent runId={42} respondInitially />);
  expect(await screen.findByText("inline response form")).toBeVisible();
  expect(screen.queryByRole("button", { name: "workflow.resume.respond" })).toBeNull();
  expect(mocks.form).toHaveBeenLastCalledWith(
    expect.objectContaining({ runId: 42, activityKind: "fs.processArtifacts" }),
  );
  expect(mocks.portal).not.toHaveBeenCalled();
});

it("uses current completed-download evidence instead of an old failure and retry form", async () => {
  const diagnose = vi.fn();
  mocks.get.mockResolvedValue({ code: 0, data: {
    id: 42, workflowDefinitionId: 0, status: WorkflowRunStatus.Waiting,
    startedAt: "2026-10-29T00:00:00Z", currentStepIndex: 3,
    activityKind: "download.awaitCompletion", waitReason: "cloud.cloudAttention",
    waitPromptJson: "{}", errorMessage: "old download failure",
    inputCount: 1, outputCount: 0, failedItemCount: 0,
  } });
  const view = render(<WorkflowRunDetailContent runId={42} respondInitially completedDownloadWait={{
    runId: 42, stepIndex: 3, message: "Downloads complete; processing paused", onDiagnose: diagnose,
  }} />);
  expect(await screen.findByText("Downloads complete; processing paused")).toBeVisible();
  expect(screen.queryByText("old download failure")).toBeNull();
  expect(screen.queryByText("inline response form")).toBeNull();
  expect(screen.queryByText("workflow.resume.waitingFor")).toBeNull();
  expect(diagnose).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.runtime.diagnoseContinuation" }));
  expect(diagnose).toHaveBeenCalledOnce();

  view.rerender(<WorkflowRunDetailContent runId={42} completedDownloadWait={{
    runId: 42, stepIndex: 2, message: "Stale completed state", onDiagnose: diagnose,
  }} />);
  expect(await screen.findByText("old download failure")).toBeVisible();
  expect(screen.getByText("workflow.resume.waitingFor")).toBeVisible();
  expect(screen.queryByText("Stale completed state")).toBeNull();
});

it("shows only the current files and response form in an acquisition file decision", async () => {
  mocks.get.mockResolvedValue({code: 0, data: {
    id: 42, workflowDefinitionId: 0, currentStepIndex: 2, status: WorkflowRunStatus.Waiting,
    waitReason: "cloud.fileProcessing", activityKind: "acquisition.plan.process", waitPromptJson: "{}",
    startedAt: "2026-10-25T12:00:00Z",
  }});
  render(<WorkflowRunDetailContent runId={42} respondInitially responseOnly />);
  expect(await screen.findByText("inline response form")).toBeVisible();
  expect(screen.getByRole("region", {name:"current files"})).toBeVisible();
  expect(mocks.form).toHaveBeenLastCalledWith(expect.objectContaining({fileTask: true}));
  expect(screen.queryByText("workflow.resume.waitingFor")).toBeNull();
  expect(screen.queryByText("workflow.runs.status.Waiting")).toBeNull();
});

it("uses the same compact response shell for a stopped download without duplicated runtime status", async () => {
  mocks.get.mockResolvedValue({code: 0, data: {
    id: 832, workflowDefinitionId: 0, currentStepIndex: 3, status: WorkflowRunStatus.Waiting,
    waitReason: "cloud.cloudAttention", activityKind: "download.awaitCompletion", waitPromptJson: "{}",
    errorMessage: "duplicate diagnostic", startedAt: "2026-10-25T12:00:00Z",
  }});
  render(<WorkflowRunDetailContent runId={832} respondInitially responseOnly />);
  expect(await screen.findByText("inline response form")).toBeVisible();
  expect(mocks.form).toHaveBeenLastCalledWith(expect.objectContaining({ compact: true, fileTask: false }));
  expect(screen.queryByText("workflow.resume.waitingFor")).toBeNull();
  expect(screen.queryByText("workflow.runs.status.Waiting")).toBeNull();
  expect(screen.queryByText("duplicate diagnostic")).toBeNull();
  expect(screen.queryByRole("region", { name: "current files" })).toBeNull();
});
