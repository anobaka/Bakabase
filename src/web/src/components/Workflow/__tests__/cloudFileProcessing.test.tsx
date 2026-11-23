import { ResumeActivityContext, ResumePostSourceContext } from "../ResumePresentation";
import { AcquisitionPlanProcessUI } from "../Activities/AcquisitionPlanProcess";
import { Children, useState, type ReactNode } from "react";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import TaskContinuationModal from "@/components/Acquire/PlanTask/TaskContinuationModal";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  ProcessCloudFilesUI,
  ProcessFileArtifactsUI,
  type ProcessingConfig,
} from "../Activities/CloudDownload/ProcessFiles";
import { AssessDeliveryUI } from "../Activities/AssessDelivery";
import { PublishFileArtifactsUI } from "../Activities/CloudDownload/PublishArtifacts";
import { CleanupArtifactsUI } from "../Activities/CloudDownload/CleanupArtifacts";
import WorksetResults from "@/components/CloudDownload/WorksetResults";

const cleanup = vi.hoisted(() => ({ restore: vi.fn(), manifest: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: { artifacts: { restoreArtifactCleanup: cleanup.restore }, workflow: { getWorkflowArtifactFiles: cleanup.manifest } },
}));

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string, options?: {format?: string}) =>
  key === "cloudDownload.deliveryReview.archiveSignature" ? `${key} ${options?.format}` : key }) }));
vi.mock("@/components/Dependency/DependencyManager", () => ({
  default: ({ id }: { id: string }) => <div data-testid="dependency-manager">{id}</div>,
}));
vi.mock("@/components/FileSystemSelector/components/Button", () => ({
  default: ({ fileSystemSelectorProps }: any) => (
    <button onClick={() => fileSystemSelectorProps.onSelected({ path: "/nas/work" })}>
      {fileSystemSelectorProps.defaultSelectedPath || "Choose directory"}
    </button>
  ),
}));
vi.mock("@/components/ThirdPartyConfig/base/DownloadDirectoryField", () => ({
  default: ({ label, value, onChange }: any) => (
    <label>
      {label}
      <input value={value} onChange={(event) => onChange(event.target.value)} />
    </label>
  ),
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@/test/radioControls")),
  Modal: ({ title, children, footer }: any) => <div role="dialog" aria-label={title}>{children}<footer>{footer}</footer></div>,
  Tooltip: ({ children }: any) => <>{children}</>,
  Spinner: () => <span role="status" />,
  Pagination: ({ page, total, onChange }: any) => <button disabled={page === total}
    onClick={() => onChange(page + 1)}>next page</button>,
  Accordion: ({ children, selectedKeys = new Set(), onSelectionChange }: any) => (
    <div>
      {Children.map(
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
    <button disabled={isDisabled} onClick={onPress} aria-label={label}>
      {children}
    </button>
  ),
  Input: ({
    label,
    value,
    type,
    onValueChange,
  }: {
    label: string;
    value: string;
    type?: string;
    onValueChange: (value: string) => void;
  }) => (
    <label>
      {label}
      <input type={type} value={value} onChange={(e) => onValueChange(e.target.value)} />
    </label>
  ),
  Textarea: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
  }) => (
    <label>
      {label}
      <textarea value={value} onChange={(e) => onValueChange(e.target.value)} />
    </label>
  ),
  Select: ({ label, selectedKeys, dataSource, children, isDisabled, onSelectionChange }: any) => (
    <label>
      {label}
      <select
        aria-label={label}
        value={(Array.from(selectedKeys ?? [])[0] as string) ?? ""}
        disabled={isDisabled}
        onChange={(event) => onSelectionChange(new Set([event.target.value]))}
      >
        {dataSource
          ? dataSource.map((item: any) => (
              <option key={item.value} value={item.value}>
                {item.label}
              </option>
            ))
          : Children.map(
              children,
              (child: any) => child && <option value={child.key}>{child.props.children}</option>,
            )}
      </select>
    </label>
  ),
  SelectItem: ({ children }: any) => <>{children}</>,
  Checkbox: ({ children, isSelected, onValueChange, isDisabled }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(e) => onValueChange(e.target.checked)}
      />
      {children}
    </label>
  ),
  Switch: ({
    children,
    isSelected,
    onValueChange,
  }: {
    children: ReactNode;
    isSelected: boolean;
    onValueChange: (value: boolean) => void;
  }) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        onChange={(e) => onValueChange(e.target.checked)}
      />
      {children}
    </label>
  ),
  NumberInput: ({
    label,
    value,
    onValueChange,
    isDisabled,
  }: {
    label: string;
    value: number;
    onValueChange: (value: number) => void;
    isDisabled?: boolean;
  }) => (
    <label>
      {label}
      <input
        type="number"
        value={value}
        disabled={isDisabled}
        onChange={(e) => onValueChange(Number(e.target.value))}
      />
    </label>
  ),
}));

const ResumeForm = ProcessCloudFilesUI.ResumeForm!;

it("shows colliding file paths and requires a revised selector before continuing", () => {
  const onSubmit = vi.fn();
  const plan = { requirement: "required", steps: [
    { id: "rename", op: "renameExtension", input: "download", extension: ".zip" },
  ] };
  render(<ResumeForm submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
    targetId: "part", reason: "ProcessingPathConflict", canEditPlan: true,
    message: "Internal collision exception", extractionPlanJson: JSON.stringify(plan),
    pathConflict: { kind: "PlannedTargetCollision", stepId: "rename", targetPath: "archive.zip",
      entries: [
        { sourcePath: "archive.mp4", targetPath: "archive.zip" },
        { sourcePath: "archive.z01", targetPath: "archive.zip" },
      ],
    },
  })} />);
  expect(screen.getByText("archive.mp4")).toBeVisible();
  expect(screen.getByText("archive.z01")).toBeVisible();
  expect(screen.getAllByText("archive.zip")).toHaveLength(2);
  expect(screen.queryByText("Internal collision exception")).toBeNull();
  expect(screen.queryByRole("combobox", { name: "cloudDownload.processing.archivePolicy" })).toBeNull();
  const button = screen.getByRole("button", { name: "cloudDownload.processing.continue" });
  expect(button).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "workflow.processing.editor.advanced" }));
  const revised = JSON.stringify({ ...plan, steps: [{ ...plan.steps[0], selector: "*.mp4" }] });
  fireEvent.change(screen.getByLabelText("workflow.processing.plan"), { target: { value: revised } });
  expect(button).toBeEnabled();
  fireEvent.click(button);
  expect(JSON.parse(onSubmit.mock.calls[0][0])).toEqual({ targetId: "part", extractionPlanJson: revised });
});

it("keeps frozen conflicting steps paused for task revision without offering a blind retry", () => {
  render(<ResumeForm submitting={false} onSubmit={vi.fn()} promptJson={JSON.stringify({
    targetId: "part", reason: "ProcessingPathConflict", canEditPlan: false,
    allowedActions: ["retryCurrentStep"],
    pathConflict: { kind: "ExistingTargetConflict", stepId: "rename", targetPath: "archive.zip",
      entries: [null, { sourcePath: "archive.mp4", targetPath: "archive.zip" }, { sourcePath: 4 }],
    },
  })} />);
  expect(screen.getByText("archive.mp4")).toBeVisible();
  expect(screen.getByText("acquisition.plan.lockedPlan")).toBeVisible();
  expect(screen.getAllByText("workflow.fileRecovery.ProcessingPathConflict.action").length).toBeGreaterThan(0);
  expect(screen.queryByRole("button", { name: "workflow.fileRecovery.retryCurrentStep" })).toBeNull();
  expect(screen.getByRole("button", { name: "cloudDownload.processing.continue" })).toBeDisabled();
});

