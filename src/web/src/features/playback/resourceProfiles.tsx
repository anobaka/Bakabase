import type {
  PlaybackKind,
  PlaybackPreference,
} from "@/components/Resource/components/PlayControl/playbackPreferences";
import type { ContentType } from "@/sdk/Api";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { TbSettings } from "react-icons/tb";
import toast from "react-hot-toast";

import { RESOURCE_PROFILES_CHANGED } from "./profileInvalidation";

import { Button, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore, useUserSideActionsRunHere } from "@/stores/remoteAccess";
import { isDesktopAppWindow } from "@/utils/clientSurface";
import { openLocalView } from "@/features/federation/switching";

export type ResourceProfileOpening = {
  openingMode?: "browser" | "native" | "ask" | null;
  profileIds?: number[];
};
export function parseProfileSource(target: string | undefined) {
  if (target?.startsWith("server:")) return { sourceDeviceId: target.slice(7) };
  const peer = /^node:([^:]+):([^:]+)$/.exec(target ?? "");

  return peer ? { sourceDeviceId: peer[1], libraryEpoch: peer[2] } : undefined;
}
type ProfileQuery = { sourceDeviceId: string; libraryEpoch?: string; resourceId: number };
type Waiter = {
  resourceId: number;
  key: string;
  resolve: (value: ResourceProfileOpening) => void;
  reject: (error: unknown) => void;
};
const pending = new Map<string, Promise<ResourceProfileOpening>>();
const cache = new Map<string, { value: ResourceProfileOpening; expires: number }>();
const batches = new Map<
  string,
  { query: Omit<ProfileQuery, "resourceId">; waiters: Waiter[]; revision: number }
>();
const subscribers = new Set<() => void>();
let revision = 0;
const invalidate = () => {
  revision += 1;
  cache.clear();
  pending.clear();
  for (const subscriber of subscribers) subscriber();
};

function subscribe(listener: () => void) {
  if (!subscribers.size) {
    window.addEventListener("focus", invalidate);
    window.addEventListener(RESOURCE_PROFILES_CHANGED, invalidate);
  }
  subscribers.add(listener);

  return () => {
    subscribers.delete(listener);
    if (!subscribers.size) {
      revision += 1;
      cache.clear();
      pending.clear();
      window.removeEventListener("focus", invalidate);
      window.removeEventListener(RESOURCE_PROFILES_CHANGED, invalidate);
    }
  };
}
async function flushBatch(batchKey: string) {
  const batch = batches.get(batchKey);

  if (!batch) return;
  batches.delete(batchKey);
  for (let offset = 0; offset < batch.waiters.length; offset += 256) {
    const waiters = batch.waiters.slice(offset, offset + 256);

    try {
      const response = await BApi.request<{
        code?: number;
        data?: (ResourceProfileOpening & { resourceId: number })[];
        message?: string;
      }>({
        path: "/player/resource-profiles",
        method: "POST",
        format: "json",
        type: "application/json" as ContentType,
        body: { ...batch.query, resourceIds: waiters.map((waiter) => waiter.resourceId) },
        showErrorToast: false,
        signal: AbortSignal.timeout(10_000),
      });

      if (response.code || !response.data)
        throw new Error(response.message || "Resource profiles unavailable");
      const results = new Map(response.data.map((result) => [result.resourceId, result]));

      for (const waiter of waiters) {
        const result = results.get(waiter.resourceId);

        if (!result) {
          waiter.reject(new Error("Resource profile result missing"));
          continue;
        }
        const value = { openingMode: result.openingMode, profileIds: result.profileIds };

        if (batch.revision === revision) {
          cache.set(waiter.key, { value, expires: Date.now() + 30_000 });
          while (cache.size > 512) cache.delete(cache.keys().next().value!);
        }
        waiter.resolve(value);
      }
    } catch (error) {
      for (const waiter of waiters) waiter.reject(error);
    }
  }
}
async function loadProfile(key: string, query: ProfileQuery, force = false) {
  if (force) cache.delete(key);
  const cached = cache.get(key);

  if (cached && cached.expires > Date.now()) return cached.value;
  const current = pending.get(key);

  if (current) return current;
  const batchKey = `${revision}:${query.sourceDeviceId}:${query.libraryEpoch ?? ""}`;
  const request = new Promise<ResourceProfileOpening>((resolve, reject) => {
    let batch = batches.get(batchKey);

    if (!batch) {
      batch = {
        query: {
          sourceDeviceId: query.sourceDeviceId,
          ...(query.libraryEpoch ? { libraryEpoch: query.libraryEpoch } : {}),
        },
        waiters: [],
        revision,
      };
      batches.set(batchKey, batch);
      setTimeout(() => void flushBatch(batchKey), 15);
    }
    batch.waiters.push({ resourceId: query.resourceId, key, resolve, reject });
  });

  pending.set(key, request);
  try {
    return await request;
  } finally {
    if (pending.get(key) === request) pending.delete(key);
  }
}

