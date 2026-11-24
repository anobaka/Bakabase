import { Children, isValidElement, type ReactNode } from "react";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import PlanTaskDetailModal from "../PlanTaskDetailModal";
import type { PlanTaskDetail } from "../models";
import AcquisitionExecutionTimeline from "../AcquisitionExecutionTimeline";
import TaskPlanSnapshot from "../TaskPlanSnapshot";
import AcquisitionSourceDetails from "../../AcquisitionSourceDetails";
import { AcquisitionStatus, WorkflowRunStatus } from "@/sdk/constants";
import AcquisitionRecoveryModal from "@/pages/acquisition/components/AcquisitionRecoveryModal";
import SourceAvailabilityReviewModal from "@/components/SourceAvailability/SourceAvailabilityReviewModal";
const mocks = vi.hoisted(() => ({
  preflight: vi.fn(),
  builders: vi.fn(),
  updateDraft: vi.fn(),
  setBuilder: vi.fn(),
  previewPreparation: vi.fn(),
  confirmPreparation: vi.fn(),
  keepPreparedFiles: vi.fn(),
  deferPreparedProcessing: vi.fn(),
  options: vi.fn(),
  get: vi.fn(),
  search: vi.fn(),
  select: vi.fn(),
  run: vi.fn(),
  update: vi.fn(),
  createPortal: vi.fn(),
  applyProcessingRevision: vi.fn(),
  processingPanel: vi.fn(),
  sourceRuns: vi.fn(),
  querySourceRuns: vi.fn(),
  assessmentIo: vi.fn(),
  cancel: vi.fn(),
  retry: vi.fn(),
  copy: vi.fn(),
  sourceCorrections: vi.fn(),
  sourceHealth: vi.fn(),
  correctAccessCode: vi.fn(),
  completedRevision: vi.fn(),
  runContent: vi.fn(),
  runtimeOverviews: vi.fn(),
  revisionOptions: vi.fn(),
}));
vi.mock("../sourceRevisionApi", () => ({ sourceRevisionApi: { options: mocks.revisionOptions } }));
vi.mock("../api", () => ({ planTaskApi: mocks }));
vi.mock("@/components/Workflow/WorkflowNodeMetadata", () => ({
  default: () => null,
  WorkflowOutputBlock: ({ block }: { block: { value?: string; code?: string } }) => <span>{block.value || block.code}</span>,
}));
vi.mock("../RuntimeOverviewSummary", () => ({
  RuntimeSources: () => <button>runtime download sources</button>,
  default: ({ overview, problemDetail }: any) => <>
    {overview?.progressScope === "confirmation" && <p>Confirming existing files</p>}
    {overview?.downloadsCompletedAwaitingWorkflow && <p>completed downloads overview</p>}
    {problemDetail && <p>{problemDetail}</p>}
  </>,
}));
vi.mock("@/pages/acquisition/components/AcquisitionRecoveryModal", () => ({ default: () => null }));
vi.mock("../CompletedProcessingRevisionForm", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../CompletedProcessingRevisionForm")>()),
  default: (props: { detail: { task: { planRevision: number } } }) => {
    mocks.completedRevision(props);
    return <div>completed revision {props.detail.task.planRevision}</div>;
  },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: { cancelAcquisition: mocks.cancel, retryAcquisition: mocks.retry, getAcquisitionAssessmentIo: mocks.assessmentIo, getAcquisitionOptions: mocks.options },
    workflowSources: { queryWorkflowSourceRuns: mocks.querySourceRuns },
  },
}));
vi.mock("@/core/clipboard", () => ({ copyTextToClipboard: mocks.copy }));
vi.mock("../../MethodPreparation", () => ({ default: () => null }));
vi.mock("../PostImageSourceAction", () => ({ default: () => null }));
vi.mock("@/components/SourceAvailability/SourcePriorityModal", () => ({ default: () => null }));
vi.mock("@/components/SourceAvailability/SourceAvailabilityReviewModal", () => ({ default: () => null }));
vi.mock("@/components/SourceAvailability/api", () => ({ sourceAvailabilityApi: { acquisition: mocks.sourceHealth } }));
vi.mock("@/components/PasswordSelector/ArchivePasswordInput", () => ({ default: () => null }));
vi.mock("@/components/FilePathValue", () => ({ default: ({ path, pathActions }: { path: string; pathActions?: ReactNode }) => <span><code>{path}</code>{pathActions}</span> }));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@/test/radioControls")),
  Select: () => null,
  Accordion: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AccordionItem: ({ children, title }: { children: ReactNode; title: string }) => (
    <section aria-label={title}>{children}</section>
  ),
  Modal: ({
    children,
    footer,
    title,
    "aria-label": ariaLabel,
    visible = true,
  }: {
    children: ReactNode;
    footer: ReactNode;
    title: ReactNode;
    "aria-label"?: string;
    visible?: boolean;
  }) =>
    visible ? (
      <div role="dialog" aria-label={ariaLabel ?? (typeof title === "string" ? title : undefined)}>
        <header>{title}</header>
        {children}
        <footer>{footer}</footer>
      </div>
    ) : null,
  Button: ({
    children,
    onPress,
    isDisabled,
    isLoading,
    color,
    ...rest
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
    isLoading?: boolean;
    color?: string;
    "aria-label"?: string;
  }) => (
    <button
      data-color={color}
      aria-label={rest["aria-label"]}
      aria-expanded={(rest as React.AriaAttributes)["aria-expanded"]}
      disabled={isDisabled || isLoading}
      onClick={onPress}
    >
      {children}
    </button>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Progress: ({ value, "aria-label": ariaLabel }: { value?: number; "aria-label"?: string }) => (
    <progress aria-label={ariaLabel} value={value} max={100} />
  ),
  Popover: ({ trigger }: { trigger: ReactNode }) => <>{trigger}</>,
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Textarea: ({label, value, onValueChange}: any) => <textarea aria-label={label} value={value}
    onChange={event => onValueChange(event.target.value)} />,
  Checkbox: ({children, isSelected, onValueChange, "aria-label": label}: any) =>
    <label><input type="checkbox" aria-label={label} checked={isSelected}
      onChange={event => onValueChange(event.target.checked)} />{children}</label>,
  Spinner: () => null,
  Input: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
  }) => <input aria-label={label} value={value} onChange={(e) => onValueChange(e.target.value)} />,
  Tabs: ({
    children,
    selectedKey,
    onSelectionChange,
  }: {
    children: ReactNode;
    selectedKey: string;
    onSelectionChange: (value: string) => void;
  }) => (
    <div role="tablist">
      {Children.map(children, (tab) =>
        isValidElement<{ title: string }>(tab) && (
          <button
            role="tab"
            aria-selected={selectedKey === tab.key}
            onClick={() => onSelectionChange(String(tab.key))}
          >
            {tab.props.title}
          </button>
        ),
      )}
    </div>
  ),
  Tab: () => null,
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ children, href }: { children: ReactNode; href: string }) => (
    <a href={href}>{children}</a>
  ),
}));
vi.mock("@/components/FileSystemSelector/components/Button", () => ({
  default: () => <button>filesystem selector</button>,
}));
vi.mock("@/components/Workflow/WorkflowRunDetailModal", () => ({
  default: () => null,
  WorkflowRunDetailContent: (props: any) => {
    mocks.runContent(props);
    const form = <div>inline run {props.runId}</div>;
    return props.wrapWaitingResponse?.({ id: props.runId }, form) ?? form;
  },
}));
vi.mock("@/components/Workflow/WorkflowStepCounts", () => ({ default: () => null }));
vi.mock("@/components/Workflow/ResumeRunModal", () => ({ default: () => null }));
vi.mock("@/components/Workflow/RenamePlanPanel", () => ({ default: () => null }));
vi.mock("../SourceRevisionModal", () => ({ default: () => null }));
vi.mock("../StorageReserveOption", () => ({ default: ({ taskId, workflowRunId, children }: any) =>
  <div><span>reserve for {taskId}/{workflowRunId}</span>{children}</div> }));
vi.mock("@/components/Workflow/displayNames", () => ({
  activityDisplayName: (_: unknown, kind: string) => kind,
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.createPortal }),
}));
vi.mock("../../useSourceRuns", () => ({
  useSourceRuns: (sources: unknown) => {
    mocks.sourceRuns(sources);
    return { rows: new Map(), loading: false, refresh: vi.fn() };
  },
}));
vi.mock("../../SourceExecutionSummary", () => ({
  default: ({ onHistory }: { onHistory: () => void }) => (
    <div>
      <span>output metadata</span>
      <button onClick={onHistory}>acquire.history.count</button>
    </div>
  ),
  SourceRunSummary: () => null,
}));
vi.mock("@/components/ProcessingPlanRevisions/SourceProcessingPlanPanel", () => ({
  default: (props: { onApply?: (revisionId: string) => Promise<void> }) => {
    mocks.processingPanel(props);
    return props.onApply ? (
      <button onClick={() => void props.onApply?.("saved-revision")}>
        apply reviewed revision
      </button>
    ) : (
      <span>frozen revision history</span>
    );
  },
}));
const detail = {
  task: {
    id: 42,
    title: "Chosen content",
    phase: "selection",
    status: AcquisitionStatus.Waiting,
    planRevision: 3,
    builderId: "some.generic.search",
    updatedAt: "now",
  },
  query: "search words",
  candidates: [
    { id: "c1", title: "First gallery", url: "https://example.com/a" },
    { id: "c2", title: "Second gallery", url: "https://example.com/b" },
  ],
  issues: [],
  steps: [],
  sourceRefs: [{ module: "acquisition", entityKind: "task", entityId: "42" }],
};
beforeEach(() => {
  vi.clearAllMocks();
  mocks.builders.mockResolvedValue([{ id: "some.generic.search", inputKind: "title", entryPoints: ["manual"], nameKey: "Search", descriptionKey: "Search" }]);
  mocks.updateDraft.mockResolvedValue(detail);
  mocks.setBuilder.mockResolvedValue(detail);
  mocks.previewPreparation.mockResolvedValue({ groups: [], requirements: [], canConfirm: false });
  mocks.options.mockResolvedValue({ code: 0, data: { useEmergencySpaceReserve: true, minimumFreeSpaceBytes: 50 * 1024 ** 3 } });
  mocks.get.mockResolvedValue(detail);
  mocks.runtimeOverviews.mockResolvedValue([]);
  mocks.revisionOptions.mockResolvedValue({ taskId: 42, expectedRunId: 75, expectedRevision: 3, canRevise: false, candidates: [] });
  mocks.sourceCorrections.mockResolvedValue({
    taskId: 42,
    expectedRevision: 3,
    canCorrect: false,
    sources: [],
  });
  mocks.sourceHealth.mockReset().mockRejectedValue(new Error("Source observations unavailable"));
  mocks.select.mockResolvedValue({ ...detail, task: { ...detail.task, phase: "ready" } });
});

it("shows current configured storage locations without an original-plan path disclosure", () => {
  render(<TaskPlanSnapshot detail={{ ...detail, task: { ...detail.task, targetDirectory: "/nas/old-plan" } } as any}
    acquisitionOptions={{ downloadRootDirectory: "/ssd/download", processingWorkspaceDirectory: "/ssd/work", deliveryRootDirectory: "/ssd/delivery" } as any}
    onRun={vi.fn()}>{null}</TaskPlanSnapshot>);
  expect(screen.getByText("/ssd/download")).toBeVisible();
  expect(screen.getByText("/ssd/work")).toBeVisible();
  expect(screen.getByText("/ssd/delivery")).toBeVisible();
  expect(screen.queryByText(/old-plan|storageLocations.historical/)).toBeNull();
});

async function openDiagnostics() {
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.plan.diagnostics.open" }));
  return within(await screen.findByRole("dialog", { name: "acquisition.plan.diagnostics.title" }));
}

async function openDiagnosticFiles() {
  const dialog = await openDiagnostics();
  fireEvent.click(dialog.getByRole("tab", { name: "acquisition.plan.tab.files" }));
  return dialog;
}

function chooseExistingFilesIfOffered() {
  const choice = screen.queryByRole("tab", { name: "acquisition.continue.files" });
  if (choice) fireEvent.click(choice);
}

function expectNoLocalContinuation() {
  const entry = screen.queryByRole("button", { name: "acquisition.continue.open" });
  if (entry && !(entry as HTMLButtonElement).disabled) fireEvent.click(entry);
  expect(screen.queryByRole("tab", { name: "acquisition.continue.files" })).toBeNull();
  expect(mocks.completedRevision).not.toHaveBeenCalled();
}

it("shows the server-bound current directory in each stage, deduplicates shared displays and ignores another task's directory record", () => {
  const task = { ...detail, stageAttempts: [], plan: { parts: [{ id: "main", title: "Main" }, { id: "extra", title: "Extra" }] },
    stageDirectories: { taskId: 42, parts: {
      main: { download: { directory: "/current/download" }, processing: { directory: "/current/processing" }, delivery: { directory: "/current/delivery" } },
      extra: { download: { directory: "/current/download" } },
    } } };
  const view = render(<AcquisitionExecutionTimeline detail={task as any} actions={{}} onOpenRun={vi.fn()} />);
  for (const stage of ["download", "processing", "delivery"]) {
    const row = screen.getByText(`acquisition.timeline.stage.${stage}`).closest("li")!;
    expect(within(row).getByText(`/current/${stage}`)).toBeVisible();
  }
  expect(screen.getAllByText("/current/download")).toHaveLength(1);
  view.rerender(<AcquisitionExecutionTimeline detail={{ ...task, stageDirectories: { ...task.stageDirectories, taskId: 99 } } as any}
    actions={{}} onOpenRun={vi.fn()} />);
  expect(screen.queryByText("/current/download")).toBeNull();
});