it.each([false, true])("offers bound keep and edit choices for a stored delivery conflict (fileTask=%s)", (fileTask) => {
  const Form = AssessDeliveryUI.ResumeForm!;
  const onSubmit = vi.fn();
  render(<Form fileTask={fileTask} submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
    targetId: "part", reason: "ProcessingPathConflict", canEditPlan: false, deliveryReview: true,
    artifactId: "artifact", fingerprint: "fp", reviewToken: "token",
    allowedArchivePolicies: ["instructions", "keep"],
    extractionPlanJson: '{"requirement":"required","steps":[{"id":"rename","op":"renameExtension","extension":".zip"}]}',
  })} />);
  expect(screen.queryByText("acquisition.plan.lockedPlan")).toBeNull();
  const submit = screen.getByRole("button", { name: "cloudDownload.processing.continue" });
  expect(submit).toBeDisabled();
  if (fileTask) {
    expect(screen.getByRole("radio", { name: "acquisition.processingChoice.manual" })).toBeEnabled();
    fireEvent.click(screen.getByRole("radio", { name: "acquisition.processingChoice.keep" }));
  } else {
    expect(screen.getByRole("option", { name: "cloudDownload.processing.policy.instructions" })).toHaveValue("instructions");
    expect(screen.getByRole("option", { name: "cloudDownload.deliveryReview.keepCurrentFiles" })).toHaveValue("keep");
    fireEvent.change(screen.getByRole("combobox", { name: "cloudDownload.processing.archivePolicy" }),
      { target: { value: "keep" } });
  }
  expect(onSubmit).not.toHaveBeenCalled();
  fireEvent.click(submit);
  expect(JSON.parse(onSubmit.mock.calls[0][0])).toEqual({
    artifactId: "artifact", fingerprint: "fp", reviewToken: "token",
    processing: { targetId: "part", archivePolicy: "keep" },
  });
});

it.each(["pyzipper-component-service", "7z-component-service"])("offers the runtime dependency %s without inferring it from the processing plan", (id) => {
  const onSubmit = vi.fn();
  render(<ResumeForm submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
    targetId: "part-files", knownStepId: "extract", reason: "DEPENDENCY_REQUIRED", canEditPlan: false,
    requiredComponentId: id, allowedActions: ["retryCurrentStep"],
    extractionPlanJson: JSON.stringify({ requirement: "required", steps: [
      { id: "extract", op: "extractArchive" },
    ] }),
  })} />);
  expect(screen.getByTestId("dependency-manager")).toHaveTextContent(id);
  fireEvent.click(screen.getByRole("button", { name: "workflow.fileRecovery.retryCurrentStep" }));
  expect(JSON.parse(onSubmit.mock.calls[0][0])).toEqual({ targetId: "part-files", action: "retryCurrentStep" });
});

it.each([null, "", "  ", { unexpected: "component" }])("keeps the generic dependency guidance when the runtime component is absent or malformed (%j)", (id) => {
  render(<ResumeForm submitting={false} onSubmit={vi.fn()} promptJson={JSON.stringify({
    targetId: "part-files", reason: "DEPENDENCY_REQUIRED", canEditPlan: false,
    requiredComponentId: id, allowedActions: ["retryCurrentStep"],
  })} />);
  expect(screen.queryByTestId("dependency-manager")).not.toBeInTheDocument();
  expect(screen.getAllByText("workflow.fileRecovery.DEPENDENCY_REQUIRED.action").length).toBeGreaterThan(0);
});

it("offers the managed ZIP dependency and retries the frozen step without replacing its plan", () => {
  const onSubmit = vi.fn();
  render(<ResumeForm submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
    targetId: "part-zip", knownStepId: "extract", reason: "DEPENDENCY_REQUIRED", canEditPlan: false,
    allowedActions: ["retryCurrentStep"],
    extractionPlanJson: JSON.stringify({ requirement: "required", steps: [
      { id: "extract", op: "extractArchive", zipPasswordCodePage: 936, password: "private-password" },
    ] }),
  })} />);
  expect(screen.getByTestId("dependency-manager")).toHaveTextContent("pyzipper-component-service");
  expect(screen.queryByDisplayValue("private-password")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "workflow.fileRecovery.retryCurrentStep" }));
  expect(JSON.parse(onSubmit.mock.calls[0][0])).toEqual({ targetId: "part-zip", action: "retryCurrentStep" });
});

it("does not offer a blind retry or password guessing after failed archive authentication", () => {
  render(<ResumeForm submitting={false} onSubmit={vi.fn()} promptJson={JSON.stringify({
    targetId: "part-zip", reason: "ArchiveIntegrityFailure", canEditPlan: false,
    message: "private-password-or-path",
  })} />);
  expect(screen.queryByText("private-password-or-path")).not.toBeInTheDocument();
  expect(screen.getAllByText("workflow.fileRecovery.ArchiveIntegrityFailure.why").length).toBeGreaterThan(0);
  expect(screen.getByRole("button", { name: "cloudDownload.processing.continue" })).toBeDisabled();
  expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
});

it.each([true, false])("requires plan review for changed archive groups and honors canEditPlan=%s", (canEditPlan) => {
  const onSubmit = vi.fn();
  render(<ResumeForm submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
    targetId: "part-groups", reason: "ArchiveGroupInputChanged", canEditPlan,
    allowedArchivePolicies: ["instructions"], allowedActions: ["retryCurrentStep"],
    message: "private-checkpoint-path",
  })} />);
  expect(screen.getAllByText("workflow.fileRecovery.ArchiveGroupInputChanged.why").length).toBeGreaterThan(0);
  expect(screen.queryByText("private-checkpoint-path")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "workflow.fileRecovery.retryCurrentStep" })).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "cloudDownload.processing.continue" })).toBeDisabled();
  expect(!!screen.queryByRole("combobox", { name: "cloudDownload.processing.archivePolicy" })).toBe(canEditPlan);
  expect(onSubmit).not.toHaveBeenCalled();
});

it("honors the processing prompt policy contract and excludes unsupported automatic probing", () => {
  const onSubmit = vi.fn();
  render(
    <ResumeForm
      promptJson={JSON.stringify({
        targetId: "part-a",
        reason: "ExtractionPlanUnknown",
        canEditPlan: true,
        allowedArchivePolicies: ["instructions", "keep"],
      })}
      submitting={false}
      onSubmit={onSubmit}
    />,
  );
  const policy = screen.getByRole("combobox", { name: "cloudDownload.processing.archivePolicy" });
  expect([...policy.querySelectorAll("option")].map((option) => option.value)).toEqual([
    "instructions",
    "keep",
  ]);
  fireEvent.change(policy, { target: { value: "keep" } });
  fireEvent.click(screen.getByRole("button", { name: "cloudDownload.processing.continue" }));
  expect(JSON.parse(onSubmit.mock.calls[0][0])).toMatchObject({
    targetId: "part-a",
    archivePolicy: "keep",
  });
});

it("lets a retained completed output receive explicit instructions through its existing confirmation form", () => {
  const submit = vi.fn();
  render(<ResumeForm submitting={false} onSubmit={submit} promptJson={JSON.stringify({
    targetId: "retained-part", reason: "ExtractionPlanUnknown", canEditPlan: true,
    knownStepId: null, allowedArchivePolicies: ["instructions", "keep"],
    extractionPlanJson: JSON.stringify({requirement: "unknown", evidence: [], steps: []}),
  })} />);
  const button = screen.getByRole("button", {name: "cloudDownload.processing.continue"});
  expect(button).toBeDisabled();
  fireEvent.click(screen.getByRole("button", {name: "workflow.processing.editor.advanced"}));
  const revised = JSON.stringify({requirement: "required", evidence: [], steps: [
    {id: "confirmed-extract", op: "extractArchive", input: "download", selector: "inner.7z"},
  ]});
  fireEvent.change(screen.getByLabelText("workflow.processing.plan"), {target: {value: revised}});
  expect(button).toBeEnabled();
  fireEvent.click(button);
  expect(JSON.parse(submit.mock.calls[0][0])).toMatchObject({
    targetId: "retained-part", extractionPlanJson: revised,
  });
});

