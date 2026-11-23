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
vi.mock("@/components/Workflow/displayNames", () => ({
  activityDisplayName: (_t: unknown, kind: string) => kind,
}));
vi.mock("@/components/bakaui", () => ({
  Tabs: ({ children }: any) => <div>{children}</div>,
  Tab: () => null,
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
  Checkbox: ({
    children,
    isSelected,
    isIndeterminate,
    isDisabled,
    onValueChange,
    ...props
  }: any) => (
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
  Pagination: ({ page, total, onChange }: any) => (
    <button onClick={() => onChange(page + 1)}>
      page {page}/{total}
    </button>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Select: ({ selectedKeys, dataSource, onSelectionChange, isDisabled, ...props }: any) => (
    <select
      aria-label={props["aria-label"]}
      disabled={isDisabled}
      value={Array.from(selectedKeys)[0] as string}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Tooltip: ({ children }: any) => children,
  Spinner: (props: any) => <span role="status" aria-label={props["aria-label"]} />,
}));
const translations = vi.hoisted(() => ({
  "acquisition.continueActions.operation.continue": "Continue current step",
  "acquisition.continueActions.operation.retryTransfer": "Retry transfer",
  "acquisition.continueActions.operation.reanalyze": "Reanalyze",
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, params?: { id: number; action: string }) =>
      key === "acquisition.continueActions.matrix.taskAction" && params
        ? `#${params.id} ${params.action}`
        : (translations[key as keyof typeof translations] ?? key),
  }),
}));
const actionLabel = (action: string) =>
  translations[`acquisition.continueActions.operation.${action}` as keyof typeof translations];
const column = (action: string) => screen.getByRole("checkbox", { name: actionLabel(action) });
const cell = (id: number, action: string) =>
  screen.getByRole("checkbox", { name: `#${id} ${actionLabel(action)}` });
const defaults = (...ids: number[]) => ids.map((taskId) => ({ taskId, action: "continue" }));
const GiB = 1024 ** 3;
const row = (taskId: number, overrides: Record<string, unknown> = {}) => ({
  taskId,
  title: `Task ${taskId}`,
  workflowRunId: taskId + 100,
  canQueue: true,
  defaultAction: overrides.canQueue === false ? undefined : "continue",
  availableActions: overrides.canQueue === false ? [] : ["continue"],
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
  api.capacity
    .mockReset()
    .mockImplementation(async (previewToken, taskIds, _signal, _mode, selections) => ({
      selections,
      previewToken,
      taskIds,
      volumes: preview().volumes,
      requiresSpaceConfirmation: true,
      estimateIncomplete: false,
    }));
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

it("keeps oversized selections intact and offers an explicit scope change without requesting a truncated preview", async () => {
  const change = vi.fn();
  show({
    taskIds: Array.from({ length: 101 }, (_, index) => index + 1),
    continuationScope: "localQueue",
    onContinuationScopeChange: change,
  });
  await screen.findByText("acquisition.continueMode.localLimit");
  expect(api.preview).not.toHaveBeenCalled();
  expect(api.create).not.toHaveBeenCalled();
  expect(confirm()).toBeDisabled();
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.continueMode.label" }), {
    target: { value: "workflow" },
  });
  expect(change).toHaveBeenCalledExactlyOnceWith("workflow");
});

it("locks mode navigation while queue creation is in flight", async () => {
  const change = vi.fn();
  let finish!: (value: unknown) => void;
  api.create.mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
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
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledWith("preview-1", [1, 2], false, undefined),
  );
});

it("sends a scoped override only after the explicit checkbox is checked", async () => {
  show();
  await screen.findByText("Task 1");
  fireEvent.click(screen.getByRole("checkbox"));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.localBatch.confirmOverride" }));
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledWith("preview-1", [1, 2], true, undefined),
  );
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
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledWith("preview-1", [1, 2], false, undefined),
  );
});

