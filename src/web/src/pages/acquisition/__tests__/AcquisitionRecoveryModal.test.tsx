import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import AcquisitionRecoveryModal from "../components/AcquisitionRecoveryModal";
const api = vi.hoisted(() => ({ preview: vi.fn(), apply: vi.fn(), cancelDiagnosis: vi.fn() }));
const defaults = vi.hoisted(() => ({ read: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { acquisition: { getAcquisitionOptions: defaults.read } } }));
const continuation = vi.hoisted(() => ({ preview: vi.fn(), create: vi.fn(), findReceipt: vi.fn(), list: vi.fn(), get: vi.fn(), cancel: vi.fn() }));
vi.mock("../components/acquisitionRecoveryApi", () => ({ acquisitionRecoveryApi: api }));
vi.mock("../components/acquisitionContinuationApi", () => ({ acquisitionContinuationApi: continuation }));
vi.mock("@/components/Workflow/displayNames", () => ({
  activityDisplayName: (_t: any, kind: string) => kind,
}));
vi.mock("@/components/Acquire/PlanTask/runtimeOverview", () => ({
  runtimeProblemLabel: (_t: any, code: string) => code,
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, footer, visible, title }: any) =>
    visible ? (
      <div role="dialog" aria-label={title}>
        {children}
        {footer}
      </div>
    ) : null,
  Button: ({ children, onPress, isDisabled, isLoading }: any) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Checkbox: ({ isSelected, isDisabled, onValueChange, ...props }: any) => (
    <input
      type="checkbox"
      aria-label={props["aria-label"]}
      checked={isSelected}
      disabled={isDisabled}
      onChange={(event) => onValueChange(event.target.checked)}
    />
  ),
  Spinner: (props: any) => <span role="status" aria-label={props["aria-label"]} />,
  Chip: ({ children }: any) => <span>{children}</span>,
  Tooltip: ({ children }: any) => children,
  Select: ({ dataSource, selectedKeys, onSelectionChange, isDisabled, ...props }: any) => (
    <select
      aria-label={props["aria-label"]}
      value={Array.from(selectedKeys)[0] as string}
      disabled={isDisabled}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Pagination: ({ page, total, onChange }: any) => (
    <button onClick={() => onChange(page + 1)}>
      page {page}/{total}
    </button>
  ),
}));
const preview = (overrides: any = {}) => ({
  previewToken: "fixed-token",
  scope: "workflow",
  expiresAt: new Date(Date.now() + 600_000).toISOString(),
  totalCount: 4,
  continuableCount: 2,
  manualCount: 2,
  rows: [
    {
      taskId: 1,
      title: "Old lifecycle failure",
      workflowRunId: 101,
      canContinue: true,
      action: "resume",
      reasonCode: "RECOVERY_READY",
      currentProblemCode: "SERVICE_SCOPE_DISPOSED",
      currentStepKind: "cloud.transfer",
      evidenceAt: "2026-10-23 01:00:00.000",
    },
    {
      taskId: 2,
      title: "Uncertain source",
      canContinue: false,
      reasonCode: "RECOVERY_MANUAL_REQUIRED",
      manualAction: "inspectSource",
    },
    {
      taskId: 3,
      title: "Unknown future action",
      canContinue: true,
      action: "futureAction",
      reasonCode: "FUTURE_REASON",
    },
    {
      taskId: 4,
      title: "Verified files",
      workflowRunId: 104,
      canContinue: true,
      action: "retry",
      reasonCode: "RECOVERY_READY",
    },
  ],
  ...overrides,
});
const confirm = () => screen.getByRole("button", { name: "acquisition.recovery.confirm" });
const checkbox = (index: number) => screen.getAllByRole("checkbox")[index];
beforeEach(() => {
  vi.clearAllMocks();
  api.preview.mockReset().mockResolvedValue(preview());
  api.apply.mockReset().mockResolvedValue({ continuedTaskIds: [1, 4], skipped: [] });
  api.cancelDiagnosis.mockReset().mockImplementation(async diagnosisId => ({ diagnosisId, status: "stopped" }));
  for (const method of Object.values(continuation)) method.mockReset();
  defaults.read.mockReset().mockResolvedValue({ code: 0, data: { useEmergencySpaceReserve: true,
    minimumFreeSpaceBytes: 50 * 1024 ** 3 } });
});
afterEach(async () => {
  cleanup();
  await Promise.resolve();
  vi.useRealTimers();
});

it("defaults destination recovery to downloads only, rejecting workflow actions in that scope", async () => {
  api.preview.mockResolvedValue(
    preview({
      scope: "downloads",
      rows: [
        { taskId: 1, title: "Original download", action: "downloads", canContinue: true },
        { taskId: 2, title: "Workflow continuation", action: "resume", canContinue: true },
      ],
    }),
  );
  api.apply.mockResolvedValue({ continuedTaskIds: [1], skipped: [] });
  render(<AcquisitionRecoveryModal taskIds={[1, 2]} initialScope="downloads" />);
  await screen.findByText("Original download");
  expect(api.preview).toHaveBeenCalledExactlyOnceWith([1, 2], "downloads", expect.objectContaining({ diagnosisId: expect.any(String), signal: expect.any(AbortSignal) }));
  expect(checkbox(0)).toBeChecked();
  expect(checkbox(1)).toBeDisabled();
  expect(api.apply).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.downloads.confirm" }));
  await screen.findByText("acquisition.recovery.downloads.result");
  expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-token", [1]);
  expect(screen.getByText("acquisition.recovery.downloads.resultHint")).toBeVisible();
  expect(screen.queryByText("acquisition.recovery.result")).toBeNull();
});

