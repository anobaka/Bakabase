import { useEffect, useState, useRef, useMemo, useCallback } from "react";
import { resourceDiscoveryChannel, type DiscoveryData } from "@/services/ResourceDiscoveryChannel";
import { DataOrigin, DataStatus, ResourceDataType } from "@/sdk/constants";
import type { Resource, ResourceDataState } from "@/core/models/Resource";

export type CoverResolutionState = {
  /** Best available covers (by provider priority) */
  covers: string[] | null;
  /** Overall status */
  status: "ready" | "loading" | "not-found";
};

export type CoverResolutionCallbacks = {
  onDiscoveryStart?: (resourceId: number, origin: DataOrigin) => void;
  onDiscoveryComplete?: (resourceId: number, origin: DataOrigin, data: DiscoveryData | null, error?: string) => void;
};

// Provider priority (lower = higher priority). Must match backend.
const COVER_PRIORITY: DataOrigin[] = [
  DataOrigin.Manual,
  DataOrigin.Steam,
  DataOrigin.DLsite,
  DataOrigin.ExHentai,
  DataOrigin.FileSystem,
];

export function useCoverResolution(
  resource: Resource,
  callbacks?: CoverResolutionCallbacks
): CoverResolutionState {
  // Get cover-related dataStates
  const coverStates = useMemo(
    () => resource.dataStates?.filter(s => s.dataType === ResourceDataType.Cover) ?? [],
    [resource.dataStates]
  );

  // Backend-resolved covers (already priority-selected)
  const [sseCovers, setSseCovers] = useState<Map<DataOrigin, string[]>>(new Map());
  const subscribedRef = useRef(new Set<DataOrigin>());

  // Determine which origins need SSE discovery
  const originsToDiscover = useMemo(() => {
    if (coverStates.length === 0) return []; // No dataStates = no capability
    return coverStates
      .filter(s => s.status === DataStatus.NotStarted)
      .map(s => s.origin);
  }, [coverStates]);

  // SSE subscription effect
  useEffect(() => {
    const unsubscribes: (() => void)[] = [];

    for (const origin of originsToDiscover) {
      if (subscribedRef.current.has(origin)) continue;
      subscribedRef.current.add(origin);

      callbacks?.onDiscoveryStart?.(resource.id, origin);

      resourceDiscoveryChannel
        .subscribe(resource.id, origin, ResourceDataType.Cover, (data, error) => {
          callbacks?.onDiscoveryComplete?.(resource.id, origin, data, error);
          if (data?.coverPaths?.length) {
            setSseCovers(prev => {
              const next = new Map(prev);
              next.set(origin, data.coverPaths!);
              return next;
            });
          }
        })
        .then(unsub => unsubscribes.push(unsub));
    }

    return () => {
      unsubscribes.forEach(fn => fn());
      subscribedRef.current = new Set();
    };
  }, [resource.id, originsToDiscover.join(",")]);

  // Reset SSE covers when resource changes
  useEffect(() => {
    setSseCovers(new Map());
    subscribedRef.current = new Set();
  }, [resource.id]);

  // Compute best covers: backend covers take precedence, then SSE results by priority
  const result = useMemo((): CoverResolutionState => {
    // No dataStates = no capability
    if (coverStates.length === 0) {
      return { covers: null, status: "not-found" };
    }

    // If backend already resolved covers, use them
    if (resource.covers?.length) {
      return { covers: resource.covers, status: "ready" };
    }

    // Check SSE-discovered covers by priority
    for (const origin of COVER_PRIORITY) {
      const sseCover = sseCovers.get(origin);
      if (sseCover?.length) {
        return { covers: sseCover, status: "ready" };
      }
    }

    // Check if any are still not-started (SSE in progress)
    const hasNotStarted = coverStates.some(s =>
      s.status === DataStatus.NotStarted
    );
    if (hasNotStarted) {
      return { covers: null, status: "loading" };
    }

    // All ready/failed but no covers
    return { covers: null, status: "not-found" };
  }, [coverStates, resource.covers, sseCovers]);

  return result;
}
