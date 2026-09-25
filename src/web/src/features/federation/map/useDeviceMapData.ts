import type { BakabaseServiceModelsViewRemoteAccessSettingsViewModel as RemoteAccessSettings } from "@/sdk/Api";
import type { ManagedServerCandidate, ManagedServersView } from "../types";
import type { SharingCandidate } from "./graph";

import { useCallback, useEffect, useRef, useState } from "react";

import { useFederationStatus } from "../hooks/useFederationStatus";
import { federationPeerApi } from "../peerApi";
import { managedServerApi } from "../serverApi";
import { isAbort } from "../transport";
import { MessageError } from "../components/common";

import BApi from "@/sdk/BApi";
import { millisecondsUntil } from "@/core/serverTime";
import { useDataSyncMap } from "@/features/data-sync/map/useDataSyncMap";

/*
 * Everything the device map draws, kept current the way the devices page keeps its own
 * sections current: a first read, then quiet re-reads that never replace a good answer with
 * an error, faster while somebody may be waiting on a request. Each source stands alone —
 * one that cannot be read leaves the others on the map.
 */

/** While a request may be decided at any moment, re-read this often. */
export const LIVE_POLL_MS = 5000;
/** Otherwise, often enough that a change made elsewhere shows up on its own. */
export const IDLE_POLL_MS = 15_000;
/** Servers are only listed from memory; probing them (reaching out) is kept for refreshes. */
const SERVERS_IDLE_POLL_MS = 30_000;

/** Failures show beside the map; a toast from the shared client would say it twice. */
const inline = { showErrorToast: false } as const;

export type MapSource = "sharing" | "servers" | "access" | "dataSync";

export interface DiscoveryState {
  running: boolean;
  /** Set once a search has finished. */
  sharing?: SharingCandidate[];
  management?: ManagedServerCandidate[];
  error?: Error;
}

const asError = (cause: unknown, fallback = "") => {
  if (cause instanceof Error) return cause;
  const body = (cause as { error?: { message?: string | null } } | undefined)?.error;

  return new MessageError(body?.message || fallback);
};

/** Library sharing's status, re-read quietly on a timer. */
function useSharing() {
  const federation = useFederationStatus();
  const { status, refresh } = federation;
  const live = (status?.requests ?? []).some(
    (request) => request.status === "awaitingApproval" && millisecondsUntil(request.expiresAt) > 0,
  );

  useEffect(() => {
    const timer = setInterval(
      () => {
        if (!document.hidden) void refresh({ quiet: true });
      },
      live ? LIVE_POLL_MS : IDLE_POLL_MS,
    );

    return () => clearInterval(timer);
  }, [live, refresh]);

  return federation;
}

/** The servers this device manages: listed at once, then probed for how each one is. */
function useManagedServers() {
  const [view, setView] = useState<ManagedServersView>();
  const [error, setError] = useState<Error>();
  const [loading, setLoading] = useState(false);
  const generation = useRef(0);
  const mounted = useRef(true);

  const load = useCallback(async (options: { probe?: boolean; quiet?: boolean } = {}) => {
    const run = ++generation.current;

    if (!options.quiet) setLoading(true);
    try {
      const fresh = await managedServerApi.list(options.probe === true);

      if (run !== generation.current) return;
      setView(fresh);
      setError(undefined);
    } catch (cause) {
      if (run === generation.current && !options.quiet) setError(asError(cause));
    } finally {
      if (run === generation.current && !options.quiet) setLoading(false);
    }
  }, []);

  useEffect(() => {
    mounted.current = true;
    void load().then(() => {
      if (mounted.current) void load({ probe: true, quiet: true });
    });

    return () => {
      mounted.current = false;
      generation.current += 1;
    };
  }, [load]);

  const waiting = (view?.requests ?? []).some((request) => request.active);

  useEffect(() => {
    if (view && !view.available) return;
    const timer = setInterval(
      () => {
        if (!document.hidden) void load({ quiet: true });
      },
      waiting ? LIVE_POLL_MS : SERVERS_IDLE_POLL_MS,
    );

    return () => clearInterval(timer);
  }, [waiting, load, view?.available]);

  return { view, error, loading, load };
}