it.each([
  [false, "acquisition.localBatch.space.checkpointGuard"],
  [true, "acquisition.localBatch.space.checkpointInsufficient"],
] as const)("shows current write admission separately from the full estimate (%s)", async (blocked, key) => {
  const volume = { ...preview().volumes[0], exceedsUsable: !blocked, currentWritesExceedUsable: blocked };
  const evidence = { volumes: [volume], requiresSpaceConfirmation: blocked, estimateIncomplete: true };
  api.preview.mockResolvedValue(preview(evidence));
  api.capacity.mockImplementation(async (previewToken, taskIds) => ({ previewToken, taskIds, ...evidence }));
  show();
  expect(await screen.findByText(key)).toBeVisible();
  expect(screen.getByText(/acquisition\.localBatch\.space\.estimated/)).toBeVisible();
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
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledWith("preview-1", [1, 2], false, undefined),
  );
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
  row(1, {
    actionCategory: "localProcessing",
    recoveryScope: "workflow",
    currentStepKind: "fs.assessDelivery",
    canOverrideStorage: true,
  }),
  row(2, { actionCategory: "workflow", canOverrideStorage: false }),
];

it("labels pending verification separately from confirmed download retries, local observation, and other workflow checkpoints", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: [
        row(1, {
          actionCategory: "workflow",
          currentStepKind: "download.awaitCompletion",
          canOverrideStorage: true,
        }),
        row(2, {
          actionCategory: "workflow",
          currentStepKind: "download.selectSources",
          canOverrideStorage: false,
        }),
        row(3, {
          actionCategory: "localProcessing",
          currentStepKind: "download.awaitCompletion",
          canOverrideStorage: true,
        }),
        row(4, {
          actionCategory: "workflow",
          currentStepKind: "download.awaitCompletion",
          reasonCode: "RECOVERY_COMPLETED_DOWNLOAD_READY",
          recoveryAction: "resume",
          canOverrideStorage: false,
        }),
        row(5, {
          actionCategory: "workflow",
          currentStepKind: "download.awaitCompletion",
          recoveryAction: "verifyAndResume",
          canOverrideStorage: true,
        }),
        row(6, {
          actionCategory: "localProcessing",
          currentStepKind: "fs.processArtifacts",
          recoveryAction: "verifyAndResume",
          canOverrideStorage: true,
        }),
      ],
    }),
  );
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2, 3, 4, 5, 6]} />);
  const verifyDownload = within(await screen.findByTestId("continuation-task-5"));
  expect(verifyDownload.getByText("acquisition.continueActions.action.verifyDownload")).toBeVisible();
  expect(verifyDownload.getByText("acquisition.continueActions.verifyDownloadHint")).toBeVisible();
  expect(verifyDownload.queryByText("acquisition.continueActions.action.resumeDownload")).toBeNull();
  const verifyStep = within(screen.getByTestId("continuation-task-6"));
  expect(verifyStep.getByText("acquisition.continueActions.action.verifyStep")).toBeVisible();
  expect(verifyStep.getByText("acquisition.continueActions.verifyStepHint")).toBeVisible();
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
  const completedDownload = within(screen.getByTestId("continuation-task-4"));
  expect(
    completedDownload.getByText("acquisition.continueActions.action.completedDownload"),
  ).toBeVisible();
  expect(completedDownload.getByRole("checkbox")).toBeChecked();
  expect(
    completedDownload.queryByText("acquisition.continueActions.action.resumeDownload"),
  ).toBeNull();
  expect(api.create).not.toHaveBeenCalled();
});

it("finds blocked tasks across pages and opens the exact task details without queueing", async () => {
  const onOpenTask = vi.fn();
  const rows = Array.from({ length: 23 }, (_, index) =>
    row(index + 1, {
      canQueue: false,
      canOverrideStorage: false,
      reasonCode: index < 21 ? "RECOVERY_TRANSFER_UNKNOWN" : "RECOVERY_SOURCE_STILL_BLOCKED",
    }),
  );
  api.preview.mockResolvedValue(preview({ rows }));
  render(
    <AcquisitionLocalBatchModal
      automatic
      taskIds={rows.map(({ taskId }) => taskId)}
      onOpenTask={onOpenTask}
    />,
  );
  const filter = await screen.findByRole("combobox", {
    name: "acquisition.continueActions.reasonFilter.label",
  });
  expect(screen.getByTestId("continuation-task-1")).toBeVisible();
  expect(screen.queryByTestId("continuation-task-22")).toBeNull();
  fireEvent.change(filter, { target: { value: "RECOVERY_SOURCE_STILL_BLOCKED" } });
  expect(screen.queryByTestId("continuation-task-1")).toBeNull();
  const blocked = within(screen.getByTestId("continuation-task-23"));
  fireEvent.click(blocked.getByRole("button", { name: "acquisition.continueActions.openTask" }));
  expect(onOpenTask).toHaveBeenCalledExactlyOnceWith(23);
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(api.create).not.toHaveBeenCalled();
});

