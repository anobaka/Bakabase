import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { forwardRef } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import DeleteAcquisitionTasksModal from "../components/DeleteAcquisitionTasksModal";
import cn from "@/locales/cn/pages/acquisition.json";
import en from "@/locales/en/pages/acquisition.json";
import { deletionReasonCodes, deletionReasonLabel } from "../components/deletionReasonLabel";
const api = vi.hoisted(() => ({ preview: vi.fn(), previewRetained: vi.fn(), remove: vi.fn(), progress: vi.fn(), names: vi.fn() }));
const translate = vi.hoisted(() => vi.fn((key: string, _options?: unknown) => key));
vi.mock("@/stores/remoteAccess", () => ({ useRemoteAccessStore: (select: (state: { serverId: string }) => unknown) => select({ serverId: "test-server" }) }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: translate }) }));
vi.mock("../components/acquisitionDeletionApi", () => ({ acquisitionDeletionApi: api }));
vi.mock("@/components/Workflow/displayNames", () => ({
  activityDisplayName: (_t: any, kind: string) => kind,
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, footer, visible, title }: any) =>
    visible ? (
      <div role="dialog" aria-label={title}>
        {children}
        {footer}
      </div>
    ) : null,
  Button: forwardRef<HTMLButtonElement, any>(
    ({ children, onPress, isDisabled, isLoading }, ref) => (
      <button ref={ref} disabled={isDisabled || isLoading} onClick={onPress}>
        {children}
      </button>
    ),
  ),
  Input: ({ label, value, onValueChange }: any) => <label>{label}<input value={value} onChange={e => onValueChange(e.target.value)} /></label>,
  Spinner: (props: any) => <span role="status" aria-label={props["aria-label"]} />,
  Chip: ({ children }: any) => <span>{children}</span>,
  Checkbox: ({ children, isSelected, isDisabled, onValueChange }: any) => (
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
  Accordion: ({ children }: any) => <div>{children}</div>,
  AccordionItem: ({ children, title, subtitle }: any) => (
    <section>
      {title}
      {subtitle}
      {children}
    </section>
  ),
  Pagination: ({ page, total, onChange }: any) => (
    <button onClick={() => onChange(page + 1)}>
      page {page}/{total}
    </button>
  ),
}));
const button = (key: string) => screen.getByRole("button", { name: key });
const next = () => button("acquisition.delete.next");
const confirm = () => button("acquisition.delete.final.submit");
const checkbox = (suffix: string) => screen.getByRole("checkbox", { name: `acquisition.delete.${suffix}` });
const defaultOptions = {
  cleanupFiles: true,
  deleteDeliveredFiles: false,
  removeTaskIfCleanupFails: false,
  retryRetainedFiles: false,
};
const finishReview = async () => {
  fireEvent.click(next());
  fireEvent.click(confirm());
  await screen.findByText("acquisition.delete.result");
};
beforeEach(() => {
  sessionStorage.clear();
  vi.clearAllMocks();
  api.preview.mockReset();
  api.previewRetained.mockReset();
  api.names.mockReset().mockResolvedValue({});
  api.remove.mockReset().mockResolvedValue({ deletedTaskIds: [1], skipped: [] });
  api.progress.mockReset().mockImplementation(async (previewId: string) => ({
    previewId, state: "running", totalTasks: 3, finishedTasks: 0, phase: "rechecking",
    completedFiles: 0, completedBytes: 0,
  }));
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

it("opens a fixed selection and reaches confirmation immediately without inspecting files", async () => {
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3, 1, -2, NaN]} taskNames={{ 1: "First task", 2: "Second task", 3: "Third task" }} scopeLabel="completed" />);
  expect(next()).toBeEnabled();
  expect(checkbox("cleanup.option")).toBeChecked();
  expect(checkbox("cleanup.removeOnFailure")).not.toBeChecked();
  expect(checkbox("delivered.option")).not.toBeChecked();
  expect(screen.queryByRole("checkbox", { name: /skipVerification|accepted/ })).toBeNull();
  expect(screen.getByText("completed")).toBeVisible();
  expect(api.preview).not.toHaveBeenCalled();
  expect(api.previewRetained).not.toHaveBeenCalled();
  fireEvent.click(next());
  expect(confirm()).toBeEnabled();
  expect(screen.getByText("acquisition.delete.final.keepOnFailure")).toBeVisible();
  for (const name of ["First task", "Second task", "Third task"]) expect(screen.getByText(name)).toBeVisible();
  expect(api.names).not.toHaveBeenCalled();
  expect(api.preview).not.toHaveBeenCalled();
  expect(api.remove).not.toHaveBeenCalled();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.remove).toHaveBeenCalledExactlyOnceWith(expect.stringMatching(/^[a-f\d]{32}$/), [1, 2, 3], defaultOptions));
});

