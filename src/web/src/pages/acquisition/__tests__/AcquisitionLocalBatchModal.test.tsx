import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import AcquisitionLocalBatchModal from "../components/AcquisitionLocalBatchModal";

const api = vi.hoisted(() => ({
  preview: vi.fn(),
  create: vi.fn(),
  list: vi.fn(),
  get: vi.fn(),
  pause: vi.fn(),
  resume: vi.fn(),
  cancel: vi.fn(),
  capacity: vi.fn(),
}));
vi.mock("../components/acquisitionLocalBatchApi", () => ({
  acquisitionLocalBatchApi: api,
  localBatchSelectionLimit: 100,
}));
vi.mock("@/components/utils", () => ({ bytesToSize: (bytes: number) => `${bytes} bytes` }));
vi.mock("@/components/Workflow/displayNames", () => ({ activityDisplayName: (_t: unknown, kind: string) => kind }));
vi.mock("@/components/bakaui", () => ({
  Tabs: ({children}: any) => <div>{children}</div>, Tab: () => null,
  Modal: ({ visible, title, children, footer, onClose }: any) =>
    visible ? (
      <section role="dialog" aria-label={title}>
        <button onClick={onClose}>modal-close</button>
        {children}
        {footer}
      </section>
    ) : null,
  Button: ({ children, onPress, isDisabled, isLoading, ...props }: any) => (
    <button aria-label={props["aria-label"]} disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Checkbox: ({ children, isSelected, isIndeterminate, isDisabled, onValueChange, ...props }: any) => (
    <label>
      <input
        type="checkbox"
        aria-label={props["aria-label"]}
        data-indeterminate={isIndeterminate || undefined}
        checked={isSelected}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Pagination: ({ page, total, onChange }: any) => <button onClick={() => onChange(page + 1)}>page {page}/{total}</button>,
  Chip: ({ children }: any) => <span>{children}</span>,
  Select: ({ selectedKeys, dataSource, onSelectionChange, isDisabled, ...props }: any) => <select
    aria-label={props["aria-label"]} disabled={isDisabled} value={Array.from(selectedKeys)[0] as string}
    onChange={event => onSelectionChange(new Set([event.target.value]))}>
    {dataSource.map((item: any) => <option key={item.value} value={item.value}>{item.label}</option>)}
  </select>,
  Tooltip: ({ children }: any) => children,
  Spinner: (props: any) => <span role="status" aria-label={props["aria-label"]} />,
}));
const GiB = 1024 ** 3;
const row = (taskId: number, overrides = {}) => ({
  taskId,
  title: `Task ${taskId}`,
  workflowRunId: taskId + 100,
  canQueue: true,
  reasonCode: "LOCAL_BATCH_READY",
  estimatedBytes: taskId * 1000,
  estimatePartial: false,
  recoveryScope: "workflow",
  workingDirectory: "/work/process",
  deliveryDirectory: "/work/Acquired",
  ...overrides,
});
const preview = (overrides = {}) => ({
  previewToken: "preview-1",
  expiresAt: new Date(Date.now() + 600_000).toISOString(),
  rows: [row(1), row(2)],
  requiresSpaceConfirmation: true,
  estimateIncomplete: false,
  volumes: [
    {
      volumeId: "ssd",
      paths: ["/work/process", "/work/Acquired"],
      availableBytes: 51 * GiB,
      totalBytes: 2 * 1024 * GiB,
      minimumFreeBytes: 50 * GiB,
      usableBytes: GiB,
      estimatedAdditionalBytes: 2 * GiB,
      estimateIncomplete: false,
      exceedsUsable: true,
    },
  ],
  ...overrides,
});
const batch = (overrides = {}) => ({
  batchId: "batch-1",
  createdAt: "2026-11-06T01:00:00Z",
  updatedAt: "2026-11-06T01:00:00Z",
  status: "running",
  allowStorageOverride: false,
  overrideExpiresAt: null,
  currentTaskId: 1,
  rows: [1, 2].map((taskId, order) => ({
    taskId,
    title: `Task ${taskId}`,
    order,
    status: order ? "queued" : "running",
    workflowRunId: taskId + 100,
    estimatedBytes: taskId * 1000,
    reasonCode: null,
  })),
  ...overrides,
});
const show = (props = {}) =>
  render(<AcquisitionLocalBatchModal taskIds={[1, 2]} onDestroyed={vi.fn()} {...props} />);
const confirm = () => screen.getByRole("button", { name: "acquisition.localBatch.confirm" });
beforeEach(() => {
  vi.clearAllMocks();
  api.preview.mockReset().mockResolvedValue(preview());
  api.create.mockReset().mockResolvedValue(batch());
  api.list.mockReset().mockResolvedValue([batch()]);
  api.get.mockReset().mockResolvedValue(batch());
  api.pause
    .mockReset()
    .mockResolvedValue(batch({ status: "paused", updatedAt: "2026-11-06T01:00:01Z" }));
  api.resume.mockReset().mockResolvedValue(batch());
  api.cancel.mockReset().mockResolvedValue(batch({ status: "cancelled" }));
  api.capacity.mockReset().mockImplementation(async (previewToken, taskIds) => ({
    previewToken, taskIds, volumes: preview().volumes, requiresSpaceConfirmation: true, estimateIncomplete: false,
  }));
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

it("keeps oversized selections intact and offers an explicit scope change without requesting a truncated preview", async () => {
  const change = vi.fn();
  show({ taskIds: Array.from({ length: 101 }, (_, index) => index + 1), continuationScope: "localQueue",
    onContinuationScopeChange: change });
  await screen.findByText("acquisition.continueMode.localLimit");
  expect(api.preview).not.toHaveBeenCalled();
  expect(api.create).not.toHaveBeenCalled();
  expect(confirm()).toBeDisabled();
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.continueMode.label" }),
    { target: { value: "workflow" } });
  expect(change).toHaveBeenCalledExactlyOnceWith("workflow");
});

it("locks mode navigation while queue creation is in flight", async () => {
  const change = vi.fn();
  let finish!: (value: unknown) => void;
  api.create.mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
  show({ continuationScope: "localQueue", onContinuationScopeChange: change });
  await screen.findByText("Task 1");
  fireEvent.click(confirm());
  expect(screen.getByRole("combobox", { name: "acquisition.continueMode.label" })).toBeDisabled();
  expect(change).not.toHaveBeenCalled();
  await act(async () => finish(batch()));
});

it("opens a read-only fixed preview and keeps space permission unchecked even when space is insufficient", async () => {
  render(
    <StrictMode>
      <AcquisitionLocalBatchModal taskIds={[2, 1, 2]} onDestroyed={vi.fn()} />
    </StrictMode>,
  );
  await screen.findByText("Task 1");
  expect(api.preview).toHaveBeenCalledTimes(1);
  expect(api.preview).toHaveBeenCalledWith([2, 1], expect.any(AbortSignal));
  expect(screen.getByRole("checkbox")).not.toBeChecked();
  expect(api.create).not.toHaveBeenCalled();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.create).toHaveBeenCalledWith("preview-1", [1, 2], false, undefined));
});

it("sends a scoped override only after the explicit checkbox is checked", async () => {
  show();
  await screen.findByText("Task 1");
  fireEvent.click(screen.getByRole("checkbox"));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.localBatch.confirmOverride" }));
  await waitFor(() => expect(api.create).toHaveBeenCalledWith("preview-1", [1, 2], true, undefined));
});

it("does not offer an unnecessary override when space estimates are sufficient and complete", async () => {
  api.preview.mockResolvedValue(
    preview({
      requiresSpaceConfirmation: false,
      volumes: [{ ...preview().volumes[0], exceedsUsable: false, estimatedAdditionalBytes: 1024 }],
    }),
  );
  show();
  await screen.findByText("Task 1");
  expect(screen.queryByRole("checkbox")).toBeNull();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.create).toHaveBeenCalledWith("preview-1", [1, 2], false, undefined));
});

