import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { defaultStoragePolicy } from "@/components/Acquire/StoragePolicyFields";
import AcquisitionStorageMaintenanceModal from "../components/AcquisitionStorageMaintenanceModal";
const api = vi.hoisted(() => ({ preview: vi.fn(), apply: vi.fn() }));
vi.mock("../components/acquisitionStorageApi", () => ({ acquisitionStorageApi: api }));
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
      onChange={(e) => onValueChange(e.target.checked)}
    />
  ),
  Spinner: () => <span role="status" />,
  Chip: ({ children }: any) => <span>{children}</span>,
  Pagination: ({ page, onChange }: any) => (
    <button onClick={() => onChange(page + 1)}>next page</button>
  ),
}));
const preview = (overrides: any = {}) => ({
  previewId: "fixed-preview",
  expiresAt: new Date(Date.now() + 600_000).toISOString(),
  eligibleCount: 1,
  rows: [
    {
      taskId: 1,
      title: "Never started",
      eligible: true,
      reasonCode: "STORAGE_POLICY_READY",
      downloadPreview: "/downloads/My title [D123]",
      processingPreview: "/work/My title/extract",
      deliveryPreview: "/delivery/My title [A1]",
      fileCount: 3,
      bytes: 1024 ** 3,
    },
    { taskId: 2, title: "Already running", eligible: false, reasonCode: "STORAGE_POLICY_STARTED" },
  ],
  ...overrides,
});
const confirm = (mode = "policy") =>
  screen.getByRole("button", { name: `acquisition.storage.batch.${mode}.confirm` });