it.each(["running", "queued"])("labels %s downloads and opens execution without answering the old wait", async (activity) => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", status: AcquisitionStatus.Waiting,
    attentionTags: ["localStorage"],
  } });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 77, currentStepIndex: 3, currentNodeKind: "download.awaitCompletion",
    downloadActivity: activity, attentionTags: [], downloadContinuation: "downloadsOnly",
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  expect(await screen.findByText(`acquisition.downloadActivity.${activity}`)).toBeVisible();
  expect(screen.queryByText("acquisition.plan.phase.attention")).toBeNull();
  expect(screen.queryByRole("button", { name: "acquisition.cancel" })).toBeNull();
  expect(screen.queryByRole("button", { name: "acquisition.continue.open" })).toBeNull();
  expect(screen.queryByRole("button", { name: "acquisition.storageOverride.open" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.plan.execution" }));
  expect(mocks.runContent).toHaveBeenLastCalledWith(expect.objectContaining({ runId: 77, readOnly: true }));
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.cancel).not.toHaveBeenCalled();
});

it("does not offer a reserve override for generic local storage trouble", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", attentionTags: ["localStorage"],
  } });
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.continue.open" }));
  const response = mocks.runContent.mock.calls.at(-1)![0];
  expect(response.wrapWaitingResponse({ id: 77, currentStepIndex: 0 }, "original answer form")).toBe("original answer form");
  expect(mocks.createPortal).not.toHaveBeenCalled();
});

it("keeps the download node's own answer form inside its per-attempt reserve choice", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77, phase: "attention" },
    steps: [{ id: "download", kind: "download.awaitCompletion", stage: "download" }] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77, workflowStatus: "waiting",
    currentStepIndex: 0, currentNodeKind: "download.awaitCompletion" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByRole("button", { name: "acquisition.continue.open" });
  await act(async () => {});
  expect(screen.queryByRole("button", { name: "acquisition.timeline.retry.download" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.open" }));
  const response = mocks.runContent.mock.calls.at(-1)![0];
  const wrapped = response.wrapWaitingResponse({ id: 77, currentStepIndex: 0, workflowDefinitionId: 0 }, "original download answer");
  expect(wrapped.props).toEqual(expect.objectContaining({ taskId: 42, workflowRunId: 77, scope: "download",
    children: "original download answer" }));
  expect(mocks.run).not.toHaveBeenCalled();
});

it("offers a space override from the typed reserve-low wait prompt", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 686, phase: "attention", status: AcquisitionStatus.Waiting,
    attentionTags: [],
    waitPromptJson: JSON.stringify({
      targetId: "part", reason: "StorageUnavailable", message: "LOCAL_STORAGE_RESERVE_LOW",
    }),
  }, plan: { parts: [{ id: "part", sources: [] }] } });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 686, currentNodeKind: "fs.assessDelivery", workflowStatus: "waiting",
    attentionTags: ["fileProcessing"], problemCode: "SOURCE_ATTENTION_REQUIRED",
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByRole("button", { name: "acquisition.continue.open" });
  expect(screen.queryByText("reserve for 42/686")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.open" }));
  expect(await screen.findByText("reserve for 42/686")).toBeVisible();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("keeps an explicit response entry for another part's issue while a download is active", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", status: AcquisitionStatus.Waiting,
  } });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 77, downloadActivity: "running", attentionTags: ["sourceUnavailable"],
    problemCode: "SOURCE_UNAVAILABLE",
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByText("acquisition.downloadActivity.running");
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.open" }));
  expect(mocks.runContent).toHaveBeenLastCalledWith(expect.objectContaining({ runId: 77, respondInitially: true }));
  expect(mocks.run).not.toHaveBeenCalled();
});

it("diagnoses completed downloads without resending the stale download wait", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", status: AcquisitionStatus.Waiting,
  } });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 77, currentStepIndex: 3, currentNodeKind: "download.awaitCompletion",
    downloadsCompletedAwaitingWorkflow: true, canDiagnoseDownloadContinuation: true,
    downloadContinuation: "downloadsOnly",
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  expect(await screen.findByText("completed downloads overview")).toBeVisible();
  expect(mocks.createPortal).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.runtime.continueWorkflow" }));
  expect(mocks.createPortal).toHaveBeenCalledWith(AcquisitionRecoveryModal, expect.objectContaining({
    taskIds: [42], initialScope: "localFiles", onChanged: expect.any(Function),
  }));
  await openDiagnosticFiles();
  expect(mocks.runContent).toHaveBeenLastCalledWith(expect.objectContaining({
    runId: 77,
    completedDownloadWait: expect.objectContaining({
      runId: 77, stepIndex: 3, message: "acquisition.runtime.downloadContinuation.downloadsOnly",
    }),
  }));
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each([false, undefined])("keeps workflow diagnosis when completed-download evidence is %s", async (completed) => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", status: AcquisitionStatus.Waiting,
  } });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 77, currentStepIndex: 3, currentNodeKind: "download.awaitCompletion",
    downloadsCompletedAwaitingWorkflow: completed, canDiagnoseDownloadContinuation: true,
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.runtime.continueWorkflow" }));
  expect(mocks.createPortal).toHaveBeenCalledWith(AcquisitionRecoveryModal, expect.objectContaining({
    taskIds: [42], initialScope: "workflow",
  }));
});

it("checks the original processing cursor before retrying a failed stage handoff", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77,
    phase: "attention", status: AcquisitionStatus.Failed, error: "STAGE_COMMIT_REFERENCED" },
    steps: [{ id: "part.process", kind: "acquisition.plan.process", stage: "processing" }],
    plan: { parts: [{ id: "part", sources: [] }] }, replanArtifacts: [
      { artifactId: "download", partId: "part", kind: "download", canReadCurrentDirectory: true },
    ] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77, currentStepIndex: 0,
    currentNodeKind: "acquisition.plan.process", workflowStatus: "failed", problemCode: "STAGE_COMMIT_REFERENCED" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.continue.open" }));
  expect(mocks.createPortal).toHaveBeenCalledWith(AcquisitionRecoveryModal, expect.objectContaining({
    taskIds: [42], initialScope: "workflow",
  }));
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each([
  ["ProcessingPathConflict", "acquisition.plan.process", true],
  ["ProcessingPathConflict", "download.awaitCompletion", false],
  ["PasswordUnknown", "acquisition.plan.process", false],
])("offers the existing plan revision only for a stopped processing path conflict: %s %s", async (problemCode, currentNodeKind, allowed) => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", status: AcquisitionStatus.Waiting,
  }, plan: { parts: [{ id: "part", sources: [] }] },
  replanArtifacts: [{ artifactId: "saved-download", partId: "part", kind: "download", canReadCurrentDirectory: allowed }] });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 77, currentStepIndex: 3, currentNodeKind, problemCode,
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await waitFor(() => expect(mocks.runtimeOverviews).toHaveBeenCalled());
  await act(async () => {});
  const button = screen.queryByRole("button", { name: "acquisition.continue.open" });
  if (allowed) {
    expect(button).toBeEnabled();
    fireEvent.click(button!);
    chooseExistingFilesIfOffered();
    expect(mocks.completedRevision).toHaveBeenLastCalledWith(expect.objectContaining({
      mode: "stopped", waitingPathConflict: { taskId: 42, runId: 77, planRevision: 3 },
    }));
  }
  else expectNoLocalContinuation();
});

it("does not hide a newer verification failure merely because downloads completed", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", status: AcquisitionStatus.Waiting,
  } });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 77, currentStepIndex: 3, currentNodeKind: "download.awaitCompletion",
    downloadsCompletedAwaitingWorkflow: true, canDiagnoseDownloadContinuation: true,
    downloadContinuation: "automatic", problemCode: "RECOVERY_MANIFEST_CHANGED", attentionTags: ["fileProcessing"],
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByText("completed downloads overview");
  await openDiagnosticFiles();
  expect(mocks.runContent).toHaveBeenLastCalledWith(expect.objectContaining({
    runId: 77, completedDownloadWait: undefined,
  }));
});

it("requires continuation evidence for every part before opening a waiting conflict revision", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", status: AcquisitionStatus.Waiting,
  }, plan: { parts: [{ id: "part", sources: [] }, { id: "missing", sources: [] }] },
  replanArtifacts: [{ artifactId: "saved-download", partId: "part", kind: "download" }] });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 77, currentStepIndex: 3,
    currentNodeKind: "acquisition.plan.process", problemCode: "ProcessingPathConflict",
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByRole("button", { name: "acquisition.continue.open" });
  expectNoLocalContinuation();
  expect(mocks.completedRevision).not.toHaveBeenCalled();
});
async function openPreparation() {
  if (screen.queryByRole("dialog", {name: "acquisition.timeline.stage.preparation"})) return;
  const timeline = await screen.findByRole("region", {name: "acquisition.timeline.title"});
  const row = within(timeline).getByRole("heading", {name: "acquisition.timeline.stage.preparation"}).closest("li")!;
  fireEvent.click(within(row).getByRole("button", {name: "acquisition.continue.open"}));
}
async function openDownload() {
  if (screen.queryByRole("dialog", {name: "acquisition.preparation.startTitle"})) return;
  const timeline = await screen.findByRole("region", {name: "acquisition.timeline.title"});
  const row = within(timeline).getByRole("heading", {name: "acquisition.timeline.stage.download"}).closest("li")!;
  fireEvent.click(within(row).getByRole("button", {name: "acquisition.continue.open"}));
}

it("keeps preparation inside Continue Processing and its modal", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, builderId: "draft", phase: "preparing" }, query: "Saved title", candidates: [] });
  mocks.search.mockResolvedValue(detail);
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByRole("region", {name: "acquisition.timeline.title"});
  expect(screen.queryByRole("button", { name: "sourceAvailability.review.one" })).toBeNull();
  expect(screen.queryByRole("button", { name: "acquisition.draft.search" })).toBeNull();
  expect(screen.queryByRole("button", { name: "filesystem selector" })).toBeNull();
  await openPreparation();
  const search = await screen.findByRole("button", { name: "acquisition.draft.search" });
  await waitFor(() => expect(search).toBeEnabled());
  fireEvent.click(search);
  expect(await screen.findByText("First gallery")).toBeVisible();
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each(["draft", "plan"])("checks saved %s sources from preparation and refreshes the detail and list", async (type) => {
  const onChanged = vi.fn();
  const source = { id: "saved", kind: "auto", url: "https://example.com/saved" };
  mocks.sourceHealth.mockResolvedValue([]);
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, builderId: "draft", phase: "attention" },
    ...(type === "draft" ? { draftSources: [source] } : { plan: { parts: [{ id: "part", sources: [source] }] } }),
  });
  render(<PlanTaskDetailModal taskId={42} onChanged={onChanged} />);
  const timeline = await screen.findByRole("region", { name: "acquisition.timeline.title" });
  const preparation = within(timeline).getByRole("heading", { name: "acquisition.timeline.stage.preparation" }).closest("li")!;
  const reviewButton = { name: "sourceAvailability.review.one" };
  expect(within(timeline).getAllByRole("button", reviewButton)).toHaveLength(1);
  fireEvent.click(within(preparation).getByRole("button", reviewButton));
  const [modal, props] = mocks.createPortal.mock.lastCall!;
  expect(modal).toBe(SourceAvailabilityReviewModal);
  expect(props.taskIds).toEqual([42]);
  expect(mocks.previewPreparation).not.toHaveBeenCalled();

  mocks.get.mockClear();
  mocks.sourceHealth.mockClear();
  await act(async () => props.onChecked());
  expect(mocks.get).toHaveBeenCalledExactlyOnceWith(42);
  expect(mocks.sourceHealth).toHaveBeenCalledExactlyOnceWith(42);
  expect(onChanged).toHaveBeenCalledOnce();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("opens recorded title-search candidates in preparation without auto-selecting one", async () => {
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByRole("region", {name: "acquisition.timeline.title"});
  expect(screen.queryByText("First gallery")).toBeNull();
  await openPreparation();
  await screen.findByText("First gallery");
  expect(mocks.select).not.toHaveBeenCalled();
  fireEvent.click(screen.getAllByRole("button", { name: "acquisition.plan.choose" })[1]);
  await waitFor(() => expect(mocks.select).toHaveBeenCalledExactlyOnceWith(42, "c2", 3));
  expect(mocks.run).not.toHaveBeenCalled();
});

const asIsInput = { taskId: 42, artifactId: "local", operationId: "op", previousRunId: 101, fingerprint: "hash" };
const readyWithFiles = {
  ...detail, task: { ...detail.task, phase: "ready" }, canKeepPreparedFiles: true,
  plan: { parts: [{ id: "part", title: "Bound download", sources: [], continueFrom: asIsInput,
    processing: { requirement: "required", steps: [{ op: "extractArchive" }] } }] },
  planHistory: [{ revision: 3, plan: { parts: [] }, sourceRefs: [], artifactEvidence: [
    { partId: "part", artifactId: "local", fingerprint: "hash", manifest: { entries: [{}, {}] } },
  ] }],
};
const keepRevision = {
  ...readyWithFiles, task: { ...readyWithFiles.task, planRevision: 4 }, plan: {
    parts: [{...readyWithFiles.plan.parts[0], processing: { requirement: "notRequired", steps: [] } }],
  },
  planHistory: [{ revision: 4, reason: "delivery.keepPreparedFiles" }],
};

it.each(["原帖 10 项，当前登记 9 个包。", ""])("uses the saved as-is revision and current reserve choice only after confirmation (%s)", async note => {
  mocks.get.mockResolvedValue(readyWithFiles);
  mocks.keepPreparedFiles.mockResolvedValue({ ...keepRevision,
    planHistory: [{ revision: 4, reason: note || "delivery.keepPreparedFiles" }] });
  mocks.run.mockResolvedValue(keepRevision);
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.keepPrepared.title" }));
  const modal = within(screen.getByRole("dialog", { name: "acquisition.keepPrepared.title" }));
  expect(modal.getByText("acquisition.keepPrepared.fileCount")).toBeVisible();
  if (note) fireEvent.change(modal.getByRole("textbox", {name: "acquisition.keepPrepared.note"}), {target: {value: `  ${note}  `}});
  fireEvent.click(await modal.findByRole("checkbox", { name: "acquisition.reserve.operation.label" }));
  expect(mocks.keepPreparedFiles).not.toHaveBeenCalled();
  expect(mocks.run).not.toHaveBeenCalled();
  fireEvent.click(modal.getByRole("button", { name: "acquisition.keepPrepared.confirm" }));
  await waitFor(() => expect(mocks.run).toHaveBeenCalledExactlyOnceWith(42, 4, false));
  expect(mocks.keepPreparedFiles).toHaveBeenCalledExactlyOnceWith(42, 3, note || undefined);
});