it("keeps a volume with many destination paths compact before the space acknowledgement", async () => {
  api.preview.mockResolvedValue(
    preview({
      volumes: [
        {
          ...preview().volumes[0],
          paths: Array.from({ length: 100 }, (_, index) => `/work/Acquired/destination-${index}`),
        },
      ],
    }),
  );
  show();
  await screen.findByText("Task 1");
  expect(screen.getByText("/work/Acquired/destination-0")).toHaveClass("truncate");
  expect(screen.getByText("acquisition.localBatch.space.morePaths")).toBeVisible();
  expect(screen.queryByText("/work/Acquired/destination-99")).toBeNull();
  expect(screen.getByRole("checkbox")).not.toBeChecked();
});

it("accepts a prepared local-file revision without inventing a previous run", async () => {
  api.preview.mockResolvedValue(
    preview({ rows: [row(1, { recoveryScope: "localFiles", workflowRunId: null }), row(2)] }),
  );
  show();
  await screen.findByText("Task 1");
  expect(confirm()).toBeEnabled();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.create).toHaveBeenCalledWith("preview-1", [1, 2], false, undefined));
});

it("keeps unknown and partial estimates visible and excludes unready tasks from the frozen submission", async () => {
  api.preview.mockResolvedValue(
    preview({
      estimateIncomplete: true,
      rows: [
        row(1, { estimatedBytes: null, estimatePartial: true }),
        row(2, { canQueue: false, recoveryScope: null }),
      ],
    }),
  );
  show();
  await screen.findByText("Task 1");
  expect(screen.getByText("acquisition.size.unknown")).toBeVisible();
  expect(screen.getByText("acquisition.size.partial.label")).toBeVisible();
  expect(screen.getAllByText("acquisition.localBatch.space.unknown").length).toBeGreaterThan(0);
  fireEvent.click(confirm());
  await waitFor(() => expect(api.create).toHaveBeenCalledWith("preview-1", [1], false, undefined));
});

