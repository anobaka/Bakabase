import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import StorageReserveOption from "../StorageReserveOption";
import { useContext } from "react";
import { ResumeSubmissionContext } from "@/components/Workflow/ResumeSubmissionContext";
import ResumeActions from "@/components/Workflow/ResumeActions";
import TaskContinuationModal from "../TaskContinuationModal";

const api = vi.hoisted(() => ({ preview: vi.fn(), capacity: vi.fn(), apply: vi.fn(), resumeDownload: vi.fn() }));
vi.mock("../storageOverrideApi", () => ({ storageOverrideApi: api }));
vi.mock("@/components/utils", () => ({ bytesToSize: (bytes: number) => `${bytes} bytes`,
  humanFileSize: (bytes: number) => `${bytes} bytes` }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, footer, title, visible = true }: any) =>
    visible ? (
      <div role="dialog" aria-label={title}>
        {children}
        {footer}
      </div>
    ) : null,
  Button: ({ children, onPress, isDisabled, isLoading, "aria-label": label }: any) => (
    <button aria-label={label} disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Checkbox: ({ children, isSelected, onValueChange, isDisabled }: any) => (
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
  Spinner: () => null,
  Tooltip: ({ children }: any) => children,
}));
const preview = () => ({
  taskId: 42,
  workflowRunId: 77,
  token: "only-this-run",
  expiresAt: new Date(Date.now() + 60000).toISOString(),
  minimumFreeBytes: 10737418240,
  overrideMinimumFreeBytes: 1073741824,
});
const regular = vi.fn();
const currentStep = (onChanged?: () => void) => (
  <StorageReserveOption taskId={42} workflowRunId={77} onChanged={onChanged} actionLabel="Continue this step">
    <button onClick={regular}>Continue this step</button>
  </StorageReserveOption>
);
const applyButton = () => screen.getByRole("button", { name: "Continue this step" });
beforeEach(() => {
  vi.clearAllMocks();
  api.preview.mockReset().mockImplementation(async () => preview());
  api.capacity.mockReset().mockResolvedValue([]);
  api.apply.mockReset().mockResolvedValue({
    taskId: 42,
    workflowRunId: 77,
    status: "applied",
    continuationAccepted: true,
  });
});

function DownloadAnswer() {
  const resume = useContext(ResumeSubmissionContext);
  return <div>
    <input aria-label="Download access code" defaultValue="fixture-code" />
    <ResumeActions><button onClick={() => void resume?.submit('{"action":"retry","targetId":"part-1","accessCode":"fixture-code"}')}>Retry download</button></ResumeActions>
  </div>;
}

it("keeps a portalled download action locked until its original-signal storage preview succeeds", async () => {
  let finish!: (value: unknown) => void;
  api.preview.mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  render(<TaskContinuationModal stale={false} onClose={vi.fn()} current={
    <StorageReserveOption taskId={42} workflowRunId={77} scope="download" actionLabel="Retry download"><DownloadAnswer /></StorageReserveOption>
  } />);
  const retry = screen.getByRole("button", { name: "Retry download" });
  expect(retry).toBeDisabled();
  await act(async () => finish({ ...preview(), scope: "download", useEmergencySpaceReserve: false }));
  expect(retry).toBeEnabled();
  expect(screen.getAllByRole("button", { name: "Retry download" })).toHaveLength(1);
  expect(api.resumeDownload).not.toHaveBeenCalled();
});

it.each([[true, false], [false, true], [true, undefined]] as const)(
  "keeps the download answer while applying global=%s and explicit=%s to this attempt",
  async (globalDefault, choice) => {
    api.preview.mockResolvedValue({ ...preview(), scope: "download", useEmergencySpaceReserve: globalDefault,
      effectiveReserveMode: globalDefault ? "configured" : "emergency" });
    api.resumeDownload.mockResolvedValue({ taskId: 42, workflowRunId: 77, status: "applied", continuationAccepted: true });
    render(<StorageReserveOption taskId={42} workflowRunId={77} scope="download" actionLabel="Retry download">
      <DownloadAnswer />
    </StorageReserveOption>);
    const checkbox = await screen.findByRole("checkbox");
    expect(api.preview).toHaveBeenCalledExactlyOnceWith(42, "download");
    if (globalDefault) expect(checkbox).toBeChecked();
    else expect(checkbox).not.toBeChecked();
    if (choice !== undefined) fireEvent.click(checkbox);
    expect(screen.getByLabelText("Download access code")).toBeVisible();
    expect(screen.getByLabelText("Download access code")).toHaveValue("fixture-code");
    expect(api.resumeDownload).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Retry download" }));
    await waitFor(() => expect(api.resumeDownload).toHaveBeenCalledExactlyOnceWith(42, "only-this-run",
      '{"action":"retry","targetId":"part-1","accessCode":"fixture-code"}', choice ?? globalDefault));
    expect(api.apply).not.toHaveBeenCalled();
  },
);