beforeEach(() => {
  vi.clearAllMocks();
  api.preview.mockReset().mockResolvedValue(preview());
  api.apply.mockReset().mockResolvedValue({ taskIds: [1], skipped: [] });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

it("captures unique IDs and previews paths without changing settings or starting tasks", async () => {
  render(
    <StrictMode>
      <AcquisitionStorageMaintenanceModal
        mode="policy"
        taskIds={[1, 2, 1, -2]}
        scopeLabel="filtered"
      />
    </StrictMode>,
  );
  await screen.findByText("#1 · Never started");
  expect(api.preview).toHaveBeenCalledExactlyOnceWith("policy", [1, 2], expect.any(AbortSignal));
  expect(api.preview.mock.calls[0][2].aborted).toBe(false);
  expect(api.apply).not.toHaveBeenCalled();
  expect(screen.getByText("/downloads/My title [D123]")).toBeVisible();
  const checks = screen.getAllByRole("checkbox");
  expect(checks[0]).toBeChecked();
  expect(checks[1]).toBeDisabled();
  fireEvent.click(confirm());
  await waitFor(() =>
    expect(api.apply).toHaveBeenCalledExactlyOnceWith("policy", "fixed-preview", [1]),
  );
});

it("cleanup requires explicit selection and reports workspace removal without promising free space", async () => {
  api.apply.mockResolvedValue({
    taskIds: [1],
    skipped: [],
    removedFiles: 3,
    removedBytes: 1024 ** 3,
  });
  render(<AcquisitionStorageMaintenanceModal mode="cleanup" taskIds={[1, 2]} />);
  await screen.findByText("#1 · Never started");
  expect(screen.getAllByRole("checkbox")[0]).not.toBeChecked();
  expect(confirm("cleanup")).toBeDisabled();
  expect(api.apply).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storage.batch.selectAll" }));
  fireEvent.click(confirm("cleanup"));
  await screen.findByText("acquisition.storage.batch.cleanup.result");
  expect(api.apply).toHaveBeenCalledExactlyOnceWith("cleanup", "fixed-preview", [1]);
  expect(screen.getByText("acquisition.storage.batch.cleanup.spaceHint")).toBeVisible();
});

it("holds the captured filter, allows deselection and rejects rows outside it", async () => {
  api.preview.mockResolvedValue(
    preview({ rows: [...preview().rows, { taskId: 999, title: "outside scope", eligible: true }] }),
  );
  const view = render(<AcquisitionStorageMaintenanceModal mode="policy" taskIds={[1, 2]} />);
  await screen.findByText("#1 · Never started");
  view.rerender(<AcquisitionStorageMaintenanceModal mode="policy" taskIds={[999]} />);
  expect(screen.queryByText(/outside scope/)).toBeNull();
  fireEvent.click(screen.getAllByRole("checkbox")[0]);
  expect(confirm()).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storage.batch.selectAll" }));
  fireEvent.click(confirm());
  await waitFor(() =>
    expect(api.apply).toHaveBeenCalledExactlyOnceWith("policy", "fixed-preview", [1]),
  );
});

it("binds explicit retention override to a fresh preview and clears the previous selection", async () => {
  render(<AcquisitionStorageMaintenanceModal mode="cleanup" taskIds={[1, 2]} />);
  await screen.findByText("#1 · Never started");
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storage.batch.selectAll" }));
  expect(confirm("cleanup")).not.toBeDisabled();
  let finish!: (value: any) => void;
  api.preview.mockImplementationOnce(() => new Promise((resolve) => { finish = resolve; }));
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.storage.batch.cleanup.overrideRetention" }));
  expect(api.preview).toHaveBeenLastCalledWith("cleanup", [1, 2], expect.any(AbortSignal), true, false);
  expect(confirm("cleanup")).toBeDisabled();
  finish(preview({ previewId: "explicit-cleanup" }));
  await screen.findByText("#1 · Never started");
  expect(confirm("cleanup")).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storage.batch.selectAll" }));
  fireEvent.click(confirm("cleanup"));
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith("cleanup", "explicit-cleanup", [1]));
});

it("ignores a late override preview after consent is unchecked", async () => {
  render(<AcquisitionStorageMaintenanceModal mode="cleanup" taskIds={[1]} />);
  await screen.findByText("#1 · Never started");
  let finish!: (value: any) => void;
  api.preview.mockImplementationOnce(() => new Promise((resolve) => { finish = resolve; }));
  const consent = screen.getByRole("checkbox", { name: "acquisition.storage.batch.cleanup.overrideRetention" });
  fireEvent.click(consent);
  const signal = api.preview.mock.calls.at(-1)![2] as AbortSignal;
  api.preview.mockResolvedValueOnce(preview({ previewId: "retention-restored" }));
  fireEvent.click(consent);
  expect(signal.aborted).toBe(true);
  await screen.findByText("#1 · Never started");
  finish(preview({ previewId: "stale-override" }));
  await Promise.resolve();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storage.batch.selectAll" }));
  fireEvent.click(confirm("cleanup"));
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith("cleanup", "retention-restored", [1]));
});

it("shows that pre-delivery cleanup preserves the download and delivery inputs", async () => {
  const row = { taskId: 91, title: "Awaiting review", eligible: false };
  api.preview
    .mockResolvedValueOnce(preview({ rows: [{ ...row, reasonCode: "processed_checkpoint_requires_confirmation" }] }))
    .mockResolvedValueOnce(preview({
      previewId: "processed-checkpoint",
      rows: [{
        ...row,
        eligible: true,
        fileCount: 2,
        bytes: 1024 ** 3,
        processedCheckpointCleanup: {
          verifiedResultArtifactIds: ["processed-output"],
          retainedDownloadArtifactIds: ["download"],
          intermediateArtifactIds: ["intermediate"],
        },
      }],
    }));
  render(<AcquisitionStorageMaintenanceModal mode="cleanup" taskIds={[91]} />);
  await screen.findByText("acquisition.storage.reason.processed_checkpoint_requires_confirmation");
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.storage.batch.cleanup.overrideRetention" }));
  await screen.findByText("acquisition.storage.batch.cleanup.processedCheckpoint");
  expect(confirm("cleanup")).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.storage.batch.selectTask" }));
  fireEvent.click(confirm("cleanup"));
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith("cleanup", "processed-checkpoint", [91]));
});

