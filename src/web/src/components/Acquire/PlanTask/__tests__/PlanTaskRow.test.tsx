import type { ReactNode } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import PlanTaskRow from "../PlanTaskRow";
import type { PlanTask } from "../models";
import { AcquisitionStatus } from "@/sdk/constants";
import type { RuntimeOverview } from "../runtimeOverview";

vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, color, "aria-label": label }: any) => <button onClick={onPress} data-color={color} aria-label={label}>{children}</button>,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Tooltip: ({ children }: { children: ReactNode }) => children,
}));

it.each([AcquisitionStatus.Failed, AcquisitionStatus.Completed])("offers a distinct danger-colored delete action for status %s without opening or changing the task", status => {
  const open = vi.fn(), remove = vi.fn();
  render(<PlanTaskRow task={{ id: 865, title: "Content", status } as PlanTask}
    onOpen={open} onDelete={remove} />);
  const button = screen.getByRole("button", { name: "acquisition.actionbar.delete #865" });
  expect(button).toHaveAttribute("data-color", "danger");
  fireEvent.click(button);
  expect(remove).toHaveBeenCalledOnce();
  expect(open).not.toHaveBeenCalled();
});
vi.mock("../../AcquisitionTiming", () => ({ default: () => null }));
vi.mock("../RuntimeOverviewSummary", () => ({
  default: ({ overview }: { overview?: RuntimeOverview }) =>
    overview ? <p>{overview.problem}</p> : null,
}));

it("does not present the original plan directory as a current task location", () => {
  render(<PlanTaskRow task={{ id: 2, title: "Task", status: AcquisitionStatus.Waiting,
    targetDirectory: "/nas/original-plan" } as PlanTask} onOpen={vi.fn()} />);
  expect(screen.queryByText(/original-plan|acquisition.plan.historicalDirectory/)).toBeNull();
});

it("distinguishes an incomplete saved draft from an actively preparing search", () => {
  const task = {
    id: 1,
    title: "Saved task",
    builderId: "draft",
    phase: "preparing",
    stage: "preparing",
    status: AcquisitionStatus.Waiting,
    planRevision: 1,
  } as PlanTask;
  const view = render(<PlanTaskRow task={task} onOpen={vi.fn()} />);
  expect(screen.getByText(/acquisition.draft.incomplete/)).toBeVisible();
  expect(screen.queryByText(/acquisition.plan.phase.preparing/)).toBeNull();

  view.rerender(<PlanTaskRow task={{ ...task, builderId: "exhentai.search" }} onOpen={vi.fn()} />);
  expect(screen.getByText(/acquisition.plan.phase.preparing/)).toBeVisible();
  expect(screen.queryByText(/acquisition.draft.incomplete/)).toBeNull();
});

it("does not show previous-run diagnostics after a new execution has been created", () => {
  const task = {
    id: 2,
    title: "Retried task",
    workflowRunId: 11,
    builderId: "prepared",
    status: AcquisitionStatus.Running,
  } as PlanTask;
  const overview = {
    taskId: 2,
    workflowRunId: 10,
    problem: "Old failed source",
  } as RuntimeOverview;
  const view = render(<PlanTaskRow task={task} overview={overview} onOpen={vi.fn()} />);
  expect(screen.queryByText("Old failed source")).toBeNull();
  view.rerender(
    <PlanTaskRow
      task={task}
      overview={{ ...overview, workflowRunId: 11, problem: "Current source issue" }}
      onOpen={vi.fn()}
    />,
  );
  expect(screen.getByText("Current source issue")).toBeVisible();
});

it("replaces an old source-failure tag with the current runtime classification", () => {
  const task = {
    id: 2, title: "Downloads recovered", workflowRunId: 11,
    builderId: "prepared", status: AcquisitionStatus.Waiting,
    attentionTags: ["sourceUnavailable"],
  } as PlanTask;
  const view = render(<PlanTaskRow task={task} onOpen={vi.fn()} overview={{
    taskId: 2, workflowRunId: 11, attentionTags: [],
    downloadsCompletedAwaitingWorkflow: true, downloadContinuation: "downloadsOnly",
  } as unknown as RuntimeOverview} />);
  expect(screen.queryByText("acquisition.attention.tag.sourceUnavailable")).toBeNull();
  view.rerender(<PlanTaskRow task={task} onOpen={vi.fn()} overview={{
    taskId: 2, workflowRunId: 10, attentionTags: [],
  } as unknown as RuntimeOverview} />);
  expect(screen.getByText("acquisition.attention.tag.sourceUnavailable")).toBeVisible();
});

it.each(["running", "queued"])("shows %s activity instead of the old attention phase while preserving waiting status", (activity) => {
  const task = { id: 4, workflowRunId: 12, title: "Active download", builderId: "prepared",
    phase: "attention", stage: "attention", status: AcquisitionStatus.Waiting, attentionTags: ["localStorage"] } as PlanTask;
  render(<PlanTaskRow task={task} onOpen={vi.fn()} overview={{
    taskId: 4, workflowRunId: 12, downloadActivity: activity, attentionTags: [],
  } as unknown as RuntimeOverview} />);
  expect(screen.getByText(`acquisition.downloadActivity.${activity}`)).toBeVisible();
  expect(screen.getByText(new RegExp(`acquisition.downloadActivity.${activity}Hint`))).toBeVisible();
  expect(screen.queryByText(/acquisition.plan.phase.attention/)).toBeNull();
  expect(screen.queryByText("acquisition.attention.tag.localStorage")).toBeNull();
  expect(screen.getByRole("button", { name: "acquisition.plan.open" })).toBeVisible();
  expect(task.status).toBe(AcquisitionStatus.Waiting);
});

it("retains another part's current attention beside the active download status", () => {
  const task = { id: 4, workflowRunId: 12, status: AcquisitionStatus.Waiting } as PlanTask;
  render(<PlanTaskRow task={task} onOpen={vi.fn()} overview={{
    taskId: 4, workflowRunId: 12, downloadActivity: "running", attentionTags: ["sourceUnavailable"],
    problem: "Another required part needs attention",
  } as unknown as RuntimeOverview} />);
  expect(screen.getByText("acquisition.downloadActivity.running")).toBeVisible();
  expect(screen.getByText("acquisition.attention.tag.sourceUnavailable")).toBeVisible();
  expect(screen.getByText("Another required part needs attention")).toBeVisible();
});
