import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { forwardRef } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import DeleteAcquisitionTasksModal from "../components/DeleteAcquisitionTasksModal";
import cn from "@/locales/cn/pages/acquisition.json";
import en from "@/locales/en/pages/acquisition.json";
import { deletionReasonCodes, deletionReasonLabel } from "../components/deletionReasonLabel";
import { bytesToSize } from "@/components/utils";
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
const preview = (overrides: any = {}) => ({
  previewId: "fixed-preview",
  expiresAt: new Date(Date.now() + 600_000).toISOString(),
  totalCount: 3,
  deletableCount: 1,
  blockedCount: 2,
  rows: [
    { taskId: 1, title: "Completed task", canDelete: true },
    { taskId: 2, title: "Running task", canDelete: false, reasonCode: "ACQUISITION_TASK_ACTIVE" },
    {
      taskId: 3,
      title: "Downloading task",
      canDelete: false,
      reasonCode: "ACQUISITION_DOWNLOAD_ACTIVE",
    },
  ],
  ...overrides,
});
const button = (key: string) => screen.getByRole("button", { name: key });
const next = () => button("acquisition.delete.next");
const confirm = () =>
  button("acquisition.delete.final.submit");
const inspect = () => button("acquisition.delete.cleanup.inspect");
const checkbox = (key: string) =>
  screen.getByRole("checkbox", { name: "acquisition.delete.cleanup." + key });
const finishReview = async () => {
  await act(async () => { fireEvent.click(next()); });
  await act(async () => { fireEvent.click(confirm()); });
};
beforeEach(() => {
  vi.clearAllMocks();
  api.preview.mockReset().mockImplementation(async (_ids, options) => preview(options));
  api.remove.mockReset().mockResolvedValue({ deletedTaskIds: [1], skipped: [] });
  api.progress.mockReset().mockImplementation(async (previewId: string) => ({
    previewId,
    state: "running",
    totalTasks: 3,
    finishedTasks: 0,
    phase: "rechecking",
    completedFiles: 0,
    completedBytes: 0,
  }));
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

it("previews a fixed, deduplicated selection and explains protection before any deletion", async () => {
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3, 1, -2]} scopeLabel="attention · #969" />);
  await screen.findByText("Completed task");
  expect(api.preview).toHaveBeenCalledWith([1, 2, 3], {}, expect.any(AbortSignal));
  expect(api.remove).not.toHaveBeenCalled();
  for (const key of [
    "preserve",
    "total",
    "eligible",
    "blocked",
    "reason.ACQUISITION_TASK_ACTIVE",
    "reason.ACQUISITION_DOWNLOAD_ACTIVE",
  ])
    expect(screen.getByText(`acquisition.delete.${key}`)).toBeVisible();
  expect(screen.getByText("attention · #969")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(api.remove).not.toHaveBeenCalled();
});

it("deletes only through the preview token and suppresses duplicate confirmation", async () => {
  let finish!: (value: unknown) => void;
  api.remove.mockReturnValue(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  const changed = vi.fn();
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} onChanged={changed} />);
  await screen.findByText("Completed task");
  fireEvent.click(cleanupCheckbox());
  fireEvent.click(next());
  expect(api.remove).not.toHaveBeenCalled();
  const submit = confirm();
  fireEvent.click(submit);
  fireEvent.click(submit);
  expect(api.remove).toHaveBeenCalledExactlyOnceWith("fixed-preview", {});
  expect(screen.getByRole("button", { name: "common.action.close" })).toBeDisabled();
  await act(async () =>
    finish({
      deletedTaskIds: [],
      skipped: [{ taskId: 1, reasonCode: "ACQUISITION_TASK_CHANGED" }],
    }),
  );
  expect(screen.getByText("acquisition.delete.result")).toBeVisible();
  expect(screen.getByText("acquisition.delete.reason.ACQUISITION_TASK_CHANGED")).toBeVisible();
  expect(changed).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole("button", { name: "acquisition.delete.final.submit" })).toBeNull();
});

it("does not infer an empty or safe selection when preview fails", async () => {
  api.preview.mockRejectedValue(new Error("offline"));
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2]} />);
  await screen.findByRole("alert");
  expect(next()).toBeDisabled();
  expect(api.remove).not.toHaveBeenCalled();
});

it("disables deletion when every task is protected", async () => {
  api.preview.mockResolvedValue(
    preview({
      deletableCount: 0,
      blockedCount: 3,
      rows: preview().rows.map((row: any) => ({ ...row, canDelete: false })),
    }),
  );
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} />);
  await screen.findByText("Completed task");
  expect(next()).toBeDisabled();
  fireEvent.click(next());
  expect(api.remove).not.toHaveBeenCalled();
});

