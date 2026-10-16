import type { PlayableItem, Resource } from "@/core/models/Resource";

import { useEffect, useState, useRef, useMemo, useCallback } from "react";

import { resourceDiscoveryChannel, type DiscoveryData } from "@/services/ResourceDiscoveryChannel";
import { DataOrigin, DataStatus, ResourceDataType } from "@/sdk/constants";
import { useResourceApi, useResourceOwner } from "@/features/management/resourceContext";

export type OriginGroup = {
  origin: DataOrigin;
  items: PlayableItem[];
  status: DataStatus;
  error?: string;
};

export type PlayableItemResolutionState = {
  /** Groups ordered by fixed display order */
  groups: OriginGroup[];
  /** Overall status */
  overallStatus: "ready" | "loading" | "not-found" | "failed";
  /** Trigger SSE discovery for a specific origin */
  triggerDiscovery: (origin: DataOrigin) => void;
};

export type PlayableItemResolutionCallbacks = {
  onDiscoveryStart?: (resourceId: number, origin: DataOrigin) => void;
  onDiscoveryComplete?: (
    resourceId: number,
    origin: DataOrigin,
    data: DiscoveryData | null,
    error?: string,
  ) => void;
};

// Fixed display order for origins
const ORIGIN_ORDER: DataOrigin[] = [DataOrigin.Steam, DataOrigin.FileSystem];