it("continues a stalled transfer by default and mixes per-task overrides in one submission", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: [
        row(1, {
          recoveryAction: "retryTransfer",
          reasonCode: "RECOVERY_TRANSFER_RECOVERY_READY",
          canOverrideStorage: false,
        }),
        row(2, {
          availableActions: ["continue", "retryTransfer", "reanalyze"],
          canOverrideStorage: false,
        }),
        row(3, {
          availableActions: ["continue", "retryTransfer", "reanalyze"],
          canOverrideStorage: false,
        }),
        row(4, { canQueue: false, canOverrideStorage: false, availableActions: ["reanalyze"] }),
      ],
    }),
  );
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2, 3, 4]} />);
  await screen.findByText("acquisition.continueActions.action.retryTransfer");
  expect(cell(1, "continue")).toBeChecked();
  expect(cell(2, "retryTransfer")).not.toBeChecked();
  expect(cell(4, "reanalyze")).not.toBeChecked();
  fireEvent.click(column("retryTransfer"));
  expect(cell(2, "continue")).not.toBeChecked();
  expect(cell(3, "retryTransfer")).toBeChecked();
  fireEvent.click(cell(3, "reanalyze"));
  fireEvent.click(cell(4, "reanalyze"));
  expect(cell(3, "retryTransfer")).not.toBeChecked();
  expect(column("retryTransfer")).toHaveAttribute("data-indeterminate", "true");
  const selections = [
    ...defaults(1),
    { taskId: 2, action: "retryTransfer" },
    { taskId: 3, action: "reanalyze" },
    { taskId: 4, action: "reanalyze" },
  ];
  const submit = screen.getByRole("button", { name: "acquisition.continueActions.confirm" });
  await waitFor(() => expect(submit).toBeEnabled());
  expect(api.capacity).toHaveBeenLastCalledWith(
    "preview-1",
    [1, 2, 3, 4],
    expect.any(AbortSignal),
    undefined,
    selections,
  );
  fireEvent.click(submit);
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledExactlyOnceWith(
      "preview-1",
      [1, 2, 3, 4],
      false,
      undefined,
      selections,
    ),
  );
});

it("limits a header to eligible tasks across every page of the active filter", async () => {
  const rows = Array.from({ length: 24 }, (_, i) =>
    row(i + 1, {
      canQueue: false,
      canOverrideStorage: false,
      availableActions: ["reanalyze"],
      reasonCode: i < 22 ? "RECOVERY_TRANSFER_UNKNOWN" : "RECOVERY_SOURCE_STILL_BLOCKED",
    }),
  );
  api.preview.mockResolvedValue(preview({ rows }));
  render(<AcquisitionLocalBatchModal automatic taskIds={rows.map((row) => row.taskId)} />);
  fireEvent.change(
    await screen.findByRole("combobox", { name: "acquisition.continueActions.reasonFilter.label" }),
    {
      target: { value: "RECOVERY_TRANSFER_UNKNOWN" },
    },
  );
  fireEvent.click(column("reanalyze"));
  fireEvent.click(screen.getByRole("button", { name: "page 1/2" }));
  expect(cell(22, "reanalyze")).toBeChecked();
  fireEvent.change(
    screen.getByRole("combobox", { name: "acquisition.continueActions.reasonFilter.label" }),
    {
      target: { value: "all" },
    },
  );
  fireEvent.click(screen.getByRole("button", { name: "page 1/2" }));
  expect(cell(24, "reanalyze")).not.toBeChecked();
  expect(api.create).not.toHaveBeenCalled();
});

