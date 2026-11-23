import { beforeEach, expect, it, vi } from "vitest";
import { acquisitionDownloadDestinationApi as api } from "../components/acquisitionDownloadDestinationApi";
const mocks = vi.hoisted(() => ({
  acquisitionOptions: vi.fn(),
  downloaderOptions: vi.fn(),
  preview: vi.fn(),
  apply: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      getAcquisitionOptions: mocks.acquisitionOptions,
      previewAcquisitionDownloadDestinationRevision: mocks.preview,
      applyAcquisitionDownloadDestinationRevision: mocks.apply,
    },
    options: { getDownloaderGlobalOptions: mocks.downloaderOptions },
  },
}));
beforeEach(() => {
  vi.clearAllMocks();
  mocks.acquisitionOptions
    .mockReset()
    .mockResolvedValue({ code: 0, data: { downloadRootDirectory: "/acquisition" } });
  mocks.downloaderOptions
    .mockReset()
    .mockResolvedValue({ code: 0, data: { defaultDownloadPath: "/downloads" } });
  mocks.preview.mockReset().mockResolvedValue({ code: 0, data: { previewToken: "token" } });
  mocks.apply.mockReset().mockResolvedValue({ code: 0, data: { appliedTaskIds: [7] } });
});
it("uses the acquisition root before any inherited downloader default", async () => {
  expect(await api.defaultRoot()).toBe("/acquisition");
  expect(mocks.downloaderOptions).not.toHaveBeenCalled();
});
it("inherits the downloader root only when the acquisition root is blank", async () => {
  mocks.acquisitionOptions.mockResolvedValue({ code: 0, data: { downloadRootDirectory: "  " } });
  expect(await api.defaultRoot()).toBe("/downloads");
  expect(mocks.downloaderOptions).toHaveBeenCalledOnce();
});
it("does not silently inherit a different root when acquisition settings are unavailable", async () => {
  mocks.acquisitionOptions.mockResolvedValue({
    code: 1,
    data: { downloadRootDirectory: "/wrong" },
    message: "unavailable",
  });
  await expect(api.defaultRoot()).rejects.toThrow("unavailable");
  expect(mocks.downloaderOptions).not.toHaveBeenCalled();
});
it("passes exact IDs and token through generated APIs without exposing path responses in toasts", async () => {
  await api.preview([7], "/ssd/new");
  await api.apply("token", [7]);
  expect(mocks.preview).toHaveBeenCalledExactlyOnceWith(
    { taskIds: [7], targetRootDirectory: "/ssd/new" },
    { sensitiveResponse: true, showErrorToast: false },
  );
  expect(mocks.apply).toHaveBeenCalledExactlyOnceWith(
    { previewToken: "token", taskIds: [7] },
    { sensitiveResponse: true, showErrorToast: false },
  );
});
it("treats application failures and missing results as failures rather than successful changes", async () => {
  mocks.apply.mockResolvedValue({
    code: 1,
    message: "PREVIEW_EXPIRED",
    data: { appliedTaskIds: [7] },
  });
  await expect(api.apply("token", [7])).rejects.toThrow("PREVIEW_EXPIRED");
  mocks.apply.mockResolvedValue({ code: 0, data: null });
  await expect(api.apply("token", [7])).rejects.toThrow("DOWNLOAD_DESTINATION_REQUEST_FAILED");
});

it("binds reuse to the requested site download and acquisition", async () => {
  await api.previewReuse(7, 24, "/ssd/new");
  expect(mocks.preview).toHaveBeenCalledExactlyOnceWith(
    {
      taskIds: [7],
      downloadTaskIds: [24],
      targetRootDirectory: "/ssd/new",
      reuseExistingFiles: true,
    },
    { sensitiveResponse: true, showErrorToast: false },
  );
  expect(mocks.apply).not.toHaveBeenCalled();
});