it.each(["saved", "wrongChoice", "changedSecondInput", "staleRevision"])("confirms every part before starting deferred local processing (%s)", async mode => {
  const parts = [readyWithFiles.plan.parts[0], {
    ...readyWithFiles.plan.parts[0], id: "part-b", title: "Second bound download",
    continueFrom: { ...asIsInput, artifactId: "local-b", operationId: "op-b" },
  }];
  mocks.get.mockResolvedValue({ ...readyWithFiles, canDeferPreparedProcessing: true, plan: { parts } });
  mocks.deferPreparedProcessing.mockResolvedValue({
    ...keepRevision, plan: {
      adaptiveFileProcessing: false, parts: parts.map((p, i) => ({
        ...p, useAdaptiveAssessment: false, processing: { requirement: mode === "wrongChoice" ? "notRequired" : "unknown", steps: [] },
        continueFrom: mode === "changedSecondInput" && i === 1 ? asIsInput : p.continueFrom,
      })),
    }, planHistory: [{ revision: mode === "staleRevision" ? 3 : 4, reason: "processing.deferPreparedFiles" }],
  });
  mocks.run.mockResolvedValue(keepRevision);
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.deferPrepared.title" }));
  const modal = within(screen.getByRole("dialog", { name: "acquisition.deferPrepared.title" }));
  expect(modal.getByText("Second bound download")).toBeVisible();
  expect(modal.queryByRole("textbox")).toBeNull();
  await modal.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  expect(mocks.deferPreparedProcessing).not.toHaveBeenCalled();
  fireEvent.click(modal.getByRole("button", { name: "acquisition.deferPrepared.confirm" }));
  await waitFor(() => expect(mocks.deferPreparedProcessing).toHaveBeenCalledExactlyOnceWith(42, 3));
  if (mode === "saved") {
    await waitFor(() => expect(mocks.run).toHaveBeenCalledExactlyOnceWith(42, 4, true));
  } else {
    await modal.findByRole("alert");
    expect(mocks.run).not.toHaveBeenCalled();
  }
  expect(mocks.keepPreparedFiles).not.toHaveBeenCalled();
});

it.each(["unbound", "missingCapability", "differentOwner"])("hides as-is delivery when the source is not an eligible ready task (%s)", async mode => {
  mocks.get.mockResolvedValue({ ...readyWithFiles, canKeepPreparedFiles: mode !== "missingCapability" ? true : undefined,
    plan: { parts: [{ ...readyWithFiles.plan.parts[0], continueFrom: mode === "unbound" ? undefined : {
      ...asIsInput, taskId: mode === "differentOwner" ? 43 : 42,
    } }] } });
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByRole("region", { name: "acquisition.timeline.title" });
  expect(screen.queryByRole("button", { name: "acquisition.keepPrepared.title" })).toBeNull();
});

it.each(["changedInput", "stillExtracts", "stillAdaptive", "missingNote", "failedSave"])("does not start or resubmit an unconfirmed as-is revision (%s)", async mode => {
  mocks.get.mockResolvedValue(readyWithFiles);
  if (mode === "failedSave") mocks.keepPreparedFiles.mockRejectedValueOnce(new Error("Request failed"));
  else mocks.keepPreparedFiles.mockResolvedValue({ ...keepRevision, planHistory: mode === "missingNote" ? [] : keepRevision.planHistory,
    plan: { adaptiveFileProcessing: mode === "stillAdaptive",
    parts: [{ ...keepRevision.plan.parts[0], continueFrom: { ...asIsInput, fingerprint: mode === "changedInput" ? "changed" : "hash" },
      processing: mode === "stillExtracts" ? readyWithFiles.plan.parts[0].processing : keepRevision.plan.parts[0].processing }] } });
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.keepPrepared.title" }));
  const modal = within(screen.getByRole("dialog", { name: "acquisition.keepPrepared.title" }));
  await modal.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  const submit = modal.getByRole("button", { name: "acquisition.keepPrepared.confirm" });
  fireEvent.click(submit);
  await modal.findByRole("alert");
  expect(submit).toBeDisabled();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("shows every declared input and queries history across plan revisions", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "ready" },
    sourceRefs: [{ ...detail.sourceRefs[0], revision: "3", resultKey: "current" }],
    steps: [
      {
        id: "extract",
        kind: "acquisition.plan.process",
        operation: "extractArchive",
        input: "ignored-legacy-input",
        inputs: ["download", "move-part"],
      },
    ],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  expect(await screen.findByRole("region", { name: "acquisition.timeline.title" })).toBeVisible();
  expect(screen.getByText("acquisition.timeline.stage.processing")).toBeVisible();
  expect(screen.getAllByText("acquisition.timeline.state.pending")).toHaveLength(3);
  const diagnostics = await openDiagnostics();
  expect(diagnostics.getByText("download + move-part")).toBeVisible();
  expect(document.body).not.toHaveTextContent("ignored-legacy-input");
  const taskHistoryRef = [{ module: "acquisition", entityKind: "task", entityId: "42" }];
  expect(mocks.sourceRuns).not.toHaveBeenCalledWith(taskHistoryRef);
  fireEvent.click(diagnostics.getByRole("tab", { name: "acquisition.plan.tab.files" }));
  expect(mocks.sourceRuns).toHaveBeenLastCalledWith(taskHistoryRef);
});

it("shows literal filename removal as an explicit empty replacement", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "ready" },
    steps: [
      {
        id: "remove",
        kind: "acquisition.plan.process",
        operation: "renameLiteral",
        labelKey: "acquisition.plan.processing.renameLiteral",
        input: "download",
        findText: "remove-me",
        replaceText: "",
      },
    ],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const diagnostics = await openDiagnostics();
  expect(diagnostics.getByText("acquisition.plan.processing.renameLiteral")).toBeVisible();
  expect(screen.getByText("remove-me")).toBeVisible();
  expect(screen.getByText("acquisition.plan.step.emptyReplacement")).toBeVisible();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("shows the exact source directory and an explicit output root for directory relocation", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "ready" },
    steps: [
      {
        id: "rebase",
        kind: "acquisition.plan.process",
        operation: "rebaseDirectory",
        labelKey: "acquisition.plan.processing.rebaseDirectory",
        input: "download",
        sourceDirectory: "outer/author",
        targetDirectory: "",
      },
    ],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const diagnostics = await openDiagnostics();
  expect(diagnostics.getByText("acquisition.plan.processing.rebaseDirectory")).toBeVisible();
  expect(screen.getByText("outer/author")).toBeVisible();
  expect(screen.getByText("acquisition.plan.step.outputRoot")).toBeVisible();
  expect(mocks.run).not.toHaveBeenCalled();
});
it("retries a revised query in the same task and renders no raw issue message", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    candidates: [],
    issues: [{ code: "BUILDER_SEARCH_FAILED", message: "private-token-canary", blocking: true }],
  });
  mocks.search.mockResolvedValue(detail);
  render(<PlanTaskDetailModal taskId={42} />);
  await openPreparation();
  await screen.findByText("acquisition.plan.noCandidates");
  expect(document.body).not.toHaveTextContent("private-token-canary");
  fireEvent.change(screen.getByRole("textbox"), { target: { value: "new query" } });
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.draft.search" })).toBeEnabled());
  fireEvent.click(screen.getByRole("button", { name: "acquisition.draft.search" }));
  await waitFor(() => expect(mocks.search).toHaveBeenCalledExactlyOnceWith(42, "new query"));
});
it("allows execution to reach a later nonblocking processing confirmation", async () => {
  const ready = {
    ...detail,
    task: { ...detail.task, phase: "ready" },
    steps: [{ id: "n1", kind: "download.enqueue" }],
    issues: [{ code: "PROCESSING_CONFIRMATION_REQUIRED", blocking: false }],
  };
  mocks.get.mockResolvedValue(ready);
  mocks.run.mockResolvedValue(ready);
  render(<PlanTaskDetailModal taskId={42} />);
  await openDownload();
  const start = await screen.findByRole("button", { name: "acquisition.plan.run" });
  expect(start).toBeEnabled();
  fireEvent.click(start);
  await waitFor(() => expect(mocks.run).toHaveBeenCalledExactlyOnceWith(42, 3));
});

it.each(["processing", "delivery"] as const)("starts a ready local plan from its %s stage", async (stage) => {
  const ready = {
    ...readyWithFiles,
    steps: [
      { id: "existing", kind: "acquisition.plan.continueFiles", stage, labelKey: "acquisition.plan.continueFiles" },
      ...(stage === "processing" ? [{ id: "extract", kind: "acquisition.plan.process", stage }] : []),
      { id: "publish", kind: "fs.publishArtifacts", stage: "delivery", labelKey: "workflow.activity.fsPublishArtifacts.name" },
    ],
  };
  mocks.get.mockResolvedValue(ready);
  mocks.run.mockResolvedValue(ready);
  render(<PlanTaskDetailModal taskId={42} />);
  const timeline = await screen.findByRole("region", { name: "acquisition.timeline.title" });
  const download = within(timeline).getByRole("heading", { name: "acquisition.timeline.stage.download" }).closest("li")!;
  expect(within(download).queryByRole("button", { name: "acquisition.continue.open" })).toBeNull();

  const stageName = `acquisition.timeline.stage.${stage}`;
  const stageRow = within(timeline).getByRole("heading", { name: stageName }).closest("li")!;
  fireEvent.click(within(stageRow).getByRole("button", { name: "acquisition.continue.open" }));
  const confirmation = await screen.findByRole("dialog", { name: stageName });
  const steps = within(confirmation).getByRole("list", { name: "acquisition.plan.steps" });
  expect(within(steps).getByText("acquisition.plan.continueFiles")).toBeVisible();
  expect(within(steps).getByText("workflow.activity.fsPublishArtifacts.name")).toBeVisible();
  expect(within(confirmation).queryByRole("region", { name: "acquisition.plan.parts" })).toBeNull();
  expect(screen.queryByRole("dialog", { name: "acquisition.preparation.startTitle" })).toBeNull();
  fireEvent.click(within(confirmation).getByRole("button", { name: "acquisition.plan.run" }));
  await waitFor(() => expect(mocks.run).toHaveBeenCalledExactlyOnceWith(42, 3));
});

it("allows a blocked prepared plan to be checked again before its first run", async () => {
  const prepared = { ...detail, task: { ...detail.task, phase: "attention", builderId: "prepared" },
    plan: { parts: [{ id: "part", sources: [] }] },
    steps: [{ id: "n1", kind: "download.enqueueSupported" }],
    issues: [{ code: "SOURCE_UNSUPPORTED", blocking: true }],
  };
  mocks.get.mockResolvedValue(prepared);
  mocks.preflight.mockResolvedValue({ ...prepared, task: { ...prepared.task, phase: "ready" }, issues: [] });
  render(<PlanTaskDetailModal taskId={42} />);
  await openDownload();
  const check = await screen.findByRole("button", { name: "acquisition.plan.preflight" });
  expect(screen.getByRole("button", { name: "acquisition.plan.run" })).toBeDisabled();
  fireEvent.click(check);
  await openDownload();
  expect(await screen.findByRole("button", { name: "acquisition.plan.run" })).toBeEnabled();
  expect(mocks.preflight).toHaveBeenCalledWith(42);
  expect(mocks.run).not.toHaveBeenCalled();
});

it("does not execute a ready plan with a blocking prerequisite", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "ready" },
    steps: [{ id: "n1", kind: "download.enqueue" }],
    issues: [{ code: "OUTPUT_DIRECTORY_REQUIRED", blocking: true }],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  await openDownload();
  const start = await screen.findByRole("button", { name: "acquisition.plan.run" });
  expect(start).toBeDisabled();
  fireEvent.click(start);
  expect(mocks.run).not.toHaveBeenCalled();
});

it("keeps a repaired plan ready when the server detects a changed download configuration", async () => {
  const ready = {
    ...detail,
    task: { ...detail.task, phase: "ready" },
    steps: [{ id: "download", kind: "download.enqueue" }],
  };
  mocks.get.mockResolvedValue(ready);
  mocks.run.mockResolvedValue({
    ...ready,
    issues: [{ code: "PLAN_REPLAN_SOURCE_CHANGED", blocking: true }],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  await openDownload();
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.plan.run" }));
  expect(
    await screen.findByText("acquisition.plan.issue.PLAN_REPLAN_SOURCE_CHANGED"),
  ).toBeVisible();
  expect(screen.getByRole("button", { name: "acquisition.plan.run" })).toBeDisabled();
  expect(screen.getByRole("button", { name: "acquisition.plan.preflight" })).toBeEnabled();
  expect(mocks.run).toHaveBeenCalledTimes(1);
});

it("keeps previous search attempts visible alongside the current plan", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    preparationAttempts: [
      {
        attempt: 1,
        query: "earlier search",
        startedAt: "2026-10-21T08:00:00Z",
        status: "attention",
        candidateCount: 0,
        code: "BUILDER_NO_RESULTS",
      },
      {
        attempt: 2,
        query: "later search",
        startedAt: "2026-10-21T09:00:00Z",
        status: "completed",
        candidateCount: 2,
      },
    ],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const diagnostics = await openDiagnostics();
  expect(diagnostics.getByText("earlier search")).toBeVisible();
  expect(screen.getByText("later search")).toBeVisible();
  expect(mocks.search).not.toHaveBeenCalled();
});

it("rechecks prerequisites after external configuration changes without starting execution", async () => {
  const ready = {
    ...detail,
    task: { ...detail.task, phase: "ready" },
    steps: [{ id: "n1", kind: "download.enqueue" }],
    issues: [{ code: "ACCOUNT_REQUIRED", blocking: true }],
  };
  mocks.get.mockResolvedValue(ready);
  mocks.preflight.mockResolvedValue({ ...ready, issues: [] });
  render(<PlanTaskDetailModal taskId={42} />);
  await openDownload();
  expect(await screen.findByRole("button", { name: "acquisition.plan.run" })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.plan.preflight" }));
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "acquisition.plan.run" })).toBeEnabled(),
  );
  expect(mocks.preflight).toHaveBeenCalledExactlyOnceWith(42);
  expect(mocks.run).not.toHaveBeenCalled();
});

