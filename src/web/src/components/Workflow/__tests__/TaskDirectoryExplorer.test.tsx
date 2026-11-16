import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import TaskDirectoryExplorer, { isTaskDirectoryPath } from "../TaskDirectoryExplorer";
import { taskDirectoryAccess } from "../useTaskDirectoryAccess";
import { AcquisitionStatus } from "@/sdk/constants";
import { managedFileCapabilities } from "@/components/FileExplorer/models";

const mocks = vi.hoisted(() => ({ get: vi.fn(), runtime: vi.fn(), explorer: vi.fn(), refresh: vi.fn(), mounted: vi.fn(), unmounted: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: {
  getAcquisitionPlanTask: mocks.get, getAcquisitionRuntimeOverviews: mocks.runtime,
} } }));
vi.mock("@/components/FileExplorer/FileExplorer", async () => {
  const { forwardRef, useImperativeHandle, useEffect } = await import("react");
  return { default: forwardRef((props: any, ref) => {
    useEffect(() => { mocks.mounted(); return () => mocks.unmounted(); }, []);
    mocks.explorer(props); useImperativeHandle(ref, () => ({ root: { expand: mocks.refresh } }));
    return <div>{props.selectable === "multiple" ? "editable browser" : "readonly browser"}</div>;
  }) };
});
vi.mock("@/components/bakaui", () => ({ Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>, Spinner: () => <span>loading</span>,
  Tooltip: ({ children, content }: any) => <span data-testid="operation-help" data-help={content.props.children}>{children}</span>,
}));
vi.mock("@/components/FileExplorer/components/DirectoryTaskStatus", () => ({ default: ({ path }: any) => <div>tasks for {path}</div> }));
const context = { taskId: 865, runId: 22 };
const detail = () => ({ task: { id: 865, workflowRunId: 22, status: AcquisitionStatus.Waiting },
  stageDirectories: { taskId: 865, parts: { p: { processing: { directory: "/work/task" } } } }, replanArtifacts: [] } as any);
const runtime = () => ({ taskId: 865, workflowRunId: 22, workflowStatus: "waiting", activeDownloadCount: 0 } as any);
beforeEach(() => { vi.clearAllMocks(); mocks.get.mockResolvedValue({ data: detail() }); mocks.runtime.mockResolvedValue({ data: [runtime()] }); });
afterEach(() => vi.useRealTimers());

it("keeps current task navigation inside its selected root", () => {
  expect(isTaskDirectoryPath("/work/task", "/work/task/sub" )).toBe(true);
  for (const path of [undefined, "/work", "/work/task2", "/work/task/../other"])
    expect(isTaskDirectoryPath("/work/task", path)).toBe(false);
});
it("allows the current completed delivery and a server-qualified file input, but not unqualified snapshots", () => {
  const task = detail(), state = runtime();
  task.task.status = AcquisitionStatus.Completed; state.workflowStatus = "success";
  expect(taskDirectoryAccess(task, state, context, "/work/task")).toBe("editable");
  task.stageDirectories.parts = {};
  task.replanArtifacts = [{ rootDirectory: "/work/input", canReadCurrentDirectory: false }];
  expect(taskDirectoryAccess(task, state, context, "/work/input")).toBe("historical");
  task.replanArtifacts[0].canReadCurrentDirectory = true;
  expect(taskDirectoryAccess(task, state, context, "/work/input")).toBe("editable");
});