it.each([true, false])("shows the current global reserve but submits only an explicit download override=%s", async globalDefault => {
  defaults.read.mockResolvedValue({ code: 0, data: { useEmergencySpaceReserve: globalDefault,
    minimumFreeSpaceBytes: 50 * 1024 ** 3 } });
  api.preview.mockResolvedValue(preview({ scope: "downloads", rows: [
    { taskId: 1, title: "Existing download", action: "downloads", canContinue: true, canOverrideStorage: true },
  ] }));
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="downloads" />);
  const choice = await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  if (globalDefault) expect(choice).toBeChecked(); else expect(choice).not.toBeChecked();
  fireEvent.click(choice);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.downloads.confirm" }));
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-token", [1], !globalDefault));
});

it("clears the old scope immediately and ignores its late preview", async () => {
  let finish!: (value: any) => void;
  api.preview.mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  api.preview.mockResolvedValueOnce(
    preview({
      previewToken: "downloads-preview",
      scope: "downloads",
      rows: [{ taskId: 1, title: "Original download", action: "downloads", canContinue: true }],
    }),
  );
  render(<AcquisitionRecoveryModal taskIds={[1]} />);
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(1));
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.recovery.scope.label" }), {
    target: { value: "downloads" },
  });
  await screen.findByText("Original download");
  await act(async () => finish(preview()));
  expect(screen.queryByText("Old lifecycle failure")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.downloads.confirm" }));
  await waitFor(() => expect(api.apply).toHaveBeenCalledWith("downloads-preview", [1]));
});

it("repreviews on every scope change and cannot reuse a previous workflow selection", async () => {
  render(<AcquisitionRecoveryModal taskIds={[1, 2, 3, 4]} />);
  await screen.findByText("Old lifecycle failure");
  api.preview.mockResolvedValueOnce(
    preview({
      previewToken: "downloads-token",
      scope: "downloads",
      rows: [{ taskId: 1, title: "Download unsupported", action: "resume", canContinue: true }],
    }),
  );
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "downloads" } });
  expect(screen.queryByText("Old lifecycle failure")).toBeNull();
  await screen.findByText("Download unsupported");
  expect(
    screen.getByRole("button", { name: "acquisition.recovery.downloads.confirm" }),
  ).toBeDisabled();
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "workflow" } });
  await screen.findByText("Old lifecycle failure");
  expect(api.preview.mock.calls.map((call) => call[1])).toEqual([
    "workflow",
    "downloads",
    "workflow",
  ]);
  expect(api.apply).not.toHaveBeenCalled();
});

it("does not select a downloads action when the caller uses the default workflow scope", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: [{ taskId: 1, title: "Download-only action", action: "downloads", canContinue: true }],
    }),
  );
  render(<AcquisitionRecoveryModal taskIds={[1]} />);
  await screen.findByText("Download-only action");
  expect(checkbox(0)).toBeDisabled();
  expect(confirm()).toBeDisabled();
});

it("diagnoses the fixed selection without execution, preselecting only explicitly safe known actions", async () => {
  render(<AcquisitionRecoveryModal taskIds={[1, 2, 3, 4, 1, -2]} scopeLabel="attention · 393" />);
  await screen.findByText("Old lifecycle failure");
  expect(api.preview).toHaveBeenCalledExactlyOnceWith([1, 2, 3, 4], "workflow", expect.objectContaining({ diagnosisId: expect.any(String), signal: expect.any(AbortSignal) }));
  expect(api.apply).not.toHaveBeenCalled();
  expect(checkbox(0)).toBeChecked();
  expect(checkbox(1)).not.toBeChecked();
  expect(checkbox(1)).toBeDisabled();
  expect(checkbox(2)).not.toBeChecked();
  expect(checkbox(2)).toBeDisabled();
  expect(checkbox(3)).toBeChecked();
  expect(screen.getByText(/attention · 393/)).toBeVisible();
  expect(screen.getByText("acquisition.recovery.manualAction.inspectSource")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  expect(api.apply).not.toHaveBeenCalled();
});

it("allows a verified never-executed plan to start only after explicit confirmation", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: [
        {
          taskId: 949,
          title: "Public share ready",
          workflowRunId: null,
          canContinue: true,
          action: "start",
          reasonCode: "RECOVERY_PREPARED_PLAN_READY",
        },
      ],
      totalCount: 1,
      continuableCount: 1,
      manualCount: 0,
    }),
  );
  render(<AcquisitionRecoveryModal taskIds={[949]} />);
  await screen.findByText("Public share ready");
  expect(screen.getByText("acquisition.recovery.rowStartEligible")).toBeVisible();
  expect(screen.queryByText("acquisition.recovery.currentProblem")).toBeNull();
  expect(checkbox(0)).toBeChecked();
  expect(api.apply).not.toHaveBeenCalled();
  fireEvent.click(confirm());
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-token", [949]));
});

it("selects only safe tasks across preview pages and submits the chosen subset with the fixed token", async () => {
  const rows = Array.from({ length: 23 }, (_, i) => ({
    taskId: i + 1,
    title: `Task ${i + 1}`,
    canContinue: i % 2 === 0,
    action: "resume",
    reasonCode: "RECOVERY_READY",
  }));
  const ids = rows.map((row) => row.taskId);
  api.preview.mockResolvedValue(
    preview({ rows, totalCount: 23, continuableCount: 12, manualCount: 11 }),
  );
  render(<AcquisitionRecoveryModal taskIds={ids} />);
  await screen.findByText("Task 1");
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.selectNone" }));
  expect(confirm()).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.selectEligible" }));
  fireEvent.click(checkbox(0));
  fireEvent.click(screen.getByText("page 1/2"));
  expect(screen.getByText("Task 23")).toBeVisible();
  expect(checkbox(0)).toBeChecked();
  expect(checkbox(1)).toBeDisabled();
  fireEvent.click(confirm());
  await screen.findByText("acquisition.recovery.result");
  expect(api.apply).toHaveBeenCalledExactlyOnceWith(
    "fixed-token",
    [3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23],
  );
});