it("fills cross-page names without a file preview and preserves the fixed task selection", async () => {
  const ids = Array.from({ length: 24 }, (_, i) => i + 1);
  let resolveNames!: (names: Record<number, string>) => void;
  api.names.mockReturnValue(new Promise(resolve => { resolveNames = resolve; }));
  const view = render(<DeleteAcquisitionTasksModal taskIds={ids} taskNames={{ 1: "First task" }} />);
  expect(screen.getByText("First task")).toBeVisible();
  expect(next()).toBeEnabled();
  expect(api.names).toHaveBeenCalledWith(ids.slice(1), expect.any(AbortSignal));
  view.rerender(<DeleteAcquisitionTasksModal taskIds={[100]} taskNames={{ 100: "Unselected task" }} />);
  await act(async () => resolveNames(Object.fromEntries(ids.slice(1).map(id => [id, `Task name ${id}`]))));
  fireEvent.click(button("page 1/2"));
  expect(screen.getByText("Task name 24")).toBeVisible();
  fireEvent.click(next());
  expect(screen.getByText("First task")).toBeVisible();
  expect(screen.getByText("Task name 24")).toBeVisible();
  expect(screen.queryByText("Unselected task")).toBeNull();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.remove).toHaveBeenCalledWith(expect.any(String), ids, defaultOptions));
  expect(api.preview).not.toHaveBeenCalled();
});

it("never blocks confirmation on missing or slow task names", async () => {
  let rejectNames!: (cause: Error) => void;
  api.names.mockReturnValue(new Promise((_resolve, reject) => { rejectNames = reject; }));
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2]} taskNames={{ 1: "Known task" }} />);
  fireEvent.click(next());
  expect(confirm()).toBeEnabled();
  await act(async () => rejectNames(new Error("offline")));
  expect(screen.getByText("Known task")).toBeVisible();
  expect(screen.getByText("acquisition.delete.nameUnavailable")).toBeVisible();
  expect(confirm()).toBeEnabled();
  expect(api.preview).not.toHaveBeenCalled();
});

it("keeps forty four tasks immediately actionable after a long review and background list updates", async () => {
  const ids = Array.from({ length: 44 }, (_, index) => index + 1);
  const view = render(<DeleteAcquisitionTasksModal taskIds={ids} />);
  view.rerender(<DeleteAcquisitionTasksModal taskIds={[...ids, 100]} />);
  fireEvent.click(checkbox("delivered.option"));
  fireEvent.click(next());
  vi.setSystemTime(new Date(Date.now() + 60 * 60 * 1000));
  expect(confirm()).toBeEnabled();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.remove).toHaveBeenCalledWith(expect.any(String), ids, { ...defaultOptions, deleteDeliveredFiles: true }));
  expect(api.preview).not.toHaveBeenCalled();
});

it("submits only once after final confirmation and shows execution progress", async () => {
  let finish!: (value: unknown) => void;
  api.remove.mockReturnValue(new Promise(resolve => { finish = resolve; }));
  api.progress.mockImplementation(async (previewId: string) => ({
    previewId, state: "running", phase: "cleaning", totalTasks: 2, finishedTasks: 1,
    currentTaskId: 2, completedFiles: 0, completedBytes: 0,
  }));
  const changed = vi.fn();
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2]} onChanged={changed} />);
  fireEvent.click(next());
  const submit = confirm();
  fireEvent.click(submit);
  fireEvent.click(submit);
  expect(api.remove).toHaveBeenCalledTimes(1);
  expect(button("common.action.close")).toBeDisabled();
  await screen.findByText("acquisition.delete.progress.phase.cleaning");
  expect(screen.getByText("acquisition.delete.progress.tasks")).toBeVisible();
  expect(screen.queryByText("acquisition.delete.progress.files")).toBeNull();
  await act(async () => finish({ deletedTaskIds: [1, 2], skipped: [] }));
  expect(changed).toHaveBeenCalledTimes(1);
  expect(screen.getByText("acquisition.delete.result")).toBeVisible();
});

