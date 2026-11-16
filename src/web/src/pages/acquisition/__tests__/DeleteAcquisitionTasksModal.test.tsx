import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { forwardRef } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import DeleteAcquisitionTasksModal from "../components/DeleteAcquisitionTasksModal";
import cn from "@/locales/cn/pages/acquisition.json";
import en from "@/locales/en/pages/acquisition.json";
import { deletionReasonCodes, deletionReasonLabel } from "../components/deletionReasonLabel";
const api = vi.hoisted(() => ({ preview: vi.fn(), remove: vi.fn(), progress: vi.fn() }));
const translate = vi.hoisted(() => vi.fn((key: string, _options?: unknown) => key));
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
  removeTaskIfCleanupFails: true,
  retryRetainedFiles: false,
};
const finishReview = async () => {
  fireEvent.click(next());
  fireEvent.click(confirm());
  await screen.findByText("acquisition.delete.result");
};
beforeEach(() => {
  vi.clearAllMocks();
  api.preview.mockReset();
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
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3, 1, -2, NaN]} scopeLabel="completed" />);
  expect(next()).toBeEnabled();
  expect(checkbox("cleanup.option")).toBeChecked();
  expect(checkbox("cleanup.removeOnFailure")).toBeChecked();
  expect(checkbox("delivered.option")).not.toBeChecked();
  expect(screen.queryByRole("checkbox", { name: /skipVerification|accepted/ })).toBeNull();
  expect(screen.getByText("completed")).toBeVisible();
  expect(api.preview).not.toHaveBeenCalled();
  fireEvent.click(next());
  expect(confirm()).toBeEnabled();
  expect(screen.getByText("#1, #2, #3")).toBeVisible();
  expect(api.preview).not.toHaveBeenCalled();
  expect(api.remove).not.toHaveBeenCalled();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.remove).toHaveBeenCalledExactlyOnceWith(expect.stringMatching(/^[a-f\d]{32}$/), [1, 2, 3], defaultOptions));
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
  expect(api.preview).not.toHaveBeenCalled();
});

it("uses the same directory choices for unfinished tasks and final-only deletion", async () => {
  render(<DeleteAcquisitionTasksModal taskIds={[832]} />);
  fireEvent.click(checkbox("cleanup.option"));
  fireEvent.click(checkbox("delivered.option"));
  fireEvent.click(checkbox("cleanup.removeOnFailure"));
  await finishReview();
  expect(api.remove).toHaveBeenCalledExactlyOnceWith(expect.any(String), [832], {
    cleanupFiles: false, deleteDeliveredFiles: true, removeTaskIfCleanupFails: false, retryRetainedFiles: false,
  });
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
  expect(screen.getByText("acquisition.delete.cleanup.directPartial")).toBeVisible();
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
  await screen.findByText("acquisition.delete.retained.result");
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
  keys.push(...["final.directCleanup", "delivered.directConfirmation", "cleanup.directResult", "cleanup.directPartial", "cleanup.removedDirectories", "cleanup.directoryStatistics"]
    .map(suffix => `acquisition.delete.${suffix}`));
  for (const key of keys) {
    expect(cn[key as keyof typeof cn], key).toBeTruthy();
    expect(en[key as keyof typeof en], key).toBeTruthy();
  }
  expect(deletionReasonLabel(((key: string) => key) as any, "raw upstream path")).toBe("acquisition.delete.reason.unknown");
});