it("refreshes an expired snapshot without widening IDs after surrounding filters change", async () => {
  api.preview.mockResolvedValueOnce(preview({ expiresAt: "2020-01-01 00:00:00.000" }));
  const view = render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} />);
  await screen.findByText("acquisition.delete.expired");
  expect(next()).toBeDisabled();
  view.rerender(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3, 4, 5]} />);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.delete.recheck" }));
  await waitFor(() => expect(next()).not.toBeDisabled());
  expect(api.preview.mock.calls.map((call) => call[0])).toEqual([
    [1, 2, 3],
    [1, 2, 3],
  ]);
  expect(api.remove).not.toHaveBeenCalled();
});

it("recovers an interrupted deletion's task, reason and counts before offering a fresh preview", async () => {
  api.remove.mockRejectedValue(new Error("CLEANUP_PATH_NOT_AUTHORIZED"));
  api.progress.mockImplementation(async (previewId: string) => ({
    previewId, state: "interrupted", phase: "interrupted", totalTasks: 3, finishedTasks: 1,
    currentTaskId: 2, completedFiles: 4, completedBytes: 12345,
  }));
  const changed = vi.fn();
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} onChanged={changed} />);
  await screen.findByText("Completed task");
  await finishReview();
  await screen.findByText("acquisition.delete.receipt.interrupted");
  expect(screen.getByText("acquisition.delete.reason.CLEANUP_PATH_NOT_AUTHORIZED")).toBeVisible();
  expect(screen.getByText("#2")).toBeVisible();
  expect(translate).toHaveBeenCalledWith("acquisition.delete.progress.tasks", { completed: 1, total: 3 });
  expect(translate).toHaveBeenCalledWith("acquisition.delete.progress.files", { count: 4, size: bytesToSize(12345) });
  expect(screen.queryByText("acquisition.delete.next")).toBeNull();
  expect(api.remove).toHaveBeenCalledTimes(1);
  expect(changed).toHaveBeenCalled();
  api.preview.mockResolvedValue(preview({ previewId: "new-fixed-preview" }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.delete.recheck" }));
  await waitFor(() => expect(next()).not.toBeDisabled());
  expect(api.remove).toHaveBeenCalledTimes(1);
});

it("follows the same running deletion after a lost response until it finishes without inferring deleted counts", async () => {
  vi.useFakeTimers();
  let disconnect!: (error: Error) => void;
  api.remove.mockReturnValue(new Promise((_, reject) => { disconnect = reject; }));
  await act(async () => { render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} />); });
  await act(async () => fireEvent.click(next()));
  await act(async () => fireEvent.click(confirm()));
  await act(async () => disconnect(new Error("connection lost")));
  expect(screen.getByText("acquisition.delete.progress.phase.rechecking")).toBeVisible();
  api.progress.mockResolvedValue({ previewId: "fixed-preview", state: "completed", phase: "completed", totalTasks: 3,
    finishedTasks: 3, completedFiles: 8, completedBytes: 67890 });
  await act(async () => vi.advanceTimersByTime(1000));
  expect(screen.getByText("acquisition.delete.receipt.completed")).toBeVisible();
  expect(screen.queryByText("acquisition.delete.result")).toBeNull();
  expect(api.remove).toHaveBeenCalledTimes(1);
  expect(api.preview).toHaveBeenCalledTimes(2);
  const reads = api.progress.mock.calls.length;
  await act(async () => vi.advanceTimersByTime(5000));
  expect(api.progress).toHaveBeenCalledTimes(reads);
});

it("rechecks only the submitted token when offline and allows a fresh review when its receipt expired", async () => {
  api.remove.mockRejectedValue(new Error("connection lost"));
  api.progress.mockRejectedValue(new Error("offline"));
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} />);
  await screen.findByText("Completed task");
  await finishReview();
  await screen.findByText("acquisition.delete.receipt.unavailable");
  api.progress.mockRejectedValueOnce(new Error("ACQUISITION_DELETE_PREVIEW_EXPIRED"));
  fireEvent.click(button("acquisition.delete.receipt.checkAgain"));
  await screen.findByText("acquisition.delete.receipt.expired");
  expect(api.progress.mock.lastCall?.[0]).toBe("fixed-preview");
  expect(api.remove).toHaveBeenCalledTimes(1);
  expect(api.preview).toHaveBeenCalledTimes(2);
  expect(button("acquisition.delete.recheck")).toBeEnabled();
});

it("reads server UTC timestamps without mistaking a fresh preview for expired", async () => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date("2026-10-25T10:00:00Z"));
  api.preview.mockResolvedValue(preview({ expiresAt: "2026-10-25 10:10:00.000" }));
  await act(async () => {
    render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} />);
  });
  expect(next()).not.toBeDisabled();
  await act(async () => {
    vi.advanceTimersByTime(600_001);
  });
  expect(next()).toBeDisabled();
  expect(screen.getByText("acquisition.delete.expired")).toBeVisible();
});

