import type { ResourceRef } from "./types";

export function readResourceRef(params: URLSearchParams): ResourceRef | undefined {
  const nodeId = params.get("node");
  const libraryEpoch = params.get("epoch");
  const rawId = params.get("resource");
  const resourceId = Number(rawId);

  if (!nodeId || !libraryEpoch || !rawId || !Number.isSafeInteger(resourceId) || resourceId <= 0)
    return undefined;

  return { nodeId, libraryEpoch, resourceId };
}

export function withResourceRef(params: URLSearchParams, ref?: ResourceRef): URLSearchParams {
  const next = new URLSearchParams(params);

  ["node", "epoch", "resource"].forEach((key) => next.delete(key));
  if (ref) {
    next.set("node", ref.nodeId);
    next.set("epoch", ref.libraryEpoch);
    next.set("resource", String(ref.resourceId));
  }

  return next;
}
