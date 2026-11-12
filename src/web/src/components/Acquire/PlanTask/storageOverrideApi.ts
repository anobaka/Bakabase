import BApi from "@/sdk/BApi";

export type StorageOverridePreview = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.previewAcquisitionStorageOverride>>["data"]
>;
export type StorageOverrideResult = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.applyAcquisitionStorageOverride>>["data"]
>;

async function unwrap<T>(request: Promise<{ code?: number; message?: string | null; data?: T }>) {
  const response = await request;
  if (response.code || !response.data)
    throw new Error(response.message || "ACQUISITION_STORAGE_OVERRIDE_FAILED");
  return response.data;
}
const options = { sensitiveResponse: true, showErrorToast: false };
export const storageOverrideApi = {
  capacity: async (taskId: number, workflowRunId: number, signal: AbortSignal) => {
    const snapshot = await unwrap(BApi.acquisition.previewAcquisitionLocalRecoveryBatch(
      { taskIds: [taskId], mode: "capacityOnly", order: "sizeAsc" }, { ...options, signal },
    ));
    if (snapshot.rows?.length !== 1 || snapshot.rows[0].taskId !== taskId ||
      snapshot.rows[0].workflowRunId !== workflowRunId) return [];
    return snapshot.volumes ?? [];
  },
  preview: (taskId: number, scope?: "download"): Promise<StorageOverridePreview> =>
    unwrap(BApi.acquisition.previewAcquisitionStorageOverride(taskId, scope ? { scope } : undefined, options)),
  apply: (taskId: number, token: string, reserveMode: "emergency" | "configured"): Promise<StorageOverrideResult> =>
    unwrap(
      BApi.acquisition.applyAcquisitionStorageOverride(taskId, { token, confirmed: true, reserveMode }, options),
    ),
  resumeDownload: (taskId: number, token: string, signalJson: string,
    useEmergencySpaceReserve: boolean): Promise<StorageOverrideResult> =>
    unwrap(BApi.acquisition.applyAcquisitionStorageOverride(taskId,
      { token, confirmed: true, signalJson, useEmergencySpaceReserve }, options)),
};