const cleanupPreview = (overrides: any = {}) =>
  preview({
    previewId: "cleanup-preview",
    cleanupFiles: true,
    skipFileVerification: true,
    cleanupFileCount: 2,
    cleanupBytes: 1024,
    rows: preview().rows.map((row: any) =>
      row.taskId !== 1
        ? row
        : {
            ...row,
            cleanup: {
              fileCount: 2,
              bytes: 1024,
              groups: [
                {
                  artifactId: "input",
                  kind: "download",
                  directory: "/work/A1",
                  eligible: true,
                  nodes: [{ runId: 8, nodeId: "part1/nested.download", kind: "download.wait" }],
                  files: [{ path: "/work/A1/archive.zip", bytes: 1024 }],
                  fileCount: 2,
                  bytes: 1024,
                  filesTruncated: true,
                },
              ],
              supersededRuns: [{ runId: 7, revision: 1, replacedByRevision: 2 }],
            },
          },
    ),
    ...overrides,
  });
const cleanupCheckbox = () => checkbox("option");
const cleanupOpts = () => ({ removeTaskIfCleanupFails: true });
const skipVerificationCheckbox = () => checkbox("skipVerification");
const openTask = async () => {
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} />);
  await screen.findByText("Completed task");
};

const deliveredCheckbox = () => screen.getByRole("checkbox", { name: "acquisition.delete.delivered.option" });

it("reuses an unexpired final-file inventory when returning to exactly the same deletion options", async () => {
  await openTask();
  fireEvent.click(skipVerificationCheckbox());
  api.preview.mockResolvedValueOnce(cleanupPreview({ deleteDeliveredFiles: true,
    skipFileVerification: false }));
  fireEvent.click(deliveredCheckbox());
  expect(api.preview).toHaveBeenCalledTimes(1);
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.delivered.confirmation");
  expect(api.preview).toHaveBeenCalledTimes(2);
  fireEvent.click(button("acquisition.delete.final.back"));
  fireEvent.click(deliveredCheckbox());
  fireEvent.click(deliveredCheckbox());
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.delivered.confirmation");
  expect(api.preview).toHaveBeenCalledTimes(2);
  expect(screen.queryByText("acquisition.delete.cleanup.listingSkip")).toBeNull();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.remove).toHaveBeenCalledExactlyOnceWith("cleanup-preview", cleanupOpts()));
});

it("does not reuse expired cached final-file inventories", async () => {
  await openTask();
  fireEvent.click(skipVerificationCheckbox());
  const initialNow = Date.now();
  api.preview.mockResolvedValueOnce(cleanupPreview({ deleteDeliveredFiles: true,
    skipFileVerification: false,
    expiresAt: new Date(initialNow + 1000).toISOString() }));
  fireEvent.click(deliveredCheckbox());
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.delivered.confirmation");
  fireEvent.click(button("acquisition.delete.final.back"));
  fireEvent.click(deliveredCheckbox());
  const clock = vi.spyOn(Date, "now").mockReturnValue(initialNow + 2000);
  try {
    api.preview.mockResolvedValueOnce(cleanupPreview({ deleteDeliveredFiles: true, previewId: "fresh-final",
      skipFileVerification: false }));
    fireEvent.click(deliveredCheckbox());
    expect(api.preview).toHaveBeenCalledTimes(2);
    fireEvent.click(next());
    await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(3));
    await screen.findByText("acquisition.delete.delivered.confirmation");
    expect(api.remove).not.toHaveBeenCalled();
  } finally { clock.mockRestore(); }
});

it("refuses a stale final-delete inventory that did not include final files", async () => {
  await openTask();
  fireEvent.click(skipVerificationCheckbox());
  api.preview.mockResolvedValueOnce(cleanupPreview({ skipFileVerification: false }));
  fireEvent.click(deliveredCheckbox());
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.cleanup.inventoryFailed");
  expect(next()).toBeEnabled();
  expect(screen.queryByText("acquisition.delete.final.submit")).toBeNull();
  fireEvent.click(deliveredCheckbox());
  expect(next()).not.toBeDisabled();
  expect(api.remove).not.toHaveBeenCalled();
});

it.each(["final", "cleanup", "skip", "failurePolicy", "close"])(
  "cancels final inventory preparation immediately on %s and ignores its delayed response", async choice => {
    await openTask();
    fireEvent.click(skipVerificationCheckbox());
    let finish!: (value: any) => void;
    api.preview.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    fireEvent.click(deliveredCheckbox());
    fireEvent.click(next());
    const signal = api.preview.mock.calls[1][2] as AbortSignal;
    expect(next()).toBeDisabled();
    fireEvent.click(choice === "close" ? button("common.action.close") : choice === "final" ? deliveredCheckbox()
      : choice === "cleanup" ? cleanupCheckbox() : choice === "skip" ? skipVerificationCheckbox() : checkbox("removeOnFailure"));
    expect(signal.aborted).toBe(true);
    if (choice !== "close") expect(next()).toBeEnabled();
    await act(async () => finish(cleanupPreview({ deleteDeliveredFiles: true,
      skipFileVerification: false })));
    expect(screen.queryByRole("button", { name: "acquisition.delete.final.submit" })).toBeNull();
    expect(screen.queryByText("/work/A1/archive.zip")).toBeNull();
    expect(api.preview).toHaveBeenCalledTimes(2);
    expect(api.remove).not.toHaveBeenCalled();
    if (choice === "close") expect(screen.queryByRole("dialog")).toBeNull();
  },
);