it("updates choices locally and requires another confirmation after going back", async () => {
  render(<DeleteAcquisitionTasksModal taskIds={[1]} />);
  fireEvent.click(checkbox("delivered.option"));
  fireEvent.click(next());
  expect(screen.getByText("acquisition.delete.delivered.directConfirmation")).toBeVisible();
  fireEvent.click(button("acquisition.delete.final.back"));
  fireEvent.click(checkbox("delivered.option"));
  fireEvent.click(checkbox("cleanup.option"));
  await finishReview();
  expect(api.remove).toHaveBeenCalledExactlyOnceWith(expect.any(String), [1], {
    cleanupFiles: false, deleteDeliveredFiles: false, removeTaskIfCleanupFails: false, retryRetainedFiles: false,
  });
  expect(screen.queryByText("acquisition.delete.cleanup.outcomeSummary")).toBeNull();
  expect(api.preview).not.toHaveBeenCalled();
});

it("uses the same directory choices for unfinished tasks and final-only deletion", async () => {
  render(<DeleteAcquisitionTasksModal taskIds={[832]} />);
  fireEvent.click(checkbox("cleanup.option"));
  fireEvent.click(checkbox("delivered.option"));
  await finishReview();
  expect(api.remove).toHaveBeenCalledExactlyOnceWith(expect.any(String), [832], {
    cleanupFiles: false, deleteDeliveredFiles: true, removeTaskIfCleanupFails: false, retryRetainedFiles: false,
  });
});

it("requires an explicit opt-in to remove a task whose file cleanup is incomplete", async () => {
  render(<DeleteAcquisitionTasksModal taskIds={[1]} />);
  fireEvent.click(checkbox("cleanup.removeOnFailure"));
  fireEvent.click(next());
  expect(screen.getByText("acquisition.delete.final.removeOnFailure")).toBeVisible();
  expect(screen.queryByText("acquisition.delete.final.keepOnFailure")).toBeNull();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.remove).toHaveBeenCalledExactlyOnceWith(expect.any(String), [1], {
    ...defaultOptions, removeTaskIfCleanupFails: true,
  }));
});

it.each([{ ids: [] }, { ids: [-1, 0, NaN] }])("cannot submit an empty selection $ids", ({ ids }) => {
  render(<DeleteAcquisitionTasksModal taskIds={ids} />);
  expect(next()).toBeDisabled();
  expect(api.preview).not.toHaveBeenCalled();
});

it("keeps the backend limit for cleaning files while allowing larger record-only selections", async () => {
  const ids = Array.from({ length: 101 }, (_, index) => index + 1);
  render(<DeleteAcquisitionTasksModal taskIds={ids} />);
  expect(checkbox("cleanup.option")).not.toBeChecked();
  expect(checkbox("cleanup.option")).toBeDisabled();
  expect(checkbox("delivered.option")).toBeDisabled();
  await finishReview();
  expect(api.remove).toHaveBeenCalledWith(expect.any(String), ids, {
    cleanupFiles: false, deleteDeliveredFiles: false, retryRetainedFiles: false, removeTaskIfCleanupFails: false,
  });
});