it("requires a fresh selection before also including original downloads", async () => {
  render(<AcquisitionStorageMaintenanceModal mode="cleanup" taskIds={[1]} />);
  await screen.findByText("#1 · Never started");
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.storage.batch.cleanup.overrideRetention" }));
  await screen.findByText("#1 · Never started");
  const include = screen.getByRole("checkbox", { name: "acquisition.storage.batch.cleanup.includeOriginalDownloads" });
  expect(include).not.toBeChecked();
  fireEvent.click(screen.getByRole("checkbox", { name: "acquisition.storage.batch.selectTask" }));
  expect(confirm("cleanup")).toBeEnabled();
  api.preview.mockResolvedValueOnce(preview({ previewId: "include-downloads" }));
  fireEvent.click(include);
  await screen.findByText("#1 · Never started");
  expect(confirm("cleanup")).toBeDisabled();
  expect(api.preview).toHaveBeenLastCalledWith("cleanup", [1], expect.any(AbortSignal), true, true);
  expect(api.apply).not.toHaveBeenCalled();
});

it("requires a new preview after unknown apply outcome and blocks duplicate submissions", async () => {
  api.apply.mockRejectedValue(new Error("network lost"));
  render(<AcquisitionStorageMaintenanceModal mode="policy" taskIds={[1, 2]} />);
  await screen.findByText("#1 · Never started");
  fireEvent.click(confirm());
  fireEvent.click(confirm());
  await screen.findByText("acquisition.storage.batch.error.submit");
  expect(api.apply).toHaveBeenCalledTimes(1);
  expect(confirm()).toBeDisabled();
  api.preview.mockResolvedValue(preview({ previewId: "fresh-preview" }));
  fireEvent.click(screen.getAllByRole("button", { name: "acquisition.storage.batch.refresh" })[0]);
  await waitFor(() => expect(confirm()).not.toBeDisabled());
});

it("does not apply expired previews", async () => {
  api.preview.mockResolvedValue(preview({ expiresAt: new Date(Date.now() - 1000).toISOString() }));
  render(<AcquisitionStorageMaintenanceModal mode="policy" taskIds={[1, 2]} />);
  await screen.findByText("acquisition.storage.batch.expired");
  expect(confirm()).toBeDisabled();
  expect(api.apply).not.toHaveBeenCalled();
});

it("shows the frozen retention policy before applying defaults", async () => {
  api.preview.mockResolvedValue(
    preview({ policy: { ...defaultStoragePolicy, sourceCleanup: "delete" } }),
  );
  render(<AcquisitionStorageMaintenanceModal mode="policy" taskIds={[1, 2]} />);
  await screen.findByText("acquisition.storage.sourceCleanup.deleteHint");
  expect(api.apply).not.toHaveBeenCalled();
});

it("cancels an outstanding read-only scan when closing without submitting cleanup", async () => {
  api.preview.mockImplementation(() => new Promise(() => {}));
  render(<AcquisitionStorageMaintenanceModal mode="cleanup" taskIds={[1, 2]} />);
  const signal = api.preview.mock.calls[0][2] as AbortSignal;
  expect(signal.aborted).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
  expect(signal.aborted).toBe(true);
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(api.apply).not.toHaveBeenCalled();
});

it("cancels an outstanding scan on real unmount but not StrictMode effect replay", async () => {
  api.preview.mockImplementation(() => new Promise(() => {}));
  const view = render(
    <StrictMode>
      <AcquisitionStorageMaintenanceModal mode="cleanup" taskIds={[1]} />
    </StrictMode>,
  );
  expect(api.preview).toHaveBeenCalledTimes(1);
  const signal = api.preview.mock.calls[0][2] as AbortSignal;
  await Promise.resolve();
  expect(signal.aborted).toBe(false);
  view.unmount();
  await waitFor(() => expect(signal.aborted).toBe(true));
  expect(api.apply).not.toHaveBeenCalled();
});