it("does not reopen confirmation after a cached preparation is invalidated in the same event turn", async () => {
  await openTask();
  fireEvent.click(skipVerificationCheckbox());
  api.preview.mockResolvedValueOnce(cleanupPreview({ deleteDeliveredFiles: true,
    skipFileVerification: false }));
  fireEvent.click(deliveredCheckbox());
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.delivered.confirmation");
  fireEvent.click(button("acquisition.delete.final.back"));
  await act(async () => {
    fireEvent.click(next());
    fireEvent.click(skipVerificationCheckbox());
  });
  expect(screen.queryByRole("button", { name: "acquisition.delete.final.submit" })).toBeNull();
  expect(next()).toBeEnabled();
  expect(api.preview).toHaveBeenCalledTimes(2);
  expect(api.remove).not.toHaveBeenCalled();
});

it("reuses an explicitly requested final-file inventory instead of issuing a duplicate Next request", async () => {
  await openTask();
  let finish!: (value: any) => void;
  api.preview.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  fireEvent.click(deliveredCheckbox());
  fireEvent.click(inspect());
  expect(next()).toBeDisabled();
  expect(api.preview).toHaveBeenCalledTimes(2);
  await act(async () => finish(cleanupPreview({ deleteDeliveredFiles: true })));
  expect(next()).toBeEnabled();
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.delivered.confirmation");
  expect(api.preview).toHaveBeenCalledTimes(2);
  expect(api.remove).not.toHaveBeenCalled();
});

it.each(["failure", "wrongTask", "expired"])("never widens to the task-only token when final inventory returns %s", async outcome => {
  await openTask();
  fireEvent.click(skipVerificationCheckbox());
  const fixed = cleanupPreview({ deleteDeliveredFiles: true,
    skipFileVerification: false });
  if (outcome === "failure") api.preview.mockRejectedValueOnce(new Error("scan unavailable"));
  else {
    if (outcome === "wrongTask") fixed.rows[0] = { ...fixed.rows[0], taskId: 999 };
    if (outcome === "expired") fixed.expiresAt = new Date(Date.now() - 1).toISOString();
    api.preview.mockResolvedValueOnce(fixed);
  }
  fireEvent.click(deliveredCheckbox());
  fireEvent.click(next());
  await screen.findByText(outcome === "expired" ? "acquisition.delete.expired" : "acquisition.delete.cleanup.inventoryFailed");
  expect(screen.queryByRole("button", { name: "acquisition.delete.final.submit" })).toBeNull();
  expect(api.preview).toHaveBeenCalledTimes(2);
  expect(api.remove).not.toHaveBeenCalled();
});

it("keeps file cleanup off for selections larger than the server's limit", async () => {
  const ids = Array.from({ length: 101 }, (_, i) => i + 1);
  api.preview.mockResolvedValue(
    preview({ totalCount: ids.length, rows: ids.map((taskId) => ({ taskId, canDelete: true })) }),
  );
  render(<DeleteAcquisitionTasksModal taskIds={ids} />);
  await screen.findByText("acquisition.delete.total");
  expect(cleanupCheckbox()).toBeDisabled();
  expect(cleanupCheckbox()).not.toBeChecked();
  expect(
    screen.queryByRole("checkbox", { name: "acquisition.delete.cleanup.skipVerification" }),
  ).toBeNull();
  await finishReview();
  await screen.findByText("acquisition.delete.result");
  expect(api.remove).toHaveBeenCalledExactlyOnceWith("fixed-preview", {});
});

it("freezes a reviewed file inventory in the final confirmation and never requests it twice", async () => {
  await openTask();
  api.preview.mockResolvedValueOnce(cleanupPreview());
  fireEvent.click(inspect());
  await screen.findByText("/work/A1/archive.zip");
  expect(api.preview).toHaveBeenLastCalledWith(
    [1, 2, 3],
    { cleanupFiles: true, deleteDeliveredFiles:false, skipFileVerification: true },
    expect.any(AbortSignal),
  );
  expect(screen.getAllByText(/part1\/nested.download/).length).toBeGreaterThan(0);
  expect(screen.getByText("acquisition.delete.cleanup.truncated")).toBeVisible();
  expect(screen.getByText("acquisition.delete.cleanup.supersededHint")).toBeVisible();
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.final.boundInventory");
  expect(screen.getByText("acquisition.delete.final.removeOnFailure")).toBeVisible();
  fireEvent.click(confirm());
  await waitFor(() =>
    expect(api.remove).toHaveBeenCalledExactlyOnceWith(
      "cleanup-preview",
      cleanupOpts(),
    ),
  );
  expect(api.preview).toHaveBeenCalledTimes(2);
});