it("reopens the confirmed scope after a refresh and only reads the receipt", async () => {
  api.remove.mockReturnValue(new Promise(() => {}));
  api.progress.mockImplementation(async (previewId: string) => ({ previewId, state: "running",
    phase: "cleaning", cleanupFiles: true, deleteDeliveredFiles: true, totalTasks: 2, finishedTasks: 0 }));
  const dialog = render(<DeleteAcquisitionTasksModal taskIds={[42, 43]} />);
  fireEvent.click(checkbox("delivered.option"));
  fireEvent.click(next());
  fireEvent.click(confirm());
  await screen.findByText("acquisition.delete.progress.background");
  expect(button("common.action.close")).toBeEnabled();
  const [operationId] = api.remove.mock.calls[0];
  dialog.unmount();
  api.progress.mockImplementation(async (previewId: string) => ({ previewId, state: "completed",
    receiptOnly: true, cleanupFiles: true, deleteDeliveredFiles: true, totalTasks: 2, finishedTasks: 2 }));
  render(<DeleteAcquisitionTasksModal taskIds={[43, 42]} />);
  await screen.findByText("acquisition.delete.receipt.finished");
  expect(screen.getByText("acquisition.delete.delivered.option")).toBeVisible();
  expect(api.progress).toHaveBeenLastCalledWith(operationId, expect.any(AbortSignal));
  expect(api.remove).toHaveBeenCalledTimes(1);
  fireEvent.click(button("acquisition.delete.cleanup.retry"));
  expect(checkbox("delivered.option")).not.toBeChecked();
  expect(api.remove).toHaveBeenCalledTimes(1);
});

it("does not replay a completed read-only receipt after losing the response", async () => {
  api.remove.mockRejectedValue(new Error("connection lost"));
  api.progress.mockImplementation(async (previewId: string) => ({ previewId, state: "completed",
    totalTasks: 1, finishedTasks: 1, receiptOnly: true, cleanupFiles: true, deleteDeliveredFiles: false }));
  render(<DeleteAcquisitionTasksModal taskIds={[42]} />);
  fireEvent.click(next());
  fireEvent.click(confirm());
  await screen.findByText("acquisition.delete.receipt.finished");
  expect(api.remove).toHaveBeenCalledTimes(1);
});

it("retrieves the saved result with the same operation ID after a lost response", async () => {
  api.remove.mockRejectedValueOnce(new Error("network lost"));
  api.progress.mockImplementation(async (previewId: string) => ({ previewId, state: "completed",
    totalTasks: 1, finishedTasks: 1, phase: "completed", completedFiles: 0, completedBytes: 0 }));
  render(<DeleteAcquisitionTasksModal taskIds={[1]} />);
  await finishReview();
  expect(api.remove).toHaveBeenCalledTimes(2);
  expect(api.remove.mock.calls[1]).toEqual(api.remove.mock.calls[0]);
  expect(api.preview).not.toHaveBeenCalled();
});

it("continues checking a running operation after a lost response without submitting another deletion", async () => {
  api.remove.mockRejectedValueOnce(new Error("network lost"));
  render(<DeleteAcquisitionTasksModal taskIds={[1]} />);
  fireEvent.click(next());
  fireEvent.click(confirm());
  await screen.findByText("acquisition.delete.progress.phase.rechecking");
  expect(api.remove).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole("button", { name: "acquisition.delete.cleanup.retry" })).toBeNull();
  expect(button("common.action.close")).toBeEnabled();
});

it("does not automatically resend when both execution and progress responses are unavailable", async () => {
  api.remove.mockRejectedValue(new Error("offline"));
  api.progress.mockRejectedValue(new Error("offline"));
  render(<DeleteAcquisitionTasksModal taskIds={[1]} />);
  fireEvent.click(next());
  fireEvent.click(confirm());
  await screen.findByText("acquisition.delete.receipt.unavailable");
  fireEvent.click(button("acquisition.delete.receipt.checkAgain"));
  await waitFor(() => expect(api.progress.mock.calls.length).toBeGreaterThanOrEqual(2));
  expect(api.remove).toHaveBeenCalledTimes(1);
  expect(api.preview).not.toHaveBeenCalled();
});

