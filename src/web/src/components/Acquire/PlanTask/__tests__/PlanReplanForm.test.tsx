import type { ReactNode } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import PlanReplanForm from "../PlanReplanForm";
import type { PlanTaskDetail } from "../models";
import { AcquisitionStatus } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ replan: vi.fn() }));
vi.mock("../api", () => ({ planTaskApi: mocks }));
vi.mock("@/components/bakaui", () => ({
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
  Checkbox: ({
    children,
    isSelected,
    onValueChange,
    isDisabled,
  }: {
    children: ReactNode;
    isSelected: boolean;
    onValueChange: (value: boolean) => void;
    isDisabled?: boolean;
  }) => (
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
  Select: () => null,
}));
const plan = {
  requirement: "required",
  evidence: [],
  steps: [{ id: "extract", op: "extractArchive", input: "download" }],
};
const detail: PlanTaskDetail = {
  stageAttempts: [],
  canEditDeliverySettings: false,
  canEditStoragePolicy: false,
  id: 4,
  task: {
    id: 4,
    planRevision: 3,
    workflowRunId: 100,
    status: AcquisitionStatus.Failed,
    stage: "attention",
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
    parts: [{ id: "main", sources: [], processing: plan }],
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
      artifactId: "available-artifact",
      rootDirectory: "/downloads/observed",
      fileCount: 4,
      kind: "download",
      fingerprint: "fingerprint-main",
    },
    {
      partId: "other",
      artifactId: "other-artifact",
      rootDirectory: "/downloads/other",
      fileCount: 1,
      kind: "download",
      fingerprint: "fingerprint-other",
    },
  ],
};
beforeEach(() => {
  vi.clearAllMocks();
  mocks.replan.mockResolvedValue({
    ...detail,
    task: { ...detail.task, phase: "ready", workflowRunId: null },
  });
});

it("requires selected evidence, records a reason automatically, preserves all inputs and only creates a ready revision", async () => {
  const saved = vi.fn();
  const { rerender } = render(
    <PlanReplanForm detail={detail} onSaved={saved} onCancel={vi.fn()} />,
  );
  const confirm = screen.getByRole("button", { name: "acquisition.plan.replan.confirm" });
  expect(confirm).toBeDisabled();
  expect(screen.queryByText("/downloads/other")).not.toBeInTheDocument();
  const revised = {
    ...plan,
    steps: [
      {
        id: "remove-mark",
        op: "renameLiteral",
        input: "download",
        findText: "remove-me",
        replaceText: "",
      },
      { id: "move", op: "moveFile", input: "download", targetDirectory: "parts" },
      { id: "extract", op: "extractArchive", inputs: ["download", "move"] },
    ],
  };
  fireEvent.click(screen.getByRole("button", { name: "workflow.processing.editor.advanced" }));
  fireEvent.change(screen.getByLabelText("workflow.processing.plan"), {
    target: { value: JSON.stringify(revised) },
  });
  expect(confirm).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox"));
  // Refreshing the parent cannot silently switch the revision being confirmed.
  rerender(
    <PlanReplanForm
      detail={{ ...detail, task: { ...detail.task, planRevision: 4 } }}
      onSaved={saved}
      onCancel={vi.fn()}
    />,
  );
  fireEvent.click(confirm);
  await waitFor(() =>
    expect(mocks.replan).toHaveBeenCalledExactlyOnceWith(4, {
      expectedRevision: 3,
      expectedRunId: 100,
      reason: "acquisition.plan.replan.title",
      items: [{ partId: "main", plan: revised, artifactEvidenceIds: ["available-artifact"] }],
    }),
  );
  await waitFor(() =>
    expect(saved).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({
        task: expect.objectContaining({ phase: "ready", workflowRunId: null }),
      }),
    ),
  );
});

