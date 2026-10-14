import type { PlayableItem } from "@/core/models/Resource";
import type { ManagementSource } from "./api";

import envConfig from "@/config/env";

/** The URL remains bound to the owner and library generation, even after UI navigation. */
export function managedMediaUrl(source: ManagementSource, resourceId: number, item: PlayableItem) {
  const query = new URLSearchParams({
    libraryEpoch: source.libraryEpoch,
    resourceId: String(resourceId),
    origin: String(item.origin),
    key: item.key,
  });

  return `${envConfig.apiEndpoint}/federation/local/management/${encodeURIComponent(source.serverId)}/media?${query}`;
}

/** The manager launches through its own desktop adapter; this never calls the owner's Play API. */
export async function playManagedResource(
  source: ManagementSource,
  resourceId: number,
  item: PlayableItem,
  explicitChoice = false,
) {
  const response = await fetch(
    `${envConfig.apiEndpoint}/federation/local/management/${encodeURIComponent(source.serverId)}/play`,
    {
      method: "POST",
      cache: "no-store",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        libraryEpoch: source.libraryEpoch,
        resourceId,
        origin: item.origin,
        key: item.key,
        explicitChoice,
      }),
      signal: AbortSignal.timeout(30_000),
    },
  );
  const result = await response.json().catch(() => undefined);

  if (!response.ok || result?.code) {
    throw new Error(
      result?.message || result?.error?.message || `${response.status} ${response.statusText}`,
    );
  }
}