it("does not replace a frozen known step with a whole new processing plan", () => {
  const onSubmit = vi.fn();
  render(
    <ResumeForm
      promptJson={JSON.stringify({
        targetId: "part-a",
        reason: "ArchiveInputRequired",
        canEditPlan: false,
        allowedArchivePolicies: ["instructions"],
      })}
      submitting={false}
      onSubmit={onSubmit}
    />,
  );
  expect(
    screen.queryByRole("combobox", { name: "cloudDownload.processing.archivePolicy" }),
  ).toBeNull();
  expect(screen.getByText("acquisition.plan.lockedPlan")).toBeVisible();
  expect(screen.getByRole("button", { name: "cloudDownload.processing.continue" })).toBeDisabled();
});
const planPrompt = (targetId: string) =>
  JSON.stringify({
    targetId,
    title: targetId,
    reason: "ExtractionPlanUnknown",
    extractionPlanJson: JSON.stringify({ requirement: "required", steps: [] }),
  });

describe("acquisition archive group recovery", () => {
  const Form = AcquisitionPlanProcessUI.ResumeForm!;
  const identity = { targetId: "part", artifactId: "snapshot", fingerprint: "saved", knownStepId: "s2" };
  const oldPrompt = { ...identity, reason: "MissingVolumes", allowedActions: [] };
  const group = { token: "first-checkpoint", memberCount: 3, totalBytes: 512, completedGroupCount: 1,
    members: ["set/part.002", "set/part.003", "set/part.004"] };
  const review = { ...oldPrompt, archiveGroupInspected: true, allowedActions: ["retainIncompleteArchiveGroup"],
    incompleteArchiveGroup: group };
  const inspect = "workflow.fileRecovery.incompleteGroup.inspect";
  const retain = "workflow.fileRecovery.incompleteGroup.retain";

  it("can inspect a saved wait without authorizing retention or a new download", async () => {
    const submit = vi.fn();
    render(<Form submitting={false} onSubmit={submit} promptJson={JSON.stringify(oldPrompt)} />);
    expect(screen.queryByRole("button", { name: retain })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: inspect }));
    expect(submit).toHaveBeenCalledTimes(1);
    expect(JSON.parse(submit.mock.calls[0][0])).toEqual({
      targetId: "part", artifactId: "snapshot", fingerprint: "saved", stepId: "s2", action: "inspectIncompleteArchiveGroup",
    });
    await waitFor(() => expect(screen.getByRole("button", { name: inspect })).toBeEnabled());
  });

  it.each([
    { ...review, incompleteArchiveGroup: { ...group, token: "" } },
    { ...review, incompleteArchiveGroup: { ...group, completedGroupCount: -1 } },
    { ...review, incompleteArchiveGroup: { ...group, members: [] } },
    { ...review, allowedActions: [] },
    { ...oldPrompt, archiveGroupInspected: true },
  ])("withholds consent when the checkpoint is missing, invalid or unavailable", prompt => {
    render(<Form submitting={false} onSubmit={vi.fn()} promptJson={JSON.stringify(prompt)} />);
    expect(screen.queryByRole("button", { name: retain })).toBeNull();
    expect(screen.queryByRole("button", { name: inspect })).toBeNull();
  });

  it("shows confirmed members and only sends the current checkpoint on an explicit click", async () => {
    let done!: () => void;
    const submit = vi.fn((_input: string) => new Promise<void>(resolve => { done = resolve; }));
    const renderForm = (prompt: typeof review) =>
      <Form submitting={false} onSubmit={submit} promptJson={JSON.stringify(prompt)} />;
    const view = render(renderForm(review));
    for (const name of group.members) expect(screen.getByText(name)).toBeVisible();
    expect(submit).not.toHaveBeenCalled();
    const button = screen.getByRole("button", { name: retain });
    fireEvent.click(button);
    fireEvent.click(button);
    expect(submit).toHaveBeenCalledTimes(1);
    expect(JSON.parse(submit.mock.calls[0][0])).toEqual({ targetId: "part", artifactId: "snapshot", fingerprint: "saved",
      stepId: "s2", action: "retainIncompleteArchiveGroup", archiveGroupToken: "first-checkpoint" });
    done();
    await waitFor(() => expect(button).toBeEnabled());
    const current = { ...review, incompleteArchiveGroup: { ...group, token: "different-checkpoint", members: ["other/part.001"] } };
    view.rerender(renderForm(current));
    expect(screen.queryByText(group.members[0])).toBeNull();
    expect(screen.getByText("other/part.001")).toBeVisible();
    expect(submit).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole("button", { name: retain }));
    expect(JSON.parse(submit.mock.calls[1][0]).archiveGroupToken).toBe("different-checkpoint");
    done();
    await waitFor(() => expect(screen.getByRole("button", { name: retain })).toBeEnabled());
  });
});

describe("current-step archive discovery recovery", () => {
  it.each([
    { reason: "ArchiveProbeTimedOut", allowedActions: ["retryCurrentStep"] },
    { reason: "ArchiveProbeToolFailed", allowedActions: ["retryCurrentStep"] },
    { reason: "ArchiveProbePrefixLimit", allowedActions: ["retryCurrentStep"] },
    { reason: "ArchiveProbePrefixLimit", allowedActions: [] },
    {
      reason: "ExtractionPlanUnknown",
      message:
        "Archive format discovery did not finish within its time budget. Narrow the selector or revise the plan.",
    },
  ])("offers a bounded retry for $reason without changing a frozen plan", (failure) => {
    const submit = vi.fn();
    render(
      <ResumeForm
        submitting={false}
        onSubmit={submit}
        promptJson={JSON.stringify({
          targetId: "part",
          canEditPlan: false,
          knownStepId: "s2",
          archiveId: "diagnostic-only",
          ...failure,
        })}
      />,
    );
    expect(screen.queryByRole("combobox")).toBeNull();
    expect(screen.queryByRole("textbox")).toBeNull();
    const button = screen.getByRole("button", { name: "workflow.fileRecovery.retryCurrentStep" });
    expect(button).toBeEnabled();
    fireEvent.click(button);
    expect(JSON.parse(submit.mock.calls[0][0])).toEqual({
      targetId: "part",
      action: "retryCurrentStep",
    });
  });

  it.each([
    { reason: "ArchiveDetectionBudgetExceeded" },
    { reason: "ArchiveProbeIncomplete" },
    { reason: "ArchiveProbeTimedOut", allowedActions: [] },
    { reason: "ArchiveProbePrefixLimit", allowedActions: ["manualReview"] },
    {
      reason: "ExtractionPlanUnknown",
      message:
        "Archive format discovery did not finish within its time budget. Narrow the selector or revise the plan. Untrusted suffix.",
    },
  ])("keeps manual review and revoked retry unavailable for $reason", (failure) => {
    render(
      <ResumeForm
        submitting={false}
        onSubmit={vi.fn()}
        promptJson={JSON.stringify({
          targetId: "part",
          canEditPlan: false,
          ...failure,
        })}
      />,
    );
    expect(
      screen.queryByRole("button", { name: "workflow.fileRecovery.retryCurrentStep" }),
    ).toBeNull();
    expect(
      screen.getByRole("button", { name: "cloudDownload.processing.continue" }),
    ).toBeDisabled();
  });
});

