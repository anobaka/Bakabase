import type { FederatedResourceDetail, PlaybackSession, ResourceRef } from "./types";

import { federationRequest, jsonBody } from "./transport";

import envConfig from "@/config/env";

export const federationResourceApi = {
  detail: (ref: ResourceRef, signal?: AbortSignal) =>
    federationRequest<{ resources: FederatedResourceDetail[] }>(
      "/resources/resolve",
      jsonBody({ refs: [ref] }, "POST", signal),
    ),
  playback: (
    resourceRef: ResourceRef,
    assetId: string,
    mode: "preview" | "player",
    signal?: AbortSignal,
  ) =>
    federationRequest<PlaybackSession>(
      "/playback-sessions",
      jsonBody({ assetRef: { resourceRef, assetId }, mode }, "POST", signal),
    ),
};

/** A media session is served by our coordinator, never an arbitrary URL from another node. */
export function localMediaUrl(path: string) {
  const base = new URL(envConfig.apiEndpoint || window.location.origin, window.location.origin);
  const url = new URL(path, base);

  if (url.origin !== base.origin || !url.pathname.startsWith("/federation/local/")) {
    throw new Error("Invalid local media session URL");
  }

  return url.href;
}