it("applies the server-bound source revision explicitly with acquisition CAS and never starts a run", async () => {
  const source = {
    module: "postParser",
    entityKind: "task",
    entityId: "41",
    revision: "1",
    resultKey: "2",
  };
  const ready = {
    ...detail,
    task: { ...detail.task, phase: "ready" },
    plan: {
      parts: [{ id: "content-part", sources: [], processingSource: source, processing: null }],
    },
  };
  mocks.get.mockResolvedValue(ready);
  mocks.applyProcessingRevision.mockResolvedValue(ready);
  render(<PlanTaskDetailModal taskId={42} />);
  await openPreparation();
  fireEvent.click(await screen.findByRole("button", { name: "apply reviewed revision" }));
  await waitFor(() =>
    expect(mocks.applyProcessingRevision).toHaveBeenCalledExactlyOnceWith(
      42,
      3,
      "content-part",
      source,
      "saved-revision",
    ),
  );
  expect(mocks.run).not.toHaveBeenCalled();
});

it("keeps plan editing on the task while its diagnostic copy is read-only", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "ready" },
    plan: {
      parts: [{ id: "part", sources: [], processingSource: detail.sourceRefs[0] }],
    },
    canEditStoragePolicy: true,
    canEditDeliverySettings: true,
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const main = within(await screen.findByRole("dialog", { name: "#42 Chosen content" }));
  expect(main.queryByRole("button", { name: "apply reviewed revision" })).toBeNull();
  await openPreparation();
  expect(await screen.findByRole("button", { name: "apply reviewed revision" })).toBeEnabled();
  expect(main.queryByRole("tablist")).toBeNull();


  const diagnostics = await openDiagnostics();
  expect(diagnostics.getByRole("tab", { name: "acquisition.plan.tab.plan" })).toHaveAttribute("aria-selected", "true");
  expect(diagnostics.getByText("frozen revision history")).toBeVisible();
  for (const name of [
    "acquisition.plan.run",
    "acquisition.delivery.edit",
    "acquisition.storage.edit",
    "sourceAvailability.review.one",
    "apply reviewed revision",
    "filesystem selector",
  ]) {
    expect(diagnostics.queryByRole("button", { name })).toBeNull();
  }

  fireEvent.click(diagnostics.getByRole("tab", { name: "acquisition.plan.tab.files" }));
  expect(diagnostics.getByText("output metadata")).toBeVisible();
  fireEvent.click(diagnostics.getByRole("button", { name: "acquisition.plan.close" }));
  expect(screen.queryByRole("dialog", { name: "acquisition.plan.diagnostics.title" })).toBeNull();
  expect(screen.getByRole("dialog", { name: "acquisition.timeline.stage.preparation" })).toBeVisible();
  expect(screen.getByRole("button", { name: "apply reviewed revision" })).toBeEnabled();
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.applyProcessingRevision).not.toHaveBeenCalled();
});

it("keeps keyboard focus inside diagnostics when navigating to a waiting historical run and back", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "executing", workflowRunId: 75 },
  });
  mocks.querySourceRuns.mockResolvedValue({
    code: 0,
    data: [{
      runCount: 1,
      runs: [{
        run: { id: 70, status: WorkflowRunStatus.Waiting, startedAt: "2026-11-09T01:00:00Z" },
        downloadTaskIds: [],
      }],
    }],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const diagnostics = await openDiagnosticFiles();

  // Each navigation target replaces the triggering button; ordinary fireEvent.click
  // does not give that button focus as keyboard and real-browser activation do.
  const activate = async (name: string) => {
    const button = await diagnostics.findByRole("button", { name });
    button.focus();
    fireEvent.click(button);
  };
  await activate("acquire.history.count");
  expect(document.activeElement).toHaveAccessibleName("acquire.history.title");
  expect(diagnostics.queryByRole("dialog")).toBeNull();
  expect(diagnostics.queryByRole("button", { name: "workflow.resume.respond" })).toBeNull();
  await activate("acquire.openRun");
  const selectedRun = diagnostics.getByRole("region", { name: "workflow.runDetail.title" });
  expect(selectedRun).toHaveFocus();
  expect(selectedRun).toHaveTextContent("inline run 70");
  expect(mocks.runContent).toHaveBeenLastCalledWith(
    expect.objectContaining({ runId: 70, readOnly: true, respondInitially: false }),
  );

  await activate("acquire.history.count");
  await activate("acquisition.plan.diagnostics.backToRun");
  expect(diagnostics.getByRole("region", { name: "workflow.runDetail.title" })).toHaveFocus();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("provides revision history but no apply action for an already started plan", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "executing", workflowRunId: 75 },
    plan: {
      parts: [
        {
          id: "part",
          sources: [],
          processingSource: {
            module: "postParser",
            entityKind: "task",
            entityId: "41",
            revision: "1",
            resultKey: "0",
          },
        },
      ],
    },
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const diagnostics = await openDiagnostics();
  expect(diagnostics.getByText("frozen revision history")).toBeVisible();
  expect(mocks.processingPanel.mock.lastCall?.[0]).toMatchObject({
    frozen: true,
    onApply: undefined,
  });
  expect(mocks.applyProcessingRevision).not.toHaveBeenCalled();
});

it("shows the frozen source address and access code without starting or retrying the task", async () => {
  const url = "https://pan.baidu.com/s/verify-this-share";
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "attention", workflowRunId: 75 },
    plan: {
      parts: [
        {
          id: "main",
          title: "Main content",
          selectedSourceId: "share",
          sources: [
            {
              id: "share",
              kind: "share",
              providerId: "baidu",
              url,
              accessCode: "a1b2",
              archivePassword: "archive-pass",
            },
          ],
        },
      ],
    },
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const diagnostics = await openDiagnostics();
  expect(diagnostics.getByRole("link", { name: url })).toHaveAttribute("href", url);
  expect(screen.getByText("a1b2")).toBeVisible();
  expect(screen.getByText("archive-pass")).toBeVisible();
  fireEvent.click(
    screen.getByRole("button", { name: "acquisition.sourceDetails.copy.accessCode" }),
  );
  await waitFor(() => expect(mocks.copy).toHaveBeenCalledWith("a1b2"));
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.cancel).not.toHaveBeenCalled();
});

it("keeps the task active when the user only closes the modal", async () => {
  render(<PlanTaskDetailModal taskId={42} />);
  const modal = await screen.findByRole("dialog", { name: "#42 Chosen content" });
  expect(within(modal).queryByRole("button", { name: "acquisition.cancel" })).toBeNull();
  expect(within(modal).getByRole("button", { name: "acquisition.delete.single" })).toBeEnabled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.plan.close" }));
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(mocks.cancel).not.toHaveBeenCalled();
});

it.each([AcquisitionStatus.Waiting, AcquisitionStatus.Failed, AcquisitionStatus.Cancelled])(
  "retains deleted task files and history without mutable controls for status %s",
  async (status) => {
    mocks.get.mockResolvedValue({
      ...detail,
      task: { ...detail.task, phase: "ready", status, workflowRunId: 75, isDeleted: true },
      steps: [{ id: "n1", kind: "download.enqueue" }],
      plan: { parts: [{ id: "part", sources: [], processingSource: detail.sourceRefs[0] }] },
    });
    render(<PlanTaskDetailModal taskId={42} />);
    expect(await screen.findByText("acquisition.delete.readOnly")).toBeVisible();
    for (const label of [
      "acquisition.plan.run",
      "acquisition.plan.preflight",
      "acquisition.cancel",
      "acquisition.retry",
      "acquisition.plan.replan.title",
      "sourceAvailability.review.one",
      "acquisition.sourceRevision.title",
      "filesystem selector",
    ])
      expect(screen.queryByRole("button", { name: label })).toBeNull();
    const diagnostics = await openDiagnostics();
    expect(mocks.processingPanel.mock.lastCall?.[0]).toMatchObject({
      frozen: true,
      isDisabled: true,
      onApply: undefined,
    });
    fireEvent.click(diagnostics.getByRole("tab", { name: "acquisition.plan.tab.files" }));
    expect(mocks.runContent).toHaveBeenLastCalledWith(
      expect.objectContaining({ runId: 75, readOnly: true }),
    );
    expect(mocks.createPortal).not.toHaveBeenCalled();
    expect(diagnostics.getByText("output metadata")).toBeVisible();
    expect(mocks.run).not.toHaveBeenCalled();
    expect(mocks.cancel).not.toHaveBeenCalled();
  },
);

it("does not permit edits to a deleted ready task that never started", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "ready", deletedAt: "2026-10-25T12:00:00Z" },
    steps: [{ id: "n1", kind: "download.enqueue" }],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByText("acquisition.delete.readOnly");
  expect(screen.queryByRole("button", { name: "acquisition.plan.run" })).toBeNull();
  expect(screen.queryByRole("button", { name: "acquisition.plan.search" })).toBeNull();
  expect(screen.queryByRole("button", { name: "filesystem selector" })).toBeNull();
});

it("opens source replacement only when the server accepts the current source and checkpoint", async () => {
  mocks.revisionOptions.mockResolvedValue({ taskId: 42, expectedRunId: 75, expectedRevision: 3,
    canRevise: true, candidates: [{ id: "producer-mirror", sourceRefs: [{ module: "postParser" }] }] });
  mocks.get.mockResolvedValue({
    ...detail,
    task: {
      ...detail.task,
      phase: "executing",
      status: AcquisitionStatus.Waiting,
      workflowRunId: 75,
    },
  });
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.sourceRevision.title" }));
  expect(mocks.createPortal).toHaveBeenCalledWith(expect.any(Function), {
    taskId: 42,
    onChanged: expect.any(Function),
  });
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.cancel).not.toHaveBeenCalled();
});

it.each(["none", "unsafe", "stale"])("hides source replacement when %s despite unrelated search candidates", async mode => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 75 } });
  mocks.revisionOptions.mockResolvedValue({ taskId: 42, expectedRunId: mode === "stale" ? 74 : 75, expectedRevision: 3,
    canRevise: mode !== "unsafe", candidates: mode === "none" ? [] : [{ id: "mirror" }] });
  render(<PlanTaskDetailModal taskId={42} />);
  await waitFor(() => expect(mocks.revisionOptions).toHaveBeenCalled());
  expect(screen.queryByRole("button", { name: "acquisition.sourceRevision.title" })).toBeNull();
});

it("does not infer access codes from URLs and keeps unsupported addresses non-clickable", () => {
  const view = render(
    <AcquisitionSourceDetails
      source={{
        url: "https://example.com/share?pwd=opaque-query",
        originalUrl: "https://short.example/a",
      }}
    />,
  );
  expect(screen.getByText("acquisition.sourceDetails.notProvided")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "acquisition.sourceDetails.copy.accessCode" }),
  ).toBeNull();
  expect(screen.getByRole("link", { name: "https://short.example/a" })).toBeVisible();
  view.rerender(
    <AcquisitionSourceDetails source={{ url: "javascript:alert(1)", accessCode: "known-code" }} />,
  );
  expect(screen.queryByRole("link")).toBeNull();
  expect(screen.getByText("javascript:alert(1)")).toBeVisible();
  expect(screen.getByText("known-code")).toBeVisible();
});

it("shows execution-only access-code revisions and their evidence without changing the saved plan", () => {
  const source = {
    url: "https://pan.baidu.com/s/reviewed-share",
    accessCode: "new-code",
    archivePassword: "archive-pass",
    accessCodeRevisions: [
      {
        correctionId: "revision-1",
        expectedSourceIdentity: "opaque-identity",
        previousAccessCode: "old-code",
        newAccessCode: "new-code",
        basis: "sourceEvidence" as const,
        evidenceReference: "https://example.com/original-post",
        evidence: "The original post gives the access code as new-code.",
        appliedAt: "2026-10-25T02:00:00Z",
      },
    ],
  };
  const before = JSON.stringify(source);
  render(<AcquisitionSourceDetails source={source} />);
  expect(screen.getByText("acquisition.sourceDetails.currentAccessCode")).toBeVisible();
  expect(screen.getByText("acquisition.sourceDetails.revisions.executionOnly")).toBeVisible();
  expect(screen.getByText("old-code")).toBeVisible();
  expect(screen.getByRole("link", { name: "https://example.com/original-post" })).toHaveAttribute(
    "href",
    "https://example.com/original-post",
  );
  expect(screen.getByText("The original post gives the access code as new-code.")).toBeVisible();
  expect(document.querySelector("time")).toHaveAttribute("datetime", "2026-10-25T02:00:00Z");
  expect(document.body).not.toHaveTextContent("opaque-identity");
  expect(screen.getByText("archive-pass")).toBeVisible();
  expect(JSON.stringify(source)).toBe(before);
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.cancel).not.toHaveBeenCalled();
});