describe("downloaded file processing", () => {
  beforeEach(() => cleanup.restore.mockReset());

  it("provides the same lifecycle controls for the distinct local file artifact activity", () => {
    expect(ProcessFileArtifactsUI.kind).toBe("fs.processArtifacts");
    expect(ProcessCloudFilesUI.kind).toBe("download.processFiles");
    expect(ProcessFileArtifactsUI.defaultConfig()).toEqual(ProcessCloudFilesUI.defaultConfig());
    const Form = ProcessFileArtifactsUI.ConfigForm;
    const change = vi.fn();
    render(<Form value={ProcessFileArtifactsUI.defaultConfig()} onChange={change} />);
    expect(
      screen.getByText("workflow.activity.fsProcessArtifacts.description"),
    ).toBeInTheDocument();
    expect(
      screen.queryByText("cloudDownload.activity.download.processFiles.description"),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("cloudDownload.processing.recursive"));
    expect(change.mock.lastCall?.[0]).toMatchObject({ recursive: { enabled: true } });
  });

  it("lets a local artifact consumer recheck a shared result without fabricating a password or plan", () => {
    const submit = vi.fn();
    const LocalResume = ProcessFileArtifactsUI.ResumeForm!;
    render(
      <LocalResume
        promptJson={JSON.stringify({ targetId: "shared-input", reason: "WaitingShared" })}
        submitting={false}
        onSubmit={submit}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.processing.checkShared" }));
    expect(JSON.parse(submit.mock.lastCall![0])).toEqual({ targetId: "shared-input" });
  });

  it("lets disconnected storage resume the same checkpoint without changing the recipe", () => {
    const submit = vi.fn();
    const Resume = ProcessFileArtifactsUI.ResumeForm!;
    render(
      <Resume
        promptJson={JSON.stringify({ targetId: "retained", reason: "StorageUnavailable" })}
        submitting={false}
        onSubmit={submit}
      />,
    );
    expect(screen.getByText("cloudDownload.resumeGuide.storage.action")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.processing.continue" }));
    expect(JSON.parse(submit.mock.lastCall![0])).toEqual({ targetId: "retained" });
  });

  it.each([
    "LOCAL_STORAGE_FULL",
    "LOCAL_STORAGE_RESERVE_LOW",
    "LOCAL_STORAGE_WRITE_FAILED",
    "LOCAL_STORAGE_ACCESS_DENIED",
    "LOCAL_STORAGE_UNAVAILABLE",
  ])(
    "continues only the retained processing target after %s without new source or plan inputs",
    (reason) => {
      const submit = vi.fn();
      const Resume = ProcessFileArtifactsUI.ResumeForm!;
      render(
        <Resume
          promptJson={JSON.stringify({ targetId: "retained", reason, canEditPlan: false })}
          submitting={false}
          onSubmit={submit}
        />,
      );
      expect(
        screen.getByText(`cloudDownload.resumeGuide.${reason === "LOCAL_STORAGE_RESERVE_LOW" ? "storageReserve" : "localStorageProcessing"}.action`),
      ).toBeInTheDocument();
      expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "cloudDownload.processing.continue" }));
      expect(submit).toHaveBeenCalledExactlyOnceWith(JSON.stringify({ targetId: "retained" }));
    },
  );

  it("keeps legacy configs safe and preserves recursive limits when toggled", () => {
    expect(ProcessCloudFilesUI.defaultConfig().extractionLayout).toBe("currentDirectory");
    expect(ProcessCloudFilesUI.parseConfig('{"extractionLayout":"archiveDirectory"}').extractionLayout)
      .toBe("archiveDirectory");
    const defaults = ProcessCloudFilesUI.parseConfig("{}");
    expect(defaults).toEqual({
      reusePolicy: "reuseVerified",
      archivePolicy: "instructions",
      recursive: {
        enabled: false,
        maxDepth: 8,
        maxArchiveGroups: 1000,
        maxFiles: 10000,
        maxExpandedBytes: 21474836480,
        minimumFreeBytes: 1073741824,
      },
    });
    expect(ProcessCloudFilesUI.parseConfig("null")).toEqual(defaults);
    expect(
      ProcessCloudFilesUI.isValid({
        ...defaults,
        recursive: { ...defaults.recursive, maxFiles: NaN },
      }),
    ).toBe(false);
    const change = vi.fn();
    function Form() {
      const [value, setValue] = useState<ProcessingConfig>(defaults);
      return (
        <ProcessCloudFilesUI.ConfigForm
          value={value}
          onChange={(config) => {
            setValue(config);
            change(config);
          }}
        />
      );
    }
    render(<Form />);
    expect(screen.queryByLabelText("cloudDownload.processing.maxDepth")).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("cloudDownload.processing.recursive"));
    fireEvent.change(screen.getByLabelText("cloudDownload.processing.maxDepth"), {
      target: { value: "12" },
    });
    fireEvent.click(screen.getByLabelText("cloudDownload.reuse.forceProcessing"));
    expect(change.mock.lastCall?.[0]).toMatchObject({
      reusePolicy: "force",
      recursive: { enabled: true, maxDepth: 12, maxArchiveGroups: 1000 },
    });
    fireEvent.click(screen.getByLabelText("cloudDownload.processing.recursive"));
    expect(change.mock.lastCall?.[0].recursive.maxDepth).toBe(12);
  });

  it("keeps inherited storage unchanged on open and preserves a legacy workspace override", () => {
    expect(ProcessCloudFilesUI.parseConfig("{}").storagePolicy).toBeUndefined();
    const parsed = ProcessCloudFilesUI.parseConfig('{"workspaceRootDirectory":"/existing-work"}');
    expect(parsed.workspaceRootDirectory).toBe("/existing-work");
    expect(parsed.storagePolicy).toBeUndefined();
    const changed = vi.fn();
    const Form = ProcessCloudFilesUI.ConfigForm;
    render(<Form value={parsed} onChange={changed} />);
    expect(changed).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "workflow.parameters.nodeStorage" }));
    expect(changed).not.toHaveBeenCalled();
  });

  it("requires complete instructions and binds the answer to the waiting target", () => {
    const submit = vi.fn();
    render(<ResumeForm promptJson={planPrompt("main")} submitting={false} onSubmit={submit} />);
    const button = screen.getByRole("button", { name: "cloudDownload.processing.continue" });
    expect(button).toBeDisabled();
    const plan = JSON.stringify({
      requirement: "required",
      steps: [{ id: "extract", op: "extractArchive", input: "download" }],
    });
    fireEvent.click(screen.getByRole("button", { name: "workflow.processing.editor.advanced" }));
    fireEvent.change(screen.getByLabelText("workflow.processing.plan"), {
      target: { value: plan },
    });
    fireEvent.click(button);
    expect(JSON.parse(submit.mock.calls[0][0])).toEqual({
      targetId: "main",
      extractionPlanJson: plan,
    });
  });

  it.each(["keep", "autoDetect"] as const)(
    "requires explicit %s approval instead of treating recursion as authorization",
    (policy) => {
      const submit = vi.fn();
      render(<ResumeForm promptJson={planPrompt("main")} submitting={false} onSubmit={submit} />);
      const button = screen.getByRole("button", { name: "cloudDownload.processing.continue" });
      expect(button).toBeDisabled();
      fireEvent.change(
        screen.getByRole("combobox", { name: "cloudDownload.processing.archivePolicy" }),
        { target: { value: policy } },
      );
      expect(button).not.toBeDisabled();
      fireEvent.click(button);
      expect(JSON.parse(submit.mock.lastCall![0])).toEqual({
        targetId: "main",
        archivePolicy: policy,
      });
    },
  );

  it("exposes a separate copy publication node and displays the delivered public directory", () => {
    const Form = PublishFileArtifactsUI.ConfigForm;
    const change = vi.fn();
    render(<Form value={PublishFileArtifactsUI.defaultConfig()} onChange={change} />);
    expect(
      screen.getByText("workflow.activity.fsPublishArtifacts.description"),
    ).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("cloudDownload.publication.directory"), {
      target: { value: "/library/results" },
    });
    expect(change.mock.lastCall![0]).toMatchObject({
      targetDirectory: "/library/results",
      source: "processed",
    });
  });

  it("gives new publication nodes an explicit layout policy without upgrading old configs", () => {
    expect(PublishFileArtifactsUI.defaultConfig().contentLayout).toEqual({
      version: 1,
      removeGeneratedDirectories: true,
      collapseSingleRootDirectory: false,
    });
    const parsed = PublishFileArtifactsUI.parseConfig(
      '{"targetDirectory":"/existing","futureField":42}',
    );
    expect(parsed.contentLayout).toBeUndefined();
    const change = vi.fn();
    const Form = PublishFileArtifactsUI.ConfigForm;
    render(<Form value={parsed} onChange={change} />);
    expect(change).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText("cloudDownload.publication.directory"), {
      target: { value: "/next" },
    });
    expect(JSON.parse(PublishFileArtifactsUI.serializeConfig(change.mock.lastCall![0]))).toEqual({
      targetDirectory: "/next",
      source: "processed",
      reusePolicy: "reuseVerified",
      futureField: 42,
    });
    fireEvent.click(screen.getByRole("button", { name: "artifact.deliveryLayout.configure" }));
    expect(change.mock.lastCall![0].contentLayout).toEqual({
      version: 1,
      removeGeneratedDirectories: true,
      collapseSingleRootDirectory: false,
    });
  });

  it("preserves explicit legacy null and rejects malformed publication policies", () => {
    const legacy = PublishFileArtifactsUI.parseConfig('{"contentLayout":null}');
    expect(legacy.contentLayout).toBeNull();
    expect(PublishFileArtifactsUI.isValid(legacy)).toBe(true);
    expect(
      PublishFileArtifactsUI.isValid(
        PublishFileArtifactsUI.parseConfig(
          '{"contentLayout":{"removeGeneratedDirectories":"true"}}',
        ),
      ),
    ).toBe(false);
  });

  it("resumes the same publication after a reserve guard without requesting another source", () => {
    const submit = vi.fn();
    const Form = PublishFileArtifactsUI.ResumeForm!;
    render(<Form promptJson={JSON.stringify({
      targetId: "retained", targetDirectory: "/delivery", reason: "StorageUnavailable",
      message: "LOCAL_STORAGE_RESERVE_LOW",
    })} submitting={false} onSubmit={submit} />);
    expect(screen.getByText("cloudDownload.resumeGuide.storageReserve.action")).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.publication.retry" }));
    expect(submit).toHaveBeenCalledExactlyOnceWith(JSON.stringify({
      targetId: "retained", targetDirectory: "/delivery",
    }));
  });

  it("submits a different delivery directory after a conflict without adding an overwrite flag", () => {
    const submit = vi.fn();
    const Form = PublishFileArtifactsUI.ResumeForm!;
    render(
      <Form
        promptJson={JSON.stringify({
          targetId: "main",
          targetDirectory: "/old",
          message: "conflict",
        })}
        submitting={false}
        onSubmit={submit}
      />,
    );
    fireEvent.change(screen.getByLabelText("cloudDownload.publication.directory"), {
      target: { value: "/new" },
    });
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.publication.retry" }));
    expect(JSON.parse(submit.mock.lastCall![0])).toEqual({
      targetId: "main",
      targetDirectory: "/new",
    });
  });

  it("resumes delivery against the current target without replacing a grouped destination", () => {
    const submit = vi.fn();
    const Form = PublishFileArtifactsUI.ResumeForm!;
    render(<Form fileTask submitting={false} onSubmit={submit} promptJson={JSON.stringify({
      targetId: "part", targetDirectory: "/outputs/task/part-b", reason: "StorageUnavailable",
      message: "LOCAL_STORAGE_RESERVE_LOW",
    })} />);
    expect(screen.getByText("/outputs/task/part-b")).toBeVisible();
    fireEvent.click(screen.getByRole("button", {name: "cloudDownload.publication.retry"}));
    expect(submit).toHaveBeenCalledWith(JSON.stringify({targetId: "part"}));
  });

  it("shows delivered file count and the user path separately from internal processing artifacts", () => {
    render(
      <WorksetResults
        json={JSON.stringify([
          {
            targets: [
              {
                id: "one",
                publishedFileCount: 2,
                publishedDirectory: "/library/result",
                publishedArtifactId: "published",
                publicationOperationId: "operation",
              },
            ],
          },
        ])}
      />,
    );
    expect(screen.getByText(/\/library\/result/)).toBeInTheDocument();
    expect(
      screen.getByText("cloudDownload.publication.directoryAtExecution: /library/result"),
    ).toBeVisible();
    expect(screen.getByText("cloudDownload.results.historicalPaths")).toBeVisible();
    expect(screen.getByText("cloudDownload.publication.delivered")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.artifacts.details" }));
    expect(screen.getByText("published")).toBeVisible();
  });

  it("clears an entered password when a different target starts waiting", () => {
    const submit = vi.fn();
    const prompt = (targetId: string) => JSON.stringify({ targetId, reason: "PasswordUnknown" });
    const view = render(
      <ResumeForm promptJson={prompt("first")} submitting={false} onSubmit={submit} />,
    );
    fireEvent.change(screen.getByLabelText("workflow.acquisition.unpack.password"), {
      target: { value: "first-password" },
    });
    view.rerender(
      <ResumeForm promptJson={prompt("second")} submitting={false} onSubmit={submit} />,
    );
    expect(screen.getByLabelText("workflow.acquisition.unpack.password")).toHaveValue("");
    expect(
      screen.getByRole("button", { name: "cloudDownload.processing.continue" }),
    ).toBeDisabled();
    fireEvent.change(screen.getByLabelText("workflow.acquisition.unpack.password"), {
      target: { value: "second-password" },
    });
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.processing.continue" }));
    expect(JSON.parse(submit.mock.calls[0][0])).toEqual({
      targetId: "second",
      password: "second-password",
    });
  });

  it("shows processed outputs separately from the original download manifest", () => {
    render(
      <WorksetResults
        json={JSON.stringify([
          {
            targets: [
              {
                id: "one",
                files: ["/downloads/original.zip"],
                processedDirectory: "/data/file-processing/result",
                processedFiles: ["/data/file-processing/result/readme.txt"],
              },
            ],
          },
        ])}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.files" }));
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.processing.files" }));
    expect(screen.getByText("/downloads/original.zip")).toBeVisible();
    expect(screen.getByText("/data/file-processing/result/readme.txt")).toBeInTheDocument();
    expect(
      screen.getByText(
        "cloudDownload.processing.directoryAtExecution: /data/file-processing/result",
      ),
    ).toBeInTheDocument();
  });

  it("binds nested archive passwords and budget changes to the waiting archive", () => {
    const submit = vi.fn();
    const view = render(
      <ResumeForm
        promptJson={JSON.stringify({
          targetId: "one",
          reason: "PasswordUnknown",
          archiveId: "nested-2",
          archiveName: "inner.rar",
          depth: 1,
        })}
        submitting={false}
        onSubmit={submit}
      />,
    );
    expect(screen.getByText("cloudDownload.processing.recursivePassword")).toBeInTheDocument();
    expect(screen.getByLabelText("workflow.acquisition.unpack.password")).toHaveAttribute("type", "text");
    fireEvent.change(screen.getByLabelText("workflow.acquisition.unpack.password"), {
      target: { value: " 内层 Password! " },
    });
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.processing.continue" }));
    expect(JSON.parse(submit.mock.lastCall![0])).toEqual({
      targetId: "one",
      archiveId: "nested-2",
      password: " 内层 Password! ",
    });
    view.rerender(
      <ResumeForm
        promptJson={JSON.stringify({
          targetId: "one",
          reason: "DepthLimit",
          archiveId: "nested-3",
          recursiveOptions: { maxDepth: 4, maxFiles: 200 },
        })}
        submitting={false}
        onSubmit={submit}
      />,
    );
    expect(screen.getByLabelText("cloudDownload.processing.maxDepth")).toHaveValue(4);
    fireEvent.change(screen.getByLabelText("cloudDownload.processing.maxDepth"), {
      target: { value: "10" },
    });
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.processing.continue" }));
    expect(JSON.parse(submit.mock.lastCall![0])).toMatchObject({
      targetId: "one",
      archiveId: "nested-3",
      recursiveOptions: { maxDepth: 10, maxFiles: 200 },
    });
    expect(JSON.parse(submit.mock.lastCall![0]).recursiveOptions).not.toHaveProperty("enabled");
    view.rerender(
      <ResumeForm
        promptJson={JSON.stringify({ targetId: "one", reason: "RepeatedContent" })}
        submitting={false}
        onSubmit={submit}
      />,
    );
    expect(screen.getByText("cloudDownload.processing.repeatedContent")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "cloudDownload.processing.continue" }),
    ).toBeDisabled();
  });

  it("keeps cleanup disabled by default and preserves explicit source-policy inheritance", () => {
    expect(CleanupArtifactsUI.defaultConfig()).toEqual({ policy: "keep" });
    expect(CleanupArtifactsUI.parseConfig("{}")).toEqual({ policy: "keep" });
    expect(CleanupArtifactsUI.isValid(CleanupArtifactsUI.parseConfig('{"policy":"delete"}'))).toBe(
      true,
    );
    expect(
      CleanupArtifactsUI.parseConfig('{"policy":"keep","useTargetStoragePolicy":true}'),
    ).toEqual({ policy: "keep", useTargetStoragePolicy: true });
    expect(
      CleanupArtifactsUI.isValid({ policy: "keep", useTargetStoragePolicy: "yes" as never }),
    ).toBe(false);
  });

  it("allows ArchiveInputRequired to edit the exact plan without guessing an extension or adding inputs", () => {
    const submit = vi.fn();
    const original = JSON.stringify({ requirement: "required", steps: [] });
    render(
      <ResumeForm
        promptJson={JSON.stringify({
          targetId: "main",
          reason: "ArchiveInputRequired",
          archiveId: "first-archive",
          extractionPlanJson: original,
        })}
        submitting={false}
        onSubmit={submit}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "workflow.processing.editor.advanced" }));
    const input = screen.getByLabelText("workflow.processing.plan");
    expect(input).toHaveValue(original);
    expect(screen.getByText("cloudDownload.processing.archiveInputHint")).toBeVisible();
    expect(
      screen.getByRole("button", { name: "cloudDownload.processing.continue" }),
    ).toBeDisabled();
    const revised = JSON.stringify({
      requirement: "required",
      steps: [
        {
          id: "move-part",
          op: "moveFile",
          input: "download",
          selector: "part1.rar",
          targetDirectory: "volumes",
        },
        {
          id: "extract",
          op: "extractArchive",
          inputs: ["download", "move-part"],
          selector: "volumes/chosen-file.part1.rar",
        },
      ],
    });
    fireEvent.change(input, { target: { value: revised } });
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.processing.continue" }));
    expect(JSON.parse(submit.mock.lastCall![0])).toEqual({
      targetId: "main",
      archiveId: "first-archive",
      extractionPlanJson: revised,
    });
    expect(JSON.parse(submit.mock.lastCall![0])).not.toHaveProperty("files");
  });

  it.each([undefined, 0])(
    "does not label a password request as nested without positive depth (%s)",
    (depth) => {
      render(
        <ResumeForm
          promptJson={JSON.stringify({
            targetId: "one",
            reason: "PasswordUnknown",
            archiveId: "archive-1",
            depth,
          })}
          submitting={false}
          onSubmit={vi.fn()}
        />,
      );
      expect(screen.getByText("cloudDownload.processing.currentPassword")).toBeVisible();
      expect(screen.queryByText("cloudDownload.processing.recursivePassword")).toBeNull();
    },
  );

  it("shows shared operation and artifact identities without losing sibling results and supports retrying restore", async () => {
    cleanup.restore
      .mockResolvedValueOnce({
        code: 0,
        data: { completed: false, status: "Blocked", message: "original path occupied" },
      })
      .mockResolvedValueOnce({ code: 0, data: { completed: true, status: "Restored" } });
    render(
      <WorksetResults
        json={JSON.stringify([
          {
            targets: [
              {
                id: "one",
                title: "Main",
                executionStatus: "reused",
                downloadOperationId: "download-op",
                downloadArtifactId: "download-artifact",
                processingOperationId: "process-op",
                processedArtifactId: "processed-artifact",
                cleanupReceiptId: "receipt-one",
                cleanupStatus: "quarantined",
                processedFiles: ["/outputs/main.txt"],
              },
              {
                id: "two",
                title: "Extras",
                executionStatus: "waitingShared",
                files: ["/downloads/extras.zip"],
              },
            ],
          },
        ])}
      />,
    );
    expect(screen.getByText("cloudDownload.execution.reused")).toBeInTheDocument();
    expect(screen.getByText("cloudDownload.execution.waitingShared")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.artifacts.details" }));
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.files" }));
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.processing.files" }));
    for (const id of ["download-op", "download-artifact", "process-op", "processed-artifact"])
      expect(screen.getByText(id)).toBeInTheDocument();
    expect(cleanup.restore).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.cleanup.restore" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("original path occupied");
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.cleanup.restore" }));
    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent("cloudDownload.cleanup.restored"),
    );
    expect(cleanup.restore.mock.calls).toEqual([["receipt-one"], ["receipt-one"]]);
    expect(screen.getByText("/outputs/main.txt")).toBeInTheDocument();
    expect(screen.getByText("/downloads/extras.zip")).toBeInTheDocument();
  });
});

