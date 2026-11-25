import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import AcquisitionDownloadDestinationModal from "../components/AcquisitionDownloadDestinationModal";
import AcquisitionRecoveryModal from "../components/AcquisitionRecoveryModal";

const mocks = vi.hoisted(() => ({
  defaultRoot: vi.fn(),
  preview: vi.fn(),
  previewReuse: vi.fn(),
  apply: vi.fn(),
  portal: vi.fn(),
  translate: vi.fn((key: string) => key),
}));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: mocks.translate }),
}));
vi.mock("../components/acquisitionDownloadDestinationApi", () => ({
  acquisitionDownloadDestinationApi: mocks,
}));
vi.mock("../components/AcquisitionRecoveryModal", () => ({ default: () => null }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("@/components/FileSystemSelector/components/StorageCapacitySummary", () => ({
  default: ({ path }: any) => <span>capacity:{path}</span>,
}));
vi.mock("@/components/FileSystemSelector", () => ({
  FileSystemSelectorButton: ({ isDisabled, fileSystemSelectorProps, ...props }: any) => (
    <button
      aria-label={props["aria-label"]}
      disabled={isDisabled}
      onClick={() => fileSystemSelectorProps.onSelected({ path: "/ssd/new-root" })}
    >
      {fileSystemSelectorProps.defaultSelectedPath || "Choose"}
    </button>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ visible, children, footer, title }: any) =>
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
  Pagination: ({ page, total, onChange }: any) => (
    <button onClick={() => onChange(page + 1)}>
      page {page}/{total}
    </button>
  ),
}));
const download = (id: number, canApply = true) => ({
  downloadTaskId: id,
  oldDirectory: `/nas/${id}`,
  newDirectory: `/ssd/${id}`,
  canApply,
  reasonCode: canApply ? "READY" : "HAS_FILES",
});
const preview = (overrides: any = {}) => ({
  previewToken: "preview-1",
  expiresAt: new Date(Date.now() + 600_000).toISOString(),
  taskCount: 3,
  downloadCount: 4,
  rows: [
    { taskId: 1, title: "Two downloads", canApply: true, downloads: [download(11), download(12)] },
    {
      taskId: 2,
      title: "Protected files",
      canApply: false,
      reasonCode: "HAS_FILES",
      downloads: [download(21, false)],
    },
    { taskId: 3, title: "Safe destination", canApply: true, downloads: [download(31)] },
  ],
  ...overrides,
});
const button = (key: string) =>
  screen.getByRole("button", { name: `acquisition.downloadDestination.${key}` });
const checkboxes = () => screen.getAllByRole("checkbox");
async function show(props: any = {}) {
  const view = render(<AcquisitionDownloadDestinationModal taskIds={[1, 2, 3]} {...props} />);
  await screen.findByRole("button", { name: "acquisition.downloadDestination.chooseDirectory" });
  return view;
}
async function inspect(props: any = {}) {
  const view = await show(props);
  fireEvent.click(button("preview"));
  await screen.findByText("Two downloads");
  return view;
}
beforeEach(() => {
  vi.clearAllMocks();
  mocks.defaultRoot.mockReset().mockResolvedValue("/ssd/downloads");
  mocks.preview.mockReset().mockResolvedValue(preview());
  mocks.apply
    .mockReset()
    .mockResolvedValue({ appliedTaskIds: [1, 3], appliedDownloadCount: 3, skipped: [] });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

const selectReuse = async (response: ReturnType<typeof preview>) => {
  mocks.preview.mockResolvedValue(
    preview({
      rows: [
        {
          taskId: 1,
          title: "Two downloads",
          canApply: false,
          downloads: [{ ...download(11, false), canPreviewReuse: true }, download(12, false)],
        },
      ],
    }),
  );
  mocks.previewReuse.mockResolvedValue(response);
  await inspect();
  fireEvent.click(button("previewReuse"));
  await waitFor(() =>
    expect(mocks.previewReuse).toHaveBeenCalledExactlyOnceWith(1, 11, "/ssd/downloads"),
  );
};

it("confirms only the selected download after showing both space budgets", async () => {
  await selectReuse(
    preview({
      previewToken: "reuse-one",
      rows: [
        {
          taskId: 1,
          title: "One download",
          canApply: true,
          downloads: [
            {
              ...download(11),
              reuseEstimate: {
                fileCount: 6,
                candidateFileCount: 3,
                candidateBytes: 3072,
                requiredDownloadBytes: 4096,
                totalBytes: 7168,
              },
            },
          ],
        },
      ],
    }),
  );
  await screen.findByText("acquisition.downloadDestination.reuseBudget");
  expect(mocks.translate).toHaveBeenCalledWith("acquisition.downloadDestination.reuseBudget", {
    minimum: "4.0 KiB",
    total: "7.0 KiB",
  });
  expect(mocks.apply).not.toHaveBeenCalled();
  mocks.apply.mockResolvedValue({ appliedTaskIds: [1], appliedDownloadCount: 1, skipped: [] });
  fireEvent.click(button("applyReuse"));
  expect(
    await screen.findByRole("link", {
      name: "acquisition.downloadDestination.viewSelectedDownload",
    }),
  ).toHaveAttribute("href", "#/downloader?id=site%3A11");
  expect(mocks.apply).toHaveBeenCalledExactlyOnceWith("reuse-one", [1]);
  expect(mocks.portal).not.toHaveBeenCalled();
});

it("rejects a reuse preview that also includes a sibling download", async () => {
  await selectReuse(
    preview({
      rows: [
        {
          taskId: 1,
          title: "Unexpected sibling",
          canApply: true,
          downloads: [download(11), download(12)],
        },
      ],
    }),
  );
  await screen.findByRole("alert");
  expect(button("applyReuse")).toBeDisabled();
  expect(mocks.apply).not.toHaveBeenCalled();
});

it("shows the original files and confirmed destination when refreshing a single download", async () => {
  await selectReuse(
    preview({
      rows: [
        {
          taskId: 1,
          title: "Existing destination",
          canApply: true,
          downloads: [
            {
              ...download(11),
              reasonCode: "DOWNLOAD_DESTINATION_REUSE_REFRESH_READY",
              reuseEstimate: {
                fileCount: 6,
                candidateFileCount: 2,
                candidateBytes: 1024,
                requiredDownloadBytes: 5120,
                totalBytes: 6144,
              },
            },
          ],
        },
      ],
    }),
  );
  await screen.findByText(
    "acquisition.downloadDestination.reason.DOWNLOAD_DESTINATION_REUSE_REFRESH_READY",
  );
  expect(screen.getByText("acquisition.downloadDestination.sourceDirectory")).toBeVisible();
  expect(screen.getByText("/nas/11")).toBeVisible();
  expect(screen.getByText("/ssd/11")).toBeVisible();
  expect(button("applyReuse")).toBeEnabled();
  expect(mocks.apply).not.toHaveBeenCalled();
});

it("links to the original download when the source inventory still matches", async () => {
  await selectReuse(
    preview({
      rows: [
        {
          taskId: 1,
          title: "Same inventory",
          canApply: false,
          downloads: [{ ...download(11, false), reasonCode: "DOWNLOAD_DESTINATION_UNCHANGED" }],
        },
      ],
    }),
  );
  expect(
    await screen.findByRole("link", {
      name: "acquisition.downloadDestination.viewSelectedDownload",
    }),
  ).toHaveAttribute("href", "#/downloader?id=site%3A11");
  expect(mocks.apply).not.toHaveBeenCalled();
});

it("loads the default root without previewing or changing tasks; closing remains read-only", async () => {
  await show();
  expect(screen.getByText("/ssd/downloads")).toBeVisible();
  expect(mocks.preview).not.toHaveBeenCalled();
  expect(mocks.apply).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(mocks.apply).not.toHaveBeenCalled();
});

it("captures deduplicated filtered IDs and previews every download, protecting ineligible tasks", async () => {
  const view = await show({ taskIds: [1, 2, 3, 1, 0, -1], scopeLabel: "attention · storage" });
  view.rerender(
    <AcquisitionDownloadDestinationModal taskIds={[99]} scopeLabel="attention · storage" />,
  );
  fireEvent.click(button("preview"));
  await screen.findByText("Two downloads");
  expect(mocks.preview).toHaveBeenCalledExactlyOnceWith([1, 2, 3], "/ssd/downloads");
  expect(screen.getByText("/nas/11")).toBeVisible();
  expect(screen.getByText("/ssd/12")).toBeVisible();
  expect(checkboxes()[0]).toBeChecked();
  expect(checkboxes()[1]).toBeDisabled();
  expect(checkboxes()[1]).not.toBeChecked();
  expect(checkboxes()[2]).toBeChecked();
  expect(mocks.apply).not.toHaveBeenCalled();
});

it("selects the eligible download of a mixed task and keeps its protected sibling in place", async () => {
  mocks.preview.mockResolvedValue(
    preview({
      rows: [
        {
          taskId: 1,
          title: "Two downloads",
          canApply: true,
          reasonCode: "DOWNLOAD_DESTINATION_ELIGIBLE_PARTS",
          downloads: [download(11), download(12, false)],
        },
        { taskId: 99, title: "Unexpected task", canApply: true, downloads: [download(99)] },
      ],
    }),
  );
  mocks.apply.mockResolvedValue({ appliedTaskIds: [1], appliedDownloadCount: 1, skipped: [] });
  await inspect();
  expect(checkboxes()).toHaveLength(1);
  expect(checkboxes()[0]).toBeEnabled();
  expect(checkboxes()[0]).toBeChecked();
  expect(screen.getByText("/ssd/11")).toBeVisible();
  expect(screen.getByText("/nas/12")).toBeVisible();
  expect(screen.queryByText("/ssd/12")).toBeNull();
  expect(screen.queryByText("Unexpected task")).toBeNull();
  expect(mocks.translate).toHaveBeenCalledWith(
    "acquisition.downloadDestination.reason.DOWNLOAD_DESTINATION_ELIGIBLE_PARTS",
    { count: 1, downloads: 2 },
  );
  expect(mocks.translate).toHaveBeenCalledWith("acquisition.downloadDestination.total", {
    count: 1,
    downloads: 2,
  });
  expect(mocks.translate).toHaveBeenCalledWith("acquisition.downloadDestination.protected", {
    count: 1,
  });
  expect(mocks.translate).toHaveBeenCalledWith("acquisition.downloadDestination.apply", {
    count: 1,
    downloads: 1,
  });
  fireEvent.click(button("apply"));
  await screen.findByText("acquisition.downloadDestination.result");
  expect(mocks.apply).toHaveBeenCalledExactlyOnceWith("preview-1", [1]);
  fireEvent.click(button("diagnose"));
  expect(mocks.portal).toHaveBeenCalledExactlyOnceWith(
    AcquisitionRecoveryModal,
    expect.objectContaining({ taskIds: [1], initialScope: "downloads" }),
  );
});

it.each([
  { canApply: false, newDirectory: "/ssd/11", reasonCode: "DOWNLOAD_DESTINATION_PREVIEW_CHANGED" },
  { canApply: true, newDirectory: "   ", reasonCode: "UNKNOWN" },
])("requires row permission and an eligible child with a proposed directory", async (row) => {
  mocks.preview.mockResolvedValue(
    preview({
      rows: [
        {
          taskId: 1,
          title: "Two downloads",
          canApply: row.canApply,
          reasonCode: row.reasonCode,
          downloads: [{ ...download(11), newDirectory: row.newDirectory }],
        },
      ],
    }),
  );
  await inspect();
  expect(checkboxes()[0]).toBeDisabled();
  expect(screen.getByText("/nas/11")).toBeVisible();
  expect(screen.queryByText("/ssd/11")).toBeNull();
  expect(button("apply")).toBeDisabled();
});

it("invalidates the preview when the target changes and requires explicit new review", async () => {
  await inspect();
  fireEvent.click(button("chooseDirectory"));
  expect(screen.queryByText("Two downloads")).toBeNull();
  expect(button("apply")).toBeDisabled();
  expect(mocks.preview).toHaveBeenCalledTimes(1);
  fireEvent.click(button("preview"));
  await screen.findByText("Two downloads");
  expect(mocks.preview).toHaveBeenLastCalledWith([1, 2, 3], "/ssd/new-root");
});

it("discards a late preview response for a previously chosen directory", async () => {
  let finish!: (value: any) => void;
  mocks.preview.mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  await show();
  fireEvent.click(button("preview"));
  fireEvent.click(button("chooseDirectory"));
  await act(async () => finish(preview()));
  expect(screen.queryByText("Two downloads")).toBeNull();
  expect(button("apply")).toBeDisabled();
});

it("applies only explicitly selected eligible tasks and opens diagnosis only for actual successes", async () => {
  const changed = vi.fn();
  mocks.apply.mockResolvedValue({
    appliedTaskIds: [1, 2, 99],
    appliedDownloadCount: 2,
    skipped: [{ taskId: 3, reasonCode: "CHANGED" }],
  });
  await inspect({ onChanged: changed });
  fireEvent.click(checkboxes()[2]);
  fireEvent.click(button("apply"));
  await screen.findByText("acquisition.downloadDestination.result");
  expect(mocks.apply).toHaveBeenCalledExactlyOnceWith("preview-1", [1]);
  expect(changed).toHaveBeenCalledOnce();
  expect(mocks.portal).not.toHaveBeenCalled();
  fireEvent.click(button("diagnose"));
  expect(mocks.portal).toHaveBeenCalledExactlyOnceWith(
    AcquisitionRecoveryModal,
    expect.objectContaining({ taskIds: [1], onChanged: changed, initialScope: "downloads" }),
  );
  expect(screen.queryByRole("dialog")).toBeNull();
});

it("bulk selection affects only eligible rows across pages", async () => {
  const rows = Array.from({ length: 21 }, (_, index) => ({
    taskId: index + 1,
    title: index ? `Task ${index + 1}` : "Two downloads",
    canApply: index !== 5,
    downloads: [download(100 + index, index !== 5)],
  }));
  mocks.preview.mockResolvedValue(preview({ rows, taskCount: 21, downloadCount: 21 }));
  await inspect({ taskIds: rows.map((row) => row.taskId) });
  fireEvent.click(button("clearSelection"));
  expect(checkboxes().every((input) => !(input as HTMLInputElement).checked)).toBe(true);
  fireEvent.click(button("selectEligible"));
  fireEvent.click(screen.getByRole("button", { name: "page 1/2" }));
  expect(checkboxes()).toHaveLength(1);
  expect(checkboxes()[0]).toBeChecked();
  fireEvent.click(button("apply"));
  await waitFor(() =>
    expect(mocks.apply).toHaveBeenCalledWith(
      "preview-1",
      rows.filter((row) => row.canApply).map((row) => row.taskId),
    ),
  );
});

it("blocks expired previews and treats an invalid expiry as expired", async () => {
  mocks.preview.mockResolvedValue(preview({ expiresAt: "not-a-date" }));
  await inspect();
  expect(button("apply")).toBeDisabled();
  expect(screen.getByRole("alert")).toHaveTextContent("acquisition.downloadDestination.expired");
  expect(mocks.apply).not.toHaveBeenCalled();
});

it("expires a previously valid preview without further user interaction", async () => {
  vi.useFakeTimers();
  mocks.preview.mockResolvedValue(
    preview({ expiresAt: new Date(Date.now() + 10_000).toISOString() }),
  );
  render(<AcquisitionDownloadDestinationModal taskIds={[1, 2, 3]} />);
  await act(async () => {});
  fireEvent.click(button("preview"));
  await act(async () => {});
  expect(button("apply")).toBeEnabled();
  await act(async () => {
    vi.advanceTimersByTime(11_000);
  });
  expect(button("apply")).toBeDisabled();
  expect(mocks.apply).not.toHaveBeenCalled();
});

it("never replays an apply after a lost response, even when preview returns the consumed token", async () => {
  mocks.apply.mockRejectedValue(new Error("response lost"));
  await inspect();
  fireEvent.click(button("apply"));
  await screen.findByText("acquisition.downloadDestination.error.submit");
  expect(button("apply")).toBeDisabled();
  fireEvent.click(button("preview"));
  await screen.findByText("Two downloads");
  expect(button("apply")).toBeDisabled();
  expect(mocks.apply).toHaveBeenCalledTimes(1);
});

it("locks closing and directory changes during submission, rejecting double clicks", async () => {
  let finish!: (value: any) => void;
  mocks.apply.mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    }),
  );
  await inspect();
  fireEvent.click(button("apply"));
  fireEvent.click(button("apply"));
  expect(button("chooseDirectory")).toBeDisabled();
  expect(screen.getByRole("button", { name: "common.action.close" })).toBeDisabled();
  expect(mocks.apply).toHaveBeenCalledOnce();
  await act(async () =>
    finish({
      appliedTaskIds: [],
      appliedDownloadCount: 0,
      skipped: [{ taskId: 1, reasonCode: "CHANGED" }],
    }),
  );
  expect(
    screen.queryByRole("button", { name: "acquisition.downloadDestination.diagnose" }),
  ).toBeNull();
});

