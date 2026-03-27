import { useEffect, useState, useRef, useMemo, useCallback } from "react";
import { resourceDiscoveryChannel, type DiscoveryData } from "@/services/ResourceDiscoveryChannel";
import { DataOrigin, DataStatus, ResourceDataType } from "@/sdk/constants";
import type { PlayableItem, Resource, ResourceDataState } from "@/core/models/Resource";

export type OriginGroup = {
  origin: DataOrigin;
  items: PlayableItem[];
  status: DataStatus;
};

export type PlayableItemResolutionState = {
  /** Groups ordered by fixed display order */
  groups: OriginGroup[];
  /** Overall status */
  overallStatus: "ready" | "loading" | "not-found";
  /** Trigger SSE discovery for a specific origin */
  triggerDiscovery: (origin: DataOrigin) => void;
};

export type PlayableItemResolutionCallbacks = {
  onDiscoveryStart?: (resourceId: number, origin: DataOrigin) => void;
  onDiscoveryComplete?: (resourceId: number, origin: DataOrigin, data: DiscoveryData | null, error?: string) => void;
};

// Fixed display order for origins
const ORIGIN_ORDER: DataOrigin[] = [
  DataOrigin.Steam,
  DataOrigin.DLsite,
  DataOrigin.ExHentai,
  DataOrigin.FileSystem,
];

export function usePlayableItemResolution(
  resource: Resource,
  callbacks?: PlayableItemResolutionCallbacks
): PlayableItemResolutionState {
  // Get playableItem-related dataStates
  const piStates = useMemo(
    () => resource.dataStates?.filter(s => s.dataType === ResourceDataType.PlayableItem) ?? [],
    [resource.dataStates]
  );

  // SSE-discovered items per origin
  const [sseItems, setSseItems] = useState<Map<DataOrigin, { items: PlayableItem[] }>>(new Map());
  const subscribedRef = useRef(new Set<DataOrigin>());
  const [manualTriggers, setManualTriggers] = useState<Set<DataOrigin>>(new Set());

  const triggerDiscovery = useCallback((origin: DataOrigin) => {
    setManualTriggers(prev => {
      const next = new Set(prev);
      next.add(origin);
      return next;
    });
  }, []);

  // Origins that need auto-discovery (NotStarted) OR have been manually triggered
  const originsToDiscover = useMemo(() => {
    if (piStates.length === 0) return [];
    const autoOrigins = piStates
      .filter(s => s.status === DataStatus.NotStarted)
      .map(s => s.origin);
    // Merge with manual triggers
    const all = new Set([...autoOrigins, ...manualTriggers]);
    return Array.from(all);
  }, [piStates, manualTriggers]);

  // SSE subscription
  useEffect(() => {
    const unsubscribes: (() => void)[] = [];

    for (const origin of originsToDiscover) {
      if (subscribedRef.current.has(origin)) continue;
      subscribedRef.current.add(origin);

      callbacks?.onDiscoveryStart?.(resource.id, origin);

      resourceDiscoveryChannel
        .subscribe(resource.id, origin, ResourceDataType.PlayableItem, (data, error) => {
          callbacks?.onDiscoveryComplete?.(resource.id, origin, data, error);
          if (data?.playableItems) {
            setSseItems(prev => {
              const next = new Map(prev);
              next.set(origin, {
                items: data.playableItems!,
              });
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

  // Reset when resource changes
  useEffect(() => {
    setSseItems(new Map());
    subscribedRef.current = new Set();
    setManualTriggers(new Set());
  }, [resource.id]);

  // Build groups
  const groups = useMemo((): OriginGroup[] => {
    if (piStates.length === 0) return [];

    // Backend items grouped by origin
    const backendItemsByOrigin = new Map<DataOrigin, PlayableItem[]>();
    for (const item of resource.playableItems ?? []) {
      const list = backendItemsByOrigin.get(item.origin) ?? [];
      list.push(item);
      backendItemsByOrigin.set(item.origin, list);
    }

    const result: OriginGroup[] = [];
    for (const origin of ORIGIN_ORDER) {
      const state = piStates.find(s => s.origin === origin);
      if (!state) continue; // This origin doesn't apply

      // SSE items override backend items for this origin
      const sseData = sseItems.get(origin);
      const items = sseData?.items ?? backendItemsByOrigin.get(origin) ?? [];

      // Don't add empty Ready groups
      if (state.status === DataStatus.Ready && items.length === 0) continue;

      result.push({
        origin,
        items,
        status: sseData ? DataStatus.Ready : state.status,
      });
    }

    return result;
  }, [piStates, resource.playableItems, sseItems]);

  const overallStatus = useMemo((): "ready" | "loading" | "not-found" => {
    if (piStates.length === 0) return "not-found";
    if (groups.some(g => g.items.length > 0)) return "ready";
    if (groups.some(g => g.status === DataStatus.NotStarted)) return "loading";
    return "not-found";
  }, [piStates, groups]);

  return { groups, overallStatus, triggerDiscovery };
}