it("polls real cleanup progress only during submission and never repeats or aborts the delete request", async () => {
  vi.useFakeTimers();
  let finish!: (value: unknown) => void;
  api.remove.mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  await act(async () => {
    render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} />);
  });
  expect(api.progress).not.toHaveBeenCalled();
  api.progress.mockResolvedValueOnce({
    previewId: "fixed-preview",
    state: "running",
    phase: "cleaning",
    totalTasks: 3,
    finishedTasks: 1,
    currentTaskId: 2,
    completedFiles: 4,
    completedBytes: 2048,
  });
  await act(async () => fireEvent.click(next()));
  await act(async () => fireEvent.click(confirm()));
  expect(screen.getByText("acquisition.delete.progress.phase.cleaning")).toBeVisible();
  expect(translate).toHaveBeenCalledWith("acquisition.delete.progress.tasks", {
    completed: 1,
    total: 3,
  });
  expect(translate).toHaveBeenCalledWith("acquisition.delete.progress.files", {
    count: 4,
    size: bytesToSize(2048),
  });
  const signal = api.progress.mock.calls[0][1] as AbortSignal;
  expect(signal.aborted).toBe(false);
  await act(async () => vi.advanceTimersByTime(1000));
  expect(api.progress).toHaveBeenCalledTimes(2);
  expect(api.remove).toHaveBeenCalledExactlyOnceWith("fixed-preview", cleanupOpts());
  await act(async () => finish({ deletedTaskIds: [1], skipped: [] }));
  expect(signal.aborted).toBe(true);
  await act(async () => vi.advanceTimersByTime(5000));
  expect(api.progress).toHaveBeenCalledTimes(2);
  expect(api.remove).toHaveBeenCalledTimes(1);
  expect(screen.queryByText("acquisition.delete.progress.phase.cleaning")).toBeNull();
});

it("treats progress failures as read-only and ignores progress for another confirmation", async () => {
  vi.useFakeTimers();
  let finish!: (value: unknown) => void;
  api.remove.mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  api.progress.mockRejectedValueOnce(new Error("offline"));
  await act(async () => {
    render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} />);
  });
  await act(async () => fireEvent.click(next()));
  await act(async () => fireEvent.click(confirm()));
  expect(screen.getByText("acquisition.delete.progress.unavailable")).toBeVisible();
  api.progress.mockResolvedValueOnce({
    previewId: "another-confirmation",
    state: "running",
    phase: "cleaning",
    totalTasks: 999,
    finishedTasks: 999,
    completedFiles: 999,
    completedBytes: 999,
  });
  await act(async () => vi.advanceTimersByTime(1000));
  expect(screen.queryByText("acquisition.delete.progress.tasks")).toBeNull();
  expect(api.remove).toHaveBeenCalledExactlyOnceWith("fixed-preview", cleanupOpts());
  await act(async () => finish({ deletedTaskIds: [1], skipped: [] }));
  expect(screen.getByText("acquisition.delete.result")).toBeVisible();
});

it("aborts a pending progress read on unmount, without canceling the deletion or starting another one", async () => {
  let finish!: (value: unknown) => void;
  api.remove.mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  api.progress.mockReturnValueOnce(new Promise(() => {}));
  const changed = vi.fn();
  const view = render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} onChanged={changed} />);
  await screen.findByText("Completed task");
  await finishReview();
  const signal = api.progress.mock.calls[0][1] as AbortSignal;
  view.unmount();
  expect(signal.aborted).toBe(true);
  expect(api.remove).toHaveBeenCalledExactlyOnceWith("fixed-preview", cleanupOpts());
  await act(async () => finish({ deletedTaskIds: [1], skipped: [] }));
  expect(changed).toHaveBeenCalledTimes(1);
});

it("separates identified totals from eligible cleanup sizes and marks incomplete accounting", async () => {
  render(<DeleteAcquisitionTasksModal taskIds={[1, 2, 3]} />);
  await screen.findByText("Completed task");
  const data = cleanupPreview({
    identifiedFileCount: 3,
    identifiedBytes: 4096,
    cleanupSizeIncomplete: true,
  });
  data.rows[0].cleanup = {
    ...data.rows[0].cleanup,
    identifiedFileCount: 3,
    identifiedBytes: 4096,
    sizeIncomplete: true,
  };
  api.preview.mockResolvedValueOnce(data);
  fireEvent.click(inspect());
  await screen.findByText("acquisition.delete.cleanup.identifiedTotal");
  expect(translate).toHaveBeenCalledWith("acquisition.delete.cleanup.identifiedTotal", {
    count: 3,
    size: bytesToSize(4096),
  });
  expect(translate).toHaveBeenCalledWith("acquisition.delete.cleanup.taskIdentified", {
    count: 3,
    size: bytesToSize(4096),
  });
  expect(translate).toHaveBeenCalledWith("acquisition.delete.cleanup.total", {
    count: 2,
    size: bytesToSize(1024),
  });
  expect(translate).toHaveBeenCalledWith("acquisition.delete.cleanup.taskTotal", {
    count: 2,
    size: bytesToSize(1024),
  });
  expect(screen.getAllByText("acquisition.delete.cleanup.incompleteSize")).toHaveLength(2);
  expect(api.remove).not.toHaveBeenCalled();
});

