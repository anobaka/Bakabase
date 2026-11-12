import BApi from "@/sdk/BApi";

export type AcquisitionRecoveryPreview = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.previewAcquisitionTaskRecovery>>["data"]
>;
export type AcquisitionRecoveryResult = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.applyAcquisitionTaskRecovery>>["data"]
>;
export type AcquisitionRecoveryRow = AcquisitionRecoveryPreview["rows"][number];
export type AcquisitionLocalFilesRecoveryPreview = NonNullable<
  AcquisitionRecoveryRow["localFiles"]
>;
export type AcquisitionRecoveryScope = "workflow" | "downloads" | "localFiles";

async function unwrap<T>(request: Promise<{ code?: number; message?: string | null; data?: T }>) {
  const response = await request;
  if (response.code || !response.data)
    throw new Error(response.message || "ACQUISITION_RECOVERY_REQUEST_FAILED");
  return response.data;
}
const options = { sensitiveResponse: true, showErrorToast: false };
export const acquisitionRecoveryApi = {
  preview: (
    taskIds: number[],
    scope: AcquisitionRecoveryScope = "workflow",
    diagnosis?: { diagnosisId: string; signal: AbortSignal },
  ): Promise<AcquisitionRecoveryPreview> =>
    unwrap(BApi.acquisition.previewAcquisitionTaskRecovery(
      { taskIds, scope, diagnosisId: diagnosis?.diagnosisId },
      { ...options, signal: diagnosis?.signal },
    )),
  cancelDiagnosis: (diagnosisId: string) =>
    unwrap(BApi.acquisition.cancelAcquisitionTaskRecoveryDiagnosis(diagnosisId, options)),
  apply: (previewToken: string, taskIds: number[], useEmergencySpaceReserve?: boolean): Promise<AcquisitionRecoveryResult> =>
    unwrap(BApi.acquisition.applyAcquisitionTaskRecovery({ previewToken, taskIds,
      ...(useEmergencySpaceReserve !== undefined && { useEmergencySpaceReserve }) }, options)),
};