it("suppresses double confirmation and reports tasks skipped after server revalidation", async () => {
  let finish!: (value: unknown) => void;
  api.apply.mockReturnValue(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  const changed = vi.fn();
  render(<AcquisitionRecoveryModal taskIds={[1, 2, 3, 4]} onChanged={changed} />);
  await screen.findByText("Old lifecycle failure");
  fireEvent.click(confirm());
  fireEvent.click(confirm());
  expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-token", [1, 4]);
  expect(screen.getByRole("button", { name: "common.action.close" })).toBeDisabled();
  await act(async () =>
    finish({
      continuedTaskIds: [1],
      skipped: [{ taskId: 4, reasonCode: "ACQUISITION_TASK_CHANGED" }],
    }),
  );
  expect(screen.getByText("acquisition.recovery.reason.ACQUISITION_TASK_CHANGED")).toBeVisible();
  expect(changed).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole("button", { name: "acquisition.recovery.confirm" })).toBeNull();
});

it("cannot submit a failed diagnosis or tasks whose recovery conditions are unknown", async () => {
  api.preview.mockRejectedValueOnce(new Error("offline"));
  render(<AcquisitionRecoveryModal taskIds={[1, 2]} />);
  await screen.findByRole("alert");
  expect(confirm()).toBeDisabled();
  api.preview.mockResolvedValue(
    preview({ rows: [preview().rows[1]], continuableCount: 0, manualCount: 1 }),
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.recheck" }));
  await screen.findByText("Uncertain source");
  expect(confirm()).toBeDisabled();
  expect(api.apply).not.toHaveBeenCalled();
});

it("refreshes an expired preview without expanding the captured filter IDs", async () => {
  api.preview.mockResolvedValueOnce(preview({ expiresAt: "2020-01-01 00:00:00.000" }));
  const view = render(<AcquisitionRecoveryModal taskIds={[1, 2, 3, 4]} />);
  await screen.findByText("acquisition.recovery.expired");
  expect(confirm()).toBeDisabled();
  view.rerender(<AcquisitionRecoveryModal taskIds={[1, 2, 3, 4, 5]} />);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.recheck" }));
  await waitFor(() => expect(confirm()).not.toBeDisabled());
  expect(api.preview.mock.calls.map((call) => call[0])).toEqual([
    [1, 2, 3, 4],
    [1, 2, 3, 4],
  ]);
  expect(api.apply).not.toHaveBeenCalled();
});

it("allows a fresh source diagnosis after a blocked result without starting downloads", async () => {
  api.preview.mockResolvedValueOnce(
    preview({
      rows: [{ ...preview().rows[1], reasonCode: "RECOVERY_SOURCE_STILL_BLOCKED" }],
      continuableCount: 0,
      manualCount: 1,
    }),
  );
  render(<AcquisitionRecoveryModal taskIds={[1, 2]} />);
  await screen.findByText("acquisition.recovery.reason.RECOVERY_SOURCE_STILL_BLOCKED");
  expect(confirm()).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.recheck" }));
  await screen.findByText("Old lifecycle failure");
  expect(api.preview.mock.calls.map((call) => call[0])).toEqual([
    [1, 2],
    [1, 2],
  ]);
  expect(api.apply).not.toHaveBeenCalled();
});

it("requires a new preview after an uncertain apply outcome and never reuses the consumed token", async () => {
  api.apply.mockRejectedValue(new Error("connection lost"));
  const changed = vi.fn();
  render(<AcquisitionRecoveryModal taskIds={[1, 2, 3, 4]} onChanged={changed} />);
  await screen.findByText("Old lifecycle failure");
  fireEvent.click(confirm());
  await screen.findByText("acquisition.recovery.error.submit");
  expect(confirm()).toBeDisabled();
  expect(changed).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.recheck" }));
  await screen.findByText("Old lifecycle failure");
  expect(confirm()).toBeDisabled();
  expect(api.apply).toHaveBeenCalledTimes(1);
});

it("reads server UTC expiry and disables the old selection as time passes", async () => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date("2026-10-25T10:00:00Z"));
  api.preview.mockResolvedValue(preview({ expiresAt: "2026-10-25 10:10:00.000" }));
  await act(async () => {
    render(<AcquisitionRecoveryModal taskIds={[1, 2, 3, 4]} />);
  });
  expect(confirm()).not.toBeDisabled();
  await act(async () => {
    vi.advanceTimersByTime(600_001);
  });
  expect(confirm()).toBeDisabled();
  expect(screen.getByText("acquisition.recovery.expired")).toBeVisible();
});

it("does not run a duplicate expensive diagnosis during Strict Mode effect replay", async () => {
  render(
    <StrictMode>
      <AcquisitionRecoveryModal taskIds={[1, 2, 3, 4]} />
    </StrictMode>,
  );
  await screen.findByText("Old lifecycle failure");
  expect(api.preview).toHaveBeenCalledTimes(1);
  expect(api.cancelDiagnosis).not.toHaveBeenCalled();
  expect(confirm()).not.toBeDisabled();
});

it("never includes server rows outside the captured task selection", async () => {
  api.preview.mockResolvedValue(
    preview({
      rows: [
        ...preview().rows,
        { taskId: 999, title: "Outside filter", canContinue: true, action: "resume" },
      ],
    }),
  );
  render(<AcquisitionRecoveryModal taskIds={[1, 2, 3, 4]} />);
  await screen.findByText("Old lifecycle failure");
  expect(screen.queryByText("Outside filter")).toBeNull();
  fireEvent.click(confirm());
  await screen.findByText("acquisition.recovery.result");
  expect(api.apply).toHaveBeenCalledWith("fixed-token", [1, 4]);
});