it("reports cleared directories without presenting old manifest bytes as measured deletion", async () => {
  const longPath = "/work/" + "long-path/".repeat(40);
  api.remove.mockResolvedValueOnce({ deletedTaskIds: [1], skipped: [], fileStatisticsIncomplete: true,
    removedDirectories: ["/work/A1/Downloads"], removedFiles: 3, removedBytes: 1000000,
    cleanupResults: [{ taskId: 1, completed: false, fileStatisticsIncomplete: true,
      removedDirectories: ["/work/A1/Downloads"], removedFiles: 3, removedBytes: 1000000,
      reasonCodes: ["ReferencedPath"], retainedDirectories: [longPath] }],
  });
  render(<DeleteAcquisitionTasksModal taskIds={[1]} />);
  await finishReview();
  expect(screen.getByText("acquisition.delete.cleanup.directResult")).toBeVisible();
  expect(screen.queryByText("acquisition.delete.cleanup.result")).toBeNull();
  expect(screen.getByText("/work/A1/Downloads")).toBeVisible();
  expect(screen.getByText(longPath)).toHaveClass("[overflow-wrap:anywhere]");
  expect(screen.getByText("acquisition.delete.reason.ReferencedPath")).toBeVisible();
  expect(screen.getByText("acquisition.delete.cleanup.incompleteTask")).toBeVisible();
  expect(screen.getByText("acquisition.delete.cleanup.taskRemoved")).toBeVisible();
  expect(screen.getByRole("alert")).toHaveTextContent("acquisition.delete.history.retainedWarning");
});

it("reports retained paths before the record outcome and counts only attempted file cleanup", async () => {
  const retained = "/work/first-task/Processing";
  api.remove.mockResolvedValueOnce({ deletedTaskIds: [2], skipped: [
    { taskId: 1, reasonCode: "CLEANUP_FILES_RETAINED" }, { taskId: 3, reasonCode: "ACQUISITION_TASK_ACTIVE" },
  ], cleanupResults: [
    { taskId: 1, completed: false, removedFiles: 0, removedBytes: 0, reasonCodes: ["ReferencedPath"], retainedDirectories: [retained] },
    { taskId: 2, completed: true, removedFiles: 0, removedBytes: 0, reasonCodes: [] },
  ] });
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} />);
  await finishReview();
  const fileOutcome = screen.getByText("acquisition.delete.cleanup.outcomeSummary");
  const recordOutcome = screen.getByText("acquisition.delete.result");
  expect(fileOutcome.compareDocumentPosition(recordOutcome) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  expect(translate).toHaveBeenCalledWith("acquisition.delete.cleanup.outcomeSummary", { completed: 1, incomplete: 1, skipped: 1 });
  expect(screen.getByRole("list", { name: "acquisition.delete.cleanup.retainedDirectories" })).toHaveTextContent(retained);
  expect(screen.getByText("acquisition.delete.cleanup.taskKept")).toBeVisible();
  expect(screen.queryByText("acquisition.delete.cleanup.directResult")).toBeNull();
  fireEvent.click(button("acquisition.delete.cleanup.retry"));
  expect(checkbox("cleanup.removeOnFailure")).not.toBeChecked();
  await finishReview();
  expect(api.remove).toHaveBeenLastCalledWith(expect.any(String), [1], defaultOptions);
});