it("does not let a legacy or wrong-scope preview authorize a download answer", async () => {
  render(<StorageReserveOption taskId={42} workflowRunId={77} scope="download" actionLabel="Retry download">
    <DownloadAnswer />
  </StorageReserveOption>);
  expect(await screen.findByRole("alert")).toHaveTextContent("acquisition.storageOverride.error.preview");
  expect(screen.getByRole("button", { name: "Retry download" })).toBeDisabled();
  expect(api.resumeDownload).not.toHaveBeenCalled();
});

it.each([0, 1024 ** 3])("keeps normal download confirmation available at a configured %s-byte reserve", async (minimumFreeBytes) => {
  api.preview.mockResolvedValue({ ...preview(), minimumFreeBytes, scope: "download", useEmergencySpaceReserve: true });
  api.resumeDownload.mockResolvedValue({ taskId: 42, workflowRunId: 77, status: "applied", continuationAccepted: true });
  api.capacity.mockResolvedValue([{ volumeId: "ssd", paths: ["/ssd"], availableBytes: 0,
    estimatedAdditionalBytes: 0, estimateIncomplete: false }]);
  render(<StorageReserveOption taskId={42} workflowRunId={77} scope="download" actionLabel="Retry download">
    <DownloadAnswer />
  </StorageReserveOption>);
  const retry = screen.getByRole("button", { name: "Retry download" });
  await waitFor(() => expect(retry).toBeEnabled());
  expect(screen.queryByRole("checkbox")).toBeNull();
  if (!minimumFreeBytes) expect(screen.queryByText("acquisition.reserve.capacity.estimatedShortfall")).toBeNull();
  fireEvent.click(retry);
  await waitFor(() => expect(api.resumeDownload).toHaveBeenCalledExactlyOnceWith(42, "only-this-run",
    '{"action":"retry","targetId":"part-1","accessCode":"fixture-code"}', true));
});

it("compares current space to this run's reserve and estimated writes, and refreshes independently", async () => {
  const gib = 1024 ** 3;
  const volume = { volumeId: "ssd", paths: ["/ssd"], availableBytes: 3 * gib, estimatedAdditionalBytes: gib,
    estimateIncomplete: true };
  api.capacity.mockResolvedValueOnce([volume]).mockResolvedValueOnce([{ ...volume, availableBytes: gib / 2 }]);
  render(currentStep());
  expect(await screen.findByText("acquisition.reserve.capacity.estimatedShortfall")).toBeVisible();
  fireEvent.click(screen.getByRole("checkbox"));
  expect(screen.queryByText("acquisition.reserve.capacity.estimatedShortfall")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.reserve.capacity.refresh" }));
  expect(await screen.findByText("acquisition.reserve.capacity.estimatedShortfall")).toBeVisible();
  expect(api.preview).toHaveBeenCalledTimes(1);
  expect(api.apply).not.toHaveBeenCalled();
});

it.each([true, false])(
  "requires explicit confirmation and reports continuationAccepted=%s without claiming completion",
  async (accepted) => {
    api.apply.mockResolvedValue({
      taskId: 42,
      workflowRunId: 77,
      status: "applied",
      continuationAccepted: accepted,
    });
    const onChanged = vi.fn();
    render(currentStep(onChanged));
    const checkbox = await screen.findByRole("checkbox");
    expect(api.preview).toHaveBeenCalledExactlyOnceWith(42);
    expect(api.apply).not.toHaveBeenCalled();
    expect(checkbox).not.toBeChecked();
    expect(applyButton()).toBeEnabled();
    fireEvent.click(checkbox);
    fireEvent.click(applyButton());
    fireEvent.click(applyButton());
    await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith(42, "only-this-run", "emergency"));
    expect(
      await screen.findByText(
        `acquisition.storageOverride.result.${accepted ? "accepted" : "uncertain"}`,
      ),
    ).toBeVisible();
    expect(screen.getByText("acquisition.storageOverride.result.hint")).toBeVisible();
    expect(onChanged).toHaveBeenCalledOnce();
    expect(regular).not.toHaveBeenCalled();
    expect(
      screen.queryByRole("button", { name: "Continue this step" }),
    ).toBeNull();
  },
);

it.each([{ taskId: 43 }, { workflowRunId: 78 }, { overrideMinimumFreeBytes: 0 }])(
  "rejects a mismatched preview %j",
  async (change) => {
    api.preview.mockResolvedValue({ ...preview(), ...change });
    render(currentStep());
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "acquisition.storageOverride.error.preview",
    );
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(applyButton()).toBeEnabled();
    expect(api.apply).not.toHaveBeenCalled();
  },
);

