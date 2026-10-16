import type { ProcessingJob } from "@/pages/resource-profile/processingApi";
import type { ContentType } from "@/sdk/Api";

import BApi from "@/sdk/BApi";

export interface ActivityDevice {
  id: string;
  name: string;
}
export interface SyncActivity {
  id: string;
  engine: "definitions" | "metadata" | "processing";
  dataType: string;
  state: string;
  phase: string;
  source: ActivityDevice;
  destination: ActivityDevice;
  worker?: ActivityDevice | null;
  lastSuccessAtUtc?: string | null;
  itemCount?: number | null;
  errorCode?: string | null;
  error?: string | null;
  cachedDataAvailable: boolean;
  pendingDecisions: number;
  linkId?: number | null;
  libraryEpoch?: string | null;
  job?: ProcessingJob | null;
}
export interface ActivityPage {
  items: SyncActivity[];
  total: number;
  nextOffset?: number | null;
  devices: ActivityDevice[];
  dataTypes: string[];
}
export interface MetadataRun {
  id: number;
  serverId: string;
  name?: string | null;
  startedAtUtc: string;
  updatedAtUtc: string;
  state: string;
  phase: string;
  processedCount: number;
  resourceCount: number;
  errorCode?: string | null;
  error?: string | null;
}
export interface ActivityFilters {
  dataType?: string;
  deviceId?: string;
  state?: string;
  offset?: number;
}
const get = <T>(path: string, query?: Record<string, unknown>, signal?: AbortSignal) =>
  BApi.request<T>({
    path: `/data-sync/activity${path}`,
    method: "GET",
    query,
    signal,
    format: "json",
    showErrorToast: false,
  });

export const activityApi = {
  list: (filters: ActivityFilters, signal?: AbortSignal) =>
    get<ActivityPage>("", { ...filters, take: 50 }, signal),
  metadataHistory: (serverId?: string, beforeId?: number, signal?: AbortSignal) =>
    get<{ entries: MetadataRun[]; nextBeforeId?: number | null }>(
      "/metadata-history",
      { serverId, beforeId, take: 30 },
      signal,
    ),
  processingHistory: (role = 0, beforeId?: string, signal?: AbortSignal) =>
    get<ProcessingJob[]>("/processing-history", { role, beforeId, take: 30 }, signal),
  metadataPause: async (row: SyncActivity, paused: boolean) => {
    const result = await BApi.request<{ code?: number; message?: string }>({
      path: `/federation/local/management/${encodeURIComponent(row.source.id)}/metadata/control`,
      method: "POST",
      format: "json",
      type: "application/json" as ContentType,
      body: { libraryEpoch: row.libraryEpoch, paused },
      showErrorToast: false,
    });

    if (result?.code) throw new Error(result.message || "MetadataSyncFailed");
  },
};