it("reconfirms only the original busy tasks with the same cleanup choices and a fresh operation ID", async () => {
  api.remove.mockResolvedValueOnce({ deletedTaskIds: [1], skipped: [
    { taskId: 2, reasonCode: "ACQUISITION_INTERMEDIATE_CLEANUP_ACTIVE" },
    { taskId: 3, reasonCode: "ACQUISITION_TASK_OPERATION_BUSY" },
    { taskId: 4, reasonCode: "CLEANUP_PATH_NOT_AUTHORIZED" },
    { taskId: 5, reasonCode: "ACQUISITION_TASK_ACTIVE" },
    { taskId: 6, reasonCode: "ACQUISITION_DOWNLOAD_ACTIVE" },
    { taskId: 1, reasonCode: "ACQUISITION_TASK_ACTIVE" },
    { taskId: 7, reasonCode: "ACQUISITION_TASK_ACTIVE" },
  ] }).mockResolvedValueOnce({ deletedTaskIds: [2, 3, 5], skipped: [{ taskId: 6, reasonCode: "ACQUISITION_TASK_CHANGED" }] });
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3, 4, 5, 6]} taskNames={{2: "Cleaning", 3: "Updating", 5: "Running", 6: "Downloading"}} />);
  fireEvent.click(checkbox("cleanup.option"));
  fireEvent.click(checkbox("delivered.option"));
  fireEvent.click(checkbox("cleanup.removeOnFailure"));
  await finishReview();
  fireEvent.click(button("acquisition.delete.retryBusy"));
  expect(api.remove).toHaveBeenCalledTimes(1);
  const list = screen.getByRole("list", { name: "acquisition.delete.taskNames" });
  expect(Array.from(list.querySelectorAll("li"), item => item.textContent)).toEqual(["#2Cleaning", "#3Updating", "#5Running", "#6Downloading"]);
  expect(screen.getByText("acquisition.delete.delivered.directConfirmation")).toBeVisible();
  expect(screen.getByText("acquisition.delete.final.removeOnFailure")).toBeVisible();
  expect(screen.queryByText("acquisition.delete.cleanup.option")).not.toBeInTheDocument();
  fireEvent.click(confirm());
  await screen.findByText("acquisition.delete.result");
  expect(api.remove.mock.calls[1]).toEqual([
    expect.stringMatching(/^[a-f\d]{32}$/), [2, 3, 5, 6],
    { ...defaultOptions, cleanupFiles: false, deleteDeliveredFiles: true, removeTaskIfCleanupFails: true },
  ]);
  expect(api.remove.mock.calls[1][0]).not.toBe(api.remove.mock.calls[0][0]);
  expect(screen.getByText("acquisition.delete.reason.ACQUISITION_TASK_CHANGED")).toBeVisible();
  expect(screen.queryByRole("button", { name: "acquisition.delete.retryBusy" })).toBeNull();
  expect(api.previewRetained).not.toHaveBeenCalled();
});

it("restricts edits after backing out of a busy retry to the remaining fixed selection", async () => {
  api.remove.mockResolvedValueOnce({ deletedTaskIds: [1], skipped: [{ taskId: 2, reasonCode: "ACQUISITION_TASK_OPERATION_BUSY" }] });
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2]} />);
  fireEvent.click(checkbox("cleanup.removeOnFailure"));
  await finishReview();
  fireEvent.click(button("acquisition.delete.retryBusy"));
  fireEvent.click(button("acquisition.delete.final.back"));
  expect(checkbox("cleanup.removeOnFailure")).toBeChecked();
  fireEvent.click(checkbox("cleanup.removeOnFailure"));
  await finishReview();
  expect(api.remove).toHaveBeenLastCalledWith(expect.any(String), [2], defaultOptions);
});

it("never offers to delete a task again after a retained-file cleanup was skipped", async () => {
  api.remove.mockResolvedValueOnce({ deletedTaskIds: [], skipped: [{ taskId: 1, reasonCode: "ACQUISITION_INTERMEDIATE_CLEANUP_ACTIVE" }] });
  render(<DeleteAcquisitionTasksModal taskIds={[1]} retryRetainedFiles />);
  fireEvent.click(next());
  fireEvent.click(button("acquisition.delete.retained.confirm"));
  await screen.findByText("acquisition.delete.reason.ACQUISITION_INTERMEDIATE_CLEANUP_ACTIVE");
  expect(screen.queryByRole("button", { name: "acquisition.delete.retryBusy" })).toBeNull();
});

it("retries only retained files with a new explicit confirmation and no preview", async () => {
  api.remove.mockResolvedValueOnce({ deletedTaskIds: [1], skipped: [{ taskId: 2, reasonCode: "ACQUISITION_TASK_ACTIVE" }],
    cleanupResults: [{ taskId: 1, completed: false, removedFiles: 0, removedBytes: 0, reasonCodes: ["ReferencedPath"] }],
  });
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2]} />);
  await finishReview();
  fireEvent.click(button("acquisition.delete.retained.title"));
  expect(screen.queryByRole("checkbox", { name: "acquisition.delete.cleanup.removeOnFailure" })).toBeNull();
  fireEvent.click(next());
  expect(api.remove).toHaveBeenCalledTimes(1);
  fireEvent.click(button("acquisition.delete.retained.confirm"));
  await screen.findByText("acquisition.delete.cleanup.outcomeSummary");
  expect(api.remove).toHaveBeenLastCalledWith(expect.any(String), [1], {
    ...defaultOptions, removeTaskIfCleanupFails: false, retryRetainedFiles: true,
  });
  expect(api.remove.mock.calls[1][0]).not.toBe(api.remove.mock.calls[0][0]);
  expect(api.preview).not.toHaveBeenCalled();
});