it.each(["running", "pending", "queued-download", "active-download", "other-run", "deleted", "unbound", "unknown-status"])("keeps %s task directories read-only", change => {
  const task = detail(), state = runtime();
  if (change === "running" || change === "pending") state.workflowStatus = change;
  if (change === "queued-download") state.downloadActivity = "queued";
  if (change === "active-download") state.activeDownloadCount = 1;
  if (change === "other-run") task.task.workflowRunId = 23;
  if (change === "deleted") task.task.deletedAt = "now";
  if (change === "unbound") task.stageDirectories.parts = {};
  if (change === "unknown-status") state.workflowStatus = "unknown";
  expect(taskDirectoryAccess(task, state, context, "/work/task")).not.toBe("editable");
});
it.each([undefined, 288])("enables existing file operations and scoped shortcuts at height %s without rewriting history", async maxContentHeight => {
  render(<TaskDirectoryExplorer path="/work/task" context={context} maxContentHeight={maxContentHeight} />);
  expect(await screen.findByText("editable browser")).toBeVisible();
  expect(mocks.explorer.mock.lastCall?.[0]).toMatchObject({ rootPath: "/work/task", maxContentHeight,
    capabilities: managedFileCapabilities, keyboard: true, watchChanges: "poll", selectable: "multiple" });
  expect(screen.getByText("tasks for /work/task")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "workflow.files.taskDirectory.refresh" }));
  expect(mocks.refresh).toHaveBeenCalledWith(true);
  expect(mocks.get).toHaveBeenCalledWith(865, expect.objectContaining({ sensitiveResponse: true }));
});
it("revokes operations when a linked download starts or a state refresh fails", async () => {
  render(<TaskDirectoryExplorer path="/work/task" context={context} />);
  await screen.findByText("editable browser");
  mocks.runtime.mockResolvedValue({ data: [{ ...runtime(), downloadActivity: "running" }] });
  act(() => window.dispatchEvent(new Event("focus")));
  expect(await screen.findByText("workflow.files.taskDirectory.active")).toBeVisible();
  expect(mocks.explorer.mock.lastCall?.[0]).toMatchObject({ keyboard: false, watchChanges: "poll", selectable: "disabled", capabilities: ["enter-directory"] });
  mocks.runtime.mockRejectedValue(new Error("offline"));
  act(() => window.dispatchEvent(new Event("focus")));
  await waitFor(() => expect(screen.getByText("workflow.files.taskDirectory.unavailable")).toBeVisible());
  expect(mocks.explorer.mock.lastCall?.[0]).toMatchObject({ watchChanges: false });
  expect(screen.queryByText("tasks for /work/task")).toBeNull();
});

it("does not watch an unbound historical directory while the current run is active", async () => {
  mocks.runtime.mockResolvedValue({ data: [{ ...runtime(), workflowStatus: "running" }] });
  render(<TaskDirectoryExplorer path="/work/old-result" context={context} maxContentHeight={288} />);
  await screen.findByText("workflow.files.taskDirectory.historical");
  expect(mocks.explorer.mock.lastCall?.[0]).toMatchObject({ watchChanges: false, selectable: "disabled" });
  expect(screen.queryByText("tasks for /work/old-result")).toBeNull();
});

it("places the input heading and concise file actions in one wrapping header without repeating the full instructions", async () => {
  render(<TaskDirectoryExplorer path="/work/task" context={context} maxContentHeight={288}
    heading={<h4>Current input files</h4>} />);
  await screen.findByText("editable browser");
  const help = screen.getByTestId("operation-help");
  expect(help).toHaveAttribute("data-help", "workflow.files.taskDirectory.editable");
  expect(screen.queryByText("workflow.files.taskDirectory.editable")).toBeNull();
  expect(screen.getByRole("button", { name: "workflow.files.taskDirectory.compact.editable" })).toBeVisible();
  expect(screen.getByRole("heading", { name: "Current input files" }).parentElement).toContainElement(help);
  expect(help.parentElement).toHaveClass("flex-wrap");
});

it("keeps the same explorer mounted when initial permission checks finish and later revoke editing", async () => {
  let complete!: (value: any) => void;
  mocks.get.mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
  render(<TaskDirectoryExplorer path="/work/task" context={context} />);
  expect(await screen.findByText("readonly browser")).toBeVisible();
  expect(mocks.mounted).toHaveBeenCalledOnce();
  await act(async () => complete({ data: detail() }));
  expect(await screen.findByText("editable browser")).toBeVisible();
  expect(mocks.mounted).toHaveBeenCalledOnce();
  expect(mocks.unmounted).not.toHaveBeenCalled();
  mocks.runtime.mockResolvedValue({ data: [{ ...runtime(), workflowStatus: "running" }] });
  act(() => window.dispatchEvent(new Event("focus")));
  expect(await screen.findByText("readonly browser")).toBeVisible();
  expect(mocks.mounted).toHaveBeenCalledOnce();
  expect(mocks.unmounted).not.toHaveBeenCalled();
});
