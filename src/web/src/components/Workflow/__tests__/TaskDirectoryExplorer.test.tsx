import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import TaskDirectoryExplorer, { isTaskDirectoryPath } from "../TaskDirectoryExplorer";
import { taskDirectoryAccess } from "../useTaskDirectoryAccess";
import { AcquisitionStatus } from "@/sdk/constants";
import { managedFileCapabilities } from "@/components/FileExplorer/models";

const mocks = vi.hoisted(() => ({ get: vi.fn(), runtime: vi.fn(), explorer: vi.fn(), refresh: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: {
  getAcquisitionPlanTask: mocks.get, getAcquisitionRuntimeOverviews: mocks.runtime,
} } }));
vi.mock("@/components/FileExplorer/FileExplorer", async () => {
  const { forwardRef, useImperativeHandle } = await import("react");
  return { default: forwardRef((props: any, ref) => {
    mocks.explorer(props); useImperativeHandle(ref, () => ({ root: { expand: mocks.refresh } }));
    return <div>{props.selectable === "multiple" ? "editable browser" : "readonly browser"}</div>;
  }) };
});
vi.mock("@/components/bakaui", () => ({ Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>, Spinner: () => <span>loading</span> }));
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
it("uses the existing file processor operations and refreshes the actual directory without rewriting history", async () => {
  render(<TaskDirectoryExplorer path="/work/task" context={context} maxContentHeight={288} />);
  expect(await screen.findByText("editable browser")).toBeVisible();
  expect(mocks.explorer.mock.lastCall?.[0]).toMatchObject({ rootPath: "/work/task", maxContentHeight: 288,
    capabilities: managedFileCapabilities, keyboard: false, watchChanges: "poll", selectable: "multiple" });
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
