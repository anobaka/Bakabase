import BApi from "@/sdk/BApi";
import type { BakabaseServiceComponentsAcquisitionPlanningAcquisitionPlanCreationInput as CreationInput } from "@/sdk/Api";
import type { SourceRef } from "../sourceHistory";
import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionPlan as Plan } from "@/sdk/Api";

async function requireResult<T>(
  request: Promise<{ code?: number; message?: string | null; data?: T }>,
): Promise<T> {
  const result = await request;
  if (result.code || result.data == null)
    throw new Error(result.message || "ACQUISITION_PLAN_REQUEST_FAILED");
  return result.data;
}

export const planTaskApi = {
  runtimeOverviews: (taskIds: number[], signal?: AbortSignal) =>
    requireResult(
      BApi.acquisition.getAcquisitionRuntimeOverviews(
        { taskIds },
        { signal, sensitiveResponse: true, showErrorToast: false },
      ),
    ),
  createDraft: (body: Parameters<typeof BApi.acquisition.createAcquisitionDraft>[0]) =>
    requireResult(BApi.acquisition.createAcquisitionDraft(body, { sensitiveResponse: true })),
  updateDraft: (id: number, body: Parameters<typeof BApi.acquisition.updateAcquisitionDraft>[1]) =>
    requireResult(BApi.acquisition.updateAcquisitionDraft(id, body, { sensitiveResponse: true })),
  setBuilder: (id: number, expectedRevision: number, builderId: string) =>
    requireResult(BApi.acquisition.setAcquisitionDraftBuilder(id, { expectedRevision, builderId })),
  prepare: (id: number, expectedRevision: number, plan: Plan) =>
    requireResult(
      BApi.acquisition.prepareAcquisitionDraft(
        id,
        { expectedRevision, plan },
        { sensitiveResponse: true },
      ),
    ),
  builders: () => requireResult(BApi.acquisition.getAcquisitionPlanBuilders()),
  create: (body: CreationInput) => requireResult(BApi.acquisition.createAcquisitionPlanTask(body)),
  get: (id: number) =>
    requireResult(BApi.acquisition.getAcquisitionPlanTask(id, { sensitiveResponse: true })),
  previewPreparation: (id: number, body: Parameters<typeof BApi.acquisition.previewAcquisitionPreparation>[1], signal?: AbortSignal) =>
    requireResult(BApi.acquisition.previewAcquisitionPreparation(id, body,
      { signal, sensitiveResponse: true, showErrorToast: false })),
  confirmPreparation: (id: number, body: Parameters<typeof BApi.acquisition.confirmAcquisitionPreparation>[1]) =>
    requireResult(BApi.acquisition.confirmAcquisitionPreparation(id, body,
      { sensitiveResponse: true, showErrorToast: false })),
  registeredDownloads: (id: number, expectedRevision: number, signal: AbortSignal) =>
    requireResult(BApi.acquisition.previewAcquisitionRegisteredDownloads(id, { expectedRevision },
      { signal, sensitiveResponse: true, showErrorToast: false })),
  useRegisteredDownload: (id: number, expectedRevision: number, token: string) =>
    requireResult(BApi.acquisition.applyAcquisitionRegisteredDownload(id, { expectedRevision, token },
      { sensitiveResponse: true, showErrorToast: false })),
  keepPreparedFiles: (id: number, expectedRevision: number, reason?: string) =>
    requireResult(BApi.acquisition.keepPreparedAcquisitionFiles(id, { expectedRevision, reason },
      { sensitiveResponse: true, showErrorToast: false })),
  deferPreparedProcessing: (id: number, expectedRevision: number) =>
    requireResult(BApi.acquisition.deferPreparedAcquisitionProcessing(id, { expectedRevision },
      { sensitiveResponse: true, showErrorToast: false })),
  sourceCorrections: (id: number, signal?: AbortSignal) =>
    requireResult(
      BApi.acquisition.getAcquisitionSourceCorrections(id, {
        signal,
        sensitiveResponse: true,
        showErrorToast: false,
      }),
    ),
  correctAccessCode: (
    id: number,
    body: Parameters<typeof BApi.acquisition.correctAcquisitionSourceAccessCode>[1],
  ) =>
    requireResult(
      BApi.acquisition.correctAcquisitionSourceAccessCode(id, body, {
        sensitiveResponse: true,
        showErrorToast: false,
      }),
    ),
  search: (id: number, query: string) =>
    requireResult(BApi.acquisition.searchAcquisitionPlanCandidates(id, { query })),
  select: (id: number, candidateId: string, expectedRevision: number) =>
    requireResult(
      BApi.acquisition.selectAcquisitionPlanCandidate(id, { candidateId, expectedRevision }),
    ),
  update: (id: number, expectedRevision: number, outputDirectory: string) =>
    requireResult(
      BApi.acquisition.updateAcquisitionPlanTask(id, { expectedRevision, outputDirectory }),
    ),
  replaceSource: (id: number, expectedRevision: number, sourceReplacement: NonNullable<
    Parameters<typeof BApi.acquisition.updateAcquisitionPlanTask>[1]["sourceReplacement"]
  >) => requireResult(BApi.acquisition.updateAcquisitionPlanTask(id, { expectedRevision, sourceReplacement },
    { sensitiveResponse: true, showErrorToast: false })),
  updateDelivery: (
    id: number,
    expectedRevision: number,
    deliverySettings: NonNullable<
      Parameters<typeof BApi.acquisition.updateAcquisitionPlanTask>[1]["deliverySettings"]
    >,
  ) =>
    requireResult(
      BApi.acquisition.updateAcquisitionPlanTask(id, { expectedRevision, deliverySettings }),
    ),
  updateStorage: (
    id: number,
    expectedRevision: number,
    storagePolicy: NonNullable<
      Parameters<typeof BApi.acquisition.updateAcquisitionPlanTask>[1]["storagePolicy"]
    >,
    storagePolicyOverrides: NonNullable<
      Parameters<typeof BApi.acquisition.updateAcquisitionPlanTask>[1]["storagePolicyOverrides"]
    >,
  ) =>
    requireResult(
      BApi.acquisition.updateAcquisitionPlanTask(id, {
        expectedRevision,
        storagePolicy,
        storagePolicyOverrides,
      }),
    ),
  run: (id: number, expectedRevision: number, useEmergencySpaceReserve?: boolean) =>
    requireResult(BApi.acquisition.runAcquisitionPlan(id, {
      expectedRevision,
      ...(useEmergencySpaceReserve !== undefined ? { useEmergencySpaceReserve } : {}),
    })),
  preflight: (id: number) => requireResult(BApi.acquisition.preflightAcquisitionPlan(id)),
  replan: (
    id: number,
    body: Omit<
      Parameters<typeof BApi.acquisition.replanAcquisitionTask>[1],
      "useCurrentStorageDefaults" | "retryDeliveryOnly"
    > & {
      retryDeliveryOnly?: boolean;
      useCurrentStorageDefaults?: boolean;
    },
  ) =>
    requireResult(
      BApi.acquisition.replanAcquisitionTask(
        id,
        {
          ...body,
          useCurrentStorageDefaults: body.useCurrentStorageDefaults ?? false,
          retryDeliveryOnly: body.retryDeliveryOnly ?? false,
        },
        {
          sensitiveResponse: true,
          showErrorToast: false,
        },
      ),
    ),
  applyProcessingRevision: (
    id: number,
    expectedRevision: number,
    partId: string,
    source: SourceRef,
    revisionId: string,
  ) =>
    requireResult(
      BApi.acquisition.applyAcquisitionProcessingRevisions(
        id,
        { expectedRevision, items: [{ partId, source, revisionId }] },
        { sensitiveResponse: true, showErrorToast: false },
      ),
    ),
};
