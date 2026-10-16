import type { Resource } from "@/core/models/Resource";
import type { ManagementSource } from "./api";
import type { ResourceSelectionKey } from "./resourceIdentity";

import { sourceKey } from "./api";
import { resourceSelectionKey } from "./resourceIdentity";

export type OwnerResourceSelection = {
  owner?: ManagementSource;
  ids: number[];
  resources: Resource[];
};

/** A mixed selection keeps each device's numeric IDs in a separate batch. */
export function groupResourceSelection(
  keys: ResourceSelectionKey[],
  resources: Resource[],
  sources: ManagementSource[] = [],
): OwnerResourceSelection[] {
  const selected = new Set(keys);
  const knownOwners = new Map(sources.map((owner) => [sourceKey(owner), owner]));
  for (const resource of resources) {
    if (resource.owner) knownOwners.set(sourceKey(resource.owner), resource.owner);
  }
  const groups = new Map<string, OwnerResourceSelection>();

  for (const key of keys) {
    let id: number;
    let owner: ManagementSource | undefined;
    let groupKey = "local";

    if (typeof key === "number") {
      id = key;
      owner = [...knownOwners.values()].find((source) => source.isLocal);
    } else {
      const [serverId, libraryEpoch, parsedId] = JSON.parse(key) as [string, string, number];

      groupKey = JSON.stringify([serverId, libraryEpoch]);
      id = parsedId;
      owner = knownOwners.get(groupKey);
      // Unknown or replaced owners must never fall back to the local server.
      if (!owner) continue;
    }
    const group = groups.get(groupKey) ?? { owner, ids: [], resources: [] };

    group.ids.push(id);
    groups.set(groupKey, group);
  }
  for (const resource of resources) {
    if (!selected.has(resourceSelectionKey(resource))) continue;
    const key = resource.owner && !resource.owner.isLocal ? sourceKey(resource.owner) : "local";

    groups.get(key)?.resources.push(resource);
  }

  return [...groups.values()];
}
