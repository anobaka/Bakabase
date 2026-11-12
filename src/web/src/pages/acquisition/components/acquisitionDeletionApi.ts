import BApi from "@/sdk/BApi";

type Api = typeof BApi.acquisition;
type PreviewOptions = Omit<Parameters<Api["previewDeleteAcquisitionTasks"]>[0], "taskIds">;
export type AcquisitionDeletionOptions = Omit<
  Parameters<Api["deleteAcquisitionTasks"]>[0],
  "previewId"
>;
export type AcquisitionDeletionPreview = NonNullable<
  Awaited<ReturnType<Api["previewDeleteAcquisitionTasks"]>>["data"]
>;
export type AcquisitionDeletionResult = NonNullable<
  Awaited<ReturnType<Api["deleteAcquisitionTasks"]>>["data"]
>;
export type AcquisitionDeletionProgress = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.getAcquisitionTaskDeletionProgress>>["data"]
>;

async function unwrap<T>(request: Promise<{ code?: number; message?: string | null; data?: T }>) {
  const response = await request;
  if (response.code || !response.data)
    throw new Error(response.message || "ACQUISITION_DELETE_REQUEST_FAILED");
  return response.data;
}
const options = { sensitiveResponse: true, showErrorToast: false };
export const acquisitionDeletionApi = {
  preview: (
    taskIds: number[],
    cleanup: PreviewOptions = {},
    signal?: AbortSignal,
  ): Promise<AcquisitionDeletionPreview> => {
    const request = { taskIds, ...cleanup };
    return unwrap(BApi.acquisition.previewDeleteAcquisitionTasks(request, { ...options, signal }));
  },
  remove: (
    previewId: string,
    choices: AcquisitionDeletionOptions = {},
  ): Promise<AcquisitionDeletionResult> => {
    const request = { previewId, ...choices };
    return unwrap(BApi.acquisition.deleteAcquisitionTasks(request, options));
  },
  progress: (previewId: string, signal?: AbortSignal): Promise<AcquisitionDeletionProgress> =>
    unwrap(BApi.acquisition.getAcquisitionTaskDeletionProgress(previewId, { ...options, signal })),
};