it.each([
  { rows: [row(1)] },
  { rows: [row(1), row(1)] },
  { rows: [row(1), row(3)] },
  { rows: [row(1), row(2, { canQueue: "true" })] },
  { rows: [row(1), row(2, { recoveryScope: "newUnknownScope" })] },
  { volumes: [{ ...preview().volumes[0], usableBytes: undefined }] },
])("rejects incomplete or mismatched preview evidence: %j", async (invalid) => {
  api.preview.mockResolvedValue(preview(invalid));
  show();
  await screen.findByRole("alert");
  expect(confirm()).toBeDisabled();
  expect(api.create).not.toHaveBeenCalled();
});

it("requires fresh acknowledgement after rechecking and refuses expired preview submission", async () => {
  show();
  await screen.findByText("Task 1");
  fireEvent.click(screen.getByRole("checkbox"));
  api.preview.mockResolvedValue(
    preview({ previewToken: "preview-2", expiresAt: "2000-01-01T00:00:00Z" }),
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.localBatch.refreshPreview" }));
  await screen.findByText("acquisition.localBatch.expired");
  expect(screen.getByRole("checkbox")).not.toBeChecked();
  expect(confirm()).toBeDisabled();
});

it("does not resubmit a consumed token when queue creation has an indeterminate outcome", async () => {
  api.create.mockRejectedValue(new Error("network lost"));
  show();
  await screen.findByText("Task 1");
  fireEvent.click(confirm());
  await screen.findByText("acquisition.localBatch.error.create");
  expect(confirm()).toBeDisabled();
  fireEvent.click(confirm());
  expect(api.create).toHaveBeenCalledTimes(1);
});

it("closing the queue never cancels running or remaining tasks", async () => {
  show({ taskIds: undefined });
  fireEvent.click(await screen.findByRole("button", { name: /acquisition.localBatch.selected/ }));
  expect(screen.getByText("acquisition.localBatch.backgroundHint")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
  expect(api.cancel).not.toHaveBeenCalled();
  expect(api.pause).not.toHaveBeenCalled();
  expect(screen.queryByRole("dialog")).toBeNull();
});

it("cancels only after its separate confirmation and describes retaining the active task", async () => {
  show({ taskIds: undefined });
  fireEvent.click(await screen.findByRole("button", { name: /acquisition.localBatch.selected/ }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.localBatch.cancel" }));
  expect(screen.getByText("acquisition.localBatch.cancelHint")).toBeVisible();
  expect(api.cancel).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.localBatch.confirmCancel" }));
  await waitFor(() => expect(api.cancel).toHaveBeenCalledWith("batch-1"));
});

it("does not let a poll started before pause overwrite the newer action result", async () => {
  vi.useFakeTimers();
  let resolvePoll!: (value: unknown) => void;
  api.get.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        resolvePoll = resolve;
      }),
  );
  const onChanged = vi.fn();
  await act(async () => {
    show({ taskIds: undefined, onChanged });
  });
  fireEvent.click(screen.getByRole("button", { name: /acquisition.localBatch.selected/ }));
  await act(async () => {
    vi.advanceTimersByTime(4000);
  });
  expect(api.get).toHaveBeenCalledTimes(1);
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "acquisition.localBatch.pause" }));
  });
  expect(screen.getByText("acquisition.localBatch.status.paused")).toBeVisible();
  await act(async () => {
    resolvePoll(batch());
  });
  expect(screen.getByText("acquisition.localBatch.status.paused")).toBeVisible();
  expect(onChanged).toHaveBeenCalledTimes(1);
});