/** Local resource profiles are evaluated against the source's metadata. Browsers keep their own playback preference. */
export function useSourceResourceProfile(target: string | undefined, resourceId?: number) {
  const nativeAvailable = useUserSideActionsRunHere();
  const desktop = nativeAvailable && isDesktopAppWindow();
  const source = parseProfileSource(target);
  const enabled = desktop && !!source && resourceId != null;
  const key = `${target}:${resourceId}`;
  const [result, setResult] = useState<{
    key: string;
    value?: ResourceProfileOpening;
    error?: boolean;
  }>();
  const generation = useRef(0);
  const load = useCallback(
    async (force = false) => {
      const revision = ++generation.current;

      if (!enabled || !source) return;
      try {
        const value = await loadProfile(key, { ...source, resourceId: resourceId! }, force);

        if (revision === generation.current) setResult({ key, value });
      } catch {
        if (revision === generation.current) setResult({ key, error: true });
      }
    },
    [enabled, key],
  );

  useEffect(() => {
    void load();
    const unsubscribe = enabled ? subscribe(() => void load(true)) : () => {};

    return () => {
      generation.current += 1;
      unsubscribe();
    };
  }, [load, enabled]);
  const current = result?.key === key ? result : undefined;
  const snapshot = enabled ? current?.value : undefined;
  const choice = (_kind?: PlaybackKind) =>
    snapshot?.openingMode ? { mode: snapshot.openingMode } : undefined;

  return {
    snapshot,
    desktop,
    nativeAvailable,
    ready: !enabled || current?.value !== undefined,
    error: enabled && !!current?.error,
    unavailable: enabled && !!current?.error,
    choice,
    preference: (_kind: PlaybackKind, fallback: PlaybackPreference): PlaybackPreference =>
      snapshot?.openingMode ? { mode: snapshot.openingMode } : fallback,
    reload: () => load(true),
  };
}

/** Configuration always opens this device's own resource-profile page, not the managed server's CRUD. */
export function ResourceProfileLink({
  target,
  compact = false,
}: {
  target?: string;
  resourceId?: number;
  label?: string;
  compact?: boolean;
}) {
  const { t } = useTranslation();
  const nativeAvailable = useUserSideActionsRunHere();
  const managing = useRemoteAccessStore((state) => state.clientMode === ClientMode.PureClient);
  const [loading, setLoading] = useState(false);
  const source = parseProfileSource(target);

  if (!source || !nativeAvailable || !isDesktopAppWindow()) return null;
  const open = async () => {
    const params = new URLSearchParams({ sourceDeviceId: source.sourceDeviceId });
    const route = `/resource-profile?${params}`;

    setLoading(true);
    try {
      if (managing) await openLocalView(route);
      else window.location.hash = route;
    } catch {
      toast.error(t("resource.profilePlayback.openFailed"));
    } finally {
      setLoading(false);
    }
  };

  return (
    <Tooltip content={t("resource.profilePlayback.configure")}>
      <Button
        aria-label={t("resource.profilePlayback.configure")}
        isIconOnly={compact}
        isLoading={loading}
        size="sm"
        variant="light"
        onPress={() => void open()}
      >
        {compact ? <TbSettings aria-hidden /> : t("resource.profilePlayback.configure")}
      </Button>
    </Tooltip>
  );
}