it("rejects a server preview from a different scope even when its action looks eligible", async () => {
  api.preview.mockResolvedValue(
    preview({
      scope: "workflow",
      rows: [{ taskId: 1, title: "Wrong scope", action: "downloads", canContinue: true }],
    }),
  );
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="downloads" />);
  await screen.findByText("acquisition.recovery.error.RECOVERY_SCOPE_MISMATCH");
  expect(screen.queryByText("Wrong scope")).toBeNull();
  expect(
    screen.getByRole("button", { name: "acquisition.recovery.downloads.confirm" }),
  ).toBeDisabled();
  expect(api.apply).not.toHaveBeenCalled();
});

it("shows partial and unknown download outcomes without counting foreign task receipts", async () => {
  api.preview.mockResolvedValue(
    preview({
      scope: "downloads",
      rows: [
        { taskId: 1, title: "Multiple original downloads", action: "downloads", canContinue: true },
      ],
    }),
  );
  api.apply.mockResolvedValue({
    continuedTaskIds: [999],
    skipped: [{ taskId: 1, reasonCode: "RECOVERY_DOWNLOAD_PARTIALLY_STARTED" }],
    downloads: [
      {
        taskId: 1,
        startedDownloadTaskIds: [9001],
        indeterminateDownloadTaskIds: [9002],
        reasonCode: "RECOVERY_DOWNLOAD_OUTCOME_UNKNOWN",
      },
      {
        taskId: 999,
        startedDownloadTaskIds: [9999],
        indeterminateDownloadTaskIds: [],
        reasonCode: null,
      },
    ],
  });
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="downloads" />);
  await screen.findByText("Multiple original downloads");
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.downloads.confirm" }));
  await screen.findByText("acquisition.recovery.downloads.result");
  expect(screen.getByText("acquisition.recovery.downloads.started")).toBeVisible();
  expect(screen.getByText("acquisition.recovery.downloads.indeterminate")).toBeVisible();
  expect(screen.getByText("acquisition.recovery.downloads.indeterminateIds")).toBeVisible();
  expect(
    screen.getByText("acquisition.recovery.reason.RECOVERY_DOWNLOAD_OUTCOME_UNKNOWN"),
  ).toBeVisible();
  expect(screen.queryByText("#999")).toBeNull();
  expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-token", [1]);
});

const localFiles = {
  supportsPrepareOnly: true,
  parts: [
    {
      partId: "main",
      downloadTaskId: 301,
      downloadResultId: 401,
      fingerprint: "bound-file-fingerprint",
      fileCount: 2,
      sourceDirectory: "/downloads/main",
      originalWorkingDirectory: "/old/work/main",
      workingDirectory: "/current/work/main",
      originalDeliveryDirectory: "/old/delivery/main",
      deliveryDirectory: "/current/delivery/main",
    },
    {
      partId: "extras",
      downloadTaskId: 302,
      downloadResultId: 402,
      fingerprint: "bound-extra-fingerprint",
      fileCount: 3,
      sourceDirectory: "/downloads/extras",
      originalWorkingDirectory: null,
      workingDirectory: "/current/work/extras",
      originalDeliveryDirectory: "/old/delivery/extras",
      deliveryDirectory: "/current/delivery/extras",
      artifactId: "existing-artifact",
    },
  ],
};
const localRow = {
  taskId: 1,
  title: "Completed local downloads",
  workflowRunId: 101,
  canContinue: true,
  action: "localFiles",
  reasonCode: "RECOVERY_LOCAL_FILES_READY",
  localFiles,
};
const localConfirm = () =>
  screen.getByRole("button", { name: "acquisition.recovery.localFiles.confirm" });

const chooseLocalAnalysis = async () => {
  const method = await screen.findByRole("combobox", { name: "acquisition.recovery.localFiles.processingMode" });
  await act(async () => {
    fireEvent.change(method, { target: { value: "analyze" } });
  });
};

it("registers existing files without starting processing or inheriting a previous AI or reserve choice", async () => {
  api.preview.mockResolvedValue(preview({ scope: "localFiles", rows: [{ ...localRow, canOverrideStorage: true }] }));
  api.apply.mockResolvedValue({
    continuedTaskIds: [], skipped: [], localFiles: [
      { taskId: 1, planRevision: 2, previousWorkflowRunId: 101, workflowRunId: null, status: "prepared", reasonCode: null },
    ],
  });
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="localFiles" />);
  await chooseLocalAnalysis();
  fireEvent.click(await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" }));
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.recovery.localFiles.processingMode" }), {
    target: { value: "prepareOnly" },
  });
  expect(screen.getByText("acquisition.recovery.localFiles.prepareHint")).toBeVisible();
  expect(screen.queryByRole("combobox", { name: "acquisition.adaptive.iterations" })).toBeNull();
  expect(screen.queryByRole("checkbox", { name: "acquisition.reserve.operation.label" })).toBeNull();
  expect(screen.queryByRole("button", { name: "acquisition.recovery.localFiles.confirm" })).toBeNull();
  expect(api.apply).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.localFiles.prepareConfirm" }));
  await screen.findByText("acquisition.recovery.localFiles.prepareResult");
  expect(screen.getByText("acquisition.recovery.localFiles.status.registered")).toBeVisible();
  expect(screen.getByText("acquisition.recovery.localFiles.prepareResultHint")).toBeVisible();
  expect(screen.queryByRole("button", { name: "acquisition.recovery.recheck" })).toBeNull();
  expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-token", [1], undefined, { prepareOnly: true });
  expect(continuation.create).not.toHaveBeenCalled();
});