it.each([false, true])(
  "reports exactly which records and directories remain (remove on failure = %s)",
  async (hide) => {
    await openTask();
    if (!hide) fireEvent.click(checkbox("removeOnFailure"));
    api.remove.mockResolvedValueOnce({
      deletedTaskIds: hide ? [1] : [],
      skipped: hide ? [] : [{ taskId: 1, reasonCode: "CLEANUP_FILES_RETAINED" }],
      removedFiles: 1,
      removedBytes: 512,
      cleanupResults: [
        {
          taskId: 1,
          completed: false,
          removedFiles: 1,
          removedBytes: 512,
          closedRunIds: [7],
          retainedDirectories: ["/work/retained"],
          reasonCodes: ["ReferencedPath", "UNSAFE_RAW_UPSTREAM_TEXT"],
        },
      ],
    });
    await finishReview();
    await screen.findByText(
      hide ? "acquisition.delete.cleanup.partialRemoved" : "acquisition.delete.cleanup.partial",
    );
    expect(screen.getByText("acquisition.delete.cleanup.result")).toBeVisible();
    expect(screen.getByText("acquisition.delete.cleanup.closedRuns")).toBeVisible();
    expect(screen.getByText("acquisition.delete.reason.unknown")).toBeVisible();
    expect(screen.getByText("/work/retained")).toBeVisible();
    expect(screen.queryByText("UNSAFE_RAW_UPSTREAM_TEXT")).toBeNull();
    if (hide) expect(screen.queryByText("acquisition.delete.cleanup.retry")).toBeNull();
    else {
      api.preview.mockResolvedValueOnce(preview({ previewId: "retry-token" }));
      fireEvent.click(button("acquisition.delete.cleanup.retry"));
      await waitFor(() => expect(next()).toBeEnabled());
    }
    expect(api.remove).toHaveBeenCalledTimes(1);
  },
);

it("keeps all deletion protection reasons translated and unknown reasons generic", () => {
  for (const code of deletionReasonCodes) {
    const key = `acquisition.delete.reason.${code}` as keyof typeof cn;
    expect(cn[key], key).toBeTruthy();
    expect(en[key], key).toBeTruthy();
  }
  const t: any = (key: string) => key;
  expect(deletionReasonLabel(t, "future.reason")).toBe("acquisition.delete.reason.unknown");
});

it("translates the confirmation and every progress phase", () => {
  for (const suffix of [
    "final.title",
    "final.removeOnFailure",
    "final.keepOnFailure",
    "cleanup.partialRemoved",
    "retained.preserveHistory",
    "retained.scopeRequired",
    "progress.starting",
    "progress.phase.rechecking",
    "progress.phase.cleaning",
    "progress.phase.deletingRecord",
    "progress.phase.completed",
    "progress.phase.interrupted",
    "receipt.checking",
    "receipt.interrupted",
    "receipt.completed",
    "receipt.preview",
    "receipt.expired",
    "receipt.unavailable",
    "receipt.checkAgain",
    "progress.tasks",
    "progress.files",
    "progress.safety",
    "progress.unavailable",
  ]) {
    const key = `acquisition.delete.${suffix}` as keyof typeof cn;
    expect(cn[key], key).toBeTruthy();
    expect(en[key], key).toBeTruthy();
  }
});

it("reviews only removed tasks with retained files and requires a fresh file-bound confirmation", async () => {
  await openTask();
  api.remove.mockResolvedValueOnce({
    deletedTaskIds: [1], skipped: [{ taskId: 2, reasonCode: "ACQUISITION_TASK_ACTIVE" }],
    cleanupResults: [{ taskId: 1, completed: false, removedFiles: 0, removedBytes: 0,
      reasonCodes: ["ReferencedPath"], retainedDirectories: ["/work/A1"] }],
  });
  await finishReview();
  const recheck = await screen.findByRole("button", { name: "acquisition.delete.retained.title" });
  const data = cleanupPreview();
  api.preview.mockResolvedValueOnce({ ...data, previewId: "retained-inventory", rows: data.rows.slice(0, 1),
    retryRetainedFiles: true, totalCount: 1, deletableCount: 1, blockedCount: 0 });
  const options = { cleanupFiles: true, deleteDeliveredFiles: false, skipFileVerification: true, retryRetainedFiles: true };
  fireEvent.click(recheck);
  await screen.findByText("/work/A1/archive.zip");
  expect(api.preview).toHaveBeenLastCalledWith([1], options, expect.any(AbortSignal));
  expect(api.remove).toHaveBeenCalledTimes(1);
  expect(screen.queryByText("acquisition.delete.cleanup.removeOnFailure")).toBeNull();
  fireEvent.click(next());
  expect(api.remove).toHaveBeenCalledTimes(1);
  api.remove.mockResolvedValueOnce({ deletedTaskIds: [], skipped: [], removedFiles: 1, removedBytes: 1024,
    cleanupResults: [{ taskId: 1, completed: true, reasonCodes: [], removedFiles: 1, removedBytes: 1024 }] });
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.delete.retained.confirm" }));
  await screen.findByText("acquisition.delete.retained.result");
  expect(api.remove).toHaveBeenCalledTimes(2);
  expect(api.remove).toHaveBeenLastCalledWith("retained-inventory", {});
  expect(screen.queryByRole("button", { name: "acquisition.delete.retained.title" })).toBeNull();
});

