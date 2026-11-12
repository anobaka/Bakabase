import type {
  BakabaseServiceComponentsAcquisitionPlanningAcquisitionSourceRevisionOptions,
  BakabaseServiceComponentsAcquisitionPlanningAcquisitionSourceRevisionPreview,
  BakabaseServiceComponentsAcquisitionPlanningAcquisitionSourceRevisionPreviewInput,
  BakabaseServiceComponentsAcquisitionPlanningAcquisitionSourceRevisionResult,
} from "@/sdk/Api";

import BApi from "@/sdk/BApi";

export type SourceRevisionOptions =
  BakabaseServiceComponentsAcquisitionPlanningAcquisitionSourceRevisionOptions;
export type SourceRevisionPreview =
  BakabaseServiceComponentsAcquisitionPlanningAcquisitionSourceRevisionPreview;
export type SourceRevisionInput =
  BakabaseServiceComponentsAcquisitionPlanningAcquisitionSourceRevisionPreviewInput;
export type SourceRevisionResult =
  BakabaseServiceComponentsAcquisitionPlanningAcquisitionSourceRevisionResult;

async function requireResult<T>(
  request: Promise<{ code?: number; message?: string | null; data?: T }>,
): Promise<T> {
  const response = await request;

  if (response.code || response.data == null)
    throw new Error(response.message || "SOURCE_REVISION_REQUEST_FAILED");

  return response.data;
}

const privateRequest = { sensitiveResponse: true, showErrorToast: false } as const;

/** Applying a preview is deliberately a single request, with no transport retry. */
export const sourceRevisionApi = {
  options: (taskId: number, signal?: AbortSignal): Promise<SourceRevisionOptions> =>
    requireResult(
      BApi.acquisition.getAcquisitionSourceRevisions(taskId, { ...privateRequest, signal }),
    ),
  preview: (
    taskId: number,
    input: SourceRevisionInput,
    signal?: AbortSignal,
  ): Promise<SourceRevisionPreview> =>
    requireResult(
      BApi.acquisition.previewAcquisitionSourceRevision(taskId, input, {
        ...privateRequest,
        signal,
      }),
    ),
  apply: (taskId: number, previewToken: string): Promise<SourceRevisionResult> =>
    requireResult(
      BApi.acquisition.applyAcquisitionSourceRevision(taskId, { previewToken }, privateRequest),
    ),
};