it("reports the actual started run if a register-only submission receives an unexpected receipt", async () => {
  api.preview.mockResolvedValue(preview({ scope: "localFiles", rows: [localRow] }));
  api.apply.mockResolvedValue({
    continuedTaskIds: [1], skipped: [], localFiles: [
      { taskId: 1, planRevision: 2, previousWorkflowRunId: 101, workflowRunId: 202, status: "started" },
    ],
  });
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="localFiles" />);
  fireEvent.change(await screen.findByRole("combobox", { name: "acquisition.recovery.localFiles.processingMode" }), {
    target: { value: "prepareOnly" },
  });
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.localFiles.prepareConfirm" }));
  await screen.findByText("acquisition.recovery.localFiles.status.started");
  expect(screen.queryByText("acquisition.recovery.localFiles.prepareResult")).toBeNull();
});

it("does not switch a prepare-only recheck into the automatic processing queue", async () => {
  api.preview.mockResolvedValueOnce(preview({ scope: "localFiles", rows: [localRow] }))
    .mockReturnValueOnce(new Promise(() => {}));
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="localFiles" />);
  fireEvent.change(await screen.findByRole("combobox", { name: "acquisition.recovery.localFiles.processingMode" }), {
    target: { value: "prepareOnly" },
  });
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.recheck" }));
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(2));
  expect(screen.queryByRole("button", { name: "acquisition.continue.skipDiagnosis" })).toBeNull();
  expect(continuation.create).not.toHaveBeenCalled();
  expect(api.apply).not.toHaveBeenCalled();
});

it("only offers registration for opted-in previews and never silently falls back when the selection changes", async () => {
  api.preview.mockResolvedValue(preview({ scope: "localFiles", rows: [
    localRow, { ...localRow, taskId: 2, title: "Older server preview", localFiles: { parts: localFiles.parts } },
  ] }));
  render(<AcquisitionRecoveryModal taskIds={[1, 2]} initialScope="localFiles" />);
  const method = await screen.findByRole("combobox", { name: "acquisition.recovery.localFiles.processingMode" });
  expect(screen.queryByRole("option", { name: "acquisition.recovery.localFiles.prepareOnly" })).toBeNull();
  fireEvent.click(checkbox(1));
  fireEvent.change(method, { target: { value: "prepareOnly" } });
  const submit = screen.getByRole("button", { name: "acquisition.recovery.localFiles.prepareConfirm" });
  expect(submit).toBeEnabled();
  fireEvent.click(checkbox(1));
  expect(submit).toBeDisabled();
  expect(method).toHaveValue("prepareOnly");
  expect(screen.getByText("acquisition.recovery.localFiles.prepareUnavailable")).toBeVisible();
  fireEvent.click(submit);
  expect(api.apply).not.toHaveBeenCalled();
});

it("shows the server reason for skipped registrations and invalid prepare-only submissions", async () => {
  api.preview.mockResolvedValue(preview({ scope: "localFiles", rows: [localRow] }));
  api.apply.mockRejectedValueOnce(new Error("RECOVERY_PREPARE_OPTIONS_INVALID"))
    .mockResolvedValueOnce({ continuedTaskIds: [], skipped: [{ taskId: 1, reasonCode: "RECOVERY_PREPARATION_CONFLICT" }], localFiles: [] });
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="localFiles" />);
  fireEvent.change(await screen.findByRole("combobox", { name: "acquisition.recovery.localFiles.processingMode" }), {
    target: { value: "prepareOnly" },
  });
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.localFiles.prepareConfirm" }));
  expect(await screen.findByText("acquisition.recovery.reason.RECOVERY_PREPARE_OPTIONS_INVALID")).toBeVisible();
  api.preview.mockResolvedValue(preview({ previewToken: "another-token", scope: "localFiles", rows: [localRow] }));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.recheck" }));
  fireEvent.click(await screen.findByRole("button", { name: "acquisition.recovery.localFiles.prepareConfirm" }));
  expect(await screen.findByText("acquisition.recovery.reason.RECOVERY_PREPARATION_CONFLICT")).toBeVisible();
  expect(screen.queryByText("acquisition.recovery.localFiles.prepareResultHint")).toBeNull();
});

it("requires an explicit AI choice for verified local files and preserves the unchecked configured reserve", async () => {
  api.preview.mockResolvedValue(preview({ scope: "localFiles", rows: [{ ...localRow, canOverrideStorage: true }] }));
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="localFiles" />);
  const method = await screen.findByRole("combobox", { name: "acquisition.recovery.localFiles.processingMode" });
  expect(method).toHaveValue("original");
  expect(screen.queryByRole("combobox", { name: "acquisition.adaptive.iterations" })).toBeNull();
  expect(screen.queryByRole("checkbox", { name: "acquisition.reserve.operation.label" })).toBeNull();
  expect(defaults.read).not.toHaveBeenCalled();
  await chooseLocalAnalysis();
  const rounds = screen.getByRole("combobox", { name: "acquisition.adaptive.iterations" });
  expect(rounds).toHaveValue("5");
  expect(Array.from(rounds.querySelectorAll("option"), option => option.value)).toEqual(["1", "2", "3", "4", "5"]);
  const reserve = await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  expect(reserve).toBeChecked();
  expect(screen.getAllByRole("checkbox", { name: "acquisition.reserve.operation.label" })).toHaveLength(1);
  fireEvent.click(reserve);
  expect(api.apply).not.toHaveBeenCalled();
  fireEvent.click(localConfirm());
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-token", [1], false,
    { analyzeFilesFirst: true, maxAdaptiveIterations: 5 }));
});