it("requires confirmation before retrying cleanup with filesystem side effects", () => {
  const Form = CleanupArtifactsUI.ResumeForm!;
  const submit = vi.fn();
  render(<Form promptJson="{}" submitting={false} onSubmit={submit} />);
  expect(screen.getByText("cloudDownload.resumeGuide.cleanup.action")).toBeVisible();
  const retry = screen.getByRole("button", { name: "cloudDownload.resumeGuide.retryCleanup" });
  expect(retry).toBeDisabled();
  fireEvent.click(
    screen.getByRole("checkbox", { name: "cloudDownload.resumeGuide.confirmCleanup" }),
  );
  fireEvent.click(retry);
  expect(submit).toHaveBeenCalledExactlyOnceWith("{}");
});

it("explains incomplete per-file evidence without exposing diagnostics or automatically retrying", () => {
  const Form = CleanupArtifactsUI.ResumeForm!;
  const submit = vi.fn();
  const view = render(<Form
    promptJson={JSON.stringify({message: "FILE_CLEANUP_COVERAGE_INCOMPLETE: /private/license.key"})}
    submitting={false} onSubmit={submit}
  />);
  for (const section of ["why", "action", "next"])
    expect(screen.getByText(`cloudDownload.resumeGuide.cleanupCoverage.${section}`)).toBeVisible();
  expect(screen.queryByText("cloudDownload.cleanup.waitingHint")).toBeNull();
  expect(screen.queryByText(/private\/license/)).toBeNull();
  const retry = screen.getByRole("button", {name: "cloudDownload.resumeGuide.retryCleanup"});
  expect(retry).toBeDisabled();
  expect(submit).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("checkbox", {name: "cloudDownload.resumeGuide.confirmCleanup"}));
  expect(retry).toBeEnabled();
  view.rerender(<Form promptJson={JSON.stringify({message: "FILE_CLEANUP_COVERAGE_INCOMPLETE: another file"})}
    submitting={false} onSubmit={submit} />);
  expect(retry).toBeDisabled();
  expect(submit).not.toHaveBeenCalled();
});