it("lets a reopened deleted task explicitly select final-only cleanup and requires its new bound inventory", async () => {
  const model = cleanupPreview();
  const options = { cleanupFiles: true, skipFileVerification: false, retryRetainedFiles: true };
  const retained = (values: any, token: string) => ({ ...model, ...values, previewId: token,
    rows: model.rows.slice(0, 1), totalCount: 1, deletableCount: 1, blockedCount: 0 });
  api.preview.mockResolvedValueOnce(retained(options, "initial-retained"));
  render(<DeleteAcquisitionTasksModal taskIds={[1]} retryRetainedFiles />);
  await screen.findByText("/work/A1/archive.zip");
  expect(deliveredCheckbox()).not.toBeChecked();
  expect(cleanupCheckbox()).toBeChecked();
  expect(cleanupCheckbox()).toBeDisabled();
  fireEvent.click(deliveredCheckbox());
  expect(api.preview).toHaveBeenCalledTimes(1);
  const finalOptions = { ...options, cleanupFiles: false, deleteDeliveredFiles: true };
  const finalPreview = retained(finalOptions, "final-retained");
  finalPreview.rows[0] = { ...finalPreview.rows[0], cleanup: { ...finalPreview.rows[0].cleanup,
    groups: [{ ...model.rows[0].cleanup.groups[0], kind: "publishedFiles", directory: "/Acquired/A1",
      files: [{ path: "/Acquired/A1/final.jpg", bytes: 1024 }] }] } };
  let finish!: (value: any) => void;
  api.preview.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  fireEvent.click(cleanupCheckbox());
  expect(screen.queryByText("/work/A1/archive.zip")).toBeNull();
  expect(deliveredCheckbox()).toBeChecked();
  expect(deliveredCheckbox()).toBeDisabled();
  expect(cleanupCheckbox()).not.toBeChecked();
  expect(api.preview).toHaveBeenCalledTimes(1);
  expect(api.remove).not.toHaveBeenCalled();
  fireEvent.click(next());
  expect(next()).toBeDisabled();
  expect(api.preview).toHaveBeenLastCalledWith([1], finalOptions, expect.any(AbortSignal));
  await act(async () => finish(finalPreview));
  await screen.findByText("/Acquired/A1/final.jpg");
  expect(screen.getByText("acquisition.delete.delivered.confirmation")).toBeVisible();
  expect(screen.getByText("acquisition.delete.retained.preserveHistory")).toBeVisible();
  expect(screen.queryByText("acquisition.delete.retained.preserve")).toBeNull();
  fireEvent.click(button("acquisition.delete.retained.confirm"));
  await waitFor(() => expect(api.remove).toHaveBeenCalledExactlyOnceWith("final-retained", {}));
});

it("hides an initial retained inventory if options changed while it was loading, then prepares only the current scope", async () => {
  const model = cleanupPreview();
  const initial = { cleanupFiles: true, skipFileVerification: false, retryRetainedFiles: true };
  let finish!: (value: any) => void;
  api.preview.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  render(<DeleteAcquisitionTasksModal taskIds={[1]} retryRetainedFiles />);
  fireEvent.click(deliveredCheckbox());
  expect(api.preview).toHaveBeenCalledTimes(1);
  await act(async () => finish({ ...model, ...initial, rows: model.rows.slice(0, 1), totalCount: 1 }));
  await screen.findByText("Completed task");
  expect(screen.queryByText("/work/A1/archive.zip")).toBeNull();
  expect(screen.queryByText("acquisition.delete.cleanup.identifiedTotal")).toBeNull();
  expect(screen.queryByText("acquisition.delete.retained.ready")).toBeNull();
  expect(screen.getByText("acquisition.delete.cleanup.prepareNext")).toBeVisible();
  api.preview.mockResolvedValueOnce({ ...model, ...initial, deleteDeliveredFiles: true,
    previewId: "new-current-scope", rows: model.rows.slice(0, 1), totalCount: 1 });
  fireEvent.click(next());
  await screen.findByRole("button", { name: "acquisition.delete.retained.confirm" });
  expect(api.preview).toHaveBeenCalledTimes(2);
  expect(api.preview).toHaveBeenLastCalledWith([1], { ...initial, deleteDeliveredFiles: true }, expect.any(AbortSignal));
  expect(screen.getByText("/work/A1/archive.zip")).toBeVisible();
  expect(api.remove).not.toHaveBeenCalled();
});

