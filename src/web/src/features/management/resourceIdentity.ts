import type { Resource } from "@/core/models/Resource";
import type { ManagementSource } from "./api";

export type ResourceSelectionKey = number | string;

export function resourceSelectionKey(
  resource: Pick<Resource, "id" | "owner">,
): ResourceSelectionKey {
  return resource.owner && !resource.owner.isLocal
    ? JSON.stringify([resource.owner.serverId, resource.owner.libraryEpoch, resource.id])
    : resource.id;
}

export function sameResourceOwner(a?: ManagementSource, b?: ManagementSource) {
  if ((!a || a.isLocal) && (!b || b.isLocal)) return true;
  if (!a || !b) return !a && !b;
  return a.serverId === b.serverId && a.libraryEpoch === b.libraryEpoch;
}

export function deviceColor(serverId: string) {
  // Stable across source ordering, reconnects and pages; independent of another device's IDs.
  let hash = 2166136261;
  for (const character of serverId) hash = Math.imul(hash ^ character.charCodeAt(0), 16777619);
  return `hsl(${(hash >>> 0) % 360} 70% 58%)`;
}
