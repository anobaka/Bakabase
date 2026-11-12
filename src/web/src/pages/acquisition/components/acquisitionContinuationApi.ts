import BApi from "@/sdk/BApi";
import type { AcquisitionRecoveryScope } from "./acquisitionRecoveryApi";

export type AcquisitionContinuation = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.getAcquisitionContinuation>>["data"]
>;
export type AcquisitionContinuationPreview = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.previewAcquisitionContinuation>>["data"]
>;
const options = { sensitiveResponse: true, showErrorToast: false };
async function unwrap<T>(request: Promise<{ code?: number; message?: string | null; data?: T }>) {
  const response = await request;
  if (response.code || response.data == null)
    throw new Error(response.message || "CONTINUATION_REQUEST_FAILED");
  return response.data;
}
export const acquisitionContinuationApi = {
  preview: (taskIds: number[], scope: AcquisitionRecoveryScope, signal?: AbortSignal) =>
    unwrap(BApi.acquisition.previewAcquisitionContinuation(
      { taskIds, scope, order: "sizeAsc" }, { ...options, signal },
    )),
  create: (previewToken: string, taskIds: number[], useEmergencySpaceReserve?: boolean): Promise<AcquisitionContinuation> =>
    unwrap(BApi.acquisition.createAcquisitionContinuation({ previewToken, taskIds,
      ...(useEmergencySpaceReserve !== undefined && { useEmergencySpaceReserve }) }, options)),
  findReceipt: (previewToken: string): Promise<AcquisitionContinuation> =>
    unwrap(BApi.acquisition.getAcquisitionContinuationByPreview(previewToken, options)),
  list: (): Promise<AcquisitionContinuation[]> =>
    unwrap(BApi.acquisition.getAcquisitionContinuations(options)),
  get: (batchId: string): Promise<AcquisitionContinuation> =>
    unwrap(BApi.acquisition.getAcquisitionContinuation(batchId, options)),
  cancel: (batchId: string): Promise<AcquisitionContinuation> =>
    unwrap(BApi.acquisition.cancelAcquisitionContinuation(batchId, options)),
};