it("blocks expired tokens and clears confirmation when refreshing", async () => {
  api.preview.mockResolvedValueOnce({ ...preview(), expiresAt: "2020-01-01T00:00:00Z" });
  render(currentStep());
  expect(await screen.findByText("acquisition.storageOverride.expired")).toBeVisible();
  expect(screen.getByRole("checkbox")).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "acquisition.storageOverride.refresh" }));
  await waitFor(() => expect(screen.getByRole("checkbox")).toBeEnabled());
  expect(screen.getByRole("checkbox")).not.toBeChecked();
  expect(applyButton()).toBeEnabled();
  expect(api.apply).not.toHaveBeenCalled();
});

it("reports a withdrawn override separately from an applied or uncertain result", async () => {
  api.apply.mockResolvedValue({
    taskId: 42,
    workflowRunId: 77,
    status: "revoked",
    continuationAccepted: false,
    reasonCode: "RECOVERY_CHECK_INCOMPLETE",
  });
  render(currentStep());
  fireEvent.click(await screen.findByRole("checkbox"));
  fireEvent.click(applyButton());
  expect(await screen.findByText("acquisition.storageOverride.result.revoked")).toBeVisible();
  expect(screen.queryByText("acquisition.storageOverride.result.accepted")).toBeNull();
  expect(screen.queryByRole("alert")).toBeNull();
});

it("explains why actual storage errors cannot be overridden", async () => {
  api.preview.mockRejectedValue(new Error("STORAGE_OVERRIDE_REASON_UNSUPPORTED"));
  render(currentStep());
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "acquisition.storageOverride.reason.STORAGE_OVERRIDE_REASON_UNSUPPORTED",
  );
  expect(api.apply).not.toHaveBeenCalled();
});

it("refreshes status after an uncertain result and prevents repeating a consumed token", async () => {
  api.apply.mockRejectedValue(new Error("connection lost"));
  const onChanged = vi.fn();
  render(currentStep(onChanged));
  fireEvent.click(await screen.findByRole("checkbox"));
  fireEvent.click(applyButton());
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "acquisition.storageOverride.error.submit",
  );
  expect(onChanged).toHaveBeenCalledOnce();
  expect(applyButton()).toBeDisabled();
  await act(async () =>
    fireEvent.click(screen.getByRole("button", { name: "acquisition.storageOverride.refresh" })),
  );
  expect(screen.getByRole("checkbox")).not.toBeChecked();
  expect(api.apply).toHaveBeenCalledOnce();
});

it("uses the original step action by default and after undoing the optional override", async () => {
  render(currentStep());
  const option = await screen.findByRole("checkbox");
  fireEvent.click(applyButton());
  fireEvent.click(option);
  fireEvent.click(option);
  fireEvent.click(applyButton());
  expect(regular).toHaveBeenCalledTimes(2);
  expect(api.apply).not.toHaveBeenCalled();
});

it("inherits a lowered global reserve and can restore the configured reserve for just this run", async () => {
  api.preview.mockResolvedValue({ ...preview(), useEmergencySpaceReserve: true, effectiveReserveMode: "emergency" });
  render(currentStep());
  const choice = await screen.findByRole("checkbox");
  expect(choice).toBeChecked();
  fireEvent.click(applyButton());
  expect(regular).toHaveBeenCalledOnce();
  expect(api.apply).not.toHaveBeenCalled();
  fireEvent.click(choice);
  fireEvent.click(applyButton());
  await waitFor(() => expect(api.apply).toHaveBeenCalledExactlyOnceWith(42, "only-this-run", "configured"));
});

it("shows an existing run override even if it differs from the global setting", async () => {
  api.preview.mockResolvedValue({ ...preview(), useEmergencySpaceReserve: true, effectiveReserveMode: "configured" });
  render(currentStep());
  expect(await screen.findByRole("checkbox")).not.toBeChecked();
  fireEvent.click(applyButton());
  expect(regular).toHaveBeenCalledOnce();
  expect(api.apply).not.toHaveBeenCalled();
});

it("shows the global default for a recovery operation that clears the previous run-only choice", async () => {
  api.preview.mockResolvedValue({ ...preview(), useEmergencySpaceReserve: true, effectiveReserveMode: "configured" });
  render(<StorageReserveOption taskId={42} workflowRunId={77} actionLabel="Continue this step" inheritGlobalDefault>
    <button onClick={regular}>Continue this step</button>
  </StorageReserveOption>);
  expect(await screen.findByRole("checkbox")).toBeChecked();
  fireEvent.click(applyButton());
  expect(regular).toHaveBeenCalledOnce();
  expect(api.apply).not.toHaveBeenCalled();
});
