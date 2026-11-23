import type { StorageReserveMode } from "@/components/Acquire/StorageReserveCheckbox";
import BApi from "@/sdk/BApi";

export type LocalBatchPreview = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.previewAcquisitionLocalRecoveryBatch>>["data"]
>;
export type LocalBatchActionSelection = NonNullable<LocalBatchCapacity["selections"]>[number];
export type LocalBatch = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.getAcquisitionLocalRecoveryBatch>>["data"]
>;
export type LocalBatchCapacity = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.previewAcquisitionLocalRecoveryBatchCapacity>>["data"]
>;

const options = { sensitiveResponse: true, showErrorToast: false };
async function unwrap<T>(
  request: Promise<{ code?: number; message?: string | null; data?: T }>,
): Promise<T> {
  const result = await request;
  if (result.code || result.data == null)
    throw new Error(result.message || "LOCAL_BATCH_REQUEST_FAILED");
  return result.data;
}

export const acquisitionLocalBatchApi = {
  preview: (
    taskIds: number[],
    signal?: AbortSignal,
    mode?: "automatic",
    maxAdaptiveIterations = 3,
  ): Promise<LocalBatchPreview> =>
    unwrap(
      BApi.acquisition.previewAcquisitionLocalRecoveryBatch(
        { taskIds, mode: mode ?? "localOnly", order: "sizeAsc", maxAdaptiveIterations },
        { ...options, signal },
      ),
    ),
  create: (
    previewToken: string,
    taskIds: number[],
    allowStorageOverride: boolean,
    reserveMode?: StorageReserveMode,
    selections?: LocalBatchActionSelection[],
  ): Promise<LocalBatch> =>
    unwrap(
      BApi.acquisition.createAcquisitionLocalRecoveryBatch(
        {
          previewToken,
          taskIds,
          allowStorageOverride,
          ...(reserveMode && { reserveMode }),
          ...(selections && { selections }),
        },
        options,
      ),
    ),
  capacity: (
    previewToken: string,
    taskIds: number[],
    signal?: AbortSignal,
    reserveMode?: StorageReserveMode,
    selections?: LocalBatchActionSelection[],
  ): Promise<LocalBatchCapacity> =>
    unwrap(
      BApi.acquisition.previewAcquisitionLocalRecoveryBatchCapacity(
        {
          previewToken,
          taskIds,
          ...(reserveMode && { reserveMode }),
          ...(selections && { selections }),
        },
        { ...options, signal },
      ),
    ),
  list: (): Promise<LocalBatch[]> =>
    unwrap(BApi.acquisition.getAcquisitionLocalRecoveryBatches(options)),
  get: (batchId: string): Promise<LocalBatch> =>
    unwrap(BApi.acquisition.getAcquisitionLocalRecoveryBatch(batchId, options)),
  pause: (batchId: string): Promise<LocalBatch> =>
    unwrap(BApi.acquisition.pauseAcquisitionLocalRecoveryBatch(batchId, options)),
  resume: (batchId: string): Promise<LocalBatch> =>
    unwrap(BApi.acquisition.resumeAcquisitionLocalRecoveryBatch(batchId, options)),
  cancel: (batchId: string): Promise<LocalBatch> =>
    unwrap(BApi.acquisition.cancelAcquisitionLocalRecoveryBatch(batchId, options)),
};

export const localBatchSelectionLimit = 100;