it("passes the selected AI round limit and consumes the confirmation after an uncertain reply", async () => {
  api.preview.mockResolvedValue(preview({ scope: "localFiles", rows: [localRow] }));
  api.apply.mockRejectedValue(new Error("Lost response"));
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="localFiles" />);
  await chooseLocalAnalysis();
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.adaptive.iterations" }), { target: { value: "2" } });
  fireEvent.click(localConfirm());
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-token", [1], undefined,
    { analyzeFilesFirst: true, maxAdaptiveIterations: 2 }));
  expect(localConfirm()).toBeDisabled();
  expect(screen.getByRole("combobox", { name: "acquisition.recovery.localFiles.processingMode" })).toBeDisabled();
  expect(screen.getByRole("combobox", { name: "acquisition.adaptive.iterations" })).toBeDisabled();
});

it("returns to the original local-files request when AI analysis is deselected", async () => {
  api.preview.mockResolvedValue(preview({ scope: "localFiles", rows: [{ ...localRow, canOverrideStorage: true }] }));
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="localFiles" />);
  await chooseLocalAnalysis();
  fireEvent.click(await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" }));
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.recovery.localFiles.processingMode" }), {
    target: { value: "original" },
  });
  expect(screen.queryByRole("checkbox", { name: "acquisition.reserve.operation.label" })).toBeNull();
  fireEvent.click(localConfirm());
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-token", [1]));
});

it("never carries local AI options into a downloads-only confirmation", async () => {
  api.preview.mockResolvedValueOnce(preview({ scope: "localFiles", rows: [localRow] }));
  api.preview.mockResolvedValueOnce(preview({ scope: "downloads", previewToken: "new-download-token", rows: [
    { taskId: 1, title: "Original download", action: "downloads", canContinue: true },
  ] }));
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="localFiles" />);
  await chooseLocalAnalysis();
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.recovery.scope.label" }), {
    target: { value: "downloads" },
  });
  await screen.findByText("Original download");
  expect(screen.queryByRole("combobox", { name: "acquisition.recovery.localFiles.processingMode" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.downloads.confirm" }));
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith("new-download-token", [1]));
});

it("previews all local file inputs and old/new directories without starting or exposing editable paths", async () => {
  api.preview.mockResolvedValue(
    preview({
      scope: "localFiles",
      rows: [
        localRow,
        { ...localRow, taskId: 2, title: "Workflow action", action: "resume" },
        { ...localRow, taskId: 3, title: "Download action", action: "downloads" },
        { ...localRow, taskId: 4, title: "Incomplete preview", localFiles: { parts: [] } },
      ],
    }),
  );
  api.apply.mockResolvedValue({
    continuedTaskIds: [1],
    skipped: [],
    localFiles: [
      {
        taskId: 1,
        previousWorkflowRunId: 101,
        workflowRunId: 201,
        planRevision: 3,
        status: "started",
      },
    ],
  });
  render(<AcquisitionRecoveryModal taskIds={[1, 2, 3, 4]} initialScope="localFiles" />);
  await screen.findByText("Completed local downloads");
  expect(api.preview).toHaveBeenCalledExactlyOnceWith([1, 2, 3, 4], "localFiles", expect.objectContaining({ diagnosisId: expect.any(String), signal: expect.any(AbortSignal) }));
  expect(checkbox(0)).toBeChecked();
  for (const index of [1, 2, 3]) expect(checkbox(index)).toBeDisabled();
  expect(screen.getAllByText("/downloads/main").length).toBeGreaterThan(0);
  expect(screen.getAllByText("/downloads/extras").length).toBeGreaterThan(0);
  expect(screen.getAllByText(/\/old\/work\/main/).length).toBeGreaterThan(0);
  expect(screen.getAllByText(/\/current\/work\/main/).length).toBeGreaterThan(0);
  expect(screen.getAllByText(/\/old\/delivery\/main/).length).toBeGreaterThan(0);
  expect(screen.getAllByText(/\/current\/delivery\/extras/).length).toBeGreaterThan(0);
  expect(screen.getByText("acquisition.recovery.localFiles.incompleteEvidence")).toBeVisible();
  expect(screen.queryByRole("textbox")).toBeNull();
  expect(screen.queryByText("bound-file-fingerprint")).toBeNull();
  expect(api.apply).not.toHaveBeenCalled();
  fireEvent.click(localConfirm());
  await screen.findByText("acquisition.recovery.localFiles.result");
  expect(api.apply).toHaveBeenCalledExactlyOnceWith("fixed-token", [1]);
  expect(screen.getByText("acquisition.recovery.localFiles.status.started")).toBeVisible();
  expect(screen.getByText("acquisition.recovery.localFiles.newRun")).toBeVisible();
  expect(screen.queryByText("acquisition.recovery.downloads.resultHint")).toBeNull();
});

it.each([
  undefined,
  { parts: "invalid" },
  { parts: [] },
  { parts: [localFiles.parts[0], { ...localFiles.parts[1], workingDirectory: "" }] },
  { parts: [localFiles.parts[0], { ...localFiles.parts[1], deliveryDirectory: "" }] },
  { parts: [localFiles.parts[0], { ...localFiles.parts[1], fingerprint: "" }] },
  { parts: [localFiles.parts[0], localFiles.parts[0]] },
])("cannot confirm an incomplete or ambiguous local-files preview: %j", async (evidence) => {
  api.preview.mockResolvedValue(
    preview({ scope: "localFiles", rows: [{ ...localRow, localFiles: evidence }] }),
  );
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="localFiles" />);
  await screen.findByText("Completed local downloads");
  expect(checkbox(0)).toBeDisabled();
  expect(localConfirm()).toBeDisabled();
  expect(api.apply).not.toHaveBeenCalled();
});