it("does not poll completed queues or refresh the task list for an unchanged queue", async () => {
  vi.useFakeTimers();
  const onChanged = vi.fn();
  await act(async () => {
    show({ taskIds: undefined, onChanged });
  });
  fireEvent.click(screen.getByRole("button", { name: /acquisition.localBatch.selected/ }));
  await act(async () => {
    vi.advanceTimersByTime(4000);
  });
  expect(onChanged).not.toHaveBeenCalled();
  api.get.mockResolvedValue(batch({ status: "completed" }));
  await act(async () => {
    vi.advanceTimersByTime(4000);
  });
  expect(onChanged).toHaveBeenCalledTimes(1);
  const count = api.get.mock.calls.length;
  await act(async () => {
    vi.advanceTimersByTime(12000);
  });
  expect(api.get).toHaveBeenCalledTimes(count);
});

it("labels an ended queue separately from its tasks that still need attention", async () => {
  api.list.mockResolvedValue([
    batch({
      status: "completed",
      rows: [
        {
          taskId: 1,
          title: "Needs a password",
          order: 0,
          status: "attention",
          estimatedBytes: 1,
          reasonCode: "LOCAL_BATCH_REVIEW_REQUIRED",
        },
      ],
    }),
  ]);
  show({ taskIds: undefined });
  fireEvent.click(await screen.findByRole("button", { name: /acquisition.localBatch.selected/ }));
  expect(screen.getByText("acquisition.localBatch.ended")).toBeVisible();
  expect(screen.getByText("acquisition.localBatch.status.attention")).toBeVisible();
  expect(screen.queryByText("acquisition.localBatch.status.completed")).toBeNull();
});

it("opens an existing blocked queue directly without creating another queue or implying it is running", async () => {
  api.get.mockResolvedValue(batch({ status: "spaceBlocked", currentTaskId: null }));
  const back = vi.fn();
  render(<AcquisitionLocalBatchModal initialBatchId="batch-1" onBackToSelection={back} />);
  await screen.findByText("acquisition.continueActions.queueNotRunning");
  expect(api.get).toHaveBeenCalledExactlyOnceWith("batch-1");
  expect(api.preview).not.toHaveBeenCalled();
  expect(api.create).not.toHaveBeenCalled();
  expect(api.resume).not.toHaveBeenCalled();
  expect(screen.getByRole("button", { name: "acquisition.localBatch.resume" })).toBeEnabled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueActions.back" }));
  expect(back).toHaveBeenCalledTimes(1);
});

const automaticRows = () => [
  row(1, { actionCategory: "localProcessing", recoveryScope: "workflow", currentStepKind: "fs.assessDelivery", canOverrideStorage: true }),
  row(2, { actionCategory: "workflow", canOverrideStorage: false }),
];

it("distinguishes original download retries from local-only observation and other workflow checkpoints", async () => {
  api.preview.mockResolvedValue(preview({ rows: [
    row(1, { actionCategory: "workflow", currentStepKind: "download.awaitCompletion", canOverrideStorage: true }),
    row(2, { actionCategory: "workflow", currentStepKind: "download.selectSources", canOverrideStorage: false }),
    row(3, { actionCategory: "localProcessing", currentStepKind: "download.awaitCompletion", canOverrideStorage: true }),
  ] }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2, 3]} />);
  const download = within(await screen.findByTestId("continuation-task-1"));
  expect(download.getByText("acquisition.continueActions.action.resumeDownload")).toBeVisible();
  expect(download.getByText("acquisition.continueActions.originalDownloadHint")).toBeVisible();
  expect(download.queryByText("acquisition.continueActions.action.resumeLocal")).toBeNull();
  expect(download.queryByText("acquisition.localBatch.reason.unknown")).toBeNull();
  const workflow = within(screen.getByTestId("continuation-task-2"));
  expect(workflow.getByText("acquisition.continueActions.action.workflow")).toBeVisible();
  expect(workflow.getByText("acquisition.recovery.reason.CONTINUATION_READY")).toBeVisible();
  const local = within(screen.getByTestId("continuation-task-3"));
  expect(local.getByText("acquisition.continueActions.action.resumeLocal")).toBeVisible();
  expect(local.queryByText("acquisition.continueActions.originalDownloadHint")).toBeNull();
  expect(api.create).not.toHaveBeenCalled();
});