/** Who may manage this device, and who is asking to. */
function useManagementAccess() {
  const [settings, setSettings] = useState<RemoteAccessSettings>();
  const [error, setError] = useState<Error>();
  const generation = useRef(0);
  const hasSettings = useRef(false);

  const load = useCallback(async (options: { quiet?: boolean } = {}) => {
    const run = ++generation.current;

    try {
      const rsp = await BApi.remoteAccess.getRemoteAccessSettings(inline);

      if (run !== generation.current) return;
      if (rsp?.code) throw new MessageError(rsp.message || "");
      if (rsp?.data) {
        hasSettings.current = true;
        setSettings(rsp.data);
        setError(undefined);
      }
    } catch (cause) {
      if (run === generation.current && (!options.quiet || !hasSettings.current))
        setError(asError(cause));
    }
  }, []);

  useEffect(() => {
    void load();

    return () => {
      generation.current += 1;
    };
  }, [load]);

  const live = (settings?.pendingRequests?.length ?? 0) > 0 || !!settings?.pairingCode;

  useEffect(() => {
    const timer = setInterval(
      () => {
        if (!document.hidden) void load({ quiet: true });
      },
      live ? LIVE_POLL_MS : IDLE_POLL_MS,
    );

    return () => clearInterval(timer);
  }, [live, load]);

  return { settings, error, load };
}

export function useDeviceMapData() {
  const sharing = useSharing();
  const servers = useManagedServers();
  const access = useManagementAccess();
  // Data sync's own reader, on the same schedule (features/data-sync/map).
  const dataSync = useDataSyncMap();
  const [discovery, setDiscovery] = useState<DiscoveryState>({ running: false });
  const search = useRef<AbortController>();

  useEffect(() => () => search.current?.abort(), []);

  const serversAvailable = servers.view?.available;
  const refreshSharing = sharing.refresh;
  const loadServers = servers.load;
  const loadAccess = access.load;
  const loadDataSync = dataSync.load;

  /** Re-reads what an action may have changed, without a loading state. */
  const reload = useCallback(
    async (sources: MapSource[] = ["sharing", "servers", "access", "dataSync"]) => {
      await Promise.all([
        sources.includes("sharing") ? refreshSharing({ quiet: false }) : undefined,
        sources.includes("servers") ? loadServers({ quiet: true }) : undefined,
        sources.includes("access") ? loadAccess({ quiet: true }) : undefined,
        sources.includes("dataSync") ? loadDataSync({ withOverview: true }) : undefined,
      ]);
    },
    [refreshSharing, loadServers, loadAccess, loadDataSync],
  );

  /** "Check status": everything again, managed servers asked how they are. */
  const refreshAll = useCallback(async () => {
    await Promise.all([
      refreshSharing(),
      loadServers({ probe: true }),
      loadAccess(),
      loadDataSync({ withOverview: true }),
    ]);
  }, [refreshSharing, loadServers, loadAccess, loadDataSync]);

  /**
   * Both kinds of looking around — sharing discovery lists devices that share their library,
   * the remote-access beacons list every server that could be managed. A few seconds each,
   * side by side; one that fails leaves the other's answer.
   */
  const discover = useCallback(async () => {
    if (search.current) return;
    const controller = new AbortController();

    search.current = controller;
    setDiscovery((previous) => ({ ...previous, running: true, error: undefined }));
    const [found, beacons] = await Promise.allSettled([
      federationPeerApi.discover(controller.signal),
      serversAvailable === false
        ? Promise.resolve(undefined)
        : managedServerApi.discover(controller.signal),
    ]);

    if (search.current === controller) search.current = undefined;
    if (controller.signal.aborted) return;
    const attempted = serversAvailable === false ? 1 : 2;
    const failures = [found, beacons].filter(
      (result): result is PromiseRejectedResult =>
        result.status === "rejected" && !isAbort(result.reason),
    );

    setDiscovery({
      running: false,
      sharing: found.status === "fulfilled" ? found.value : [],
      management: beacons.status === "fulfilled" ? (beacons.value?.servers ?? []) : [],
      // Only a search that found nothing because every way of looking failed is an error.
      error: failures.length === attempted ? asError(failures[0].reason) : undefined,
    });
  }, [serversAvailable]);

  return {
    status: sharing.status,
    sharingError: sharing.error,
    sharingLoading: sharing.loading,
    servers: servers.view,
    serversError: servers.error,
    serversLoading: servers.loading,
    access: access.settings,
    accessError: access.error,
    dataSync: dataSync.view,
    dataSyncError: dataSync.error,
    discovery,
    discover,
    reload,
    refreshAll,
  };
}