it("can prepare another retained review for the same task after a retained cleanup still reports files", async () => {
  const model = cleanupPreview();
  api.preview.mockImplementation(async (_ids, options) => ({ ...model, ...options,
    previewId: `retained-${api.preview.mock.calls.length}`, rows: model.rows.slice(0, 1), totalCount: 1 }));
  render(<DeleteAcquisitionTasksModal taskIds={[1]} retryRetainedFiles />);
  await screen.findByText("/work/A1/archive.zip");
  api.remove.mockResolvedValueOnce({ deletedTaskIds: [], skipped: [], cleanupResults: [{
    taskId: 1, completed: false, removedFiles: 0, removedBytes: 0, reasonCodes: ["ReferencedPath"],
  }] });
  fireEvent.click(next());
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.delete.retained.confirm" }));
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.delete.retained.title" }));
  await screen.findByText("/work/A1/archive.zip");
  expect(api.preview).toHaveBeenCalledTimes(2);
  expect(next()).toBeEnabled();
  expect(api.remove).toHaveBeenCalledTimes(1);
});

it("offers the same task-wide download, processing and delivery scopes for every stage", async () => {
  const rows = ["download", "processing", "delivery", "completed"].map((title, i) =>
    ({taskId: i + 10, title, canDelete: true, willStopExecution: i === 0}));
  const ids = rows.map(row => row.taskId);
  api.preview.mockImplementation(async (_ids, options) => preview({rows, totalCount: 4, deletableCount: 4,
    previewId: options.cleanupFiles ? "all-associated-files" : "overview", ...options}));
  render(<DeleteAcquisitionTasksModal taskIds={ids} />);
  await screen.findByText("download");
  expect(cleanupCheckbox()).toBeChecked();
  expect(skipVerificationCheckbox()).toBeChecked();
  expect(checkbox("removeOnFailure")).toBeChecked();
  expect(deliveredCheckbox()).not.toBeChecked();
  fireEvent.click(deliveredCheckbox());
  fireEvent.click(skipVerificationCheckbox());
  for (const removed of ["selected.option", "abandon.option", "cleanup.accepted", "delivered.accepted"])
    expect(screen.queryByRole("checkbox", {name:`acquisition.delete.${removed}`})).toBeNull();
  expect(api.preview).toHaveBeenCalledTimes(1);
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.final.boundInventory");
  expect(screen.getByText("acquisition.delete.stopOnConfirm")).toBeVisible();
  expect(screen.getByText("acquisition.delete.delivered.afterStop")).toBeVisible();
  expect(api.preview).toHaveBeenLastCalledWith(ids,
    {cleanupFiles:true, deleteDeliveredFiles:true, skipFileVerification:false}, expect.any(AbortSignal));
  expect(api.remove).not.toHaveBeenCalled();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.remove).toHaveBeenCalledExactlyOnceWith("all-associated-files", {removeTaskIfCleanupFails:true}));
});

it("can delete only delivery files from an unfinished task with no acceptance or file picker", async () => {
  const rows = [{taskId:2, title:"Waiting for delivery", canDelete:true}];
  api.preview.mockImplementation(async (_ids, options) => preview({rows, ...options, previewId:options.deleteDeliveredFiles ? "delivery-only" : "task"}));
  render(<DeleteAcquisitionTasksModal taskIds={[2]} />);
  await screen.findByText("Waiting for delivery");
  fireEvent.click(cleanupCheckbox());
  fireEvent.click(deliveredCheckbox());
  fireEvent.click(checkbox("removeOnFailure"));
  fireEvent.click(next());
  fireEvent.click(await screen.findByRole("button", {name:"acquisition.delete.final.submit"}));
  expect(api.preview).toHaveBeenLastCalledWith([2],
    {cleanupFiles:false,deleteDeliveredFiles:true,skipFileVerification:true}, expect.any(AbortSignal));
  await waitFor(() => expect(api.remove).toHaveBeenCalledExactlyOnceWith("delivery-only", {removeTaskIfCleanupFails:false}));
});

it.each([
  {cleanupFiles:false}, {deleteDeliveredFiles:true}, {skipFileVerification:false},
  {rows:preview().rows.slice(0,2)}, {rows:[preview().rows[0],preview().rows[0],preview().rows[2]]},
])("requires a receipt for the reviewed tasks and cleanup options (%j)", async mismatch => {
  await openTask();
  api.preview.mockResolvedValueOnce(cleanupPreview(mismatch));
  fireEvent.click(next());
  await screen.findByText("acquisition.delete.cleanup.inventoryFailed");
  expect(screen.queryByRole("button", {name:"acquisition.delete.final.submit"})).toBeNull();
  expect(api.remove).not.toHaveBeenCalled();
});