it("keeps automatic processing visible but disabled until a complete preview is available", () => {
  api.preview.mockReturnValue(new Promise(() => {}));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1]} />);
  const all = screen.getByRole("checkbox", { name: "acquisition.continueActions.all" });
  expect(all).toBeDisabled();
  expect(all).not.toBeChecked();
  expect(api.create).not.toHaveBeenCalled();
});

it("selects actual automatic actions, greys out excluded tasks and submits only the remaining task once", async () => {
  api.preview.mockResolvedValue(preview({ rows: automaticRows(), requiresSpaceConfirmation: false }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  await screen.findByRole("checkbox", { name: "acquisition.continueActions.category.workflow" });
  const all = screen.getByRole("checkbox", { name: "acquisition.continueActions.all" });
  expect(all).toBeChecked();
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.continueActions.category.workflow" }));
  expect(all).toHaveAttribute("data-indeterminate", "true");
  expect(screen.getByTestId("continuation-task-2")).toHaveClass("bg-default-50");
  expect(screen.getByText("acquisition.continueActions.excluded")).toBeVisible();
  await waitFor(() => expect(screen.getByRole("button", { name: "acquisition.continueActions.confirm" })).toBeEnabled());
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueActions.confirm" }));
  await waitFor(() => expect(api.create).toHaveBeenCalledExactlyOnceWith("preview-1", [1], false, undefined));
});

it("rejects stale or mismatched capacity results and never submits an old selection", async () => {
  const pending: { ids: number[]; resolve: (value: unknown) => void }[] = [];
  api.capacity.mockImplementation((_token, ids) => new Promise(resolve => pending.push({ ids, resolve })));
  api.preview.mockResolvedValue(preview({ rows: automaticRows() }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  await waitFor(() => expect(pending).toHaveLength(1));
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.continueActions.category.workflow" }));
  await waitFor(() => expect(pending).toHaveLength(2));
  const capacity = (ids: number[]) => ({ previewToken: "preview-1", taskIds: ids, volumes: [], requiresSpaceConfirmation: false, estimateIncomplete: false });
  await act(async () => pending[0].resolve(capacity([1, 2])));
  expect(screen.getByRole("button", { name: "acquisition.continueActions.confirm" })).toBeDisabled();
  await act(async () => pending[1].resolve(capacity([1, 99])));
  await screen.findByText("acquisition.continueActions.capacityFailed");
  expect(screen.getByRole("button", { name: "acquisition.continueActions.confirm" })).toBeDisabled();
  expect(api.create).not.toHaveBeenCalled();
});

it("requires a new explicit storage acknowledgement after the selected tasks change", async () => {
  api.preview.mockResolvedValue(preview({ rows: automaticRows() }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  const acknowledgement = await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  expect(acknowledgement).not.toBeChecked();
  fireEvent.click(acknowledgement);
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.continueActions.category.workflow" }));
  const fresh = await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  expect(fresh).not.toBeChecked();
  await waitFor(() => expect(api.capacity).toHaveBeenLastCalledWith("preview-1", [1], expect.any(AbortSignal), undefined));
  fireEvent.click(fresh);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.localBatch.confirmOverride" }));
  await waitFor(() => expect(api.create).toHaveBeenCalledExactlyOnceWith("preview-1", [1], true, undefined));
});

it("displays the owning blocked queue and inspects it without resuming or duplicate submission", async () => {
  const queue = { kind: "localQueue", batchId: "previous-queue", status: "spaceBlocked", rowStatus: "queued", canRequeue: true };
  api.preview.mockResolvedValue(preview({ rows: [row(1, { actionCategory: "localProcessing", canOverrideStorage: true, queue })] }));
  const inspect = vi.fn();
  render(<AcquisitionLocalBatchModal automatic taskIds={[1]} onOpenQueue={inspect} />);
  expect(await screen.findByText("acquisition.localBatch.status.spaceBlocked")).toBeVisible();
  expect(screen.getByText("acquisition.continueActions.requeueHint")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueActions.viewQueue" }));
  expect(inspect).toHaveBeenCalledExactlyOnceWith(queue);
  expect(api.create).not.toHaveBeenCalled();
  expect(api.resume).not.toHaveBeenCalled();
});

it("keeps the automatic master visible but never executes an unknown future action", async () => {
  api.preview.mockResolvedValue(preview({ rows: [row(1, { actionCategory: "futureAction", canOverrideStorage: false })] }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1]} />);
  await screen.findByText("acquisition.continueActions.action.unavailable");
  const all = screen.getByRole("checkbox", { name: "acquisition.continueActions.all" });
  expect(all).toBeDisabled();
  expect(all).not.toBeChecked();
  expect(screen.queryByRole("checkbox", { name: "acquisition.continueActions.category.workflow" })).toBeNull();
  expect(screen.getByRole("button", { name: "acquisition.continueActions.confirm" })).toBeDisabled();
  expect(api.create).not.toHaveBeenCalled();
});

it("restores previous action and per-task choices without restoring a storage override", async () => {
  api.preview.mockResolvedValue(preview({ rows: automaticRows() }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]}
    initialSelection={{ actions: ["localProcessing"], excludedTaskIds: [1] }} />);
  await screen.findByRole("checkbox", { name: "acquisition.continueActions.category.localProcessing" });
  const all = screen.getByRole("checkbox", { name: "acquisition.continueActions.all" });
  expect(all).not.toBeChecked();
  expect(screen.getByRole("button", { name: "acquisition.continueActions.confirm" })).toBeDisabled();
  expect(screen.queryByRole("checkbox", { name: "acquisition.reserve.operation.label" })).toBeNull();
  expect(api.create).not.toHaveBeenCalled();
});

it("inherits the global reserve and refreshes bound capacity before applying a configured override", async () => {
  api.preview.mockResolvedValue(preview({ rows: automaticRows(), useEmergencySpaceReserve: true, configuredMinimumFreeBytes: 50 * GiB }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  const choice = await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  expect(choice).toBeChecked();
  await waitFor(() => expect(api.capacity).toHaveBeenCalledWith("preview-1", [1, 2], expect.any(AbortSignal), "inherit"));
  fireEvent.click(choice);
  const submit = screen.getByRole("button", { name: "acquisition.continueActions.confirm" });
  expect(submit).toBeDisabled();
  await waitFor(() => expect(api.capacity).toHaveBeenLastCalledWith("preview-1", [1, 2], expect.any(AbortSignal), "configured"));
  await waitFor(() => expect(submit).toBeEnabled());
  fireEvent.click(submit);
  await waitFor(() => expect(api.create).toHaveBeenCalledWith("preview-1", [1, 2], false, "configured"));
});

it("keeps the checkbox visible when the reduced reserve now fits", async () => {
  api.preview.mockResolvedValue(preview({ rows: automaticRows(), useEmergencySpaceReserve: false, configuredMinimumFreeBytes: 50 * GiB }));
  api.capacity.mockImplementation(async (previewToken, taskIds, _signal, mode) => ({
    previewToken, taskIds, volumes: preview().volumes, requiresSpaceConfirmation: mode !== "emergency", estimateIncomplete: false,
  }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  const choice = await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  fireEvent.click(choice);
  await waitFor(() => expect(api.capacity).toHaveBeenLastCalledWith("preview-1", [1, 2], expect.any(AbortSignal), "emergency"));
  expect(choice).toBeChecked();
  fireEvent.click(choice);
  await waitFor(() => expect(api.capacity).toHaveBeenLastCalledWith("preview-1", [1, 2], expect.any(AbortSignal), "inherit"));
  expect(api.create).not.toHaveBeenCalled();
});

it("offers the same reserve choice for an eligible download-only selection", async () => {
  api.preview.mockResolvedValue(preview({ rows: [row(1, { actionCategory: "workflow", currentStepKind: "download.awaitCompletion", canOverrideStorage: true })],
    useEmergencySpaceReserve: true, configuredMinimumFreeBytes: 50 * GiB }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1]} />);
  const choice = await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  expect(choice).toBeChecked();
  fireEvent.click(choice);
  await waitFor(() => expect(api.capacity).toHaveBeenLastCalledWith("preview-1", [1], expect.any(AbortSignal), "configured"));
  const submit = screen.getByRole("button", { name: "acquisition.continueActions.confirm" });
  await waitFor(() => expect(submit).toBeEnabled());
  fireEvent.click(submit);
  await waitFor(() => expect(api.create).toHaveBeenCalledWith("preview-1", [1], false, "configured"));
});
