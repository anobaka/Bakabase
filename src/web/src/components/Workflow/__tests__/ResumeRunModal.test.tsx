import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import ResumeRunModal, { ResumeRunContent } from "../ResumeRunModal";
import { ResumeSubmissionContext } from "../ResumeSubmissionContext";

const api = vi.hoisted(() => ({ definition: vi.fn(), resume: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: { workflow: { getWorkflow: api.definition, resumeWorkflowRun: api.resume } },
}));
vi.mock("../Activities", () => ({
  getWorkflowActivityUI: (kind: string) =>
    kind === "unknown.activity"
      ? undefined
      : kind === "known.acknowledge"
        ? {}
        : { ResumeForm: () => <p>Form: {kind}</p> },
}));
vi.mock("../displayNames", () => ({ activityDisplayName: (_t: unknown, kind: string) => kind }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, onOk }: any) => (
    <div>
      {children}
      {onOk && <button onClick={onOk}>Generic continue</button>}
    </div>
  ),
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
  Spinner: () => <span>Resolving node</span>,
  toast: { success: vi.fn() },
}));
beforeEach(() => {
  api.definition.mockReset();
  api.resume.mockReset();
});

it.each([true, false])("uses only the matching run's owner submission and reports accepted=%s", async accepted => {
  const submit = vi.fn().mockResolvedValue(accepted);
  const resumed = vi.fn();
  render(<ResumeSubmissionContext.Provider value={{ runId: 30, submit }}>
    <ResumeRunContent runId={30} workflowDefinitionId={0} activityKind="known.acknowledge" onResumed={resumed} />
  </ResumeSubmissionContext.Provider>);
  fireEvent.click(screen.getByRole("button", { name: "workflow.resume.continue" }));
  await waitFor(() => expect(submit).toHaveBeenCalledExactlyOnceWith("{}"));
  expect(api.resume).not.toHaveBeenCalled();
  if (accepted) expect(resumed).toHaveBeenCalledOnce();
  else expect(resumed).not.toHaveBeenCalled();
});

it("does not submit another run's answer to a task owner's handler", async () => {
  const submit = vi.fn();
  api.resume.mockResolvedValue({ code: 0 });
  render(<ResumeSubmissionContext.Provider value={{ runId: 31, submit }}>
    <ResumeRunContent runId={30} workflowDefinitionId={0} activityKind="known.acknowledge" />
  </ResumeSubmissionContext.Provider>);
  fireEvent.click(screen.getByRole("button", { name: "workflow.resume.continue" }));
  await waitFor(() => expect(api.resume).toHaveBeenCalledExactlyOnceWith(30, { signalJson: "{}" }));
  expect(submit).not.toHaveBeenCalled();
});

it("uses the frozen activity identity instead of the edited definition when resuming", () => {
  render(
    <ResumeRunModal
      runId={30}
      workflowDefinitionId={4}
      currentStepIndex={2}
      activityKind="download.processFiles"
    />,
  );
  expect(screen.getByText("Form: download.processFiles")).toBeVisible();
  expect(api.definition).not.toHaveBeenCalled();
});

it("supports the old-definition fallback without accepting its late result after a frozen run is selected", async () => {
  let finish!: (response: unknown) => void;
  api.definition.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const view = render(<ResumeRunModal runId={1} workflowDefinitionId={4} currentStepIndex={0} />);
  expect(screen.queryByText("Generic continue")).toBeNull();
  view.rerender(
    <ResumeRunModal
      runId={114}
      workflowDefinitionId={4}
      currentStepIndex={0}
      activityKind="fs.processArtifacts"
    />,
  );
  await act(async () => finish({ data: { activities: [{ kind: "wrong.new.definition" }] } }));
  expect(screen.getByText("Form: fs.processArtifacts")).toBeVisible();
  expect(screen.queryByText("Form: wrong.new.definition")).toBeNull();
});

it("does not offer an empty generic response when the legacy definition cannot be loaded", async () => {
  api.definition
    .mockRejectedValueOnce(new Error("Unavailable"))
    .mockResolvedValueOnce({ code: 0, data: { activities: [{ kind: "download.processFiles" }] } });
  render(<ResumeRunModal runId={1} workflowDefinitionId={4} currentStepIndex={0} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("workflow.resume.activityUnavailable");
  expect(screen.queryByText("Generic continue")).toBeNull();
  fireEvent.click(screen.getByText("common.action.retry"));
  expect(await screen.findByText("Form: download.processFiles")).toBeVisible();
});

it("blocks empty responses for an unknown frozen node and keeps diagnostic fields collapsed", () => {
  render(
    <ResumeRunModal
      runId={677}
      workflowDefinitionId={4}
      currentStepIndex={3}
      activityKind="unknown.activity"
      waitReason="cloud.cloudAttention"
    />,
  );
  expect(screen.getByRole("alert")).toHaveTextContent("workflow.resume.guide.unsupported");
  expect(screen.queryByText("Generic continue")).toBeNull();
  expect(screen.queryByText("cloud.cloudAttention")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "workflow.resume.guide.details" }));
  expect(screen.getByText("cloud.cloudAttention")).toBeVisible();
});

it("does not allow a generic empty response when no historical node can be identified", () => {
  render(<ResumeRunModal runId={1} workflowDefinitionId={4} />);
  expect(screen.getByRole("alert")).toHaveTextContent("workflow.resume.guide.unsupported");
  expect(screen.queryByText("Generic continue")).toBeNull();
});

it("acknowledges a known waiting node inside its host panel only after explicit submission", async () => {
  api.resume.mockResolvedValue({ code: 0 });
  const onResumed = vi.fn();
  const onClose = vi.fn();
  render(
    <ResumeRunContent
      runId={77}
      workflowDefinitionId={0}
      activityKind="known.acknowledge"
      onResumed={onResumed}
      onClose={onClose}
    />,
  );
  expect(api.resume).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "workflow.resume.continue" }));
  await waitFor(() => expect(api.resume).toHaveBeenCalledExactlyOnceWith(77, { signalJson: "{}" }));
  expect(onResumed).toHaveBeenCalledTimes(1);
  expect(onClose).toHaveBeenCalledTimes(1);
});

it("leaves the run waiting when the embedded form is dismissed", () => {
  const onClose = vi.fn();
  render(
    <ResumeRunContent
      runId={77}
      workflowDefinitionId={0}
      activityKind="known.acknowledge"
      onClose={onClose}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "workflow.resume.guide.leaveWaiting" }));
  expect(onClose).toHaveBeenCalledTimes(1);
  expect(api.resume).not.toHaveBeenCalled();
});
