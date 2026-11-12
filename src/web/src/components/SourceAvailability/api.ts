import BApi from "@/sdk/BApi";
import { DownloadSourceProviderIds } from "@/sdk/constants";
import type {
  BakabaseServiceComponentsSourceAvailabilitySourceAvailabilityRow as ApiRow,
  BakabaseServiceComponentsSourceAvailabilitySourceAvailabilityBatchView as ApiBatch,
} from "@/sdk/Api";
import type {
  SourceAvailabilityAction,
  SourceAvailabilityPreview,
  SourceAvailabilityResult,
  SourceAvailabilityRow,
  SourcePriorityOptions,
} from "./models";

async function result<T>(
  promise: Promise<{ code?: number; message?: string | null; data?: T }>,
): Promise<T> {
  const response = await promise;
  if (response.code || response.data == null)
    throw new Error(response.message || "SOURCE_AVAILABILITY_REQUEST_FAILED");
  return response.data;
}
function row(value: ApiRow): SourceAvailabilityRow {
  return {
    key: value.key,
    taskId: value.taskId,
    taskTitle: value.title,
    partId: value.partId,
    sourceId: value.sourceId,
    url: value.url,
    accessCode: value.accessCode,
    providerId: value.providerId,
    health: value.availability
      ? {
          status: value.availability.status,
          code: value.availability.reasonCode,
          checkedAt: value.availability.checkedAt,
          expiresAt: value.availability.expiresAt,
          evidence: value.availability.evidence,
        }
      : undefined,
    disabled: value.disabled,
    inUse: value.protected,
    actionable: value.disabled ? value.canRestore : value.canDisable,
    reasonCode: value.blockReason,
  };
}
function batch(value: ApiBatch): SourceAvailabilityPreview {
  const rows = value.rows.map(row);
  return {
    batchId: value.batchId,
    status: value.status === "checking" || value.status === "ready" ? value.status : "failed",
    processed: value.processed,
    total: value.total,
    expiresAt: value.expiresAt,
    rows,
    taskCount: new Set(rows.map((item) => item.taskId)).size,
    sourceCount: rows.length,
  };
}
const sensitive = { sensitiveResponse: true, showErrorToast: false };
export const sourceAvailabilityApi = {
  options: async (): Promise<SourcePriorityOptions> => {
    const options = await result(BApi.acquisition.getAcquisitionOptions());
    return { providerPriority: options.sourceProviderPriority ?? [] };
  },
  saveOptions: async (value: SourcePriorityOptions): Promise<SourcePriorityOptions> => {
    // Fetch immediately before updating so unrelated settings are not overwritten by an old editor snapshot.
    const options = await result(BApi.acquisition.getAcquisitionOptions());
    const response = await BApi.acquisition.putAcquisitionOptions({
      ...options,
      sourceProviderPriority: value.providerPriority,
    });
    if (response.code) throw new Error(response.message || "SOURCE_AVAILABILITY_REQUEST_FAILED");
    return value;
  },
  providerIds: async (): Promise<string[]> => [...DownloadSourceProviderIds],
  preview: async (ids: number[], force: boolean): Promise<SourceAvailabilityPreview> =>
    batch(
      await result(
        BApi.sourceAvailability.previewAcquisitionSourceAvailability(
          { taskIds: ids, force },
          sensitive,
        ),
      ),
    ),
  batch: async (id: string): Promise<SourceAvailabilityPreview> =>
    batch(await result(BApi.sourceAvailability.getSourceAvailabilityBatch(id, sensitive))),
  apply: async (
    batchId: string,
    keys: string[],
    action: SourceAvailabilityAction,
  ): Promise<SourceAvailabilityResult> => {
    const response = await result(
      BApi.sourceAvailability.applyAcquisitionSourceAvailability(
        { batchId, selectedKeys: keys, action },
        sensitive,
      ),
    );
    return {
      changedCount: response.appliedKeys.length,
      skippedCount: response.skipped.length,
      rows: [
        ...response.appliedKeys.map((key) => ({ key, status: "applied" })),
        ...response.skipped.map((item) => ({
          key: item.key,
          status: "skipped",
          code: item.reason,
        })),
      ],
    };
  },
  acquisition: async (id: number): Promise<SourceAvailabilityRow[]> =>
    (await result(BApi.sourceAvailability.getAcquisitionSourceAvailability(id, sensitive))).map(
      row,
    ),
};