it("requires a file scope for reopened retained cleanup, without requiring an inventory", () => {
  render(<DeleteAcquisitionTasksModal taskIds={[1]} retryRetainedFiles />);
  fireEvent.click(checkbox("cleanup.option"));
  expect(next()).toBeDisabled();
  fireEvent.click(checkbox("delivered.option"));
  expect(next()).toBeEnabled();
  expect(api.preview).not.toHaveBeenCalled();
});

it("keeps deletion reasons and direct-confirmation text translated", () => {
  const keys = [...deletionReasonCodes].map(code => `acquisition.delete.reason.${code}`);
  keys.push(...["retryBusy", "final.directCleanup", "delivered.directConfirmation", "cleanup.directResult", "cleanup.incompleteTask", "cleanup.taskKept",
    "cleanup.taskRemoved", "cleanup.outcomeSummary", "cleanup.removedDirectories", "cleanup.directoryStatistics"]
    .map(suffix => `acquisition.delete.${suffix}`));
  for (const key of keys) {
    expect(cn[key as keyof typeof cn], key).toBeTruthy();
    expect(en[key as keyof typeof en], key).toBeTruthy();
  }
  expect(deletionReasonLabel(((key: string) => key) as any, "raw upstream path")).toBe("acquisition.delete.reason.unknown");
});

it("lists the exact waiting executions per deleted task and submits their server preview after confirmation", async () => {
  const rows = [
    { taskId: 5, title: "Five", canDelete: true, cleanup: { supersededRuns: [
      { runId: 91, revision: 1, replacedByRevision: 3 }, { runId: 93, revision: 3, replacedByRevision: 3 },
    ] } },
    { taskId: 7, title: "Seven", canDelete: false, reasonCode: "SharedOwner" },
  ];
  const options = { ...defaultOptions, retryRetainedFiles: true };
  api.previewRetained.mockResolvedValue({ previewId: "server-preview", ...options, rows, deletableCount: 1 });
  render(<DeleteAcquisitionTasksModal taskIds={[5,7]} taskNames={{5:"Five",7:"Seven"}} retryRetainedFiles reviewWaitingRuns />);
  expect(api.previewRetained).not.toHaveBeenCalled();
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.history.closeWaitingRuns");
  expect(api.previewRetained).toHaveBeenCalledExactlyOnceWith([5,7], options, expect.any(AbortSignal));
  expect(screen.getByText("#5 Five").closest("li")).toHaveTextContent("acquisition.delete.cleanup.supersededRun");
  expect(screen.getByText("#5 Five").closest("li")).toHaveTextContent("acquisition.delete.history.waitingRun");
  expect(screen.getByText("#7 Seven").closest("li")).toHaveTextContent("acquisition.delete.reason.SharedOwner");
  expect(translate).toHaveBeenCalledWith("acquisition.delete.cleanup.supersededRun", { run: 91, revision: 1, replacedBy: 3 });
  expect(translate).toHaveBeenCalledWith("acquisition.delete.history.waitingRun", { run: 93, revision: 3, replacedBy: 3 });
  expect(api.remove).not.toHaveBeenCalled();
  fireEvent.click(button("acquisition.delete.retained.confirm"));
  await waitFor(() => expect(api.remove).toHaveBeenCalledExactlyOnceWith("server-preview", undefined, options));
});

it("requires a fresh historical preview after changing scopes and keeps a blocked preview read-only", async () => {
  const preview = { previewId: "first", rows: [{ taskId: 5, canDelete: false, reasonCode: "ActiveOwner" }],
    retryRetainedFiles: true, cleanupFiles: true, deleteDeliveredFiles: false, deletableCount: 0 };
  api.previewRetained.mockResolvedValueOnce(preview).mockResolvedValueOnce(preview);
  render(<DeleteAcquisitionTasksModal taskIds={[5]} retryRetainedFiles reviewWaitingRuns />);
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.reason.ActiveOwner");
  expect(button("acquisition.delete.retained.confirm")).toBeDisabled();
  fireEvent.click(button("acquisition.delete.final.back"));
  fireEvent.click(checkbox("delivered.option"));
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.error.preview");
  expect(screen.queryByText("acquisition.delete.retained.confirm")).toBeNull();
  expect(api.previewRetained.mock.calls[1][1].deleteDeliveredFiles).toBe(true);
  expect(api.remove).not.toHaveBeenCalled();
});

