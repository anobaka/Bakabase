import type {
  CreateMoveRequest,
  MoveBatch,
  MovePanelOptions,
  MovePreview,
  MoveResourceRef,
  MoveSourceContext,
} from "./types";

import BApi from "@/sdk/BApi";
import { ContentType } from "@/sdk/Api";

type Response<T> = { code?: number; message?: string; data?: T };
/** A received domain rejection is distinct from an ambiguous transport failure. */
export class MoveRequestRejected extends Error {}
async function request<T>(
  path: string,
  method = "GET",
  body?: unknown,
  query?: Record<string, string | number | boolean>,
) {
  let response: Response<T>;

  try {
    response = await BApi.request<Response<T>>({
      path: `/resource-move${path}`,
      method,
      body,
      query,
      type: body ? ContentType.Json : undefined,
      format: "json",
      showErrorToast: false,
    });
  } catch (error) {
    const received = error as { status?: number; error?: { message?: string } };

    // HTTP 4xx is an explicit refusal. Network errors and 5xx remain ambiguous.
    if (received?.status && received.status >= 400 && received.status < 500) {
      throw new MoveRequestRejected(
        received.error?.message || `Request rejected (${received.status})`,
      );
    }
    throw error;
  }
  if (response.code) throw new MoveRequestRejected(response.message || "Move request rejected");

  return response.data as T;
}
export const movePanelApi = {
  context: () => request<MoveSourceContext>("/context"),
  options: () => request<MovePanelOptions>("/panel-options"),
  saveOptions: (options: MovePanelOptions) =>
    request<MovePanelOptions>("/panel-options", "PUT", options),
  batches: (skip = 0) =>
    request<MoveBatch[]>("/batches", "GET", undefined, { origin: "move-panel", skip, take: 100 }),
  batch: (id: string) => request<MoveBatch>(`/batches/${encodeURIComponent(id)}`),
  activeBatches: () =>
    request<MoveBatch[]>("/batches", "GET", undefined, { activeOnly: true, take: 10000 }),
  preview: (resourceIds: number[], destDir: string, resourceRefs: MoveResourceRef[]) =>
    request<MovePreview>("/preview", "POST", {
      resourceIds,
      resourceRefs,
      destDir,
      origin: "move-panel",
    }),
  create: (body: CreateMoveRequest) =>
    request<{ batchId: string; skippedResourceCount: number }>("", "POST", body),
  cancel: (id: string) => request(`/batches/${encodeURIComponent(id)}/cancel`, "POST"),
  retry: (id: string) => request(`/batches/${encodeURIComponent(id)}/retry`, "POST"),
  resolve: (
    id: number,
    action: "overwrite" | "skip" | "restoreSource",
    scope: "once" | "batch" | "panel",
    conflictVersion: number,
  ) => request(`/records/${id}/resolve`, "POST", { action, scope, conflictVersion }),
};

export const cancelResourceMoveBatch = movePanelApi.cancel;