it("pins a fresh local-files preview when switching scopes and rejects a late workflow result", async () => {
  let finish!: (value: unknown) => void;
  api.preview.mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  api.preview.mockResolvedValueOnce(
    preview({ scope: "localFiles", previewToken: "local-token", rows: [localRow] }),
  );
  render(<AcquisitionRecoveryModal taskIds={[1]} />);
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(1));
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "localFiles" } });
  await screen.findByText("Completed local downloads");
  await act(async () => finish(preview()));
  expect(screen.queryByText("Old lifecycle failure")).toBeNull();
  expect(api.apply).not.toHaveBeenCalled();
  fireEvent.click(localConfirm());
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith("local-token", [1]));
});

it("rejects local continuation rows in the workflow scope", async () => {
  api.preview.mockResolvedValue(preview({ rows: [localRow] }));
  render(<AcquisitionRecoveryModal taskIds={[1]} />);
  await screen.findByText("Completed local downloads");
  expect(checkbox(0)).toBeDisabled();
  expect(confirm()).toBeDisabled();
  expect(screen.queryByText("/downloads/main")).toBeNull();
});

it("reports prepared local revisions separately and rechecks without repeating the apply", async () => {
  api.preview.mockResolvedValue(preview({ scope: "localFiles", rows: [localRow] }));
  api.apply.mockResolvedValue({
    continuedTaskIds: [],
    skipped: [],
    localFiles: [
      {
        taskId: 1,
        previousWorkflowRunId: 101,
        workflowRunId: null,
        planRevision: 3,
        status: "prepared",
        reasonCode: "RECOVERY_STORAGE_SETTINGS_CHANGED",
      },
      {
        taskId: 999,
        previousWorkflowRunId: 900,
        workflowRunId: 901,
        planRevision: 8,
        status: "started",
      },
    ],
  });
  render(<AcquisitionRecoveryModal taskIds={[1]} initialScope="localFiles" />);
  await screen.findByText("Completed local downloads");
  fireEvent.click(localConfirm());
  await screen.findByText("acquisition.recovery.localFiles.status.prepared");
  expect(
    screen.getByText("acquisition.recovery.reason.RECOVERY_STORAGE_SETTINGS_CHANGED"),
  ).toBeVisible();
  expect(screen.queryByText("#999")).toBeNull();
  expect(screen.queryByText("acquisition.recovery.localFiles.status.started")).toBeNull();
  api.preview.mockResolvedValue(
    preview({ scope: "localFiles", previewToken: "prepared-revision-token", rows: [localRow] }),
  );
  fireEvent.click(screen.getByRole("button", { name: "acquisition.recovery.recheck" }));
  await screen.findByText("Completed local downloads");
  expect(api.preview).toHaveBeenCalledTimes(2);
  expect(api.apply).toHaveBeenCalledTimes(1);
  expect(localConfirm()).toBeEnabled();
});

const submittedContinuation = () => ({ batchId: "continued-1", status: "finished", scope: "workflow",
  createdAt: "2026-11-06T00:00:00Z", rows: [{ taskId: 1, title: "Submitted resource", status: "submitted" }] });
const deferredPreview = () => ({ previewToken: "continuation-preview", scope: "workflow",
  expiresAt: new Date(Date.now() + 600_000).toISOString(),
  rows: [{ taskId: 1, title: "Eligible", canQueue: true }, { taskId: 4, title: "Manual", canQueue: false }] });

it("preserves an explicit unchecked reserve when diagnosis is skipped", async () => {
  api.preview.mockReturnValueOnce(new Promise(() => {}));
  continuation.preview.mockResolvedValue({ ...deferredPreview(), rows: [
    { taskId: 1, title: "Eligible", canQueue: true, canOverrideStorage: true },
  ] });
  continuation.create.mockResolvedValue(submittedContinuation());
  render(<AcquisitionRecoveryModal taskIds={[1]} />);
  const choice = await screen.findByRole("checkbox", { name: "acquisition.reserve.operation.label" });
  expect(choice).toBeChecked();
  fireEvent.click(choice);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.skipDiagnosis" }));
  await waitFor(() => expect(continuation.create).toHaveBeenCalledExactlyOnceWith("continuation-preview", [1], false));
  expect(api.apply).not.toHaveBeenCalled();
});

it("waits for diagnosis cancellation acknowledgement before a fixed-scope continuation without storage override", async () => {
  let diagnose!: (value: unknown) => void;
  let stop!: (value: unknown) => void;
  api.preview.mockReturnValueOnce(new Promise(resolve => { diagnose = resolve; }));
  api.cancelDiagnosis.mockReturnValueOnce(new Promise(resolve => { stop = resolve; }));
  continuation.preview.mockResolvedValue(deferredPreview());
  continuation.create.mockResolvedValue(submittedContinuation());
  const view = render(<AcquisitionRecoveryModal taskIds={[1, 4, 1, -1]} />);
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(1));
  const diagnosis = api.preview.mock.calls[0][2];
  view.rerender(<AcquisitionRecoveryModal taskIds={[1, 4, 99]} />);
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.skipDiagnosis" }));
  expect(diagnosis.signal.aborted).toBe(true);
  expect(api.cancelDiagnosis).toHaveBeenCalledExactlyOnceWith(diagnosis.diagnosisId);
  expect(continuation.preview).not.toHaveBeenCalled();
  expect(continuation.create).not.toHaveBeenCalled();
  await act(async () => stop({ diagnosisId: diagnosis.diagnosisId, status: "stopped" }));
  await screen.findByText("acquisition.continue.submissionHint");
  expect(continuation.preview).toHaveBeenCalledExactlyOnceWith([1, 4], "workflow", expect.any(AbortSignal));
  expect(continuation.create).toHaveBeenCalledExactlyOnceWith("continuation-preview", [1]);
  expect(api.apply).not.toHaveBeenCalled();
  await act(async () => diagnose(preview()));
  expect(screen.queryByText("Old lifecycle failure")).toBeNull();
  expect(screen.getAllByText(/acquisition.continue.status.submitted/).length).toBeGreaterThan(0);
  expect(screen.queryByText("acquisition.stage.completed")).toBeNull();
});