it("cleans only the selected paths from a retained-file preview across tasks and preserves that scope after a lost response", async () => {
  const group = (directory: string, eligible = true) => ({ directory, eligible, fileCount: 1, bytes: 128,
    registeredFilesOnly: directory === "/ssd/work/one" });
  api.previewRetained.mockResolvedValue({ previewId: "directory-review", retryRetainedFiles: true,
    cleanupFiles: true, deleteDeliveredFiles: false, deletableCount: 2, rows: [
      { taskId: 5, title: "One", canDelete: true, cleanup: { groups: [group("/ssd/work/one"), group("/nas/work/one")] } },
      { taskId: 7, title: "Two", canDelete: true, cleanup: { groups: [group("/ssd/work/two"), group("/ssd/shared", false)] } },
    ] });
  const saved = { cleanupResults: [{ taskId: 5, completed: true, unselectedDirectories: ["/nas/work/one"] }], deletedTaskIds: [], skipped: [] };
  api.remove.mockRejectedValueOnce(new Error("lost response")).mockResolvedValueOnce(saved);
  api.progress.mockResolvedValue({ previewId: "directory-review", state: "completed", totalTasks: 2, finishedTasks: 2 });
  render(<DeleteAcquisitionTasksModal taskIds={[5,7]} retryRetainedFiles />);
  fireEvent.click(checkbox("directories.choose"));
  fireEvent.click(next());
  const send = await screen.findByRole("button", { name: "acquisition.delete.retained.confirm" });
  expect(send).toBeDisabled();
  expect(screen.getByRole("checkbox", { name: "/ssd/shared" })).toBeDisabled();
  const filter = screen.getByLabelText("acquisition.delete.directories.filter");
  expect(screen.getAllByText("acquisition.delete.directories.registeredFilesOnly")).toHaveLength(1);
  fireEvent.change(filter, { target: { value: "/ssd/" } });
  fireEvent.click(checkbox("directories.selectMatches"));
  fireEvent.change(filter, { target: { value: "/nas/" } });
  expect(screen.getByRole("checkbox", { name: "/nas/work/one" })).not.toBeChecked();
  fireEvent.click(send);
  await screen.findByText("acquisition.delete.directories.kept");
  expect(api.remove).toHaveBeenCalledTimes(2);
  const scope = { cleanupFiles: true, deleteDeliveredFiles: false, removeTaskIfCleanupFails: false, retryRetainedFiles: true,
    selectedCleanupDirectories: { "5": ["/ssd/work/one"], "7": ["/ssd/work/two"] } };
  expect(api.remove.mock.calls).toEqual([["directory-review", undefined, scope], ["directory-review", undefined, scope]]);
});

it("clears selected directories when a user returns and reviews a different scope", async () => {
  api.previewRetained.mockImplementation(async (ids, options) => ({
    ...options, previewId: options.deleteDeliveredFiles ? "new-preview" : "first-preview", deletableCount: 1,
    rows: [{ taskId: ids[0], canDelete: true, cleanup: { groups: [{
      directory: "/ssd/selected", eligible: true, fileCount: 1, bytes: 20,
    }] } }],
  }));
  render(<DeleteAcquisitionTasksModal taskIds={[5]} retryRetainedFiles />);
  fireEvent.click(checkbox("directories.choose"));
  fireEvent.click(next());
  fireEvent.click(await screen.findByRole("checkbox", { name: "/ssd/selected" }));
  expect(button("acquisition.delete.retained.confirm")).not.toBeDisabled();
  fireEvent.click(button("acquisition.delete.final.back"));
  fireEvent.click(checkbox("delivered.option"));
  fireEvent.click(next());
  expect(await screen.findByRole("checkbox", { name: "/ssd/selected" })).not.toBeChecked();
  expect(button("acquisition.delete.retained.confirm")).toBeDisabled();
});