it("rechecks optional actions when refreshing the preview and accepts an explicit analysis iteration count", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: [
        row(1, {
          canQueue: false,
          availableActions: ["reanalyze"],
          canOverrideStorage: false,
        }),
      ],
    }),
  );
  render(<AcquisitionLocalBatchModal automatic taskIds={[1]} />);
  await screen.findByTestId("continuation-task-1");
  fireEvent.click(cell(1, "reanalyze"));
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.adaptive.iterations" }), {
    target: { value: "5" },
  });
  await waitFor(() =>
    expect(api.preview).toHaveBeenLastCalledWith([1], expect.any(AbortSignal), "automatic", 5),
  );
  await screen.findByTestId("continuation-task-1");
  expect(cell(1, "reanalyze")).toBeChecked();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.localBatch.refreshPreview" }));
  await screen.findByTestId("continuation-task-1");
  expect(cell(1, "reanalyze")).not.toBeChecked();
  expect(
    screen.getByRole("button", { name: "acquisition.continueActions.confirm" }),
  ).toBeDisabled();
  expect(api.create).not.toHaveBeenCalled();
});

it("allows excluding one task while leaving its column partially selected", async () => {
  api.preview.mockResolvedValue(preview({ rows: automaticRows() }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  await screen.findByTestId("continuation-task-2");
  fireEvent.click(cell(2, "continue"));
  expect(column("continue")).toHaveAttribute("data-indeterminate", "true");
  expect(screen.getByTestId("continuation-task-2")).toHaveClass("bg-default-50");
  expect(screen.getByText("acquisition.continueActions.excluded")).toBeVisible();
  const submit = screen.getByRole("button", { name: "acquisition.continueActions.confirm" });
  await waitFor(() => expect(submit).toBeEnabled());
  fireEvent.click(submit);
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledExactlyOnceWith(
      "preview-1",
      [1],
      false,
      undefined,
      defaults(1),
    ),
  );
});

it("rejects stale or mismatched capacity results and never submits an old selection", async () => {
  const pending: { ids: number[]; resolve: (value: unknown) => void }[] = [];
  api.capacity.mockImplementation(
    (_token, ids) => new Promise((resolve) => pending.push({ ids, resolve })),
  );
  api.preview.mockResolvedValue(preview({ rows: automaticRows() }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  await waitFor(() => expect(pending).toHaveLength(1));
  fireEvent.click(cell(2, "continue"));
  await waitFor(() => expect(pending).toHaveLength(2));
  const capacity = (ids: number[]) => ({
    previewToken: "preview-1",
    taskIds: ids,
    volumes: [],
    requiresSpaceConfirmation: false,
    estimateIncomplete: false,
  });
  await act(async () => pending[0].resolve(capacity([1, 2])));
  expect(
    screen.getByRole("button", { name: "acquisition.continueActions.confirm" }),
  ).toBeDisabled();
  await act(async () => pending[1].resolve(capacity([1, 99])));
  await screen.findByText("acquisition.continueActions.capacityFailed");
  expect(
    screen.getByRole("button", { name: "acquisition.continueActions.confirm" }),
  ).toBeDisabled();
  expect(api.create).not.toHaveBeenCalled();
});

it("does not submit capacity evidence for a different action on the same task", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: [row(1, { availableActions: ["continue", "reanalyze"], canOverrideStorage: false })],
    }),
  );
  api.capacity.mockImplementation(async (previewToken, taskIds) => ({
    previewToken,
    taskIds,
    selections: defaults(1),
    volumes: [],
    requiresSpaceConfirmation: false,
    estimateIncomplete: false,
  }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1]} />);
  const submit = screen.getByRole("button", { name: "acquisition.continueActions.confirm" });
  await waitFor(() => expect(submit).toBeEnabled());
  fireEvent.click(cell(1, "reanalyze"));
  expect(submit).toBeDisabled();
  await screen.findByText("acquisition.continueActions.capacityFailed");
  expect(submit).toBeDisabled();
  expect(api.create).not.toHaveBeenCalled();
});