it("does not enqueue anything when diagnosis cancellation is unconfirmed", async () => {
  api.preview.mockReturnValueOnce(new Promise(() => {}));
  api.cancelDiagnosis.mockResolvedValueOnce({ diagnosisId: "different", status: "stopped" });
  render(<AcquisitionRecoveryModal taskIds={[1]} />);
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(1));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.skipDiagnosis" }));
  await screen.findByText("acquisition.recovery.error.RECOVERY_DIAGNOSIS_STOP_UNCONFIRMED");
  expect(continuation.preview).not.toHaveBeenCalled();
  expect(continuation.create).not.toHaveBeenCalled();
  expect(api.apply).not.toHaveBeenCalled();
});

it.each([
  ["foreign task", [1, 999]],
  ["missing task", [1]],
  ["duplicate task", [1, 1]],
] as const)("rejects a continuation preview with a %s instead of submitting a partial or different selection", async (_kind, taskIds) => {
  api.preview.mockReturnValueOnce(new Promise(() => {}));
  continuation.preview.mockResolvedValue({
    ...deferredPreview(),
    rows: taskIds.map(taskId => ({ taskId, title: `Task ${taskId}`, canQueue: true })),
  });
  render(<AcquisitionRecoveryModal taskIds={[1, 4]} />);
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(1));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.skipDiagnosis" }));
  await screen.findByText("acquisition.continue.prepareFailed");
  expect(continuation.preview).toHaveBeenCalledExactlyOnceWith([1, 4], "workflow", expect.any(AbortSignal));
  expect(continuation.create).not.toHaveBeenCalled();
  expect(continuation.findReceipt).not.toHaveBeenCalled();
  expect(api.apply).not.toHaveBeenCalled();
});

it("resolves a lost continuation create reply only through its receipt, never by resubmitting", async () => {
  api.preview.mockReturnValueOnce(new Promise(() => {}));
  continuation.preview.mockResolvedValue(deferredPreview());
  continuation.create.mockRejectedValueOnce(new Error("reply lost"));
  continuation.findReceipt.mockRejectedValueOnce(new Error("receipt temporarily unavailable"))
    .mockResolvedValueOnce(submittedContinuation());
  render(<AcquisitionRecoveryModal taskIds={[1, 4]} />);
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(1));
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.skipDiagnosis" }));
  await screen.findByText("acquisition.continue.unknownOutcome");
  expect(continuation.create).toHaveBeenCalledExactlyOnceWith("continuation-preview", [1]);
  expect(continuation.findReceipt).toHaveBeenCalledExactlyOnceWith("continuation-preview");
  fireEvent.click(screen.getByRole("button", { name: "acquisition.continue.checkReceipt" }));
  await screen.findByText("acquisition.continue.submissionHint");
  expect(continuation.findReceipt.mock.calls.map(call => call[0])).toEqual(["continuation-preview", "continuation-preview"]);
  expect(continuation.create).toHaveBeenCalledTimes(1);
  expect(api.apply).not.toHaveBeenCalled();
});

it("waits for a matching stop acknowledgement before navigating to another continuation mode", async () => {
  let stop!: (value: unknown) => void;
  api.preview.mockReturnValueOnce(new Promise(() => {}));
  api.cancelDiagnosis.mockReturnValueOnce(new Promise(resolve => { stop = resolve; }));
  const change = vi.fn();
  render(<AcquisitionRecoveryModal taskIds={[1]} continuationScope="workflow" onContinuationScopeChange={change} />);
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(1));
  const diagnosis = api.preview.mock.calls[0][2];
  fireEvent.change(screen.getByRole("combobox", { name: "acquisition.continueMode.label" }), { target: { value: "localQueue" } });
  expect(diagnosis.signal.aborted).toBe(true);
  expect(change).not.toHaveBeenCalled();
  await act(async () => stop({ diagnosisId: diagnosis.diagnosisId, status: "stopped" }));
  expect(change).toHaveBeenCalledExactlyOnceWith("localQueue");
  expect(api.preview).toHaveBeenCalledTimes(1);
});

it("does not close until cancellation is acknowledged and stops a diagnosis on real unmount", async () => {
  let stop!: (value: unknown) => void;
  api.preview.mockReturnValueOnce(new Promise(() => {}));
  api.cancelDiagnosis.mockReturnValueOnce(new Promise(resolve => { stop = resolve; }));
  const view = render(<AcquisitionRecoveryModal taskIds={[1]} />);
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(1));
  const diagnosis = api.preview.mock.calls[0][2];
  fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
  expect(screen.getByRole("dialog")).toBeVisible();
  expect(diagnosis.signal.aborted).toBe(true);
  await act(async () => stop({ diagnosisId: diagnosis.diagnosisId, status: "stopped" }));
  expect(screen.queryByRole("dialog")).toBeNull();
  view.unmount();

  api.preview.mockReturnValueOnce(new Promise(() => {}));
  const second = render(<AcquisitionRecoveryModal taskIds={[4]} />);
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(2));
  const other = api.preview.mock.calls[1][2];
  second.unmount();
  await Promise.resolve();
  expect(other.signal.aborted).toBe(true);
  expect(api.cancelDiagnosis).toHaveBeenLastCalledWith(other.diagnosisId);
  expect(continuation.create).not.toHaveBeenCalled();
});