it.each([AssessDeliveryUI, AcquisitionPlanProcessUI])("binds a final archive decision to the exact artifact and review revision ($kind)", activity => {
  const Form = activity.ResumeForm!;
  const onSubmit = vi.fn();
  render(<Form submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
    targetId: "part-a", reason: "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED", deliveryReview: true,
    artifactId: "processed-a", fingerprint: "manifest-a", reviewToken: "revision-a",
    files: ["nested/remaining.zip"], canEditPlan: true, extractionPlanJson: "",
    allowedArchivePolicies: ["instructions", "keep"],
  })} />);
  expect(screen.getByText("nested/remaining.zip")).toBeVisible();
  expect(screen.getByText("cloudDownload.resumeGuide.archiveDelivery.action")).toBeVisible();
  expect(screen.getByRole("button", {name: "cloudDownload.processing.continue"})).toBeDisabled();
  const policy = screen.getByRole("combobox", {name: "cloudDownload.processing.archivePolicy"});
  expect(screen.getByRole("option", {name: "cloudDownload.deliveryReview.keepArchives"})).toHaveValue("keep");
  fireEvent.change(policy, {target: {value: "keep"}});
  expect(onSubmit).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", {name: "cloudDownload.processing.continue"}));
  expect(JSON.parse(onSubmit.mock.calls[0][0])).toEqual({
    artifactId: "processed-a", fingerprint: "manifest-a", reviewToken: "revision-a",
    processing: {targetId: "part-a", archivePolicy: "keep"},
  });
});