it("requires a new explicit storage acknowledgement after the selected tasks change", async () => {
  api.preview.mockResolvedValue(preview({ rows: automaticRows() }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  const acknowledgement = await screen.findByRole("checkbox", {
    name: "acquisition.reserve.operation.label",
  });
  expect(acknowledgement).not.toBeChecked();
  fireEvent.click(acknowledgement);
  fireEvent.click(cell(2, "continue"));
  const fresh = await screen.findByRole("checkbox", {
    name: "acquisition.reserve.operation.label",
  });
  expect(fresh).not.toBeChecked();
  await waitFor(() =>
    expect(api.capacity).toHaveBeenLastCalledWith(
      "preview-1",
      [1],
      expect.any(AbortSignal),
      undefined,
      defaults(1),
    ),
  );
  fireEvent.click(fresh);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.localBatch.confirmOverride" }));
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledExactlyOnceWith(
      "preview-1",
      [1],
      true,
      undefined,
      defaults(1),
    ),
  );
});

it("displays the owning blocked queue and inspects it without resuming or duplicate submission", async () => {
  const queue = {
    kind: "localQueue",
    batchId: "previous-queue",
    status: "spaceBlocked",
    rowStatus: "queued",
    canRequeue: true,
  };
  api.preview.mockResolvedValue(
    preview({
      rows: [row(1, { actionCategory: "localProcessing", canOverrideStorage: true, queue })],
    }),
  );
  const inspect = vi.fn();
  render(<AcquisitionLocalBatchModal automatic taskIds={[1]} onOpenQueue={inspect} />);
  expect(await screen.findByText("acquisition.localBatch.status.spaceBlocked")).toBeVisible();
  expect(screen.getByText("acquisition.continueActions.requeueHint")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continueActions.viewQueue" }));
  expect(inspect).toHaveBeenCalledExactlyOnceWith(queue);
  expect(api.create).not.toHaveBeenCalled();
  expect(api.resume).not.toHaveBeenCalled();
});

it("renders new capabilities from the server, with a readable fallback and explicit per-task selection", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: [
        row(1, {
          canOverrideStorage: false,
          availableActions: ["futureAction"],
          defaultAction: "futureAction",
        }),
        row(2, {
          canOverrideStorage: false,
          availableActions: ["retryTransfer"],
          defaultAction: "retryTransfer",
        }),
      ],
    }),
  );
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  await screen.findByTestId("continuation-task-1");
  expect(column("continue")).toBeDisabled();
  expect(column("retryTransfer")).not.toBeChecked();
  expect(
    screen.getByRole("button", { name: "acquisition.continueActions.confirm" }),
  ).toBeDisabled();
  const future = screen.getByRole("checkbox", { name: "Future Action" });
  fireEvent.click(future);
  expect(screen.getByRole("checkbox", { name: "#1 Future Action" })).toBeChecked();
  const submit = screen.getByRole("button", { name: "acquisition.continueActions.confirm" });
  await waitFor(() => expect(submit).toBeEnabled());
  fireEvent.click(submit);
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledExactlyOnceWith("preview-1", [1], false, undefined, [
      { taskId: 1, action: "futureAction" },
    ]),
  );
});

it("restores per-task choices on return without restoring a space override", async () => {
  api.preview.mockResolvedValue(preview({ rows: automaticRows() }));
  render(
    <AcquisitionLocalBatchModal automatic taskIds={[1, 2]} initialSelection={{ 1: "continue" }} />,
  );
  await screen.findByTestId("continuation-task-1");
  expect(cell(1, "continue")).toBeChecked();
  expect(cell(2, "continue")).not.toBeChecked();
  expect(
    await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" }),
  ).not.toBeChecked();
  expect(api.create).not.toHaveBeenCalled();
});

it("inherits the global reserve and refreshes bound capacity before applying a configured override", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: automaticRows(),
      useEmergencySpaceReserve: true,
      configuredMinimumFreeBytes: 50 * GiB,
    }),
  );
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  const choice = await screen.findByRole("checkbox", {
    name: "acquisition.reserve.operation.label",
  });
  expect(choice).toBeChecked();
  await waitFor(() =>
    expect(api.capacity).toHaveBeenCalledWith(
      "preview-1",
      [1, 2],
      expect.any(AbortSignal),
      "inherit",
      defaults(1, 2),
    ),
  );
  fireEvent.click(choice);
  const submit = screen.getByRole("button", { name: "acquisition.continueActions.confirm" });
  expect(submit).toBeDisabled();
  await waitFor(() =>
    expect(api.capacity).toHaveBeenLastCalledWith(
      "preview-1",
      [1, 2],
      expect.any(AbortSignal),
      "configured",
      defaults(1, 2),
    ),
  );
  await waitFor(() => expect(submit).toBeEnabled());
  fireEvent.click(submit);
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledWith(
      "preview-1",
      [1, 2],
      false,
      "configured",
      defaults(1, 2),
    ),
  );
});

