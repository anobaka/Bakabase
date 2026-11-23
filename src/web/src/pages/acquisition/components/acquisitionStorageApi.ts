import BApi from "@/sdk/BApi";
import type { StoragePolicy } from "@/components/Acquire/StoragePolicyFields";

export type StorageMaintenanceMode = "policy" | "cleanup";
export type StorageMaintenanceRow = {
  taskId: number;
  title?: string;
  eligible: boolean;
  reasonCode?: string;
  revision?: number;
  downloadPreview?: string;
  processingPreview?: string;
  deliveryPreview?: string;
  artifactIds?: string[];
  fileCount?: number;
  bytes?: number;
  processedCheckpointCleanup?: {
    verifiedResultArtifactIds: string[];
    retainedDownloadArtifactIds: string[];
    intermediateArtifactIds: string[];
  } | null;
};
export type StorageMaintenancePreview = {
  previewId: string;
  expiresAt: string;
  rows: StorageMaintenanceRow[];
  eligibleCount: number;
  policy?: StoragePolicy | null;
};
export type StorageMaintenanceResult = {
  taskIds: number[];
  skipped: { taskId: number; reasonCode?: string }[];
  removedFiles?: number;
  removedBytes?: number;
};
async function unwrap<T>(
  request: Promise<{ code?: number; message?: string | null; data?: T }>,
): Promise<T> {
  const response = await request;
  if (response.code || !response.data)
    throw new Error(response.message || "STORAGE_POLICY_REQUEST_FAILED");
  return response.data;
}
const options = { sensitiveResponse: true, showErrorToast: false };
export const acquisitionStorageApi = {
  preview: (
    mode: StorageMaintenanceMode,
    taskIds: number[],
    signal?: AbortSignal,
    overrideRetention = false,
    includeOriginalDownloads = false,
  ): Promise<StorageMaintenancePreview> =>
    mode === "policy"
      ? unwrap(
          BApi.acquisition.previewAcquisitionStoragePolicy(
            {
              taskIds,
              outputDirectory: "",
              applyCurrentSettings: true,
              overrideRetention: false,
              preserveOriginalDownloads: false,
            },
            { ...options, signal },
          ),
        )
      : unwrap(
          BApi.acquisition.previewAcquisitionWorkspaceCleanup(
            {
              taskIds,
              overrideRetention,
              preserveOriginalDownloads: overrideRetention && !includeOriginalDownloads,
            },
            { ...options, signal },
          ),
        ),
  apply: async (
    mode: StorageMaintenanceMode,
    previewId: string,
    taskIds: number[],
  ): Promise<StorageMaintenanceResult> => {
    if (mode === "policy") {
      const result = await unwrap(
        BApi.acquisition.applyAcquisitionStoragePolicy({ previewId, taskIds }, options),
      );
      return { taskIds: result.appliedTaskIds, skipped: result.skipped };
    }
    const result = await unwrap(
      BApi.acquisition.reclaimAcquisitionWorkspaces({ previewId, taskIds }, options),
    );
    return {
      taskIds: result.completedTaskIds,
      skipped: result.skipped,
      removedFiles: result.removedFiles,
      removedBytes: result.removedBytes,
    };
  },
};