it("distinguishes a recorded code removal from an unknown access code and renders unsafe evidence as text", () => {
  render(
    <AcquisitionSourceDetails
      source={{
        url: "/account/folder",
        accessCodeRevisions: [
          {
            correctionId: "removed",
            expectedSourceIdentity: "opaque-identity",
            previousAccessCode: "wrong-code",
            newAccessCode: null,
            basis: "manual",
            evidenceReference: "javascript:alert(1)",
            appliedAt: "2026-10-25T02:00:00Z",
          },
        ],
      }}
    />,
  );
  expect(screen.getByText("acquisition.sourceDetails.revisions.removed")).toBeVisible();
  expect(screen.getByText("wrong-code")).toBeVisible();
  expect(screen.queryByRole("link")).toBeNull();
  expect(screen.getByText("javascript:alert(1)")).toBeVisible();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("uses corrected execution source only for the exact part and source while retaining the original plan", async () => {
  const originalSource = {
    id: "shared-id",
    kind: "cloud",
    providerId: "baidu",
    url: "https://pan.baidu.com/s/original",
    accessCode: "original-plan-code",
  };
  const taskDetail = {
    ...detail,
    task: { ...detail.task, phase: "ready", workflowRunId: 75 },
    plan: {
      parts: [
        { id: "part-a", sources: [originalSource] },
        { id: "part-b", sources: [{ ...originalSource, accessCode: "other-part-code" }] },
      ],
    },
  };
  const original = JSON.stringify(taskDetail);
  mocks.get.mockResolvedValue(taskDetail);
  mocks.sourceCorrections.mockResolvedValue({
    taskId: 42,
    expectedRunId: 75,
    expectedRevision: 3,
    canCorrect: false,
    reasonCode: "SOURCE_CORRECTION_ALREADY_ACQUIRED",
    sources: [
      {
        targetId: "part-a",
        sourceId: "shared-id",
        expectedSourceIdentity: "opaque",
        currentSource: { ...originalSource, accessCode: "corrected-runtime-code" },
        canCorrect: false,
      },
    ],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const diagnostics = await openDiagnostics();
  const parts = diagnostics.getByRole("region", { name: "acquisition.plan.parts" });
  await waitFor(() => expect(within(parts).getByText("corrected-runtime-code")).toBeVisible());
  expect(within(parts).queryByText("original-plan-code")).toBeNull();
  expect(within(parts).getByText("other-part-code")).toBeVisible();
  expect(within(parts).getByText("acquisition.sourceCorrection.reason.ALREADY_ACQUIRED")).toBeVisible();
  expect(within(parts).getByText("acquisition.sourceCorrection.currentSource")).toBeVisible();
  expect(within(parts).getByText("acquisition.sourceCorrection.originalSource")).toBeVisible();
  expect(JSON.stringify(taskDetail)).toBe(original);
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.correctAccessCode).not.toHaveBeenCalled();
});

it("does not present source values from another execution as the current plan source", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "ready", workflowRunId: 75 },
    plan: {
      parts: [
        {
          id: "part",
          sources: [
            {
              id: "source",
              url: "https://pan.baidu.com/s/original",
              accessCode: "plan-code",
            },
          ],
        },
      ],
    },
  });
  mocks.sourceCorrections.mockResolvedValue({
    taskId: 42,
    expectedRunId: 74,
    expectedRevision: 3,
    canCorrect: false,
    sources: [
      {
        targetId: "part",
        sourceId: "source",
        currentSource: {
          url: "https://pan.baidu.com/s/original",
          accessCode: "other-run-code",
        },
        canCorrect: false,
      },
    ],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const diagnostics = await openDiagnostics();
  const parts = diagnostics.getByRole("region", { name: "acquisition.plan.parts" });
  await waitFor(() => expect(mocks.sourceCorrections).toHaveBeenCalled());
  expect(within(parts).getByText("plan-code")).toBeVisible();
  expect(within(parts).queryByText("other-run-code")).toBeNull();
  expect(within(parts).getByText("acquisition.sourceCorrection.originalSource")).toBeVisible();
});

const completedDetail = {
  ...detail,
  task: {
    ...detail.task,
    phase: "completed",
    status: AcquisitionStatus.Completed,
    workflowRunId: 75,
  },
  plan: {
    parts: [
      { id: "main", sources: [] },
      { id: "extra", sources: [] },
    ],
  },
  replanArtifacts: [
    { partId: "main", artifactId: "main-output", kind: "processedFiles" },
    { partId: "extra", artifactId: "extra-output", kind: "processedFiles" },
  ],
};
it.each(["only-download", "missing-part"])(
  "does not enable completed continuation with %s evidence",
  async (mode) => {
    mocks.get.mockResolvedValue({
      ...completedDetail,
      replanArtifacts:
        mode === "only-download"
          ? completedDetail.replanArtifacts.map((a) => ({ ...a, kind: "download" }))
          : completedDetail.replanArtifacts.slice(0, 1),
    });
    render(<PlanTaskDetailModal taskId={42} />);
    expect(
      await screen.findByRole("button", { name: "acquisition.continue.again" }),
    ).toBeDisabled();
    expect(mocks.completedRevision).not.toHaveBeenCalled();
  },
);

it("keeps the completed review snapshot mounted when polling changes the task state and revision", async () => {
  let poll: (() => void) | undefined;
  const timer = vi.spyOn(globalThis, "setInterval").mockImplementation((handler) => {
    poll = handler as () => void;
    return 1 as unknown as ReturnType<typeof setInterval>;
  });
  try {
    mocks.get.mockResolvedValue(completedDetail);
    render(<PlanTaskDetailModal taskId={42} />);
    fireEvent.click(
      await screen.findByRole("button", { name: "acquisition.continue.again" }),
    );
    expect(screen.getByText("completed revision 3")).toBeVisible();
    mocks.get.mockResolvedValue({
      ...completedDetail,
      task: {
        ...completedDetail.task,
        status: AcquisitionStatus.Running,
        planRevision: 4,
        workflowRunId: 76,
      },
    });
    await act(async () => {
      poll?.();
    });
    expect(screen.getByText("completed revision 3")).toBeVisible();
    expect(mocks.completedRevision.mock.lastCall?.[0].detail.task).toMatchObject({
      planRevision: 3,
      workflowRunId: 75,
    });
    expect(mocks.run).not.toHaveBeenCalled();
    expect(mocks.cancel).not.toHaveBeenCalled();
  } finally {
    timer.mockRestore();
  }
});

it.each([false, true])("retains the stopped-file reserve choice (%s) from saving until explicit start", async useEmergencySpaceReserve => {
  const stopped = {
    ...completedDetail,
    task: { ...completedDetail.task, status: AcquisitionStatus.Failed, phase: "attention" },
  };
  mocks.get.mockResolvedValue(stopped);
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.plan.replan.title" }));
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.plan.continueStopped.title" }));
  const ready = {
    ...stopped,
    task: { ...stopped.task, status: AcquisitionStatus.Waiting, phase: "ready", workflowRunId: undefined, planRevision: 4 },
    steps: [{ id: "continue", kind: "acquisition.plan.continueFiles" }],
  };
  mocks.get.mockResolvedValue(ready);
  mocks.run.mockResolvedValue(ready);
  await act(async () => { mocks.completedRevision.mock.lastCall![0].onSaved(ready, { useEmergencySpaceReserve }); });
  expect(mocks.run).not.toHaveBeenCalled();
  await openDownload();
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.plan.run" }));
  await waitFor(() => expect(mocks.run).toHaveBeenCalledExactlyOnceWith(42, 4, useEmergencySpaceReserve));
});

it("starts only the returned plan revision after confirming a stage retry", async () => {
  mocks.get.mockResolvedValue(completedDetail);
  render(<PlanTaskDetailModal taskId={42} />);
  const again = await screen.findByRole("button", { name: "acquisition.continue.again" });
  expect(again).toHaveAttribute("data-color", "default");
  fireEvent.click(again);
  const next = {
    ...completedDetail,
    task: {
      ...completedDetail.task,
      status: AcquisitionStatus.Waiting,
      phase: "ready",
      workflowRunId: undefined,
      planRevision: 4,
    },
    steps: [{ id: "continue", kind: "acquisition.plan.continueFiles" }],
  };
  mocks.run.mockResolvedValue(next);
  await act(async () => {
    mocks.completedRevision.mock.lastCall?.[0].onSaved(next, { useEmergencySpaceReserve: false });
  });
  expect(mocks.run).toHaveBeenCalledExactlyOnceWith(42, 4, false);
  expect(mocks.select).not.toHaveBeenCalled();
  expect(mocks.cancel).not.toHaveBeenCalled();
});

it("opens the actionable current run directly in the continuation dialog", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "executing", workflowRunId: 75 },
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const review = await screen.findByRole("button", { name: "acquisition.continue.open" });
  fireEvent.click(review);
  expect(screen.getByRole("dialog", { name: "#42 Chosen content · acquisition.continue.open" })).toBeVisible();
  expect(screen.queryByRole("dialog", { name: "acquisition.plan.diagnostics.title" })).toBeNull();
  expect(screen.getByText("inline run 75")).toBeVisible();
  expect(mocks.runContent).toHaveBeenLastCalledWith(
    expect.objectContaining({ runId: 75, respondInitially: true, readOnly: false }),
  );
  expect(mocks.createPortal).not.toHaveBeenCalled();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("offers a local current-storage revision for an exact publication storage wait with processed evidence", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", status: AcquisitionStatus.Waiting,
    waitPromptJson: JSON.stringify({ targetId: "part", reason: "StorageUnavailable", message: "LOCAL_STORAGE_UNAVAILABLE" }),
  }, plan: { parts: [{ id: "part", sources: [] }] },
  replanArtifacts: [{ artifactId: "processed-output", partId: "part", kind: "processedFiles" }] });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 77, currentNodeKind: "fs.publishArtifacts", workflowStatus: "waiting",
    problemCode: "LOCAL_STORAGE_UNAVAILABLE", problemReasonCode: "LOCAL_STORAGE_UNAVAILABLE",
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByRole("button", { name: "acquisition.continue.delivery" });
  const button = await screen.findByRole("button", { name: "acquisition.continue.again" });
  expect(button).toBeEnabled();
  fireEvent.click(button);
  chooseExistingFilesIfOffered();
  await waitFor(() => expect(mocks.completedRevision).toHaveBeenLastCalledWith(expect.objectContaining({
    mode: "stopped", waitingPublicationStorage: { taskId: 42, runId: 77, planRevision: 3 },
    waitingPathConflict: undefined,
  })));
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each([
  ["assessment", "fs.assessDelivery", {
    targetId: "part", reason: "FILE_DELIVERY_RESULT_UNCERTAIN", artifactId: "processed-output",
    fingerprint: "fingerprint", reviewToken: "review-token", deliveryReview: true,
  }, "processedFiles"],
  ["legacy initial unknown plan", "acquisition.plan.process", {
    targetId: "part", reason: "ExtractionPlanUnknown", canEditPlan: true, allowedActions: [],
    allowedArchivePolicies: ["instructions", "keep"],
  }, "download"],
])("offers the same local continuation for %s with verified evidence and generic attention", async (_case, kind, prompt, artifactKind) => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", status: AcquisitionStatus.Waiting,
    waitPromptJson: JSON.stringify(prompt),
  }, plan: { parts: [{ id: "part", sources: [] }] },
  replanArtifacts: [{ artifactId: "processed-output", partId: "part", kind: artifactKind,
    canReadCurrentDirectory: true }] });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 77, currentNodeKind: kind, workflowStatus: "waiting",
    problemCode: "SOURCE_ATTENTION_REQUIRED", problemReasonCode: null,
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  const button = await screen.findByRole("button", { name: "acquisition.continue.open" });
  expect(button).toBeEnabled();
  fireEvent.click(button);
  const dialog = within(screen.getByRole("dialog", { name: /acquisition\.continue\.open$/ }));
  expect(dialog.queryByText("inline run 77")).toBeNull();
  expect(dialog.queryByRole("tab")).toBeNull();
  expect(mocks.completedRevision).toHaveBeenLastCalledWith(expect.objectContaining({
    mode: "stopped", waitingLocalProcessing: { taskId: 42, runId: 77, planRevision: 3 },
  }));
  expect(dialog.getByText("completed revision 3")).toBeVisible();
  fireEvent.click(dialog.getByRole("button", { name: "acquisition.plan.close" }));
  expect(screen.queryByRole("dialog", { name: /acquisition\.continue\.open$/ })).toBeNull();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("shows the current AI file mismatch in the task and its existing-file continuation without opening execution records", async () => {
  const message = "The requested release RJ01006490 differs from archive RJ01301819.zip. Verify the current file.";
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77,
    status: AcquisitionStatus.Waiting, phase: "attention", currentStepIndex: 0,
    waitPromptJson: JSON.stringify({ targetId: "part", reason: "FILE_DELIVERY_RESULT_UNCERTAIN",
      adaptiveAssessment: true, deliveryReview: true, artifactId: "processed-output", message }),
  }, plan: { parts: [{ id: "part", sources: [] }] },
    steps: [{ id: "part.assess", kind: "fs.assessDelivery", stage: "processing" }],
    replanArtifacts: [{ artifactId: "processed-output", partId: "part", kind: "processedFiles", canReadCurrentDirectory: true }] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77, currentStepIndex: 0,
    currentNodeKind: "fs.assessDelivery", workflowStatus: "waiting", problemCode: "FILE_DELIVERY_RESULT_UNCERTAIN" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  expect(await screen.findByText(message)).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.open" }));
  const form = within(screen.getByRole("dialog", { name: /acquisition\.continue\.open$/ }));
  expect(form.getByText(message)).toBeVisible();
  expect(form.getByText("completed revision 3")).toBeVisible();
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.assessmentIo).not.toHaveBeenCalled();
});

it.each([
  ["acquisition.plan.process", "FILE_DELIVERY_RESULT_UNCERTAIN"],
  ["acquisition.plan.process", "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED"],
  ["fs.assessDelivery", "FILE_DELIVERY_RESULT_UNCERTAIN"],
  ["fs.assessDelivery", "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED"],
])("opens the current part's signed delivery review on %s for %s", async (kind, reason) => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77,
    status: AcquisitionStatus.Waiting, phase: "attention", currentStepIndex: 0,
    waitPromptJson: JSON.stringify({ targetId: "part", reason, deliveryReview: true,
      artifactId: "processed-output", fingerprint: "manifest", reviewToken: "review",
      files: ["first.zip.pdf", "second.zip.pdf", "attachment"], allowedArchivePolicies: ["instructions", "keep"] }),
  }, plan: { parts: [{ id: "part", sources: [] }, { id: "other", sources: [] }] },
    steps: [{ id: "review", kind, stage: "processing" }], replanArtifacts: [
      { artifactId: "processed-output", partId: "part", kind: "processedFiles", canReadCurrentDirectory: true },
      { artifactId: "second-download", partId: "other", kind: "download", canReadCurrentDirectory: true },
    ] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77, currentStepIndex: 0,
    currentNodeKind: kind, workflowStatus: "waiting", problemCode: "SOURCE_ATTENTION_REQUIRED" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByRole("button", { name: "acquisition.continue.open" });
  await act(async () => {});
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.open" }));
  const dialog = within(await screen.findByRole("dialog", { name: /acquisition\.continue\.open$/ }));
  expect(dialog.getByText("inline run 77")).toBeVisible();
  fireEvent.click(dialog.getByRole("radio", { name: "acquisition.continue.checkpoint.files" }));
  expect(dialog.getByText("completed revision 3")).toBeVisible();
  expect(mocks.applyProcessingRevision).not.toHaveBeenCalled();
});