export function usePlayableItemResolution(
  resource: Resource,
  callbacks?: PlayableItemResolutionCallbacks,
): PlayableItemResolutionState {
  const api = useResourceApi();
  const source = useResourceOwner();
  const remote = source && !source.isLocal ? source : undefined;
  const identity = useMemo(
    () => ({ id: resource.id, reloadToken: resource.reloadToken, path: resource.path, remote }),
    [resource.id, resource.reloadToken, resource.path, remote],
  );
  const latestIdentity = useRef(identity);

  latestIdentity.current = identity;
  // Get playableItem-related dataStates
  const piStates = useMemo(
    () =>
      resource.dataStates?.filter(
        (s) => s.dataType === ResourceDataType.PlayableItem && ORIGIN_ORDER.includes(s.origin),
      ) ?? [],
    [resource.dataStates],
  );

  // Each origin owns an independent attempt. Retrying one source must neither
  // restart other sources nor let the previous attempt overwrite its result.
  const [sseItems, setSseItems] = useState(() => ({
    owner: identity,
    items: new Map<
      DataOrigin,
      { items: PlayableItem[]; status: DataStatus; error?: string; attempt: number }
    >(),
  }));
  const [manualTriggers, setManualTriggers] = useState(() => ({
    owner: identity,
    attempts: new Map<DataOrigin, number>(),
  }));
  const [remoteResult, setRemoteResult] = useState<{
    owner: typeof identity;
    items: PlayableItem[];
    error?: string;
  }>();

  useEffect(() => {
    if (!remote) return;
    let cancelled = false;

    setRemoteResult(undefined);
    void api.resource
      .getResourcePlayableItems(resource.id)
      .then((result) => {
        if (!cancelled) setRemoteResult({ owner: identity, items: result.data ?? [] });
      })
      .catch((error) => {
        if (!cancelled) setRemoteResult({ owner: identity, items: [], error: String(error) });
      });

    return () => {
      cancelled = true;
    };
  }, [api, remote, identity, manualTriggers]);
  const subscriptions = useRef(
    new Map<
      DataOrigin,
      {
        attempt: number;
        active: boolean;
        unsubscribe?: () => void;
      }
    >(),
  );
  const latestCallbacks = useRef(callbacks);

  latestCallbacks.current = callbacks;

  const triggerDiscovery = useCallback(
    (origin: DataOrigin) => {
      if (!ORIGIN_ORDER.includes(origin)) return;
      setManualTriggers((prev) => {
        const attempts = new Map(prev.owner === identity ? prev.attempts : []);

        attempts.set(origin, (attempts.get(origin) ?? 0) + 1);

        return { owner: identity, attempts };
      });
    },
    [identity],
  );

  useEffect(
    () => () => {
      for (const subscription of subscriptions.current.values()) {
        subscription.active = false;
        subscription.unsubscribe?.();
      }
      subscriptions.current.clear();
    },
    [identity],
  );

  useEffect(() => {
    if (remote) return;
    for (const state of piStates) {
      const origin = state.origin;
      const attempt =
        manualTriggers.owner === identity ? (manualTriggers.attempts.get(origin) ?? 0) : 0;

      if (attempt === 0 && state.status !== DataStatus.NotStarted) continue;
      const previous = subscriptions.current.get(origin);

      if (previous?.attempt === attempt) continue;
      if (previous) {
        previous.active = false;
        previous.unsubscribe?.();
      }
      const subscription: { attempt: number; active: boolean; unsubscribe?: () => void } = {
        attempt,
        active: true,
      };

      subscriptions.current.set(origin, subscription);
      latestCallbacks.current?.onDiscoveryStart?.(resource.id, origin);

      const complete = (data: DiscoveryData | null, error?: string) => {
        if (!subscription.active || latestIdentity.current !== identity) return;
        latestCallbacks.current?.onDiscoveryComplete?.(resource.id, origin, data, error);
        setSseItems((prev) => {
          if (!subscription.active || latestIdentity.current !== identity) return prev;
          const next = new Map(prev.owner === identity ? prev.items : []);

          next.set(origin, {
            items: data?.playableItems?.filter((item) => item.origin === origin) ?? [],
            status: data && !error ? DataStatus.Ready : DataStatus.Failed,
            error,
            attempt,
          });

          return { owner: identity, items: next };
        });
      };

      resourceDiscoveryChannel
        .subscribe(resource.id, origin, ResourceDataType.PlayableItem, complete)
        .then((unsubscribe) => {
          if (subscription.active && latestIdentity.current === identity)
            subscription.unsubscribe = unsubscribe;
          else unsubscribe();
        })
        .catch((error) => complete(null, error instanceof Error ? error.message : String(error)));
    }
  }, [identity, piStates, manualTriggers, remote]);

  // Build groups
  const groups = useMemo((): OriginGroup[] => {
    if (remote) {
      const result = remoteResult?.owner === identity ? remoteResult : undefined;
      const origins = ORIGIN_ORDER.filter(
        (origin) =>
          piStates.some((state) => state.origin === origin) ||
          result?.items.some((item) => item.origin === origin) ||
          (origin === DataOrigin.FileSystem && resource.hasLocalPath),
      );

      return origins.map((origin) => ({
        origin,
        items: result?.items.filter((item) => item.origin === origin) ?? [],
        status: !result
          ? DataStatus.NotStarted
          : result.error
            ? DataStatus.Failed
            : DataStatus.Ready,
        error: result?.error,
      }));
    }
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
      const state = piStates.find((s) => s.origin === origin);

      if (!state) continue; // This origin doesn't apply

      // SSE items override backend items for this origin
      const sseData = sseItems.owner === identity ? sseItems.items.get(origin) : undefined;
      const attempt =
        manualTriggers.owner === identity ? (manualTriggers.attempts.get(origin) ?? 0) : 0;
      const pendingRetry = attempt > (sseData?.attempt ?? 0);
      const status = pendingRetry ? DataStatus.NotStarted : (sseData?.status ?? state.status);
      const items =
        status === DataStatus.Ready
          ? (sseData?.items ?? backendItemsByOrigin.get(origin) ?? [])
          : [];

      // Always include applicable origins. The consumer (PlayControl) filters
      // `sources` by items.length > 0, and having an empty-but-Ready group is
      // what lets fsDiscoveryStatus resolve to "ready" instead of "idle".
      result.push({
        origin,
        items,
        status,
        error: pendingRetry ? undefined : sseData?.error,
      });
    }

    return result;
  }, [
    piStates,
    resource.playableItems,
    sseItems,
    identity,
    manualTriggers,
    remote,
    remoteResult,
    resource.hasLocalPath,
  ]);

  const overallStatus = useMemo((): "ready" | "loading" | "not-found" | "failed" => {
    if (groups.length === 0) return "not-found";
    if (groups.some((g) => g.items.length > 0)) return "ready";
    if (groups.some((g) => g.status === DataStatus.NotStarted)) return "loading";
    if (groups.some((g) => g.status === DataStatus.Failed)) return "failed";

    return "not-found";
  }, [piStates, groups]);

  return { groups, overallStatus, triggerDiscovery };
}