it("allows a reanalysis-only selection to restore the configured reserve while the global reserve is reduced", async () => {
  api.preview.mockResolvedValue(preview({
    rows: [row(1, { canQueue: false, canOverrideStorage: false, availableActions: ["reanalyze"] })],
    useEmergencySpaceReserve: true,
    configuredMinimumFreeBytes: 50 * GiB,
  }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1]} />);
  fireEvent.click(await screen.findByRole("checkbox", { name: "#1 " + actionLabel("reanalyze") }));
  const choice = await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  expect(choice).toBeChecked();
  fireEvent.click(choice);
  const selected = [{ taskId: 1, action: "reanalyze" }];
  await waitFor(() => expect(api.capacity).toHaveBeenLastCalledWith("preview-1", [1], expect.any(AbortSignal), "configured", selected));
  const submit = screen.getByRole("button", { name: "acquisition.continueActions.confirm" });
  await waitFor(() => expect(submit).toBeEnabled());
  fireEvent.click(submit);
  await waitFor(() => expect(api.create).toHaveBeenCalledWith("preview-1", [1], false, "configured", selected));
});

it("keeps the checkbox visible when the reduced reserve now fits", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: automaticRows(),
      useEmergencySpaceReserve: false,
      configuredMinimumFreeBytes: 50 * GiB,
    }),
  );
  api.capacity.mockImplementation(async (previewToken, taskIds, _signal, mode, selections) => ({
    selections,
    previewToken,
    taskIds,
    volumes: preview().volumes,
    requiresSpaceConfirmation: mode !== "emergency",
    estimateIncomplete: false,
  }));
  render(<AcquisitionLocalBatchModal automatic taskIds={[1, 2]} />);
  const choice = await screen.findByRole("checkbox", {
    name: "acquisition.reserve.operation.label",
  });
  fireEvent.click(choice);
  await waitFor(() =>
    expect(api.capacity).toHaveBeenLastCalledWith(
      "preview-1",
      [1, 2],
      expect.any(AbortSignal),
      "emergency",
      defaults(1, 2),
    ),
  );
  expect(choice).toBeChecked();
  fireEvent.click(choice);
  await waitFor(() =>
    expect(api.capacity).toHaveBeenLastCalledWith(
      "preview-1",
      [1, 2],
      expect.any(AbortSignal),
      "inherit",
      defaults(1, 2),
    ),
  );
  expect(api.create).not.toHaveBeenCalled();
});

it("offers the same reserve choice for an eligible download-only selection", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: [
        row(1, {
          actionCategory: "workflow",
          currentStepKind: "download.awaitCompletion",
          canOverrideStorage: true,
        }),
      ],
      useEmergencySpaceReserve: true,
      configuredMinimumFreeBytes: 50 * GiB,
    }),
  );
  render(<AcquisitionLocalBatchModal automatic taskIds={[1]} />);
  const choice = await screen.findByRole("checkbox", {
    name: "acquisition.reserve.operation.label",
  });
  expect(choice).toBeChecked();
  fireEvent.click(choice);
  await waitFor(() =>
    expect(api.capacity).toHaveBeenLastCalledWith(
      "preview-1",
      [1],
      expect.any(AbortSignal),
      "configured",
      defaults(1),
    ),
  );
  const submit = screen.getByRole("button", { name: "acquisition.continueActions.confirm" });
  await waitFor(() => expect(submit).toBeEnabled());
  fireEvent.click(submit);
  await waitFor(() =>
    expect(api.create).toHaveBeenCalledWith("preview-1", [1], false, "configured", defaults(1)),
  );
});