it("allows explicit directory selection when defaults cannot be loaded", async () => {
  mocks.defaultRoot.mockRejectedValue(new Error("unavailable"));
  await show();
  expect(screen.getByText("acquisition.downloadDestination.defaultsFailed")).toBeVisible();
  expect(button("preview")).toBeDisabled();
  fireEvent.click(button("chooseDirectory"));
  fireEvent.click(button("preview"));
  await screen.findByText("Two downloads");
  expect(mocks.preview).toHaveBeenCalledWith([1, 2, 3], "/ssd/new-root");
});

it("loads defaults once under StrictMode and performs no implicit preview or apply", async () => {
  render(
    <StrictMode>
      <AcquisitionDownloadDestinationModal taskIds={[1]} />
    </StrictMode>,
  );
  await screen.findByRole("button", { name: "acquisition.downloadDestination.chooseDirectory" });
  expect(mocks.defaultRoot).toHaveBeenCalledOnce();
  expect(mocks.preview).not.toHaveBeenCalled();
  expect(mocks.apply).not.toHaveBeenCalled();
});

it("reports partial mutations without diagnosing the incomplete task and permits fresh inspection", async () => {
  mocks.apply.mockResolvedValue({
    appliedTaskIds: [],
    appliedDownloadCount: 1,
    skipped: [{ taskId: 1, reasonCode: "DOWNLOAD_DESTINATION_PARTIALLY_APPLIED" }],
  });
  await inspect();
  fireEvent.click(button("apply"));
  await screen.findByText("acquisition.downloadDestination.result");
  expect(screen.getByRole("alert")).toHaveTextContent(
    "acquisition.downloadDestination.reason.DOWNLOAD_DESTINATION_PARTIALLY_APPLIED",
  );
  expect(
    screen.queryByRole("button", { name: "acquisition.downloadDestination.diagnose" }),
  ).toBeNull();
  mocks.preview.mockResolvedValue(preview({ previewToken: "fresh-token" }));
  fireEvent.click(button("recheck"));
  await screen.findByText("Two downloads");
  expect(mocks.preview).toHaveBeenCalledTimes(2);
  expect(mocks.apply).toHaveBeenCalledTimes(1);
});

it("shows machine-readable storage protection reasons without exposing raw server messages", async () => {
  mocks.preview.mockRejectedValueOnce(new Error("LOCAL_STORAGE_RESERVE_LOW"));
  await show();
  fireEvent.click(button("preview"));
  await screen.findByText("acquisition.downloadDestination.reason.LOCAL_STORAGE_RESERVE_LOW");
  mocks.preview.mockRejectedValueOnce(new Error("Error at /private/path?secret=value"));
  fireEvent.click(button("preview"));
  await waitFor(() =>
    expect(
      screen.queryByText("acquisition.downloadDestination.reason.LOCAL_STORAGE_RESERVE_LOW"),
    ).toBeNull(),
  );
  expect(screen.getByRole("alert")).toHaveTextContent(
    "acquisition.downloadDestination.error.preview",
  );
  expect(screen.queryByText(/private\/path/)).toBeNull();
});