it.each([true, false])("shows only server-observed archive headers alongside each reviewed filename (fileTask=%s)", fileTask => {
  const Form = AssessDeliveryUI.ResumeForm!;
  const onSubmit = vi.fn();
  const prompt = { targetId: "part", reason: "FILE_DELIVERY_RESULT_UNCERTAIN", deliveryReview: true,
    artifactId: "artifact", fingerprint: "fp", reviewToken: "same-review", allowedArchivePolicies: ["instructions", "keep"],
    files: ["part.mp4", "clip.mov", "guess.zip", "movie.mp4"] };
  const view = render(<Form fileTask={fileTask} submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({ ...prompt,
    archiveSignatures: { "part.mp4": ".7z", "clip.mov": ".rar", "guess.zip": "probably ZIP", "unlisted.mp4": ".zip" },
  })} />);
  expect(screen.getByText("part.mp4").closest("li")).toHaveTextContent("cloudDownload.deliveryReview.archiveSignature 7z");
  expect(screen.getByText("clip.mov").closest("li")).toHaveTextContent("cloudDownload.deliveryReview.archiveSignature RAR");
  expect(screen.getByText("guess.zip").closest("li")).toHaveTextContent(/^guess\.zip$/);
  expect(screen.getByText("movie.mp4").closest("li")).toHaveTextContent(/^movie\.mp4$/);
  expect(screen.queryByText("unlisted.mp4")).toBeNull();
  view.rerender(<Form fileTask={fileTask} submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify(prompt)} />);
  expect(screen.getByText("part.mp4").closest("li")).toHaveTextContent(/^part\.mp4$/);
  expect(onSubmit).not.toHaveBeenCalled();
});

it("submits only explicit delivery exclusions and retains choices across manifest pages", async () => {
  const firstPage = Array.from({ length: 50 }, (_, i) => ({ relativePath: `media/${i}.jpg`, length: 1, sha256: "abc" }));
  const omitted = ".smbdeleteAAA12345.1";
  cleanup.manifest.mockImplementation(async (_run, id, { pageIndex }) => ({ code: 0, data: {
    artifactId: id, pageIndex, pageSize: 50, state: "available", fileCount: 51,
    entries: pageIndex === 1 ? firstPage : [{ relativePath: omitted, length: 159, sha256: "def" }],
  } }));
  const onSubmit = vi.fn();
  const Form = AssessDeliveryUI.ResumeForm!;
  render(<Form fileTask runId={42} submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
    targetId: "part", reason: "FILE_DELIVERY_RESULT_UNCERTAIN", deliveryReview: true,
    artifactId: "artifact", fingerprint: "fp", reviewToken: "preview", allowedArchivePolicies: ["instructions", "keep"],
  })} />);
  fireEvent.click(screen.getByRole("button", { name: "cloudDownload.deliveryReview.selectFiles" }));
  expect(await screen.findByRole("checkbox", { name: "media/0.jpg" })).toBeChecked();
  const confirm = screen.getByRole("button", { name: "cloudDownload.deliveryReview.confirmSelection" });
  expect(confirm).toBeEnabled();
  fireEvent.click(confirm);
  expect(JSON.parse(onSubmit.mock.calls[0][0])).toEqual({ artifactId: "artifact", fingerprint: "fp", reviewToken: "preview",
    processing: { targetId: "part", archivePolicy: "keep" } });
  onSubmit.mockClear();
  fireEvent.click(screen.getByRole("checkbox", { name: "media/0.jpg" }));
  fireEvent.click(screen.getByRole("button", { name: "next page" }));
  fireEvent.click(await screen.findByRole("checkbox", { name: omitted }));
  expect(onSubmit).not.toHaveBeenCalled();
  fireEvent.click(confirm);
  expect(JSON.parse(onSubmit.mock.calls[0][0])).toEqual({ artifactId: "artifact", fingerprint: "fp", reviewToken: "preview",
    processing: { targetId: "part", archivePolicy: "keep" }, excludedFiles: ["media/0.jpg", omitted] });
});

it("does not allow archive confirmation when the saved review identity is missing", () => {
  const Form = AssessDeliveryUI.ResumeForm!;
  const onSubmit = vi.fn();
  render(<Form submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
    artifactId: "processed-a", fingerprint: "manifest-a", deliveryReview: true,
  })} />);
  expect(screen.getByText("cloudDownload.deliveryReview.reload")).toBeVisible();
  expect(screen.queryByRole("button")).toBeNull();
  expect(onSubmit).not.toHaveBeenCalled();
});


it("keeps unknown sibling files in the final review instead of guessing what to delete", () => {
  const Form = AssessDeliveryUI.ResumeForm!;
  const onSubmit = vi.fn();
  render(<Form submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
    targetId: "part-a", reason: "FILE_DELIVERY_RESULT_UNCERTAIN", deliveryReview: true,
    artifactId: "processed-a", fingerprint: "manifest-a", reviewToken: "revision-a",
    files: ["archive.zip", "license.key"], canEditPlan: true,
    allowedArchivePolicies: ["instructions", "keep"],
  })} />);
  expect(screen.getByText("license.key")).toBeVisible();
  expect(screen.getByText("cloudDownload.deliveryReview.automaticRule")).toBeVisible();
  expect(screen.getByRole("option", {name: "cloudDownload.deliveryReview.keepCurrentFiles"})).toHaveValue("keep");
  expect(onSubmit).not.toHaveBeenCalled();
  fireEvent.change(screen.getByRole("combobox", {name: "cloudDownload.processing.archivePolicy"}), {target: {value: "keep"}});
  fireEvent.click(screen.getByRole("button", {name: "cloudDownload.processing.continue"}));
  expect(JSON.parse(onSubmit.mock.calls[0][0])).toMatchObject({
    artifactId: "processed-a", fingerprint: "manifest-a", reviewToken: "revision-a",
    processing: {archivePolicy: "keep"},
  });
});

it.each([
  ["LLM_DECISION_INVALID: 模型返回的处理方案不是有效 JSON。", "LLM_DECISION_INVALID_JSON"],
  ["LLM_DECISION_INVALID: 模型未返回受支持的处理状态。", "LLM_DECISION_UNSUPPORTED_STATUS"],
  ["LLM_OUTPUT_TRUNCATED: private diagnostic", "LLM_OUTPUT_TRUNCATED"],
  ["LLM_ASSESSMENT_TIMEOUT: private diagnostic", "LLM_ASSESSMENT_TIMEOUT"],
])("shows the specific AI diagnostic in the current processing form for %s", (message, code) => {
  const Form = AssessDeliveryUI.ResumeForm!;
  render(<Form submitting={false} onSubmit={vi.fn()} promptJson={JSON.stringify({
    targetId: "part-a", reason: "FILE_DELIVERY_RESULT_UNCERTAIN", deliveryReview: true,
    artifactId: "processed-a", fingerprint: "manifest-a", reviewToken: "revision-a",
    adaptiveAssessment: true, message,
  })} />);
  expect(screen.getByText(`cloudDownload.problemCode.${code}`)).toBeVisible();
  expect(screen.queryByText("cloudDownload.resumeGuide.archiveDelivery.why")).not.toBeInTheDocument();
  expect(screen.queryByText(message)).not.toBeInTheDocument();
});

