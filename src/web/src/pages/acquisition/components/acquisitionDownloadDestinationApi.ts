import BApi from "@/sdk/BApi";

export type DownloadDestinationPreview = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.previewAcquisitionDownloadDestinationRevision>>["data"]
>;
export type DownloadDestinationResult = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.applyAcquisitionDownloadDestinationRevision>>["data"]
>;
export type DownloadDestinationRow = DownloadDestinationPreview["rows"][number];

async function unwrap<T>(request: Promise<{ code?: number; message?: string | null; data?: T }>) {
  const response = await request;
  if (response.code || !response.data)
    throw new Error(response.message || "DOWNLOAD_DESTINATION_REQUEST_FAILED");
  return response.data;
}
const options = { sensitiveResponse: true, showErrorToast: false };

export const acquisitionDownloadDestinationApi = {
  defaultRoot: async (): Promise<string> => {
    const acquisition = await unwrap(BApi.acquisition.getAcquisitionOptions(options));
    if (acquisition.downloadRootDirectory?.trim()) return acquisition.downloadRootDirectory;
    const downloader = await unwrap(BApi.options.getDownloaderGlobalOptions(options));
    return downloader.defaultDownloadPath?.trim() ?? "";
  },
  preview: (taskIds: number[], targetRootDirectory: string): Promise<DownloadDestinationPreview> =>
    unwrap(
      BApi.acquisition.previewAcquisitionDownloadDestinationRevision(
        { taskIds, targetRootDirectory },
        options,
      ),
    ),
  previewReuse: (
    taskId: number,
    downloadTaskId: number,
    targetRootDirectory: string,
  ): Promise<DownloadDestinationPreview> =>
    unwrap(
      BApi.acquisition.previewAcquisitionDownloadDestinationRevision(
        {
          taskIds: [taskId],
          targetRootDirectory,
          reuseExistingFiles: true,
          downloadTaskIds: [downloadTaskId],
        },
        options,
      ),
    ),
  apply: (previewToken: string, taskIds: number[]): Promise<DownloadDestinationResult> =>
    unwrap(
      BApi.acquisition.applyAcquisitionDownloadDestinationRevision(
        { previewToken, taskIds },
        options,
      ),
    ),
};
