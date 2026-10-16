import type { ContentType } from "@/sdk/Api";

import BApi from "@/sdk/BApi";

export interface ProcessingJob {
  id: string;
  jobId: string;
  role: string;
  ownerId: string;
  ownerEpoch: string;
  resourceId: number;
  kind: string;
  state: string;
  phase?: string | null;
  processedCount: number;
  totalCount?: number | null;
  currentStepPhase?: string | null;
  currentStepProcessedCount?: number | null;
  currentStepTotalCount?: number | null;
  attempt: number;
  pauseRequested: boolean;
  cancelRequested: boolean;
  errorCode?: string | null;
  error?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
}
export type ProcessingControl = "pause" | "resume" | "cancel" | "retry";
export const PROCESSING_JOBS_CHANGED = "bakabase:processing-jobs-changed";
const request = <T>(
  path: string,
  body?: unknown,
  signal?: AbortSignal,
  query?: Record<string, unknown>,
) =>
  BApi.request<T>({
    path: `/resource-processing/jobs${path}`,
    method: body === undefined ? "GET" : "POST",
    body,
    signal,
    query,
    format: "json",
    type: "application/json" as ContentType,
    showErrorToast: false,
  });

export const processingApi = {
  list: (signal?: AbortSignal, beforeId?: string) =>
    request<ProcessingJob[]>("", undefined, signal, {
      take: 30,
      ...(beforeId ? { beforeId } : {}),
    }),
  start: async (profileId: number, kind: 0 | 1) => {
    const result = await request<ProcessingJob>("", { profileId, kind });

    window.dispatchEvent(new Event(PROCESSING_JOBS_CHANGED));

    return result;
  },
  control: (job: ProcessingJob, action: ProcessingControl) =>
    request<ProcessingJob>(`/${encodeURIComponent(job.jobId)}/control`, {
      action,
      expectedVersion: job.version,
    }),
};
export const processingIsActive = (job: ProcessingJob) => ["Queued", "Running"].includes(job.state);
export const processingActions = (job: ProcessingJob): ProcessingControl[] => {
  if (job.cancelRequested) return [];
  switch (job.state) {
    case "Queued":
    case "Running":
      return job.pauseRequested ? ["cancel"] : ["pause", "cancel"];
    case "Paused":
      return ["resume", "cancel"];
    case "Failed":
      return ["retry", "cancel"];
    case "Conflict":
      return ["cancel"];
    default:
      return [];
  }
};