it("can review the original output without creating a new attempt when the server offers a bound keep action", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77,
    status: AcquisitionStatus.Waiting, phase: "attention", currentStepIndex: 0,
    waitPromptJson: JSON.stringify({ targetId: "part", reason: "FILE_DELIVERY_RESULT_UNCERTAIN",
      adaptiveAssessment: true, deliveryReview: true, artifactId: "processed-output", fingerprint: "manifest",
      reviewToken: "review", allowedArchivePolicies: ["instructions", "keep"] }),
  }, plan: { parts: [{ id: "part", sources: [] }] },
    steps: [{ id: "part.assess", kind: "fs.assessDelivery", stage: "processing" }],
    replanArtifacts: [{ artifactId: "processed-output", partId: "part", kind: "processedFiles", canReadCurrentDirectory: true }] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77, currentStepIndex: 0,
    currentNodeKind: "fs.assessDelivery", workflowStatus: "waiting", problemCode: "FILE_DELIVERY_RESULT_UNCERTAIN" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByRole("button", { name: "acquisition.continue.open" });
  await act(async () => {});
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.open" }));
  const dialog = within(await screen.findByRole("dialog", { name: /acquisition\.continue\.open$/ }));
  expect(dialog.getByText("completed revision 3")).toBeVisible();
  fireEvent.click(dialog.getByRole("radio", { name: "acquisition.continue.reviewOutput" }));
  expect(dialog.getByText("inline run 77")).toBeVisible();
  expect(dialog.queryByText("completed revision 3")).toBeNull();
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each([false, undefined, "missing-part"])("preserves the current password answer form when old inputs cannot be read as a fresh directory (%s)", async availability => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77, phase: "attention",
    status: AcquisitionStatus.Waiting, waitPromptJson: JSON.stringify({ targetId: "part", reason: "PasswordUnknown" }),
  }, plan: { parts: [{ id: "part", sources: [] }, ...(availability === "missing-part" ? [{ id: "other", sources: [] }] : [])] },
    replanArtifacts: [{ artifactId: "old-input", partId: "part", kind: "processedFiles", canReadCurrentDirectory: availability === "missing-part" ? true : availability }] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77,
    currentNodeKind: "fs.assessDelivery", workflowStatus: "waiting", problemCode: "PasswordUnknown" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.continue.open" }));
  const dialog = within(screen.getByRole("dialog", { name: /acquisition\.continue\.open$/ }));
  expect(dialog.getByText("inline run 77")).toBeVisible();
  expect(dialog.queryByText("completed revision 3")).toBeNull();
  expect(dialog.queryByRole("tab")).toBeNull();
  expect(mocks.runContent).toHaveBeenLastCalledWith(expect.objectContaining({ runId: 77, respondInitially: true }));
  expect(mocks.run).not.toHaveBeenCalled();
});

it("can reopen current manual processing files during a stopped delivery without any historical artifact", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77, phase: "attention",
    status: AcquisitionStatus.Waiting, waitPromptJson: JSON.stringify({targetId:"part",reason:"PublicationConflict"}) },
    plan:{parts:[{id:"part",sources:[]}]},steps:[{stage:"delivery"}],replanArtifacts:[],
    stageDirectories:{version:1,taskId:42,parts:{part:{processing:{directory:"/manual/current",ownership:"borrowed",evidence:"configured",bindingId:"fixed"}}}},
  });
  mocks.runtimeOverviews.mockResolvedValue([{taskId:42,workflowRunId:77,currentStepIndex:0,currentNodeKind:"fs.publishArtifacts",workflowStatus:"waiting",activeDownloadCount:0}]);
  render(<PlanTaskDetailModal taskId={42}/>);
  fireEvent.click(await screen.findByRole("button",{name:"acquisition.continue.again"}));
  expect(mocks.completedRevision).toHaveBeenLastCalledWith(expect.objectContaining({inputStage:"processing",retryStage:"processing"}));
  expect(screen.queryByRole("radio",{name:"acquisition.continue.checkpoint.current"})).toBeNull();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("resumes a complete saved space checkpoint by default and offers explicit reanalysis in the same modal", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77, phase: "attention",
    status: AcquisitionStatus.Waiting, waitPromptJson: JSON.stringify({ targetId: "part", reason: "StorageUnavailable",
      message: "LOCAL_STORAGE_RESERVE_LOW", deliveryReview: true, artifactId: "current-input", fingerprint: "fp", reviewToken: "review",
      extractionPlanJson: JSON.stringify({ requirement: "required", steps: [{ id: "extract", op: "extractArchive", input: "input", selector: "*.7z" }] }) }),
  }, plan: { parts: [{ id: "part", sources: [] }] }, steps: [{ stage: "processing" }],
    replanArtifacts: [{ artifactId: "current-input", partId: "part", kind: "currentFiles", fingerprint: "fp", canReadCurrentDirectory: true }] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77, currentStepIndex: 0,
    currentNodeKind: "fs.assessDelivery", workflowStatus: "waiting", problemCode: "LOCAL_STORAGE_RESERVE_LOW" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await screen.findByRole("button", { name: "acquisition.continue.open" });
  await act(async () => {});
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.open" }));
  const dialog = within(screen.getByRole("dialog", { name: /acquisition\.continue\.open$/ }));
  expect(dialog.getByRole("radio", { name: "acquisition.continue.checkpoint.current" })).toBeChecked();
  expect(dialog.getByText("inline run 77")).toBeVisible();
  expect(mocks.completedRevision).not.toHaveBeenCalled();
  fireEvent.click(dialog.getByRole("radio", { name: "acquisition.continue.checkpoint.files" }));
  expect(dialog.getByText("completed revision 3")).toBeVisible();
  expect(dialog.queryByText("inline run 77")).toBeNull();
  expect(mocks.completedRevision).toHaveBeenLastCalledWith(expect.objectContaining({ preferAi: true }));
  fireEvent.click(dialog.getByRole("radio", { name: "acquisition.continue.checkpoint.current" }));
  expect(dialog.getByText("inline run 77")).toBeVisible();
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.applyProcessingRevision).not.toHaveBeenCalled();
});

it.each(["acquisition.plan.process", "fs.assessDelivery"])("answers the current password wait in place before offering a new file attempt on %s", async kind => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77, phase: "attention", currentStepIndex: 1,
    status: AcquisitionStatus.Waiting, waitPromptJson: JSON.stringify({ targetId: "part", reason: "PasswordUnknown",
      canEditPlan: false, allowedActions: [], allowedArchivePolicies: ["instructions", "keep"] }),
  }, plan: { parts: [{ id: "part", sources: [] }] },
    replanArtifacts: [{ artifactId: "current-input", partId: "part", kind: "processedFiles", canReadCurrentDirectory: true }] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77, currentStepIndex: 1,
    currentNodeKind: kind, workflowStatus: "waiting", problemCode: "SOURCE_ATTENTION_REQUIRED" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.continue.open" }));
  const modal = within(await screen.findByRole("dialog", { name: /acquisition\.continue\.open$/ }));
  expect(modal.getByText("inline run 77")).toBeVisible();
  expect(modal.getByRole("radio", { name: "acquisition.continue.providePassword" })).toBeChecked();
  expect(mocks.runContent).toHaveBeenLastCalledWith(expect.objectContaining({ runId: 77, respondInitially: true }));
  fireEvent.click(modal.getByRole("radio", { name: "acquisition.continue.checkpoint.files" }));
  expect(modal.getByText("completed revision 3")).toBeVisible();
  expect(modal.queryByText("inline run 77")).toBeNull();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("opens a legacy missing-volume wait in its original run when a new two-part attempt is also available", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77, currentStepIndex: 1,
    phase: "attention", status: AcquisitionStatus.Waiting, waitPromptJson: JSON.stringify({
      reason: "MissingVolumes", targetId: "part", artifactId: "input", fingerprint: "frozen", knownStepId: "s2", allowedActions: [] }),
  }, plan: { parts: [{ id: "part", sources: [] }, { id: "other", sources: [] }] }, replanArtifacts: [
    { artifactId: "input", partId: "part", kind: "processedFiles", canReadCurrentDirectory: true },
    { artifactId: "other-input", partId: "other", kind: "download", canReadCurrentDirectory: true },
  ] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77, currentStepIndex: 1,
    currentNodeKind: "acquisition.plan.process", workflowStatus: "waiting", problemCode: "MissingVolumes" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.continue.open" }));
  const modal = within(await screen.findByRole("dialog", { name: /acquisition\.continue\.open$/ }));
  expect(modal.getByText("inline run 77")).toBeVisible();
  expect(modal.getByRole("radio", { name: "acquisition.continue.checkpoint.current" })).toBeChecked();
  fireEvent.click(modal.getByRole("radio", { name: "acquisition.continue.checkpoint.files" }));
  expect(modal.getByText("completed revision 3")).toBeVisible();
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.applyProcessingRevision).not.toHaveBeenCalled();
});

it.each(["ExtractionPlanUnknown", "FutureProcessingWait"])("defaults readable current files to the AI-capable processing form for %s without an old action allowlist", async reason => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77, phase: "attention",
    status: AcquisitionStatus.Waiting, waitPromptJson: JSON.stringify({ targetId: "part", reason,
      canEditPlan: true, allowedActions: [], allowedArchivePolicies: ["instructions", "keep"] }),
  }, plan: { parts: [{ id: "part", sources: [] }] },
    replanArtifacts: [{ artifactId: "current-input", partId: "part", kind: "download", canReadCurrentDirectory: true }] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77,
    currentNodeKind: "acquisition.plan.process", workflowStatus: "waiting", problemCode: "SOURCE_ATTENTION_REQUIRED" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.continue.open" }));
  const dialog = within(screen.getByRole("dialog", { name: /acquisition\.continue\.open$/ }));
  expect(dialog.getByText("completed revision 3")).toBeVisible();
  expect(dialog.queryByText("inline run 77")).toBeNull();
  expect(mocks.completedRevision).toHaveBeenLastCalledWith(expect.objectContaining({ inDialog: true,
    retryStage: "processing", waitingLocalProcessing: { taskId: 42, runId: 77, planRevision: 3 } }));
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each([
  "wrongRun", "wrongNode", "wrongReason", "wrongTarget", "malformedPrompt", "unknownStorageCode",
  "untypedStorage", "noEvidence", "rawDownload", "missingPart", "activeTask", "activeRun",
])("does not offer publication storage revision with %s", async (change) => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention",
    status: change === "activeTask" ? AcquisitionStatus.Running : AcquisitionStatus.Waiting,
    waitPromptJson: change === "malformedPrompt" ? "{" : JSON.stringify({
      targetId: change === "wrongTarget" ? "other" : "part",
      reason: change === "wrongReason" ? "PublicationConflict" : "StorageUnavailable",
      message: "LOCAL_STORAGE_UNAVAILABLE",
    }),
  }, plan: { parts: [{ id: "part", sources: [] }, ...(change === "missingPart" ? [{ id: "missing", sources: [] }] : [])] },
  replanArtifacts: change === "noEvidence" ? [] : [{ artifactId: "processed-output", partId: "part",
    kind: change === "rawDownload" ? "download" : "processedFiles" }] });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: change === "wrongRun" ? 78 : 77,
    workflowStatus: change === "activeRun" ? "running" : "waiting",
    currentNodeKind: change === "wrongNode" ? "download.awaitCompletion" : "fs.publishArtifacts",
    problemCode: change === "unknownStorageCode" ? "LOCAL_STORAGE_GUESSED" :
      change === "untypedStorage" ? "StorageUnavailable" : "LOCAL_STORAGE_UNAVAILABLE",
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await waitFor(() => expect(mocks.runtimeOverviews).toHaveBeenCalled());
  await act(async () => {});
  expectNoLocalContinuation();
  expect(screen.queryByRole("button", { name: "acquisition.plan.replan.title" })).toBeNull();
  expect(mocks.completedRevision).not.toHaveBeenCalled();
  expect(mocks.run).not.toHaveBeenCalled();
});


it.each([
  ["SOURCE_ATTENTION_REQUIRED", null, "LOCAL_STORAGE_RESERVE_LOW", 77, true],
  [null, null, "LOCAL_STORAGE_RESERVE_LOW", 77, true],
  ["", "", "LOCAL_STORAGE_RESERVE_LOW", 77, true],
  ["PublicationConflict", null, "LOCAL_STORAGE_RESERVE_LOW", 77, false],
  ["SOURCE_ATTENTION_REQUIRED", "PublicationConflict", "LOCAL_STORAGE_RESERVE_LOW", 77, false],
  ["PublicationConflict", "SOURCE_ATTENTION_REQUIRED", "LOCAL_STORAGE_RESERVE_LOW", 77, false],
  ["SOURCE_ATTENTION_REQUIRED", null, "Error mentioned LOCAL_STORAGE_RESERVE_LOW", 77, false],
  ["SOURCE_ATTENTION_REQUIRED", null, "LOCAL_STORAGE_NOT_A_CODE", 77, false],
  ["SOURCE_ATTENTION_REQUIRED", null, "LOCAL_STORAGE_RESERVE_LOW", 78, false],
])("uses the current typed publication prompt only without a specific overview problem: %s %s %s run=%s", async (
  problemCode, problemReasonCode, message, workflowRunId, allowed,
) => {
  mocks.get.mockResolvedValue({ ...detail, task: {
    ...detail.task, workflowRunId: 77, phase: "attention", status: AcquisitionStatus.Waiting,
    waitPromptJson: JSON.stringify({ targetId: "part", reason: "StorageUnavailable", message }),
  }, plan: { parts: [{ id: "part", sources: [] }] },
  replanArtifacts: [{ artifactId: "processed-output", partId: "part", kind: "processedFiles" }] });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId, currentNodeKind: "fs.publishArtifacts", workflowStatus: "waiting",
    problemCode, problemReasonCode,
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await waitFor(() => expect(mocks.runtimeOverviews).toHaveBeenCalled());
  await act(async () => {});
  const button = screen.queryByRole("button", { name: "acquisition.continue.again" });
  if (allowed) {
    expect(button).toBeEnabled();
    fireEvent.click(button!);
    chooseExistingFilesIfOffered();
    expect(mocks.completedRevision).toHaveBeenLastCalledWith(expect.objectContaining({
      mode: "stopped", waitingPublicationStorage: { taskId: 42, runId: 77, planRevision: 3 },
    }));
  } else {
    expectNoLocalContinuation();
  }
  expect(mocks.run).not.toHaveBeenCalled();
});

