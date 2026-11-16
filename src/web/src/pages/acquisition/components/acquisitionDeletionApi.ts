import BApi from "@/sdk/BApi";

type Api = typeof BApi.acquisition;
export type AcquisitionDeletionOptions = Pick<Parameters<Api["deleteAcquisitionTasks"]>[0],
  "cleanupFiles" | "deleteDeliveredFiles" | "removeTaskIfCleanupFails" | "retryRetainedFiles">;
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
  names: async (taskIds: number[], signal?: AbortSignal): Promise<Record<number, string>> => {
    const names = await unwrap(BApi.acquisition.getAcquisitionTaskNames({ taskIds }, { ...options, signal }));
    return Object.fromEntries(Object.entries(names).filter((entry): entry is [string, string] => !!entry[1]?.trim()));
  },
  remove: (
    operationId: string,
    taskIds: number[],
    choices: AcquisitionDeletionOptions = {},
  ): Promise<AcquisitionDeletionResult> => {
    const request = { previewId: operationId, taskIds, ...choices };
    return unwrap(BApi.acquisition.deleteAcquisitionTasks(request, options));
  },
  progress: (operationId: string, signal?: AbortSignal): Promise<AcquisitionDeletionProgress> =>
    unwrap(BApi.acquisition.getAcquisitionTaskDeletionProgress(operationId, { ...options, signal })),
};
