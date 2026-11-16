import type { ReactNode } from "react";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import CompletedProcessingRevisionForm, {
  hasStoppedProcessingInputs,
} from "../CompletedProcessingRevisionForm";
import type { PlanTaskDetail } from "../models";
import { AcquisitionStatus } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ replan: vi.fn(), run: vi.fn(), select: vi.fn(), reserveDefaults: vi.fn() }));
vi.mock("../api", () => ({ planTaskApi: mocks }));
vi.mock("../../useStorageReserveDefaults", () => ({ default: mocks.reserveDefaults }));
vi.mock("@/components/Workflow/CurrentProcessingFiles", () => ({
  ProcessingDirectoryFiles: ({ path }: { path: string }) => <span data-testid="current-directory-files">{path}</span>,
  ProcessingFileList: ({ artifactId }: { artifactId: string }) => <span data-testid="selected-file-manifest">{artifactId}</span>,
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@/test/radioControls")),
  Modal: ({title,children,footer}: any) => <div role="dialog" aria-label={title}>{children}{footer}</div>,
  Tooltip: ({children}: any) => <>{children}</>,
  Spinner: ({"aria-label": label}: any) => <span role="status" aria-label={label} />,
  Checkbox: ({ children, isSelected, onValueChange, isDisabled }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Input: ({ label, value, onValueChange, isDisabled }: any) => (
    <input
      aria-label={label}
      value={value}
      disabled={isDisabled}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Button: ({
    children,
    onPress,
    isDisabled,
    isLoading,
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
    isLoading?: boolean;
  }) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  NumberInput: ({ label, value, onValueChange }: any) => (
    <input
      type="number"
      aria-label={label}
      value={value}
      onChange={(e) => onValueChange(Number(e.target.value))}
    />
  ),
  Textarea: ({
    label,
    value,
    onValueChange,
    isDisabled,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
    isDisabled?: boolean;
  }) => (
    <textarea
      aria-label={label}
      value={value}
      disabled={isDisabled}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Select: ({
    label,
    selectedKeys,
    dataSource,
    onSelectionChange,
    isDisabled,
  }: {
    label: string;
    selectedKeys: string[];
    dataSource: { value: string; label: string }[];
    onSelectionChange: (value: Set<string>) => void;
    isDisabled?: boolean;
  }) => (
    <select
      aria-label={label}
      value={selectedKeys[0] ?? ""}
      disabled={isDisabled}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      <option value="">Select an output</option>
      {dataSource.map((item) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
}));
const oldPlan = {
  requirement: "required",
  evidence: [],
  steps: [
    { id: "already-done", op: "extractArchive", input: "download", password: "old-plan-canary" },
  ],
};
const remaining = {
  requirement: "required",
  evidence: [],
  steps: [{ id: "remaining", op: "extractArchive", input: "download", selector: "inner.zip" }],
};
const keep = { requirement: "notRequired", evidence: [], steps: [] };
const detail: PlanTaskDetail = {
  stageAttempts: [],
  canEditDeliverySettings: false,
  canEditStoragePolicy: false,
  id: 4,
  task: {
    id: 4,
    planRevision: 3,
    workflowRunId: 100,
    status: AcquisitionStatus.Completed,
    phase: "completed",
    stage: "completed",
    leadKind: 2,
    createdAt: "2026-10-21T15:00:00Z",
    updatedAt: "2026-10-21T15:00:00Z",
    purchases: [],
    creationOrigins: [],
    attentionTags: [],
    isFinished: true,
    isDeleted: false,
  },
  plan: {
    version: 1,
    adaptiveFileProcessing: false,
    adaptiveFileProcessingMaxIterations: 3,
    publishRetainedFiles: false,
    coverCandidates: [],
    title: "Content",
    outputDirectory: "/downloads",
    delivery: "files",
    parts: [
      { id: "main", title: "Main files", sources: [], processing: oldPlan },
      { id: "extra", title: "Extra files", sources: [], processing: oldPlan },
    ],
  },
  candidates: [],
  issues: [],
  steps: [],
  sourceRefs: [],
  planHistory: [],
  preparationAttempts: [],
  draftSources: [],
  legacyLeads: [],
  replanArtifacts: [
    {
      partId: "main",
      artifactId: "main-output",
      rootDirectory: "/processed/main",
      fileCount: 4,
      kind: "processedFiles",
      fingerprint: "main-fp",
      canReadCurrentDirectory: true,
    },
    {
      partId: "extra",
      artifactId: "extra-output",
      rootDirectory: "/processed/extra",
      fileCount: 1,
      kind: "processedFiles",
      fingerprint: "extra-fp",
      canReadCurrentDirectory: true,
    },
    {
      partId: "main",
      artifactId: "raw-download",
      rootDirectory: "/raw/main",
      fileCount: 1,
      kind: "download",
      fingerprint: "raw-fp",
      canReadCurrentDirectory: true,
    },
    {
      partId: "unrelated",
      artifactId: "other-output",
      rootDirectory: "/processed/other",
      fileCount: 1,
      kind: "processedFiles",
      fingerprint: "other-fp",
      canReadCurrentDirectory: true,
    },
  ],
};
const confirm = () => screen.getByRole("button", { name: "acquisition.plan.replan.confirm" });
function setPart(title: string, artifact: string, plan: typeof remaining | typeof keep) {
  const part = within(screen.getByRole("region", { name: title }));
  const choice =
    plan.requirement === "notRequired"
      ? "keep"
      : plan.requirement === "unknown" && !plan.steps.length
        ? "defer"
        : "manual";
  fireEvent.change(
    part.getByRole("combobox", { name: /acquisition.plan.continue(Stopped|Completed).input/ }),
    { target: { value: artifact } },
  );
  fireEvent.click(part.getByRole("radio", { name: `acquisition.processingChoice.${choice}` }));
  if (choice === "manual") {
    if (!part.queryByLabelText("workflow.processing.plan"))
      fireEvent.click(part.getByRole("button", { name: "workflow.processing.editor.advanced" }));
    fireEvent.change(part.getByLabelText("workflow.processing.plan"), {
      target: { value: JSON.stringify(plan) },
    });
  }
}
function fill() {
  setPart("Main files", "main-output", remaining);
  setPart("Extra files", "extra-output", keep);
}

it("always processes the selected directories' current files without asking for a verification mode", async () => {
  render(<CompletedProcessingRevisionForm detail={detail} retryStage="processing" onSaved={vi.fn()} onCancel={vi.fn()} />);
  fill();
  const main = within(screen.getByRole("region", { name: "Main files" }));
  const extra = within(screen.getByRole("region", { name: "Extra files" }));
  expect(screen.queryByRole("checkbox", { name: "acquisition.processingInput.currentDirectory" })).toBeNull();
  expect(main.getByTestId("current-directory-files")).toHaveTextContent("/processed/main");
  expect(main.queryByTestId("selected-file-manifest")).toBeNull();
  expect(extra.getByTestId("current-directory-files")).toHaveTextContent("/processed/extra");
  expect(extra.queryByTestId("selected-file-manifest")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.timeline.startAttempt" }));
  await waitFor(() => expect(mocks.replan).toHaveBeenCalledOnce());
  expect(mocks.replan.mock.calls[0][1].items).toMatchObject([
    { partId: "main", continueFromArtifactId: "main-output", artifactEvidenceIds: ["main-output"], inputMode: "currentDirectory" },
    { partId: "extra", continueFromArtifactId: "extra-output", artifactEvidenceIds: ["extra-output"], inputMode: "currentDirectory" },
  ]);
});

it("uses the clicked stage's current directory for every part without selecting historical artifacts", async () => {
  const current = { ...detail, replanArtifacts: [], stageDirectories: {version:1,taskId:4,parts:{
    main:{processing:{directory:"/current/main",ownership:"exclusive",evidence:"configured"}},
    extra:{processing:{directory:"/current/extra",ownership:"exclusive",evidence:"configured"}},
  }} };
  render(<CompletedProcessingRevisionForm detail={current} retryStage="processing" inputStage="processing"
    onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(screen.queryByRole("combobox")).toBeNull();
  expect(screen.getAllByTestId("current-directory-files").map(item=>item.textContent)).toEqual(["/current/main","/current/extra"]);
  for (const control of screen.getAllByRole("radio",{name:"acquisition.processingChoice.keep"})) fireEvent.click(control);
  fireEvent.click(screen.getByRole("button",{name:"acquisition.timeline.startAttempt"}));
  await waitFor(()=>expect(mocks.replan).toHaveBeenCalledOnce());
  expect(mocks.replan.mock.calls[0][1].items).toEqual(expect.arrayContaining([
    expect.objectContaining({partId:"main",inputStage:"processing",inputMode:"stageDirectory",artifactEvidenceIds:[]}),
    expect.objectContaining({partId:"extra",inputStage:"processing",inputMode:"stageDirectory",artifactEvidenceIds:[]}),
  ]));
  expect(mocks.replan.mock.calls[0][1].items.every((item:any)=>!item.continueFromArtifactId)).toBe(true);
});

it("does not substitute another stage or historical artifact when a clicked stage has no current directory", () => {
  const current = { ...detail, stageDirectories: {version:1,taskId:4,parts:{
    main:{download:{directory:"/raw/main",ownership:"exclusive",evidence:"configured"}},
    extra:{download:{directory:"/raw/extra",ownership:"exclusive",evidence:"configured"}},
  }} };
  render(<CompletedProcessingRevisionForm detail={current} retryStage="processing" inputStage="processing"
    onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(screen.queryByRole("combobox")).toBeNull();
  expect(screen.queryByTestId("current-directory-files")).toBeNull();
  expect(screen.getAllByRole("alert")).toHaveLength(2);
  for (const control of screen.getAllByRole("radio",{name:"acquisition.processingChoice.keep"})) fireEvent.click(control);
  expect(screen.getByRole("button",{name:"acquisition.timeline.startAttempt"})).toBeDisabled();
});

function initialProcessingDetail(): PlanTaskDetail {
  return {
    ...detail,
    task: { ...detail.task, status: AcquisitionStatus.Waiting, stage: "attention", isFinished: false },
    stageDirectories: {version:1, taskId:4, parts:{
      main:{download:{directory:"/raw/main",ownership:"borrowed",evidence:"downloadRecord"}},
      extra:{download:{directory:"/raw/extra",ownership:"borrowed",evidence:"downloadRecord"}},
    }},
    replanArtifacts: detail.replanArtifacts.slice(0, 2).map(item => ({ ...item,
      kind: "download", rootDirectory: `/raw/${item.partId}`, canReadCurrentDirectory: true,
    })),
  };
}

it("starts first processing from each readable downloaded directory and submits the displayed stage", async () => {
  render(<CompletedProcessingRevisionForm detail={initialProcessingDetail()} retryStage="processing"
    inputStage="processing" mode="stopped" preferAi onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(screen.getAllByTestId("current-directory-files").map(item => item.textContent))
    .toEqual(["/raw/main", "/raw/extra"]);
  expect(screen.queryByRole("combobox")).toBeNull();
  expect(screen.queryByRole("alert")).toBeNull();
  fireEvent.click(screen.getByRole("button", {name:"acquisition.timeline.startAttempt"}));
  await waitFor(() => expect(mocks.replan).toHaveBeenCalledOnce());
  expect(mocks.replan.mock.calls[0][1].items).toEqual(expect.arrayContaining([
    expect.objectContaining({partId:"main", inputStage:"download", inputMode:"stageDirectory", artifactEvidenceIds:[], useAdaptiveAssessment:true}),
    expect.objectContaining({partId:"extra", inputStage:"download", inputMode:"stageDirectory", artifactEvidenceIds:[], useAdaptiveAssessment:true}),
  ]));
});

it("keeps existing processing inputs while another part starts from its completed download", async () => {
  const current = initialProcessingDetail();
  current.stageDirectories!.parts.extra.processing = {directory:"/current/extra",ownership:"exclusive",evidence:"configured"};
  render(<CompletedProcessingRevisionForm detail={current} retryStage="processing" inputStage="processing"
    mode="stopped" preferAi onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(screen.getAllByTestId("current-directory-files").map(item => item.textContent))
    .toEqual(["/raw/main", "/current/extra"]);
  fireEvent.click(screen.getByRole("button", {name:"acquisition.timeline.startAttempt"}));
  await waitFor(() => expect(mocks.replan).toHaveBeenCalledOnce());
  expect(mocks.replan.mock.calls[0][1].items).toEqual(expect.arrayContaining([
    expect.objectContaining({partId:"main", inputStage:"download"}),
    expect.objectContaining({partId:"extra", inputStage:"processing"}),
  ]));
});

it("does not replace a bound processing directory with a readable download after processing becomes unreadable", () => {
  const current = initialProcessingDetail();
  current.stageDirectories!.parts.main.processing = {directory:"/unavailable/main",ownership:"borrowed",evidence:"processingRecord"};
  current.replanArtifacts.push({...current.replanArtifacts[0], artifactId:"unavailable-processing",
    kind:"processedFiles", rootDirectory:"/unavailable/main", canReadCurrentDirectory:false});
  render(<CompletedProcessingRevisionForm detail={current} retryStage="processing" inputStage="processing"
    mode="stopped" preferAi onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(screen.getAllByTestId("current-directory-files").map(item => item.textContent))
    .toEqual(["/unavailable/main", "/raw/extra"]);
});

it.each(["unreadable", "differentRoot", "notDownload", "otherTask", "completed"])(
  "does not start first processing from unconfirmed download inputs: %s", (reason) => {
    const current = initialProcessingDetail();
    if (reason === "unreadable") current.replanArtifacts[0].canReadCurrentDirectory = false;
    if (reason === "differentRoot") current.replanArtifacts[0].rootDirectory = "/older/main";
    if (reason === "notDownload") current.replanArtifacts[0].kind = "processedFiles";
    if (reason === "otherTask") current.stageDirectories!.taskId = 999;
    if (reason === "completed") current.task.status = AcquisitionStatus.Completed;
    render(<CompletedProcessingRevisionForm detail={current} retryStage="processing" inputStage="processing"
      mode="stopped" preferAi onSaved={vi.fn()} onCancel={vi.fn()} />);
    expect(screen.getByRole("button", {name:"acquisition.timeline.startAttempt"})).toBeDisabled();
    fireEvent.click(screen.getByRole("button", {name:"acquisition.timeline.startAttempt"}));
    expect(mocks.replan).not.toHaveBeenCalled();
  },
);

it("does not silently fall back when a selected current directory is unavailable or shared", async () => {
  const unavailable = { ...detail, replanArtifacts: detail.replanArtifacts.map(item => ({ ...item, canReadCurrentDirectory: false })) };
  render(<CompletedProcessingRevisionForm detail={unavailable} retryStage="processing" onSaved={vi.fn()} onCancel={vi.fn()} />);
  fill();
  const submit = screen.getByRole("button", { name: "acquisition.timeline.startAttempt" });
  expect(submit).toBeDisabled();
  expect(screen.getAllByRole("alert")).toHaveLength(2);
  expect(screen.queryByTestId("current-directory-files")).toBeNull();
  expect(screen.queryByTestId("selected-file-manifest")).toBeNull();
  expect(screen.queryByRole("checkbox", { name: "acquisition.processingInput.currentDirectory" })).toBeNull();
  fireEvent.click(submit);
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("offers the separate delivery-only mode only when every part has a published output and preserves a processing draft on return", () => {
  const onlyProcessed = render(
    <CompletedProcessingRevisionForm detail={detail} onSaved={vi.fn()} onCancel={vi.fn()} />,
  );
  expect(
    screen.queryByRole("button", { name: "artifact.deliveryLayout.revisionTitle" }),
  ).toBeNull();
  onlyProcessed.unmount();
  const published = {
    ...detail,
    replanArtifacts: detail.replanArtifacts?.map((item) => ({
      ...item,
      kind: item.kind === "processedFiles" ? "publishedFiles" : item.kind,
      canRepublish: item.kind === "processedFiles",
    })),
  };
  render(
    <CompletedProcessingRevisionForm detail={published} onSaved={vi.fn()} onCancel={vi.fn()} />,
  );
  setPart("Main files", "main-output", remaining);
  fireEvent.click(screen.getByRole("button", { name: "artifact.deliveryLayout.revisionTitle" }));
  expect(
    screen.getByRole("button", { name: "artifact.deliveryLayout.saveRevision" }),
  ).toBeDisabled();
  expect(screen.queryByRole("button", { name: "workflow.processing.editor.advanced" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "artifact.deliveryLayout.backToProcessing" }));
  expect(
    within(screen.getByRole("region", { name: "Main files" })).getByRole("radio", { name: "acquisition.processingChoice.manual" }),
  ).toBeChecked();
  expect(mocks.replan).not.toHaveBeenCalled();
});
beforeEach(() => {
  vi.clearAllMocks();
  mocks.reserveDefaults.mockReturnValue({ value: { selected: false, minimumBytes: 50 * 1024 ** 3 }, failed: false, reload: vi.fn() });
  mocks.replan.mockResolvedValue({
    ...detail,
    task: {
      ...detail.task,
      status: AcquisitionStatus.Waiting,
      phase: "ready",
      workflowRunId: undefined,
    },
  });
});

function emptyProcessingReview(): PlanTaskDetail {
  return {
    ...detail,
    task: { ...detail.task, status: AcquisitionStatus.Waiting, waitPromptJson: JSON.stringify({
      targetId: "main", artifactId: "main-output", fingerprint: "main-fp",
      deliveryReview: true, reviewToken: "current-review", reason: "PasswordUnknown",
    }) },
    plan: { ...detail.plan!, parts: [detail.plan!.parts[0]] },
    stageDirectories: { version: 1, taskId: 4, parts: {
      main: { processing: { directory: "/current/main", ownership: "exclusive",
        evidence: "taskAllocation", bindingId: "main-directory" } },
    } },
    replanArtifacts: [{ ...detail.replanArtifacts[0], kind: "currentFiles" }],
  };
}

it("offers the exact current review directory only after an empty stage input and preserves the user's attempt options", async () => {
  mocks.reserveDefaults.mockReturnValue({ value: { selected: true, minimumBytes: 50 * 1024 ** 3 }, failed: false, reload: vi.fn() });
  mocks.replan.mockRejectedValueOnce(new Error("PLAN_CURRENT_DIRECTORY_EMPTY"));
  const saved = vi.fn();
  render(<CompletedProcessingRevisionForm detail={emptyProcessingReview()} retryStage="processing" inputStage="processing"
    mode="stopped" preferAi waitingLocalProcessing={{ taskId: 4, runId: 100, planRevision: 3 }}
    onSaved={saved} onCancel={vi.fn()} />);
  expect(screen.queryByRole("button", { name: "acquisition.processingInput.useReviewedDirectory" })).toBeNull();
  expect(screen.getByTestId("current-directory-files")).toHaveTextContent("/current/main");
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.reserve.operation.label" }));
  fireEvent.change(screen.getByLabelText("acquisition.plan.continueCompleted.adaptiveIterations"), { target: { value: "2" } });
  fireEvent.change(screen.getByLabelText("acquisition.plan.continueCompleted.adaptiveContext"), { target: { value: "Inspect the current archive only." } });
  const submit = screen.getByRole("button", { name: "acquisition.timeline.startAttempt" });
  fireEvent.click(submit);
  const recover = await screen.findByRole("button", { name: "acquisition.processingInput.useReviewedDirectory" });
  expect(mocks.replan).toHaveBeenCalledOnce();
  expect(mocks.replan.mock.calls[0][1].items).toEqual([expect.objectContaining({
    inputMode: "stageDirectory", inputStage: "processing", artifactEvidenceIds: [],
  })]);
  expect(screen.getByTestId("current-directory-files")).toHaveTextContent("/current/main");
  expect(saved).not.toHaveBeenCalled();
  fireEvent.click(recover);
  expect(mocks.replan).toHaveBeenCalledOnce();
  expect(screen.getByTestId("current-directory-files")).toHaveTextContent("/processed/main");
  expect(screen.getByRole("combobox", { name: "acquisition.plan.continueStopped.input" })).toHaveValue("main-output");
  expect(screen.getByLabelText("acquisition.plan.continueCompleted.adaptiveIterations")).toHaveValue(2);
  expect(screen.getByLabelText("acquisition.plan.continueCompleted.adaptiveContext")).toHaveValue("Inspect the current archive only.");
  expect(screen.getByRole("checkbox", { name: "acquisition.reserve.operation.label" })).not.toBeChecked();
  fireEvent.click(submit);
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(mocks.replan.mock.calls[1][1]).toMatchObject({
    expectedRevision: 3, expectedRunId: 100, enableAdaptiveAssessment: true, maxAdaptiveIterations: 2,
    adaptiveContext: "Inspect the current archive only.",
    items: [{ partId: "main", inputMode: "currentDirectory", artifactEvidenceIds: ["main-output"],
      continueFromArtifactId: "main-output", useAdaptiveAssessment: true }],
  });
  expect(mocks.replan.mock.calls[1][1].items[0]).not.toHaveProperty("inputStage");
  expect(saved.mock.calls[0][1]).toEqual({ useEmergencySpaceReserve: false });
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.select).not.toHaveBeenCalled();
});

it.each(["unreadable", "changedFingerprint", "ambiguous", "sameDirectory", "unboundDirectory", "differentPart", "multipleParts", "differentError"])(
  "does not offer an empty-directory recovery without an exact distinct current review: %s", async reason => {
    const current = emptyProcessingReview();
    if (reason === "unreadable") current.replanArtifacts[0].canReadCurrentDirectory = false;
    if (reason === "changedFingerprint") current.replanArtifacts[0].fingerprint = "changed";
    if (reason === "ambiguous") current.replanArtifacts.push({ ...current.replanArtifacts[0] });
    if (reason === "sameDirectory") current.replanArtifacts[0].rootDirectory = "/current/main";
    if (reason === "unboundDirectory") current.stageDirectories!.parts.main.processing!.bindingId = undefined;
    if (reason === "differentPart") current.replanArtifacts[0].partId = "extra";
    if (reason === "multipleParts") {
      current.plan!.parts.push(detail.plan!.parts[1]);
      current.stageDirectories!.parts.extra = { processing: { directory: "/current/extra", ownership: "exclusive",
        evidence: "taskAllocation", bindingId: "extra-directory" } };
    }
    mocks.replan.mockRejectedValueOnce(new Error(reason === "differentError" ? "PLAN_CURRENT_DIRECTORY_UNAVAILABLE" : "PLAN_CURRENT_DIRECTORY_EMPTY"));
    render(<CompletedProcessingRevisionForm detail={current} retryStage="processing" inputStage="processing"
      mode="stopped" preferAi waitingLocalProcessing={{ taskId: 4, runId: 100, planRevision: 3 }}
      onSaved={vi.fn()} onCancel={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "acquisition.timeline.startAttempt" }));
    await screen.findByRole("alert");
    expect(screen.queryByRole("button", { name: "acquisition.processingInput.useReviewedDirectory" })).toBeNull();
    expect(mocks.replan).toHaveBeenCalledOnce();
  },
);

it("starts AI with an explicit configured reserve when the global emergency default is unchecked", async () => {
  mocks.reserveDefaults.mockReturnValue({ value: { selected: true, minimumBytes: 50 * 1024 ** 3 }, failed: false, reload: vi.fn() });
  const saved = vi.fn();
  render(<CompletedProcessingRevisionForm detail={detail} retryStage="processing" preferAi
    onSaved={saved} onCancel={vi.fn()} />);
  const reserve = screen.getByRole("checkbox", {name:"acquisition.reserve.operation.label"});
  expect(reserve).toBeChecked();
  fireEvent.click(reserve);
  fireEvent.click(screen.getByRole("button", {name:"acquisition.timeline.startAttempt"}));
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(saved.mock.calls[0][1]).toEqual({useEmergencySpaceReserve:false});
  expect(mocks.replan.mock.calls[0][1].enableAdaptiveAssessment).toBe(true);
  expect(mocks.replan.mock.calls[0][1]).not.toHaveProperty("useEmergencySpaceReserve");
});

it("passes a one-time emergency reserve choice for ordinary file processing", async () => {
  const saved = vi.fn();
  render(<CompletedProcessingRevisionForm detail={detail} onSaved={saved} onCancel={vi.fn()} />);
  fill();
  fireEvent.click(screen.getByRole("checkbox", {name:"acquisition.reserve.operation.label"}));
  fireEvent.click(confirm());
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(saved.mock.calls[0][1]).toEqual({useEmergencySpaceReserve:true});
});

it("does not save or start until the current reserve default is known, and allows reloading a failed read", () => {
  const reload = vi.fn();
  mocks.reserveDefaults.mockReturnValue({value:undefined, failed:false, reload});
  const props = {detail, retryStage:"processing" as const, preferAi:true, onSaved:vi.fn(), onCancel:vi.fn()};
  const view = render(<CompletedProcessingRevisionForm {...props} />);
  const submit = screen.getByRole("button", {name:"acquisition.timeline.startAttempt"});
  expect(submit).toBeDisabled();
  expect(screen.getByRole("status", {name:"acquisition.plan.loading"})).toBeVisible();
  fireEvent.click(submit);
  expect(mocks.replan).not.toHaveBeenCalled();
  mocks.reserveDefaults.mockReturnValue({value:undefined, failed:true, reload});
  view.rerender(<CompletedProcessingRevisionForm {...props} />);
  fireEvent.click(screen.getByRole("button", {name:"acquisition.reserve.reload"}));
  expect(reload).toHaveBeenCalledOnce();
  expect(submit).toBeDisabled();
  mocks.reserveDefaults.mockReturnValue({value:{selected:true, minimumBytes:50 * 1024 ** 3}, failed:false, reload});
  view.rerender(<CompletedProcessingRevisionForm {...props} />);
  expect(submit).toBeEnabled();
  expect(screen.getByRole("checkbox", {name:"acquisition.reserve.operation.label"})).toBeChecked();
});

it("preselects AI only after an explicit reanalysis choice without submitting or changing task-level defaults", () => {
  render(<CompletedProcessingRevisionForm detail={detail} retryStage="processing" preferAi onSaved={vi.fn()} onCancel={vi.fn()} />);
  const choices = screen.getAllByRole("radio", { name: "acquisition.processingChoice.ai" });
  expect(choices).toHaveLength(2);
  for (const choice of choices) expect(choice).toBeChecked();
  expect(detail.plan?.adaptiveFileProcessing).toBe(false);
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("requires explicit processed outputs and remaining plans for every part, saving without starting or selecting sources", async () => {
  const saved = vi.fn();
  const { rerender } = render(
    <CompletedProcessingRevisionForm detail={detail} onSaved={saved} onCancel={vi.fn()} />,
  );
  expect(confirm()).toBeDisabled();
  expect(
    screen
      .getAllByRole("radio", { name: "acquisition.processingChoice.defer" })
      .every((field) => (field as HTMLInputElement).checked),
  ).toBe(true);
  expect(screen.queryByLabelText("workflow.processing.plan")).toBeNull();
  expect(
    screen
      .getAllByLabelText("acquisition.plan.continueCompleted.input")
      .every((field) => (field as HTMLSelectElement).value === ""),
  ).toBe(true);
  expect(document.body).not.toHaveTextContent("old-plan-canary");
  expect(screen.queryByRole("option", { name: "/raw/main" })).not.toBeInTheDocument();
  expect(screen.queryByRole("option", { name: "/processed/other" })).not.toBeInTheDocument();
  setPart("Main files", "main-output", remaining);
  expect(confirm()).toBeDisabled();
  setPart("Extra files", "extra-output", keep);
  // Background refresh cannot change the revision, evidence or draft being reviewed.
  rerender(
    <CompletedProcessingRevisionForm
      detail={{
        ...detail,
        task: { ...detail.task, planRevision: 9, workflowRunId: 300 },
        replanArtifacts: [],
      }}
      onSaved={saved}
      onCancel={vi.fn()}
    />,
  );
  fireEvent.click(confirm());
  await waitFor(() =>
    expect(mocks.replan).toHaveBeenCalledExactlyOnceWith(4, {
      expectedRevision: 3,
      expectedRunId: 100,
      reason: "acquisition.timeline.retry.processing",
      enableAdaptiveAssessment: false,
      items: [
        {
          partId: "main",
          plan: remaining,
          useAdaptiveAssessment: false,
          artifactEvidenceIds: ["main-output"],
          continueFromArtifactId: "main-output",
          inputMode: "currentDirectory",
        },
        {
          partId: "extra",
          plan: keep,
          useAdaptiveAssessment: false,
          artifactEvidenceIds: ["extra-output"],
          continueFromArtifactId: "extra-output",
          inputMode: "currentDirectory",
        },
      ],
    }),
  );
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.select).not.toHaveBeenCalled();
});

it("cannot substitute a download artifact or skip a part with no processed evidence", () => {
  render(
    <CompletedProcessingRevisionForm
      detail={{
        ...detail,
        replanArtifacts: detail.replanArtifacts.filter((a) => a.partId !== "extra"),
      }}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  fill();
  expect(screen.getByText("acquisition.plan.replan.noEvidence")).toBeVisible();
  expect(confirm()).toBeDisabled();
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("explicit keep hides the step editor and never requires a reason or invokes analysis", async () => {
  render(<CompletedProcessingRevisionForm detail={detail} onSaved={vi.fn()} onCancel={vi.fn()} />);
  fill();
  setPart("Main files", "main-output", keep);
  expect(screen.queryByRole("button", { name: "workflow.processing.editor.addStep" })).toBeNull();
  expect(screen.queryByLabelText("acquisition.plan.replan.reason")).toBeNull();
  expect(screen.queryByLabelText("acquisition.plan.continueCompleted.adaptiveContext")).toBeNull();
  fireEvent.click(confirm());
  await waitFor(() => expect(mocks.replan).toHaveBeenCalledOnce());
  const request = mocks.replan.mock.calls[0][1];
  expect(request.enableAdaptiveAssessment).toBe(false);
  expect(request.items).toHaveLength(2);
  expect(
    request.items.every(
      (part: any) =>
        part.useAdaptiveAssessment === false && part.plan.requirement === "notRequired",
    ),
  ).toBe(true);
});

it("can retain every part and save an explicit unknown plan without starting or adding fake steps", async () => {
  render(<CompletedProcessingRevisionForm detail={detail} onSaved={vi.fn()} onCancel={vi.fn()} />);
  fill();
  const pending = { requirement: "unknown", evidence: [], steps: [] };
  setPart("Main files", "main-output", pending);
  expect(screen.queryByLabelText("acquisition.plan.replan.reason")).toBeNull();
  expect(confirm()).toBeEnabled();
  fireEvent.click(confirm());
  await waitFor(() =>
    expect(mocks.replan).toHaveBeenCalledWith(
      4,
      expect.objectContaining({
        items: [
          {
            partId: "main",
            plan: pending,
            useAdaptiveAssessment: false,
            artifactEvidenceIds: ["main-output"],
            continueFromArtifactId: "main-output",
          inputMode: "currentDirectory",
          },
          {
            partId: "extra",
            plan: keep,
            useAdaptiveAssessment: false,
            artifactEvidenceIds: ["extra-output"],
            continueFromArtifactId: "extra-output",
          inputMode: "currentDirectory",
          },
        ],
      }),
    ),
  );
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.select).not.toHaveBeenCalled();
});

it("does not treat unknown instructions containing executable steps as a confirmed local revision", () => {
  render(<CompletedProcessingRevisionForm detail={detail} onSaved={vi.fn()} onCancel={vi.fn()} />);
  fill();
  setPart("Main files", "main-output", { ...remaining, requirement: "unknown" });
  expect(confirm()).toBeDisabled();
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("can save the displayed pending default after choosing every existing output without toggling the requirement", async () => {
  render(<CompletedProcessingRevisionForm detail={detail} onSaved={vi.fn()} onCancel={vi.fn()} />);
  for (const [title, artifact] of [
    ["Main files", "main-output"],
    ["Extra files", "extra-output"],
  ]) {
    const part = within(screen.getByRole("region", { name: title }));
    expect(part.getByRole("radio", {name: "acquisition.processingChoice.defer"})).toBeChecked();
    fireEvent.change(part.getByLabelText("acquisition.plan.continueCompleted.input"), {
      target: { value: artifact },
    });
  }
  expect(confirm()).toBeEnabled();
  fireEvent.click(confirm());
  await waitFor(() => expect(mocks.replan).toHaveBeenCalledOnce());
  expect(mocks.replan.mock.calls[0][1].items).toEqual([
    {
      partId: "main",
      plan: { requirement: "unknown", steps: [], evidence: [] },
      useAdaptiveAssessment: false,
      artifactEvidenceIds: ["main-output"],
      continueFromArtifactId: "main-output",
          inputMode: "currentDirectory",
    },
    {
      partId: "extra",
      plan: { requirement: "unknown", steps: [], evidence: [] },
      useAdaptiveAssessment: false,
      artifactEvidenceIds: ["extra-output"],
      continueFromArtifactId: "extra-output",
          inputMode: "currentDirectory",
    },
  ]);
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.select).not.toHaveBeenCalled();
});

it("keeps the complete draft after a revision conflict without automatic or stale repeated submission", async () => {
  mocks.replan.mockRejectedValue(new Error("PLAN_VERSION_CONFLICT"));
  const saved = vi.fn();
  const { rerender } = render(
    <CompletedProcessingRevisionForm detail={detail} onSaved={saved} onCancel={vi.fn()} />,
  );
  fill();
  fireEvent.click(confirm());
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "acquisition.plan.replan.error.PLAN_VERSION_CONFLICT",
  );
  rerender(
    <CompletedProcessingRevisionForm
      detail={{ ...detail, task: { ...detail.task, planRevision: 4 } }}
      onSaved={saved}
      onCancel={vi.fn()}
    />,
  );
  expect(
    within(screen.getByRole("region", { name: "Main files" })).getByLabelText(
      "workflow.processing.plan",
    ),
  ).toHaveValue(JSON.stringify(remaining));
  expect(screen.queryByLabelText("acquisition.plan.replan.reason")).toBeNull();
  expect(confirm()).toBeDisabled();
  fireEvent.click(confirm());
  expect(mocks.replan).toHaveBeenCalledTimes(1);
  expect(saved).not.toHaveBeenCalled();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("prevents duplicate in-flight submission and preserves the draft on an unknown error", async () => {
  let reject!: (error: Error) => void;
  mocks.replan.mockReturnValue(
    new Promise((_, fail) => {
      reject = fail;
    }),
  );
  const saved = vi.fn();
  render(<CompletedProcessingRevisionForm detail={detail} onSaved={saved} onCancel={vi.fn()} />);
  fill();
  fireEvent.click(confirm());
  fireEvent.click(confirm());
  expect(mocks.replan).toHaveBeenCalledTimes(1);
  expect(confirm()).toBeDisabled();
  reject(new Error("private-server-canary"));
  await screen.findByRole("alert");
  expect(document.body).not.toHaveTextContent("private-server-canary");
  expect(screen.queryByLabelText("acquisition.plan.replan.reason")).toBeNull();
  expect(saved).not.toHaveBeenCalled();
  expect(mocks.run).not.toHaveBeenCalled();
});

it("cancels without modifying the completed task", () => {
  const cancel = vi.fn();
  render(<CompletedProcessingRevisionForm detail={detail} onSaved={vi.fn()} onCancel={cancel} />);
  fill();
  fireEvent.click(screen.getByRole("button", { name: "common.action.cancel" }));
  expect(cancel).toHaveBeenCalledOnce();
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("accepts verified delivered outputs and opts into current storage only when explicitly selected", async () => {
  const completed = {
    ...detail,
    replanArtifacts: detail.replanArtifacts.map((a) =>
      a.artifactId === "main-output"
        ? { ...a, kind: "publishedFiles", rootDirectory: "/delivered/main" }
        : a,
    ),
  };
  render(
    <CompletedProcessingRevisionForm detail={completed} onSaved={vi.fn()} onCancel={vi.fn()} />,
  );
  openPlacement();
  const currentStorage = screen.getByRole("checkbox", {
    name: /acquisition.plan.continueCompleted.useCurrentStorage/,
  });
  expect(currentStorage).not.toBeChecked();
  expect(screen.getByRole("option", { name: "/delivered/main" })).toBeVisible();
  fill();
  fireEvent.click(currentStorage);
  applyPlacement();
  fireEvent.click(confirm());
  await waitFor(() =>
    expect(mocks.replan).toHaveBeenCalledWith(
      4,
      expect.objectContaining({
        expectedRevision: 3,
        expectedRunId: 100,
        useCurrentStorageDefaults: true,
        items: expect.arrayContaining([
          expect.objectContaining({ continueFromArtifactId: "main-output" }),
        ]),
      }),
    ),
  );
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each([AcquisitionStatus.Failed, AcquisitionStatus.Cancelled])(
  "continues stopped status %s using explicitly selected files for every part, without replaying source actions",
  async (status) => {
    const stopped = { ...detail, task: { ...detail.task, status } };
    render(
      <CompletedProcessingRevisionForm
        mode="stopped"
        detail={stopped}
        onSaved={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(
      screen.getByRole("region", { name: "acquisition.plan.continueStopped.title" }),
    ).toBeVisible();
    expect(screen.getByRole("option", { name: "/raw/main" })).toBeVisible();
    expect(screen.queryByRole("option", { name: "/processed/other" })).toBeNull();
    expect(
      screen.queryByRole("button", { name: "artifact.deliveryLayout.revisionTitle" }),
    ).toBeNull();
    openPlacement();
  const currentStorage = screen.getByRole("checkbox", {
      name: /acquisition.plan.continueCompleted.useCurrentStorage/,
    });
    expect(currentStorage).not.toBeChecked();
    setPart("Main files", "raw-download", keep);
    expect(confirm()).toBeDisabled();
    setPart("Extra files", "extra-output", keep);
    expect(confirm()).toBeEnabled();
    fireEvent.click(currentStorage);
  applyPlacement();
    fireEvent.click(confirm());
    await waitFor(() =>
      expect(mocks.replan).toHaveBeenCalledExactlyOnceWith(4, {
        expectedRevision: 3,
        expectedRunId: 100,
        reason: "acquisition.timeline.retry.processing",
        enableAdaptiveAssessment: false,
        useCurrentStorageDefaults: true,
        items: [
          {
            partId: "main",
            plan: keep,
            useAdaptiveAssessment: false,
            artifactEvidenceIds: ["raw-download"],
            continueFromArtifactId: "raw-download",
          inputMode: "currentDirectory",
          },
          {
            partId: "extra",
            plan: keep,
            useAdaptiveAssessment: false,
            artifactEvidenceIds: ["extra-output"],
            continueFromArtifactId: "extra-output",
          inputMode: "currentDirectory",
          },
        ],
      }),
    );
    expect(mocks.run).not.toHaveBeenCalled();
    expect(mocks.select).not.toHaveBeenCalled();
  },
);

it("keeps old placement unless the stopped-task storage option is explicitly selected", async () => {
  render(
    <CompletedProcessingRevisionForm
      mode="stopped"
      detail={{ ...detail, task: { ...detail.task, status: AcquisitionStatus.Cancelled } }}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  fill();
  fireEvent.click(confirm());
  await waitFor(() => expect(mocks.replan).toHaveBeenCalledOnce());
  expect(mocks.replan.mock.calls[0][1]).not.toHaveProperty("useCurrentStorageDefaults");
  expect(
    mocks.replan.mock.calls[0][1].items.every(
      (item: { continueFromArtifactId?: string }) => !!item.continueFromArtifactId,
    ),
  ).toBe(true);
});

it.each([
  AcquisitionStatus.Pending,
  AcquisitionStatus.Running,
  AcquisitionStatus.Waiting,
  AcquisitionStatus.Completed,
])(
  "does not allow stopped-file mode for status %s, even when every artifact is present",
  (status) => {
    const forbidden = { ...detail, task: { ...detail.task, status } };
    expect(hasStoppedProcessingInputs(forbidden)).toBe(false);
    render(
      <CompletedProcessingRevisionForm
        mode="stopped"
        detail={forbidden}
        onSaved={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    fill();
    expect(confirm()).toBeDisabled();
    expect(mocks.replan).not.toHaveBeenCalled();
  },
);

it("does not accept stopped-file mode when one part has no candidate or the previous run is missing", () => {
  const stopped = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Cancelled } };
  expect(
    hasStoppedProcessingInputs({
      ...stopped,
      replanArtifacts: stopped.replanArtifacts.filter((a) => a.partId !== "extra"),
    }),
  ).toBe(false);
  expect(
    hasStoppedProcessingInputs({ ...stopped, task: { ...stopped.task, workflowRunId: undefined } }),
  ).toBe(false);
});

it("explicitly combines all selected registered parts into one local processing plan", async () => {
  mocks.replan.mockResolvedValue(detail);
  const onSaved = vi.fn();
  render(<CompletedProcessingRevisionForm detail={detail} onSaved={onSaved} onCancel={vi.fn()} />);
  const combine = screen.getByRole("checkbox", { name: /acquisition.plan.composite.title/ });
  expect(combine).not.toBeChecked();
  fireEvent.click(combine);
  openPlacement();
  expect(
    screen.getByRole("checkbox", { name: /acquisition.plan.continueCompleted.useCurrentStorage/ }),
  ).toBeChecked();
  expect(
    screen.getByRole("checkbox", { name: /acquisition.plan.continueCompleted.useCurrentStorage/ }),
  ).toBeDisabled();
  applyPlacement();
  fireEvent.change(
    within(screen.getByRole("region", { name: "Main files" })).getByRole("combobox"),
    { target: { value: "main-output" } },
  );
  fireEvent.change(
    within(screen.getByRole("region", { name: "Extra files" })).getByRole("combobox"),
    { target: { value: "extra-output" } },
  );
  const scope = within(screen.getByRole("region", { name: "acquisition.plan.composite.scope" }));
  fireEvent.click(scope.getByRole("button", { name: "workflow.processing.editor.advanced" }));
  fireEvent.change(scope.getByLabelText("workflow.processing.plan"), {
    target: { value: JSON.stringify(remaining) },
  });
  expect(confirm()).not.toBeDisabled();
  fireEvent.click(confirm());
  await waitFor(() => expect(onSaved).toHaveBeenCalled());
  expect(mocks.replan).toHaveBeenCalledWith(
    4,
    expect.objectContaining({
      expectedRevision: 3,
      expectedRunId: 100,
      useCurrentStorageDefaults: true,
      compositeInputs: [
        { partId: "main", artifactId: "main-output" },
        { partId: "extra", artifactId: "extra-output" },
      ],
      items: [
        {
          partId: "main",
          plan: remaining,
          useAdaptiveAssessment: false,
          artifactEvidenceIds: ["main-output", "extra-output"],
        },
      ],
    }),
  );
  expect(mocks.run).not.toHaveBeenCalled();
});

it("requires every source part and executable steps before combining and does not reuse unrelated artifacts", () => {
  render(<CompletedProcessingRevisionForm detail={detail} onSaved={vi.fn()} onCancel={vi.fn()} />);
  fireEvent.click(screen.getByRole("checkbox", { name: /acquisition.plan.composite.title/ }));
  fireEvent.change(
    within(screen.getByRole("region", { name: "Main files" })).getByRole("combobox"),
    { target: { value: "main-output" } },
  );
  expect(confirm()).toBeDisabled();
  expect(screen.queryByRole("option", { name: "/processed/other" })).toBeNull();
  expect(screen.queryByRole("option", { name: "/raw/main" })).toBeNull();
  fireEvent.change(
    within(screen.getByRole("region", { name: "Extra files" })).getByRole("combobox"),
    { target: { value: "extra-output" } },
  );
  expect(confirm()).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox", { name: /acquisition.plan.composite.title/ }));
  expect(screen.queryByRole("region", { name: "acquisition.plan.composite.scope" })).toBeNull();
  openPlacement();
  expect(
    screen.getByRole("checkbox", { name: /acquisition.plan.continueCompleted.useCurrentStorage/ }),
  ).not.toBeChecked();
});

it("continues every waiting conflict part from explicit local artifacts without replaying downloads", async () => {
  const waiting = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Waiting } };
  const admission = { taskId: 4, runId: 100, planRevision: 3 };
  const saved = vi.fn();
  render(
    <CompletedProcessingRevisionForm
      detail={waiting}
      mode="stopped"
      waitingPathConflict={admission}
      onSaved={saved}
      onCancel={vi.fn()}
    />,
  );
  expect(screen.queryByRole("checkbox", { name: /acquisition.plan.composite.title/ })).toBeNull();
  setPart("Main files", "raw-download", remaining);
  expect(confirm()).toBeDisabled();
  setPart("Extra files", "extra-output", keep);
  expect(confirm()).toBeEnabled();
  fireEvent.click(confirm());
  await waitFor(() => expect(saved).toHaveBeenCalled());
  expect(mocks.replan).toHaveBeenCalledExactlyOnceWith(4, {
    expectedRevision: 3,
    expectedRunId: 100,
    reason: "acquisition.timeline.retry.processing",
    enableAdaptiveAssessment: false,
    items: [
      {
        partId: "main",
        plan: remaining,
        useAdaptiveAssessment: false,
        artifactEvidenceIds: ["raw-download"],
        continueFromArtifactId: "raw-download",
          inputMode: "currentDirectory",
      },
      {
        partId: "extra",
        plan: keep,
        useAdaptiveAssessment: false,
        artifactEvidenceIds: ["extra-output"],
        continueFromArtifactId: "extra-output",
          inputMode: "currentDirectory",
      },
    ],
  });
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each([
  undefined,
  { taskId: 5, runId: 100, planRevision: 3 },
  { taskId: 4, runId: 101, planRevision: 3 },
  { taskId: 4, runId: 100, planRevision: 4 },
])("does not authorize a waiting revision without matching conflict evidence: %j", (admission) => {
  const waiting = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Waiting } };
  expect(hasStoppedProcessingInputs(waiting, admission)).toBe(false);
  render(
    <CompletedProcessingRevisionForm
      detail={waiting}
      mode="stopped"
      waitingPathConflict={admission}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  fill();
  expect(confirm()).toBeDisabled();
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("continues publication storage waits without further processing only after explicitly choosing that plan", async () => {
  const waiting = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Waiting } };
  const admission = { taskId: 4, runId: 100, planRevision: 3 };
  const saved = vi.fn();
  render(
    <CompletedProcessingRevisionForm
      detail={waiting}
      mode="stopped"
      waitingPublicationStorage={admission}
      onSaved={saved}
      onCancel={vi.fn()}
    />,
  );
  openPlacement();
  const storage = screen.getByRole("checkbox", {
    name: /acquisition.plan.continueCompleted.useCurrentStorage/,
  });
  expect(storage).toBeChecked();
  expect(storage).toBeDisabled();
  expect(screen.queryByRole("checkbox", { name: /acquisition.plan.composite.title/ })).toBeNull();
  expect(screen.queryByLabelText("acquisition.plan.replan.reason")).toBeNull();
  expect(screen.queryByRole("option", { name: "/raw/main" })).toBeNull();
  expect(
    within(screen.getByRole("region", { name: "Main files" })).getByRole("combobox", {
      name: "acquisition.plan.continueStopped.input",
    }),
  ).toHaveValue("main-output");
  expect(
    within(screen.getByRole("region", { name: "Extra files" })).getByRole("combobox", {
      name: "acquisition.plan.continueStopped.input",
    }),
  ).toHaveValue("extra-output");
  setPart("Main files", "main-output", keep);
  setPart("Extra files", "extra-output", keep);
  expect(confirm()).toBeEnabled();
  fireEvent.click(confirm());
  await waitFor(() => expect(saved).toHaveBeenCalled());
  expect(mocks.replan).toHaveBeenCalledExactlyOnceWith(4, {
    expectedRevision: 3,
    expectedRunId: 100,
    reason: "acquisition.timeline.retry.processing",
    enableAdaptiveAssessment: false,
    useCurrentStorageDefaults: true,
    items: [
      {
        partId: "main",
        plan: keep,
        useAdaptiveAssessment: false,
        artifactEvidenceIds: ["main-output"],
        continueFromArtifactId: "main-output",
          inputMode: "currentDirectory",
      },
      {
        partId: "extra",
        plan: keep,
        useAdaptiveAssessment: false,
        artifactEvidenceIds: ["extra-output"],
        continueFromArtifactId: "extra-output",
          inputMode: "currentDirectory",
      },
    ],
  });
  expect(mocks.run).not.toHaveBeenCalled();
});

it.each([
  { taskId: 5, runId: 100, planRevision: 3 },
  { taskId: 4, runId: 101, planRevision: 3 },
  { taskId: 4, runId: 100, planRevision: 4 },
])("rejects stale publication storage admission: %j", (admission) => {
  const waiting = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Waiting } };
  expect(hasStoppedProcessingInputs(waiting, undefined, admission)).toBe(false);
  render(
    <CompletedProcessingRevisionForm
      detail={waiting}
      mode="stopped"
      waitingPublicationStorage={admission}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  expect(confirm()).toBeDisabled();
});

it("requires a processed candidate for every publication part and fresh waiting admission at submission", () => {
  const waiting = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Waiting } };
  const admission = { taskId: 4, runId: 100, planRevision: 3 };
  expect(
    hasStoppedProcessingInputs(
      {
        ...waiting,
        replanArtifacts: waiting.replanArtifacts.map((item) =>
          item.partId === "extra" ? { ...item, kind: "download" } : item,
        ),
      },
      undefined,
      admission,
    ),
  ).toBe(false);
  const view = render(
    <CompletedProcessingRevisionForm
      detail={waiting}
      mode="stopped"
      waitingPublicationStorage={admission}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  expect(confirm()).toBeEnabled();
  view.rerender(
    <CompletedProcessingRevisionForm
      detail={waiting}
      mode="stopped"
      waitingPublicationStorage={undefined}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  expect(confirm()).toBeDisabled();
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("keeps publication storage processing unresolved by default instead of assuming processed artifacts need no further work", async () => {
  const waiting = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Waiting } };
  render(
    <CompletedProcessingRevisionForm
      detail={waiting}
      mode="stopped"
      waitingPublicationStorage={{ taskId: 4, runId: 100, planRevision: 3 }}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  fireEvent.click(confirm());
  await waitFor(() => expect(mocks.replan).toHaveBeenCalledOnce());
  expect(mocks.replan.mock.calls[0][1]).toMatchObject({
    useCurrentStorageDefaults: true,
    items: [
      {
        partId: "main",
        plan: { requirement: "unknown", steps: [], evidence: [] },
        continueFromArtifactId: "main-output",
          inputMode: "currentDirectory",
      },
      {
        partId: "extra",
        plan: { requirement: "unknown", steps: [], evidence: [] },
        continueFromArtifactId: "extra-output",
          inputMode: "currentDirectory",
      },
    ],
  });
  expect(mocks.run).not.toHaveBeenCalled();
});

it("allows explicitly reviewed remaining archive processing in publication storage revisions", async () => {
  const waiting = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Waiting } };
  render(
    <CompletedProcessingRevisionForm
      detail={waiting}
      mode="stopped"
      waitingPublicationStorage={{ taskId: 4, runId: 100, planRevision: 3 }}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  setPart("Main files", "main-output", remaining);
  setPart("Extra files", "extra-output", keep);
  fireEvent.click(confirm());
  await waitFor(() => expect(mocks.replan).toHaveBeenCalledOnce());
  expect(mocks.replan.mock.calls[0][1]).toMatchObject({
    useCurrentStorageDefaults: true,
    items: [
      { partId: "main", plan: remaining },
      { partId: "extra", plan: keep },
    ],
  });
  expect(mocks.run).not.toHaveBeenCalled();
});

it("retries only the recorded publication inputs and excludes unrelated retained files", async () => {
  const saved = vi.fn();
  const stopped = {
    ...detail,
    task: { ...detail.task, status: AcquisitionStatus.Failed },
    replanArtifacts: detail.replanArtifacts.map((a) => ({
      ...a,
      canRetryDelivery: a.kind === "processedFiles" && a.partId !== "unrelated",
    })),
  };
  render(
    <CompletedProcessingRevisionForm
      detail={stopped}
      mode="stopped"
      retryStage="delivery"
      onSaved={saved}
      onCancel={vi.fn()}
    />,
  );
  expect(screen.queryByRole("option", { name: "/raw/main" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.timeline.startAttempt" }));
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(mocks.replan).toHaveBeenCalledWith(
    4,
    expect.objectContaining({
      retryDeliveryOnly: true,
      useCurrentStorageDefaults: true,
      items: [
        {
          partId: "main",
          plan: keep,
          artifactEvidenceIds: ["main-output"],
          continueFromArtifactId: "main-output",
        },
        {
          partId: "extra",
          plan: keep,
          artifactEvidenceIds: ["extra-output"],
          continueFromArtifactId: "extra-output",
        },
      ],
    }),
  );
});

it("saves the reviewed LLM iteration limit and optional note with this processing revision", async () => {
  const saved = vi.fn();
  render(<CompletedProcessingRevisionForm detail={detail} onSaved={saved} onCancel={vi.fn()} />);
  fill();
  const main = within(screen.getByRole("region", { name: "Main files" }));
  fireEvent.click(main.getByRole("radio", { name: `acquisition.processingChoice.ai` }));
  expect(main.queryByLabelText("workflow.processing.plan")).toBeNull();
  fireEvent.change(screen.getByLabelText("acquisition.plan.continueCompleted.adaptiveIterations"), {
    target: { value: "2" },
  });
  fireEvent.change(screen.getByLabelText("acquisition.plan.continueCompleted.adaptiveContext"), {
    target: { value: "The two volumes belong together." },
  });
  fireEvent.click(confirm());
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(mocks.replan.mock.calls[0][1]).toMatchObject({
    enableAdaptiveAssessment: true,
    maxAdaptiveIterations: 2,
    adaptiveContext: "The two volumes belong together.",
    items: [
      { partId: "main", useAdaptiveAssessment: true, inputMode: "currentDirectory", plan: { requirement: "unknown", steps: [] } },
      {
        partId: "extra",
        useAdaptiveAssessment: false,
        inputMode: "currentDirectory",
        plan: { requirement: "notRequired", steps: [] },
      },
    ],
  });
  expect(mocks.replan.mock.calls[0][1]).not.toHaveProperty("adaptiveFileProcessingMaxIterations");
});

it.each(["download", "currentFiles"])("can request AI for a legacy unknown wait using its %s current directory without replaying network work", async kind => {
  const waiting: PlanTaskDetail = {
    ...detail,
    task: { ...detail.task, status: AcquisitionStatus.Waiting, waitPromptJson: JSON.stringify({
      targetId: "main", reason: "ExtractionPlanUnknown", canEditPlan: true, allowedActions: [],
    }) },
    plan: { ...detail.plan!, parts: [detail.plan!.parts[0]] },
    replanArtifacts: detail.replanArtifacts.filter((item) => item.artifactId === "raw-download").map(item => ({ ...item, kind })),
  };
  const saved = vi.fn();
  render(<CompletedProcessingRevisionForm detail={waiting} mode="stopped" retryStage="processing"
    waitingLocalProcessing={{ taskId: 4, runId: 100, planRevision: 3 }}
    onSaved={saved} onCancel={vi.fn()} />);
  const part = within(screen.getByRole("region", { name: "Main files" }));
  fireEvent.click(part.getByRole("radio", { name: "acquisition.processingChoice.ai" }));
  const submit = screen.getByRole("button", { name: "acquisition.timeline.startAttempt" });
  expect(part.getByLabelText("acquisition.plan.continueStopped.input")).toHaveValue("raw-download");
  expect(submit).toBeEnabled();
  fireEvent.click(submit);
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(mocks.replan).toHaveBeenCalledWith(4, expect.objectContaining({
    expectedRunId: 100, expectedRevision: 3, enableAdaptiveAssessment: true,
    items: [expect.objectContaining({ partId: "main", useAdaptiveAssessment: true,
      artifactEvidenceIds: ["raw-download"], continueFromArtifactId: "raw-download",
      inputMode: "currentDirectory",
      plan: expect.objectContaining({ requirement: "unknown", steps: [] }),
    })],
  }));
  expect(mocks.run).not.toHaveBeenCalled();
  expect(mocks.select).not.toHaveBeenCalled();
});

it.each([
  ["current proof", "main", "main-output", "main-fp", "main-output"],
  ["changed fingerprint", "main", "main-output", "old-hash", ""],
  ["different part", "extra", "main-output", "main-fp", ""],
  ["raw download", "main", "raw-download", "raw-fp", ""],
])(
  "uses only the exact retained review artifact to preselect processing inputs: %s",
  (_case, targetId, artifactId, fingerprint, expected) => {
    const waiting = {
      ...detail,
      task: {
        ...detail.task,
        status: AcquisitionStatus.Waiting,
        waitPromptJson: JSON.stringify({
          targetId,
          artifactId,
          fingerprint,
          deliveryReview: true,
          reviewToken: "signed-review",
        }),
      },
    };
    render(
      <CompletedProcessingRevisionForm
        detail={waiting}
        mode="stopped"
        retryStage="processing"
        waitingLocalProcessing={{ taskId: 4, runId: 100, planRevision: 3 }}
        onSaved={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(
      within(screen.getByRole("region", { name: "Main files" })).getByRole("combobox", {
        name: "acquisition.plan.continueStopped.input",
      }),
    ).toHaveValue(expected);
    expect(
      within(screen.getByRole("region", { name: "Extra files" })).getByRole("combobox", {
        name: "acquisition.plan.continueStopped.input",
      }),
    ).toHaveValue("");
  },
);

it.each([true, false])("preselects the exact currentFiles review only when its directory is readable (%s)", canReadCurrentDirectory => {
  const waiting = { ...detail,
    task: { ...detail.task, status: AcquisitionStatus.Waiting, waitPromptJson: JSON.stringify({
      targetId: "main", artifactId: "main-output", fingerprint: "main-fp", deliveryReview: true,
      reviewToken: "signed-review",
    }) },
    replanArtifacts: detail.replanArtifacts.map(item => item.artifactId === "main-output"
      ? { ...item, kind: "currentFiles", canReadCurrentDirectory } : item),
  };
  render(<CompletedProcessingRevisionForm detail={waiting} mode="stopped" retryStage="processing"
    waitingLocalProcessing={{ taskId: 4, runId: 100, planRevision: 3 }} onSaved={vi.fn()} onCancel={vi.fn()} />);
  const part = within(screen.getByRole("region", { name: "Main files" }));
  expect(part.getByRole("combobox", { name: "acquisition.plan.continueStopped.input" }))
    .toHaveValue(canReadCurrentDirectory ? "main-output" : "");
  expect(screen.queryByLabelText("acquisition.processingInput.currentDirectory")).toBeNull();
});

it("requires a choice for a processing retry and does not requeue an already waiting task when every part defers", () => {
  const waiting = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Waiting } };
  render(
    <CompletedProcessingRevisionForm
      detail={waiting}
      mode="stopped"
      retryStage="processing"
      waitingLocalProcessing={{ taskId: 4, runId: 100, planRevision: 3 }}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  const submit = screen.getByRole("button", { name: "acquisition.timeline.startAttempt" });
  const choices = screen.getAllByRole("radio", { name: "acquisition.processingChoice.defer" });
  expect(screen.queryByRole("radio", { checked: true })).toBeNull();
  for (const [title, id] of [
    ["Main files", "main-output"],
    ["Extra files", "extra-output"],
  ]) {
    const part = within(screen.getByRole("region", { name: title }));
    fireEvent.change(part.getByLabelText("acquisition.plan.continueStopped.input"), {
      target: { value: id },
    });
  }
  expect(submit).toBeDisabled();
  for (const choice of choices) fireEvent.click(choice);
  expect(submit).toBeDisabled();
  expect(screen.getByText("acquisition.processingChoice.alreadyWaiting")).toBeVisible();
  fireEvent.click(submit);
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("drops hidden AI notes and manual instructions when the user changes to keep", async () => {
  const stopped = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Failed } };
  render(
    <CompletedProcessingRevisionForm
      detail={stopped}
      mode="stopped"
      retryStage="processing"
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  setPart("Main files", "main-output", remaining);
  setPart("Extra files", "extra-output", keep);
  const part = within(screen.getByRole("region", { name: "Main files" }));
  fireEvent.click(part.getByRole("radio", { name: `acquisition.processingChoice.ai` }));
  fireEvent.change(screen.getByLabelText("acquisition.plan.continueCompleted.adaptiveContext"), {
    target: { value: "private-old-note" },
  });
  fireEvent.click(part.getByRole("radio", { name: `acquisition.processingChoice.keep` }));
  expect(screen.queryByLabelText("acquisition.plan.continueCompleted.adaptiveContext")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.timeline.startAttempt" }));
  await waitFor(() => expect(mocks.replan).toHaveBeenCalledOnce());
  const body = mocks.replan.mock.calls[0][1];
  expect(body).not.toHaveProperty("adaptiveContext");
  expect(body.reason).toBe("acquisition.timeline.retry.processing");
  expect(body.items[0]).toMatchObject({ useAdaptiveAssessment: false, plan: keep });
});

it("distinguishes last legacy delivery from the next revision root and the current user settings", () => {
  const historical = {
    ...detail,
    plan: { ...detail.plan!, outputDirectory: "/nas/Downloads" },
    replanArtifacts: [{
      ...detail.replanArtifacts[0], kind: "publishedFiles", canRepublish: true,
      rootDirectory: "/nas/Downloads/old-hash/result",
    }],
  };
  render(<CompletedProcessingRevisionForm detail={historical} currentDeliveryRoot="/ssd/Acquired"
    onSaved={vi.fn()} onCancel={vi.fn()} />);
  openPlacement();
  const destination = (key: string) => screen.getByText("acquisition.revisionRoot." + key).nextElementSibling;
  expect(destination("published")).toHaveTextContent("/nas/Downloads/old-hash/result");
  expect(destination("current")).toHaveTextContent("/ssd/Acquired");
  expect(destination("next")).toHaveTextContent("/nas/Downloads/Acquired");
  const useCurrent = screen.getByRole("checkbox", { name: /acquisition.plan.continueCompleted.useCurrentStorage/ });
  fireEvent.click(useCurrent);
  expect(destination("next")).toHaveTextContent("/ssd/Acquired");
  fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", {name:"common.action.cancel"}));
  expect(screen.queryByRole("dialog")).toBeNull();
  openPlacement();
  expect(destination("next")).toHaveTextContent("/nas/Downloads/Acquired");
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("preserves an explicit task root and distinguishes unavailable settings from an implicit Acquired directory", () => {
  const custom = {
    ...detail, plan: { ...detail.plan!, outputDirectory: "D:\\Downloads",
      deliveryLayout: {version: 1, taskId: 4, rootDirectory: "X:\\Shared", taskDirectory: "X:\\Shared\\Content",
        settings: {rootDirectory: "X:\\Shared", directoryTemplate: "{Title}", partDirectoryTemplate: "{PartId}"},
        partDirectories: {}},
    },
  };
  const props = {detail: custom, onSaved: vi.fn(), onCancel: vi.fn()};
  const view = render(<CompletedProcessingRevisionForm {...props} />);
  openPlacement();
  const destination = (key: string) => screen.getByText("acquisition.revisionRoot." + key).nextElementSibling;
  expect(destination("current")).toHaveTextContent("acquisition.revisionRoot.unavailable");
  expect(destination("next")).toHaveTextContent("X:\\Shared");
  view.rerender(<CompletedProcessingRevisionForm {...props} currentDeliveryRoot={null} />);
  expect(destination("saved")).toHaveTextContent("X:\\Shared");
  expect(destination("current")).toHaveTextContent("acquisition.revisionRoot.downloadDefault");
  fireEvent.click(screen.getByRole("checkbox", { name: /acquisition.plan.continueCompleted.useCurrentStorage/ }));
  expect(destination("next")).toHaveTextContent("D:\\Downloads\\Acquired");
});

function openPlacement() {
  fireEvent.click(screen.getByRole("button", {name: "acquisition.placement.open"}));
}

it("omits a redundant single-part title inside the task dialog, retaining standalone and multi-part headings", () => {
  const single = { ...detail, plan: { ...detail.plan!, parts: [detail.plan!.parts[0]] } };
  const view = render(<CompletedProcessingRevisionForm inDialog detail={single} retryStage="processing"
    onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(screen.getByRole("region", { name: "Main files" })).toBeVisible();
  expect(screen.queryByRole("heading", { name: "Main files" })).toBeNull();
  view.rerender(<CompletedProcessingRevisionForm detail={single} retryStage="processing"
    onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(screen.getByRole("heading", { name: "Main files" })).toBeVisible();
  view.unmount();
  render(<CompletedProcessingRevisionForm inDialog detail={detail} retryStage="processing"
    onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(screen.getByRole("heading", { name: "Main files" })).toBeVisible();
  expect(screen.getByRole("heading", { name: "Extra files" })).toBeVisible();
});
function applyPlacement() {
  fireEvent.click(screen.getByRole("button", {name: "common.action.apply"}));
}

it("offers borrowed deliveries only when identified by the server as this waiting publication’s input", () => {
  const admission = { taskId: 4, runId: 100, planRevision: 3 };
  const waiting = { ...detail, task: { ...detail.task, status: AcquisitionStatus.Waiting },
    replanArtifacts: detail.replanArtifacts.filter(a => a.kind === "processedFiles").map(a => ({
      ...a, kind: "publishedFiles", canRetryDelivery: true,
    })) };
  expect(hasStoppedProcessingInputs(waiting, undefined, admission)).toBe(true);
  expect(hasStoppedProcessingInputs({ ...waiting, replanArtifacts:
    waiting.replanArtifacts.map(a => ({ ...a, canRetryDelivery: false })) }, undefined, admission)).toBe(false);
  render(<CompletedProcessingRevisionForm detail={waiting} mode="stopped" retryStage="processing"
    waitingPublicationStorage={admission} onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(within(screen.getByRole("region", { name: "Main files" })).getByRole("combobox", {
    name: "acquisition.plan.continueStopped.input",
  })).toHaveValue("main-output");
  expect(mocks.replan).not.toHaveBeenCalled();
});