it("does not submit without usable evidence", () => {
  render(
    <PlanReplanForm
      detail={{ ...detail, replanArtifacts: [] }}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  expect(screen.getByText("acquisition.plan.replan.noEvidence")).toBeVisible();
  expect(screen.getByRole("button", { name: "acquisition.plan.replan.confirm" })).toBeDisabled();
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("retains the draft after a conflict and does not render raw server errors", async () => {
  mocks.replan.mockRejectedValue(new Error("private-server-error-canary"));
  const saved = vi.fn();
  render(<PlanReplanForm detail={detail} onSaved={saved} onCancel={vi.fn()} />);
  fireEvent.click(screen.getByRole("checkbox"));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.plan.replan.confirm" }));
  await screen.findByRole("alert");
  expect(document.body).not.toHaveTextContent("private-server-error-canary");
  fireEvent.click(screen.getByRole("button", { name: "workflow.processing.editor.advanced" }));
  expect(screen.getByLabelText("workflow.processing.plan")).toHaveValue(
    JSON.stringify(plan, null, 2),
  );
  expect(saved).not.toHaveBeenCalled();
});

const localDetail: PlanTaskDetail = {
  ...detail,
  plan: {
    ...detail.plan!,
    parts: [
      {
        ...detail.plan!.parts[0],
        continueFrom: {
          taskId: 4,
          previousRunId: 80,
          artifactId: "available-artifact",
          operationId: "earlier-operation",
          fingerprint: "fingerprint-main",
        },
      },
    ],
  },
};

it("keeps the existing earlier-run local input selected and submits it explicitly when only editing processing", async () => {
  render(<PlanReplanForm detail={localDetail} onSaved={vi.fn()} onCancel={vi.fn()} />);
  const input = screen.getByRole("checkbox");
  expect(input).toBeChecked();
  expect(input).toBeDisabled();
  expect(screen.getByText("acquisition.plan.continueFiles")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.plan.replan.confirm" }));
  await waitFor(() =>
    expect(mocks.replan).toHaveBeenCalledExactlyOnceWith(4, {
      expectedRevision: 3,
      expectedRunId: 100,
      reason: "acquisition.plan.replan.title",
      items: [
        {
          partId: "main",
          plan,
          artifactEvidenceIds: ["available-artifact"],
          continueFromArtifactId: "available-artifact",
        },
      ],
    }),
  );
});

it.each(["missing", "changed"])(
  "does not turn a %s retained local input into a source download",
  (fault) => {
    render(
      <PlanReplanForm
        detail={{
          ...localDetail,
          replanArtifacts:
            fault === "missing"
              ? []
              : localDetail.replanArtifacts.map((a) => ({
                  ...a,
                  fingerprint: "changed-fingerprint",
                })),
        }}
        onSaved={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(
      screen.getByText("acquisition.plan.replan.error.PLAN_REPLAN_EVIDENCE_UNAVAILABLE"),
    ).toBeVisible();
    expect(screen.getByRole("button", { name: "acquisition.plan.replan.confirm" })).toBeDisabled();
    expect(mocks.replan).not.toHaveBeenCalled();
  },
);

it("opens the explicit local-only revision without submitting, and returns to the original draft", () => {
  render(<PlanReplanForm detail={detail} onSaved={vi.fn()} onCancel={vi.fn()} />);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.plan.continueStopped.title" }));
  expect(
    screen.getByRole("region", { name: "acquisition.plan.continueStopped.title" }),
  ).toBeVisible();
  expect(
    screen.getByRole("checkbox", { name: /acquisition.plan.continueCompleted.useCurrentStorage/ }),
  ).not.toBeChecked();
  expect(screen.getByRole("button", { name: "acquisition.plan.replan.confirm" })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.plan.continueStopped.back" }));
  expect(screen.queryByLabelText("acquisition.plan.replan.reason")).not.toBeInTheDocument();
  expect(mocks.replan).not.toHaveBeenCalled();
});

it("does not offer local-only revision when a part has no input files", () => {
  render(
    <PlanReplanForm
      detail={{
        ...detail,
        plan: {
          ...detail.plan!,
          parts: [...detail.plan!.parts, { id: "missing", sources: [], processing: plan }],
        },
      }}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  expect(
    screen.queryByRole("button", { name: "acquisition.plan.continueStopped.title" }),
  ).toBeNull();
});

it.each([AcquisitionStatus.Pending, AcquisitionStatus.Running, AcquisitionStatus.Waiting])(
  "does not offer local-only revision for active or waiting status %s",
  (status) => {
    render(
      <PlanReplanForm
        detail={{ ...detail, task: { ...detail.task, status } }}
        onSaved={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(
      screen.queryByRole("button", { name: "acquisition.plan.continueStopped.title" }),
    ).toBeNull();
  },
);