it("keeps forty attempts in four compact stages with earlier outputs reachable on demand", async () => {
  const make = (id: string, stage: string, attempt: number, runId: number, status: string) => ({
    id, stage, attempt, runId, status, nodes: [], outputs: [{kind: "scalar", key: id, value: id}],
    startedAt: "2026-11-11 01:02:03.000", completedAt: "2026-11-11 01:04:05.000",
  });
  mocks.get.mockResolvedValue({...detail, task: {...detail.task, workflowRunId: 88},
    steps: [{ id: "assess", kind: "fs.assessDelivery", stage: "processing" },
      {id: "deliver", kind: "fs.publishArtifacts", stage: "delivery"}],
    stageAttempts: [
      make("download", "download", 1, 70, "completed"),
      ...Array.from({length:39}, (_,i)=>make(`processing-${i+1}`, "processing", i+1, i===38 ? 88 : 70, "waiting")),
    ],
  });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 88, currentStepIndex: 0, currentNodeKind: "fs.assessDelivery" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  const timeline = await screen.findByRole("region", {name: "acquisition.timeline.title"});
  const rows = within(timeline).getAllByRole("listitem");
  expect(rows).toHaveLength(4);
  expect(within(rows[1]).getByText(/workflow.timing.duration/)).toBeVisible();
  expect(within(rows[2]).getByText(/workflow.timing.duration/)).toBeVisible();
  expect(rows[1]).not.toHaveAttribute("aria-current");
  expect(rows[2]).toHaveAttribute("aria-current", "step");
  await within(rows[3]).findByText("acquisition.timeline.state.pending");
  expect(within(rows[2]).getByRole("button",{name:"acquisition.continue.open"})).toBeVisible();
  fireEvent.click(within(rows[2]).getByRole("button", { name: "acquisition.records.open" }));
  const records = screen.getByRole("dialog", {name: "acquisition.records.title"});
  expect(within(records).getByText("processing-39")).toBeVisible();
  expect(within(records).getByText(/workflow.timing.duration/)).toBeVisible();
  const list = within(records).getByRole("navigation", {name: "acquisition.records.attempts"});
  const choices = within(list).getAllByRole("button");
  expect(choices).toHaveLength(39);
  fireEvent.click(choices[38]);
  expect(within(records).getByText("processing-1")).toBeVisible();
  expect(within(records).getByText(/workflow.timing.duration/)).toBeVisible();
  fireEvent.click(within(records).getByRole("button", {name:"acquisition.timeline.openRun"}));
  expect(mocks.runContent).toHaveBeenLastCalledWith(expect.objectContaining({runId:70,readOnly:true}));
  const diagnosticDialog = screen.getByRole("dialog", { name: "acquisition.plan.diagnostics.title" });
  expect(within(diagnosticDialog).getByText("inline run 70")).toBeVisible();
  fireEvent.click(within(diagnosticDialog).getByRole("button", {name: "acquisition.plan.close"}));
  expect(screen.queryByRole("dialog", {name: "acquisition.records.title"})).toBeNull();
  expect(within(timeline).getAllByRole("listitem")).toHaveLength(4);
});

it.each([2, undefined])("marks a previous delivery as an earlier result while the current processing attempt waits (step %s)", currentStepIndex => {
  const attempt = (id: string, stage: string, runId: number, status: string) => ({
    id, stage, runId, status, attempt: 1, legacy: false, outputsTruncated: false, nodes: [],
    hasAssessmentCalls: false,
    outputs: [{ kind: "scalar", key: id, value: id }],
  });
  // This isolated timeline fixture intentionally supplies only the fields it renders.
  const saved = { ...detail, task: { ...detail.task, workflowRunId: 80 },
    steps: [{ id: "input", kind: "acquisition.plan.continueFiles", labelKey: "input", stage: "processing" },
      { id: "process", kind: "acquisition.plan.process", labelKey: "process", stage: "processing" },
      { id: "assess", kind: "fs.assessDelivery", labelKey: "assess", stage: "processing" },
      { id: "deliver", kind: "fs.publishArtifacts", labelKey: "deliver", stage: "delivery" }],
    stageAttempts: [attempt("old-download", "download", 70, "completed"),
      attempt("old-delivery", "delivery", 70, "completed"),
      attempt("current-processing", "processing", 80, "waiting")],
    stageDirectories: { version: 1, taskId: 42, parts: { main: { delivery: {
      directory: "/Acquired/previous", ownership: "borrowed", evidence: "recorded",
    } } } },
  } as unknown as PlanTaskDetail;
  const view = render(<AcquisitionExecutionTimeline detail={saved} currentStepIndex={currentStepIndex}
    actions={{}} onOpenRun={vi.fn()} />);
  const rows = screen.getAllByRole("listitem");
  const downloadHeader = within(rows[1]).getByRole("heading", { name: "acquisition.timeline.stage.download" }).parentElement!;
  const deliveryHeader = within(rows[3]).getByRole("heading", { name: "acquisition.timeline.stage.delivery" }).parentElement!;
  expect(within(downloadHeader).getByText("acquisition.timeline.state.completed")).toBeVisible();
  expect(within(deliveryHeader).getByText("acquisition.timeline.state.pending")).toBeVisible();
  expect(within(deliveryHeader).queryByText("acquisition.timeline.state.completed")).toBeNull();
  expect(within(rows[3]).getByText("acquisition.timeline.previousResult")).toBeVisible();
  expect(within(rows[3]).getByText("/Acquired/previous")).toBeVisible();
  fireEvent.click(within(rows[3]).getByRole("button", { name: "acquisition.records.open" }));
  expect(screen.getByText("old-delivery")).toBeVisible();
  view.rerender(<AcquisitionExecutionTimeline detail={{ ...saved,
    task: { ...saved.task, status: AcquisitionStatus.Completed },
  }} currentStepIndex={currentStepIndex} actions={{}} onOpenRun={vi.fn()} />);
  expect(within(deliveryHeader).getByText("acquisition.timeline.state.completed")).toBeVisible();
  expect(within(rows[3]).queryByText("acquisition.timeline.previousResult")).toBeNull();
});

it("keeps the saved local revision available if starting a stage attempt fails", async () => {
  mocks.get.mockResolvedValue(completedDetail);
  render(<PlanTaskDetailModal taskId={42} />);
  fireEvent.click(await screen.findByRole("button", {name:"acquisition.continue.again"}));
  const ready = {...completedDetail,task:{...completedDetail.task,status:AcquisitionStatus.Waiting,
    phase:"ready",workflowRunId:undefined,planRevision:4},steps:[{id:"continue",kind:"acquisition.plan.continueFiles"}]};
  mocks.get.mockResolvedValue(ready);
  mocks.run.mockRejectedValueOnce(new Error("start failed"));
  await act(async () => { mocks.completedRevision.mock.lastCall?.[0].onSaved(ready, {useEmergencySpaceReserve:false}); });
  await openDownload();
  expect(await screen.findByRole("button", {name:"acquisition.plan.run"})).toBeEnabled();
  expect(mocks.run).toHaveBeenCalledExactlyOnceWith(42,4,false);
  expect(screen.getByRole("alert")).toHaveTextContent("acquisition.plan.actionFailed");
  mocks.run.mockResolvedValue(ready);
  fireEvent.click(screen.getByRole("button", {name:"acquisition.plan.run"}));
  await waitFor(() => expect(mocks.run).toHaveBeenCalledTimes(2));
  expect(mocks.run).toHaveBeenLastCalledWith(42,4,false);
});

it("shows only the latest AI diagnosis while retaining every attempt in execution details", async () => {
  const make = (runId: number, code: string, key = "adaptive-assessment") => ({
    id: `${runId}/1`, runId, stage: "processing", attempt: runId - 3904, status: "waiting",
    nodes: ["fs.assessDelivery"],
    outputs: [{ kind: "warning", key, code, message: "unreviewed raw response" }],
  });
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 3909 },
    stageAttempts: [
      make(3905, "MAX_LLM_ASSESSMENT_CALLS"), make(3906, "LLM_OUTPUT_TRUNCATED"),
      make(3907, "LLM_DECISION_INVALID_JSON", "unrelated"), make(3908, "LLM_DECISION_INVALID_JSON"),
      make(3909, "LLM_ASSESSMENT_FAILED"),
    ],
  });
  render(<PlanTaskDetailModal taskId={42} />);
  const analysis = await screen.findByRole("region", { name: "acquisition.timeline.aiAnalysis" });
  expect(within(analysis).getByText(/#3909/).parentElement)
    .toHaveTextContent("cloudDownload.problemCode.LLM_ASSESSMENT_FAILED");
  expect(within(analysis).queryByText(/#390[5678]/)).not.toBeInTheDocument();
  expect(within(analysis).getAllByRole("button", { name: "acquisition.records.viewIo" })).toHaveLength(1);
  expect(screen.queryByText("unreviewed raw response")).not.toBeInTheDocument();
  mocks.assessmentIo.mockResolvedValue({code: 0, data: {calls: [
    {inputKind: "recordedFiles", inputPreview: "old-file.7z", outputKind: "notRecorded"}
  ]}});
  expect(mocks.assessmentIo).not.toHaveBeenCalled();
  const reason = within(analysis).getByText(/#3909/).parentElement!;
  fireEvent.click(within(reason).getByRole("button", {name: "acquisition.records.viewIo"}));
  const records = screen.getByRole("dialog", {name: "acquisition.records.title"});
  expect(await within(records).findByText("old-file.7z")).toBeVisible();
  expect(within(records).getByText("acquisition.records.missingOutput")).toBeVisible();
  expect(mocks.assessmentIo).toHaveBeenCalledWith(42,3909,{attemptId:"3909/1"},expect.objectContaining({sensitiveResponse:true}));
  fireEvent.click(within(records).getByRole("button", {name: /#3905/}));
  expect(within(records).getByText("MAX_LLM_ASSESSMENT_CALLS")).toBeVisible();
  expect(within(records).queryByText("old-file.7z")).toBeNull();
  expect(mocks.runContent).not.toHaveBeenCalled();
});

it("offers saved AI input and output directly when the model succeeded but the processing selector did not match", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 3931 },
    stageAttempts: [{ id: "3931/100", runId: 3931, stage: "processing", attempt: 3, status: "waiting",
      hasAssessmentCalls: true, nodes: ["acquisition.plan.process", "fs.assessDelivery"],
      outputs: [{ kind: "warning", key: "part:processing", code: "ProcessingInputNotMatched" }] }],
  });
  mocks.assessmentIo.mockResolvedValue({code: 0, data: {calls: [{ iteration: 1,
    inputKind: "recordedFiles", inputPreview: "a.zip, b.zip", outputKind: "structure",
    outputPreview: '{"status":"continue","operations":[{"type":"extract"}]}'
  }]}});
  render(<PlanTaskDetailModal taskId={42} />);
  const analysis = await screen.findByRole("region", {name: "acquisition.timeline.aiAnalysis"});
  expect(within(analysis).queryByRole("heading")).toBeNull();
  expect(within(analysis).queryByText("acquisition.timeline.aiAnalysis")).toBeNull();
  expect(within(analysis).getByText(/#3931/)).toBeVisible();
  expect(within(analysis).queryByText(/cloudDownload.problemCode/)).toBeNull();
  fireEvent.click(within(analysis).getByRole("button", {name: "acquisition.records.viewIo"}));
  const records = screen.getByRole("dialog", {name: "acquisition.records.title"});
  expect(await within(records).findByText("a.zip, b.zip")).toBeVisible();
  expect(within(records).getByText(/"status":"continue"/)).toBeVisible();
  expect(mocks.assessmentIo).toHaveBeenCalledWith(42,3931,{attemptId:"3931/100"},expect.objectContaining({sensitiveResponse:true}));
});

it("keeps the latest AI inspection entry without repeating the same current failure", () => {
  render(<AcquisitionExecutionTimeline detail={{ ...detail,
    task: { ...detail.task, workflowRunId: 77 },
    stageAttempts: [{ id: "77/1", runId: 77, stage: "processing", attempt: 1, status: "waiting",
      legacy: false, outputsTruncated: false, hasAssessmentCalls: true, nodes: ["fs.assessDelivery"],
      outputs: [{ kind: "warning", key: "adaptive-assessment", code: "LLM_DECISION_INVALID_JSON" }] }],
  } as unknown as PlanTaskDetail} actions={{ processing: <p>cloudDownload.problemCode.LLM_DECISION_INVALID_JSON</p> }}
    currentProblemCode="LLM_DECISION_INVALID_JSON" onOpenRun={vi.fn()} />);
  expect(screen.getAllByText("cloudDownload.problemCode.LLM_DECISION_INVALID_JSON")).toHaveLength(1);
  expect(screen.getByRole("button", { name: "acquisition.records.viewIo" })).toBeVisible();
  const rows = screen.getAllByRole("listitem");
  expect(rows[1]).not.toHaveClass("border-primary-300");
  expect(rows[2]).toHaveClass("border-primary-300");
  expect(rows[3]).not.toHaveClass("border-primary-300");
});

it("keeps download source evidence in the download stage while file processing is waiting", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77, status: AcquisitionStatus.Waiting },
    steps: [{ id: "download", stage: "download" }, { id: "process", stage: "processing" }] });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77, currentStepIndex: 1,
    workflowStatus: "waiting", currentNodeKind: "acquisition.plan.process", sources: [{ sourceId: "baidu-share" }] }]);
  render(<PlanTaskDetailModal taskId={42} />);
  const evidence = await screen.findByRole("button", { name: "runtime download sources" });
  expect(evidence.closest("li")).toHaveTextContent("acquisition.timeline.stage.download");
  expect(evidence.closest("li")).not.toHaveTextContent("acquisition.timeline.stage.processing");
});


