import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import StorageUsageButton, { StorageUsageModal } from "../StorageUsage";

const mocks = vi.hoisted(() => ({ load: vi.fn(), portal: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({ default: { app: { getAppStorageUsage: mocks.load } } }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("@/components/FilePathValue", () => ({
  default: ({ path }: { path?: string }) => <code>{path}</code>,
}));
vi.mock("@/components/utils", () => ({ humanFileSize: (bytes: number) => `${bytes} B` }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Modal: ({ title, children }: any) => (
    <div aria-label={title} role="dialog">
      {children}
    </div>
  ),
  Spinner: () => <span />,
}));

const summary = {
  rootPath: "/data/current-profile",
  totalBytes: 120,
  fileCount: 3,
  scannedAt: "2026-10-23T01:00:00Z",
  fromCache: false,
  categories: [
    { id: "database", bytes: 100, fileCount: 1, path: "/data/current-profile/db" },
    { id: "configuration", bytes: 20, fileCount: 2 },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.load.mockResolvedValue({ code: 0, data: summary });
});
afterEach(cleanup);

describe("current data storage usage", () => {
  it("does not scan until opened and reuses the shared modal portal", () => {
    render(<StorageUsageButton />);
    expect(mocks.load).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "configuration.storageUsage.open" }));
    expect(mocks.portal).toHaveBeenCalledWith(StorageUsageModal, {});
  });

  it("requests cached statistics on each open and bypasses cache only on manual refresh", async () => {
    const first = render(<StorageUsageModal />);

    expect(await screen.findByText("120 B")).toBeVisible();
    expect(mocks.load).toHaveBeenLastCalledWith({ forceRefresh: false }, { showErrorToast: false });
    expect(screen.getByText("/data/current-profile/db")).toBeVisible();
    expect(screen.getByText("configuration.storageUsage.fresh")).toBeVisible();
    first.unmount();
    mocks.load.mockResolvedValueOnce({ code: 0, data: { ...summary, fromCache: true } });
    render(<StorageUsageModal />);
    expect(await screen.findByText("configuration.storageUsage.cached")).toBeVisible();
    expect(mocks.load).toHaveBeenLastCalledWith({ forceRefresh: false }, { showErrorToast: false });
    mocks.load.mockResolvedValueOnce({ code: 0, data: { ...summary, totalBytes: 160 } });
    fireEvent.click(screen.getByRole("button", { name: "configuration.storageUsage.refresh" }));
    expect(await screen.findByText("160 B")).toBeVisible();
    expect(mocks.load).toHaveBeenLastCalledWith({ forceRefresh: true }, { showErrorToast: false });
  });

  it("keeps the last successful result visible when refresh fails, then allows retry", async () => {
    render(<StorageUsageModal />);
    await screen.findByText("120 B");
    mocks.load.mockRejectedValueOnce(new Error("Read failed"));
    fireEvent.click(screen.getByRole("button", { name: "configuration.storageUsage.refresh" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Read failed");
    expect(screen.getByText("120 B")).toBeVisible();
    await waitFor(() => expect(screen.getByRole("button")).toBeEnabled());
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });

  it("shows incomplete scans and skipped links and disables refresh during scanning", async () => {
    let complete: (value: unknown) => void = () => {};

    mocks.load.mockReturnValueOnce(
      new Promise((resolve) => {
        complete = resolve;
      }),
    );
    render(<StorageUsageModal />);
    expect(screen.getByRole("button")).toBeDisabled();
    expect(screen.getByRole("status")).toBeVisible();
    await act(async () =>
      complete({
        code: 0,
        data: {
          ...summary,
          partial: true,
          skippedSymbolicLinks: 2,
          errors: [{ path: "/data/current-profile/private", message: "Permission denied" }],
          categories: [{ ...summary.categories[0], partial: true }],
        },
      }),
    );
    expect(screen.getByRole("alert")).toHaveTextContent("configuration.storageUsage.partial");
    expect(screen.getByText("configuration.storageUsage.skippedLinks")).toBeVisible();
    expect(screen.getByText("configuration.storageUsage.categoryPartial")).toBeVisible();
    fireEvent.click(screen.getByText("configuration.storageUsage.errors"));
    expect(screen.getByText("Permission denied")).toBeVisible();
  });
});