it.each([false, true])("exposes the concrete model observation directly in the waiting response (file task %s)", fileTask => {
  const Form = AssessDeliveryUI.ResumeForm!;
  const onSubmit = vi.fn();
  const message = 'The archive identifier differs from the requested release. <script>untrusted text</script>';
  const view = render(<Form fileTask={fileTask} submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
    targetId: "part-a", reason: "FILE_DELIVERY_RESULT_UNCERTAIN", deliveryReview: true,
    artifactId: "processed-a", fingerprint: "manifest-a", reviewToken: "revision-a",
    adaptiveAssessment: true, message,
  })} />);
  expect(screen.getByText(message)).toBeVisible();
  expect(screen.getAllByText("cloudDownload.resumeGuide.archiveDelivery.why")).toHaveLength(1);
  expect(view.container.querySelector("script")).toBeNull();
  expect(onSubmit).not.toHaveBeenCalled();
});

it.each([false, true])("uses a neutral archive category for executor errors without describing them as an AI answer (file task %s)", fileTask => {
  const Form = AssessDeliveryUI.ResumeForm!;
  const message = "Could not open 2.7z. Check the password and all archive parts.";
  render(<Form fileTask={fileTask} submitting={false} onSubmit={vi.fn()} promptJson={JSON.stringify({
    targetId: "part-a", title: "Standalone workflow input", reason: "PasswordUnknown",
    adaptiveAssessment: true, message, deliveryReview: true, artifactId: "current-files",
    fingerprint: "current-fingerprint", reviewToken: "current-review",
  })} />);
  expect(screen.getByText("workflow.metadata.warning.PasswordUnknown")).toBeVisible();
  expect(screen.queryByText(message)).toBeNull();
  expect(screen.getByRole("button", { name: "workflow.processing.problem.details" })).toBeVisible();
  expect(screen.queryByText("Standalone workflow input") !== null).toBe(!fileTask);
});

it("keeps an unfamiliar executor error visible even without an AI assessment flag", () => {
  const Form = ProcessCloudFilesUI.ResumeForm!;
  const message = "A new processor reported an unsupported block layout.";
  render(<Form fileTask submitting={false} onSubmit={vi.fn()} promptJson={JSON.stringify({
    targetId: "part-a", reason: "FutureProcessingFailure", message,
  })} />);
  expect(screen.getByText(message)).toBeVisible();
  expect(screen.queryByRole("button", { name: "workflow.processing.problem.details" })).toBeNull();
});

it("identifies the task in the continuation dialog title while independent workflow forms keep their plain title", () => {
  render(<TaskContinuationModal taskTitle="#529 Example task" stale={false} onClose={vi.fn()}
    files={<p>Current files</p>} />);
  expect(screen.getByRole("dialog", { name: "#529 Example task · acquisition.continue.open" })).toBeVisible();
});

it("answers initial acquisition file instructions with one explicit choice", () => {
  const Form = AcquisitionPlanProcessUI.ResumeForm!;
  const onSubmit = vi.fn();
  render(<Form fileTask promptJson={JSON.stringify({
    targetId: "a", reason: "ExtractionPlanUnknown", canEditPlan: true,
    allowedArchivePolicies: ["instructions", "keep"], extractionPlanJson: '{"requirement":"unknown","steps":[]}',
  })} submitting={false} onSubmit={onSubmit} />);
  const submit = screen.getByRole("button", { name: "cloudDownload.processing.continue" });
  expect(submit).toBeDisabled();
  expect(screen.queryByRole("combobox", { name: "cloudDownload.processing.archivePolicy" })).toBeNull();
  expect(screen.queryByText("workflow.resume.guide.why")).toBeNull();
  fireEvent.click(screen.getByRole("radio", { name: "acquisition.processingChoice.keep" }));
  fireEvent.click(submit);
  expect(JSON.parse(onSubmit.mock.calls[0][0])).toEqual({targetId: "a", archivePolicy: "keep"});
});

it("keeps AI instructions separate from the manual plan and from keeping a reviewed artifact", () => {
  const Form = AssessDeliveryUI.ResumeForm!;
  const onSubmit = vi.fn();
  render(<TaskContinuationModal stale={false} onClose={vi.fn()} current={<Form fileTask promptJson={JSON.stringify({
    targetId: "item", reason: "FILE_DELIVERY_RESULT_UNCERTAIN", allowedArchivePolicies: ["instructions", "keep"],
    artifactId: "artifact", fingerprint: "fp", reviewToken: "token", deliveryReview: true, adaptiveAssessment: true,
    extractionPlanJson: '{"requirement":"required","steps":[{"id":"a","op":"extractArchive"}]}',
  })} submitting={false} onSubmit={onSubmit} />} />);
  const footer = within(screen.getByRole("dialog").querySelector("footer")!);
  const submit = footer.getByRole("button", { name: "cloudDownload.processing.continue" });
  expect(footer.getAllByRole("button")).toHaveLength(2);
  const note = screen.getByRole("textbox", { name: "acquisition.continue.aiNote" });
  expect(submit).toBeDisabled();
  fireEvent.change(note, {target: {value: "unpack split archive"}});
  fireEvent.click(submit);
  const envelope = JSON.parse(onSubmit.mock.calls[0][0]);
  expect(envelope).toEqual({ artifactId: "artifact", fingerprint: "fp", reviewToken: "token",
    processing: { targetId: "item", userContext: "unpack split archive" } });
  fireEvent.click(screen.getByRole("radio", { name: "acquisition.processingChoice.keep" }));
  fireEvent.click(submit);
  expect(JSON.parse(onSubmit.mock.calls[1][0]).processing).toEqual({targetId: "item", archivePolicy: "keep"});
});

it("keeps a publication storage wait and its bound retry in the common footer", () => {
  const Form = PublishFileArtifactsUI.ResumeForm!;
  const onSubmit = vi.fn();
  render(<TaskContinuationModal delivery stale={false} onClose={vi.fn()} current={
    <Form fileTask submitting={false} onSubmit={onSubmit} promptJson={JSON.stringify({
      reason: "StorageUnavailable", message: "LOCAL_STORAGE_RESERVE_LOW", targetId: "part", targetDirectory: "/ssd/Acquired/Task",
    })} />
  } />);
  expect(screen.getByText("/ssd/Acquired/Task")).toBeVisible();
  const footer = within(screen.getByRole("dialog").querySelector("footer")!);
  expect(footer.getAllByRole("button")).toHaveLength(2);
  fireEvent.click(footer.getByRole("button", { name: "cloudDownload.publication.retry" }));
  expect(onSubmit).toHaveBeenCalledExactlyOnceWith('{"targetId":"part"}');
});

it.each([AcquisitionPlanProcessUI, AssessDeliveryUI])("offers the original post once for a legacy password wait in $kind", activity => {
  const Form = activity.ResumeForm!;
  const submit = vi.fn();
  const review = activity === AssessDeliveryUI ? {
    artifactId: "evidence", fingerprint: "verified", reviewToken: "bound", deliveryReview: true,
  } : {};
  const prompt = { targetId: "part", archiveId: "group", reason: "PasswordUnknown", ...review };
  const view = (checked?: boolean, post = true, kind = activity.kind) => (
    <ResumePostSourceContext.Provider value={post}>
      <ResumeActivityContext.Provider value={kind}>
        <Form fileTask submitting={false} onSubmit={submit}
          promptJson={JSON.stringify({ ...prompt, sourcePasswordEvidenceChecked: checked })} />
      </ResumeActivityContext.Provider>
    </ResumePostSourceContext.Provider>
  );
  const { rerender } = render(view());
  const button = () => screen.queryByRole("button", { name: "workflow.processing.checkPostPassword" });
  expect(button()).toBeEnabled();
  fireEvent.click(button()!);
  const action = { targetId: "part", action: "tryPostPasswordEvidence", archiveId: "group" };
  expect(JSON.parse(submit.mock.calls[0][0])).toEqual(activity === AssessDeliveryUI
    ? { artifactId: "evidence", fingerprint: "verified", reviewToken: "bound", processing: action } : action);
  rerender(view(true)); expect(button()).toBeNull();
  rerender(view(undefined, false)); expect(button()).toBeNull();
  rerender(view(false, true, "download.awaitCompletion")); expect(button()).toBeNull();
});