it("opens redelivery in a separate dialog and closing the draft leaves the task intact", async () => {
  mocks.get.mockResolvedValue({
    ...detail,
    task: { ...detail.task, workflowRunId: 77, status: AcquisitionStatus.Waiting, phase: "attention",
      waitPromptJson: JSON.stringify({ reason: "StorageUnavailable", message: "LOCAL_STORAGE_RESERVE_LOW", targetId: "part" }) },
    plan: { parts: [{ id: "part", sources: [] }] },
    replanArtifacts: [{ partId: "part", artifactId: "p", kind: "processedFiles", canRetryDelivery: true }],
  });
  mocks.runtimeOverviews.mockResolvedValue([{
    taskId: 42, workflowRunId: 77, currentNodeKind: "fs.publishArtifacts", workflowStatus: "waiting",
  }]);
  render(<PlanTaskDetailModal taskId={42} />);
  const main = await screen.findByRole("dialog", {name: "#42 Chosen content"});
  const button = within(main).getByRole("button", {name: "acquisition.timeline.retry.delivery"});
  fireEvent.click(button);
  const dialog = await screen.findByRole("dialog", {name: "acquisition.timeline.retry.delivery"});
  expect(within(dialog).getByText("completed revision 3")).toBeVisible();
  expect(within(main).queryByText("completed revision 3")).toBeNull();
  const props = mocks.completedRevision.mock.lastCall![0];
  expect(props).toMatchObject({retryStage: "delivery", inDialog: true});
  act(() => props.onCancel());
  expect(screen.queryByRole("dialog", {name: "acquisition.timeline.retry.delivery"})).toBeNull();
  expect(mocks.run).not.toHaveBeenCalled();
});

const waitingRedelivery = {
  ...detail,
  task: { ...detail.task, workflowRunId: 77, status: AcquisitionStatus.Waiting, phase: "attention", currentStepIndex: 0 },
  plan: { parts: [{ id: "part", sources: [] }] },
  steps: [{ id: "publish", kind: "fs.publishArtifacts", stage: "delivery" }],
  stageDirectories: { version: 1, taskId: 42, parts: { part: { delivery: { directory: "/Acquired/retained" } } } },
  stageAttempts: [{ id: "77/1", runId: 77, stage: "delivery", attempt: 1, status: "waiting", nodes: [], outputs: [] }],
};
const waitingRedeliveryOverview = {
  taskId: 42, planRevision: 3, workflowRunId: 77, workflowStatus: "waiting",
  currentStepIndex: 0, currentNodeKind: "fs.publishArtifacts", canStopWaitingRun: true,
};
const stopWaitingOpen = { name: "acquisition.runtime.stopWaiting.open" };
const stopWaitingDialog = { name: "acquisition.runtime.stopWaiting.title" };
const stopWaitingConfirm = { name: "acquisition.runtime.stopWaiting.confirm" };
const retryDelivery = { name: "acquisition.timeline.retry.delivery" };

it("asks before stopping the current delivery wait and keeps redelivery after cancellation", async () => {
  mocks.get.mockResolvedValue(waitingRedelivery);
  mocks.runtimeOverviews.mockResolvedValue([waitingRedeliveryOverview]);
  mocks.cancel.mockImplementation(async () => {
    mocks.get.mockResolvedValue({
      ...waitingRedelivery,
      task: { ...waitingRedelivery.task, status: AcquisitionStatus.Cancelled },
      stageAttempts: [{ ...waitingRedelivery.stageAttempts[0], status: "cancelled" }],
    });
    mocks.runtimeOverviews.mockResolvedValue([{ ...waitingRedeliveryOverview,
      canStopWaitingRun: false, workflowStatus: "cancelled" }]);
    return { code: 0 };
  });
  const onChanged = vi.fn();
  render(<PlanTaskDetailModal taskId={42} onChanged={onChanged} />);
  const open = await screen.findByRole("button", stopWaitingOpen);
  expect(open.closest("li")).toHaveTextContent("acquisition.timeline.stage.delivery");
  expect(screen.getByRole("button", retryDelivery)).toBeEnabled();
  fireEvent.click(open);
  const confirmation = await screen.findByRole("dialog", stopWaitingDialog);
  expect(within(confirmation).getByText("acquisition.runtime.stopWaiting.description")).toBeVisible();
  expect(mocks.cancel).not.toHaveBeenCalled();

  fireEvent.click(within(confirmation).getByRole("button", stopWaitingConfirm));
  await waitFor(() => expect(onChanged).toHaveBeenCalledOnce());
  expect(mocks.cancel).toHaveBeenCalledExactlyOnceWith(42, { expectedRunId: 77, expectedRevision: 3 },
    { sensitiveResponse: true, showErrorToast: false });
  expect(screen.queryByRole("dialog", stopWaitingDialog)).toBeNull();
  expect(screen.queryByRole("button", stopWaitingOpen)).toBeNull();
  const redelivery = screen.getByRole("button", retryDelivery);
  expect(redelivery.closest("li")).toHaveTextContent("acquisition.timeline.state.cancelled");
  expect(redelivery).toBeEnabled();
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each([
  ["without the capability", { canStopWaitingRun: false }],
  ["for a different run", { workflowRunId: 76 }],
  ["for a different revision", { planRevision: 2 }],
])("keeps redelivery and hides stop waiting %s", async (_, overview) => {
  mocks.get.mockResolvedValue(waitingRedelivery);
  mocks.runtimeOverviews.mockResolvedValue([{ ...waitingRedeliveryOverview, ...overview }]);
  await act(async () => { render(<PlanTaskDetailModal taskId={42} />); });
  expect(mocks.runtimeOverviews).toHaveBeenCalledWith([42], expect.any(AbortSignal));
  expect(screen.getByRole("button", retryDelivery)).toBeEnabled();
  expect(screen.queryByRole("button", stopWaitingOpen)).toBeNull();
  expect(mocks.cancel).not.toHaveBeenCalled();
});

it("cannot confirm stopping a delivery wait after the active run changes", async () => {
  vi.useFakeTimers();
  try {
    mocks.get.mockResolvedValue(waitingRedelivery);
    mocks.runtimeOverviews.mockResolvedValue([waitingRedeliveryOverview]);
    await act(async () => { render(<PlanTaskDetailModal taskId={42} />); });
    fireEvent.click(screen.getByRole("button", stopWaitingOpen));
    expect(within(screen.getByRole("dialog", stopWaitingDialog)).getByRole("button", stopWaitingConfirm)).toBeEnabled();

    mocks.get.mockResolvedValue({ ...waitingRedelivery,
      task: { ...waitingRedelivery.task, workflowRunId: 78 } });
    mocks.runtimeOverviews.mockResolvedValue([{ ...waitingRedeliveryOverview,
      workflowRunId: 78, canStopWaitingRun: false }]);
    await act(async () => { await vi.advanceTimersByTimeAsync(5000); });

    const confirmation = screen.queryByRole("dialog", stopWaitingDialog);
    if (confirmation) {
      const confirm = within(confirmation).getByRole("button", stopWaitingConfirm);
      expect(confirm).toBeDisabled();
      fireEvent.click(confirm);
    }
    expect(screen.queryByRole("button", stopWaitingOpen)).toBeNull();
    expect(screen.getByRole("button", retryDelivery)).toBeEnabled();
    expect(mocks.cancel).not.toHaveBeenCalled();
  } finally {
    vi.useRealTimers();
  }
});

it("shows the existing action error when stopping a delivery wait is rejected", async () => {
  mocks.get.mockResolvedValue(waitingRedelivery);
  mocks.runtimeOverviews.mockResolvedValue([waitingRedeliveryOverview]);
  mocks.cancel.mockResolvedValue({ code: 400, message: "The run has changed." });
  const onChanged = vi.fn();
  render(<PlanTaskDetailModal taskId={42} onChanged={onChanged} />);
  fireEvent.click(await screen.findByRole("button", stopWaitingOpen));
  fireEvent.click(within(await screen.findByRole("dialog", stopWaitingDialog)).getByRole("button", stopWaitingConfirm));

  expect(await screen.findByText("acquisition.plan.actionFailed")).toBeVisible();
  expect(screen.getByRole("button", retryDelivery)).toBeEnabled();
  expect(onChanged).not.toHaveBeenCalled();
  expect(mocks.cancel).toHaveBeenCalledOnce();
});

it("can review retained files from a removed, completed task's read-only details", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, status: AcquisitionStatus.Completed,
    isDeleted: true, deletedAt: "2026-11-10T12:00:00Z", workflowRunId: 77 } });
  render(<PlanTaskDetailModal taskId={42} />);
  const inspect = await screen.findByRole("button", { name: "acquisition.delete.retained.title" });
  fireEvent.click(inspect);
  expect(mocks.createPortal).toHaveBeenCalledWith(expect.anything(), {
    taskIds: [42], taskNames: { 42: "Chosen content" }, retryRetainedFiles: true, reviewWaitingRuns: true, onChanged: expect.any(Function),
  });
  expect(mocks.run).not.toHaveBeenCalled();
});

it("presents a workflow source-selection wait in preparation and continues through the existing modal", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, workflowRunId: 77, status: AcquisitionStatus.Waiting },
    steps: [{ id: "select", kind: "download.selectSources", stage: "download" }],
    stageAttempts: [{ id: "77/1", runId: 77, stage: "download", attempt: 1, status: "waiting", nodes: [], outputs: [] }],
  });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, workflowRunId: 77, currentStepIndex: 0,
    workflowStatus: "waiting", currentNodeKind: "download.selectSources", sources: [] }]);
  render(<PlanTaskDetailModal taskId={42} />);
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.continue.open" }).closest("li"))
    .toHaveTextContent("acquisition.timeline.stage.preparation"));
  const button = screen.getByRole("button", { name: "acquisition.continue.open" });
  expect(button.closest("li")).toHaveAttribute("aria-current", "step");
  const download = screen.getByRole("heading", { name: "acquisition.timeline.stage.download" }).closest("li")!;
  expect(download).not.toHaveAttribute("aria-current");
  fireEvent.click(button);
  expect(await screen.findByText("inline run 77")).toBeVisible();
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each(["cancelled", "failed", "interrupted"])("retries the existing stopped workflow step (%s) and refreshes the detail and list", async (workflowStatus) => {
  const stopped = {...detail, task: {...detail.task, workflowRunId: 77, phase: "attention",
    status: workflowStatus === "cancelled" ? AcquisitionStatus.Cancelled : AcquisitionStatus.Failed},
    steps: [{id: "publish", kind: "fs.publishArtifacts", stage: "delivery"}]};
  mocks.get.mockResolvedValue(stopped);
  mocks.runtimeOverviews.mockResolvedValue([{taskId: 42, workflowRunId: 77, workflowStatus,
    currentStepIndex: 0, currentNodeKind: "fs.publishArtifacts", sources: []}]);
  mocks.retry.mockResolvedValue({code: 0, data: {id: 42, workflowRunId: 77}});
  const onChanged = vi.fn();
  render(<PlanTaskDetailModal taskId={42} onChanged={onChanged} />);
  const button = await screen.findByRole("button", {name: "acquisition.plan.retryCurrentStep"});
  expect(button.closest("li")).toHaveTextContent("acquisition.timeline.stage.delivery");
  mocks.get.mockResolvedValue({...stopped, task: {...stopped.task, status: AcquisitionStatus.Pending}});
  fireEvent.click(button);
  await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
  expect(mocks.retry).toHaveBeenCalledWith(42, {sensitiveResponse: true, showErrorToast: false});
  expect(screen.queryByRole("button", {name: "acquisition.plan.retryCurrentStep"})).toBeNull();
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each([
  [AcquisitionStatus.Cancelled, "running", 77, false], [AcquisitionStatus.Cancelled, "waiting", 77, false],
  [AcquisitionStatus.Completed, "success", 77, false], [AcquisitionStatus.Failed, "cancelled", 76, false],
  [AcquisitionStatus.Cancelled, "cancelled", 77, true],
])("hides current step retry when run or task cannot be retried (%s %s %s %s)", async (status, workflowStatus, runId, isDeleted) => {
  mocks.get.mockResolvedValue({...detail, task: {...detail.task, workflowRunId: 77, status, isDeleted}});
  mocks.runtimeOverviews.mockResolvedValue([{taskId: 42, workflowRunId: runId, workflowStatus, currentStepIndex: 0, sources: []}]);
  render(<PlanTaskDetailModal taskId={42} />);
  await waitFor(() => expect(mocks.runtimeOverviews).toHaveBeenCalled());
  expect(screen.queryByRole("button", {name: "acquisition.plan.retryCurrentStep"})).toBeNull();
});

it("preserves the stopped record when the single-task retry is rejected", async () => {
  mocks.get.mockResolvedValue({...detail, task: {...detail.task, workflowRunId: 77, status: AcquisitionStatus.Cancelled}});
  mocks.runtimeOverviews.mockResolvedValue([{taskId: 42, workflowRunId: 77, workflowStatus: "cancelled", sources: []}]);
  mocks.retry.mockResolvedValue({code: 400, message: "run changed"});
  const onChanged = vi.fn();
  render(<PlanTaskDetailModal taskId={42} onChanged={onChanged} />);
  fireEvent.click(await screen.findByRole("button", {name: "acquisition.plan.retryCurrentStep"}));
  await waitFor(() => expect(mocks.get.mock.calls.length).toBeGreaterThan(1));
  expect(onChanged).not.toHaveBeenCalled();
  expect(screen.getByRole("button", {name: "acquisition.plan.retryCurrentStep"})).toBeEnabled();
});

it("observes an in-flight file confirmation from another client before a workflow has started", async () => {
  mocks.get.mockResolvedValue({ ...detail, task: { ...detail.task, phase: "ready", workflowRunId: null } });
  mocks.runtimeOverviews.mockResolvedValue([{ taskId: 42, planRevision: 3, workflowRunId: null,
    stage: "verifyingFiles", progressScope: "confirmation" }]);
  render(<PlanTaskDetailModal taskId={42} />);
  expect(await screen.findByText("Confirming existing files")).toBeVisible();
  expect(mocks.run).not.toHaveBeenCalled();
});
