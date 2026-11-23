import type { ReactNode } from "react";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import PlanTaskRow from "../PlanTaskRow";
import type { PlanTask } from "../models";
import { AcquisitionStatus } from "@/sdk/constants";
import type { RuntimeOverview } from "../runtimeOverview";
import { createInstance } from "i18next";
import { acquisitionStageLabel } from "../../taskStage";
import cn from "@/locales/cn/pages/acquisition.json";
import en from "@/locales/en/pages/acquisition.json";

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
    overview ? <p>{overview.stage ?? overview.problem}</p> : null,
}));

it("does not present the original plan directory as a current task location", () => {
  render(<PlanTaskRow task={{ id: 2, title: "Task", status: AcquisitionStatus.Waiting,
    targetDirectory: "/nas/original-plan" } as PlanTask} onOpen={vi.fn()} />);
  expect(screen.queryByText(/original-plan|acquisition.plan.historicalDirectory/)).toBeNull();
});

it("distinguishes a draft waiting for data preparation from an actively preparing search", () => {
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

it.each(["cn", "en"] as const)("labels preparation consistently in the %s task status and saved message", async language => {
  const i18n = createInstance();
  await i18n.init({ lng: language, resources: { cn: { translation: cn }, en: { translation: en } } });
  const stage = acquisitionStageLabel(i18n.t, { status: AcquisitionStatus.Waiting, builderId: "draft", phase: "preparing", stage: "preparing" });
  expect(stage).toBe(language === "cn" ? "准备数据" : "Preparing data");
  expect(i18n.t("acquisition.plan.phase.preparing")).toBe(stage);
  expect(i18n.t("acquisition.plan.phase.draft")).toBe(stage);
  expect(i18n.t("acquisition.draft.saved")).toContain(language === "cn" ? "等待准备数据" : "Waiting to prepare data");
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

it("shows both the registered file condition and the current reason in the task's tag list", () => {
  render(<PlanTaskRow task={{ id: 4, status: AcquisitionStatus.Waiting,
    attentionTags: ["localStorage"] } as PlanTask}
    facts={["spaceInsufficient"]} onOpen={vi.fn()} />);
  const tags = within(screen.getByRole("list", { name: "acquisition.filters.title" }));
  expect(tags.getByText("acquisition.attention.tag.localStorage")).toBeVisible();
  expect(tags.getByText("acquisition.filters.fact.spaceInsufficient")).toBeVisible();
});

it("updates the file release badge when a refreshed search returns new task facts", () => {
  const task = { id: 4, status: AcquisitionStatus.Completed } as PlanTask;
  const open = vi.fn();
  const row = render(<PlanTaskRow task={task} facts={["awaitingFileRelease"]} onOpen={open} />);
  const tags = within(screen.getByRole("list", { name: "acquisition.filters.title" }));
  expect(tags.getByText("acquisition.filters.fact.awaitingFileRelease")).toBeVisible();

  row.rerender(<PlanTaskRow task={task} facts={[]} onOpen={open} />);
  expect(screen.queryByText("acquisition.filters.fact.awaitingFileRelease")).toBeNull();
});

it("offers a new cleanup confirmation only after automatic cleanup requires review", () => {
  const task = { id: 5, status: AcquisitionStatus.Completed } as PlanTask;
  const open = vi.fn(), openDeletion = vi.fn();
  const card = (facts: string[]) => <PlanTaskRow task={task} facts={facts} onOpen={open} onDelete={openDeletion} />;
  const review = "acquisition.filters.fact.fileCleanupNeedsReview";
  const waiting = "acquisition.filters.fact.awaitingFileRelease";
  const row = render(card([]));
  expect(screen.queryByText(review)).toBeNull();
  row.rerender(card(["awaitingFileRelease", "fileCleanupNeedsReview"]));
  expect(screen.getByText(waiting)).toBeVisible();
  expect(screen.queryByText(review)).toBeNull();
  row.rerender(card(["fileCleanupNeedsReview"]));
  expect(screen.queryByText(waiting)).toBeNull();
  expect(openDeletion).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: review }));
  expect(openDeletion).toHaveBeenCalledOnce();
  expect(open).not.toHaveBeenCalled();
});

it("checks the revision and nullable run before showing an active confirmation on a ready row", () => {
  const task = { id: 2, phase: "ready", builderId: "prepared", planRevision: 4, status: AcquisitionStatus.Waiting } as PlanTask;
  const confirmation = { taskId: 2, planRevision: 4,
    progressScope: "confirmation", stage: "verifyingFiles" } as RuntimeOverview;
  const view = render(<PlanTaskRow task={task} overview={confirmation} onOpen={vi.fn()} />);
  expect(screen.getByText("verifyingFiles")).toBeVisible();
  view.rerender(<PlanTaskRow task={{ ...task, planRevision: 5 }} overview={confirmation} onOpen={vi.fn()} />);
  expect(screen.queryByText("verifyingFiles")).toBeNull();
  view.rerender(<PlanTaskRow task={{ ...task, workflowRunId: 11 }} overview={confirmation} onOpen={vi.fn()} />);
  expect(screen.queryByText("verifyingFiles")).toBeNull();
});
