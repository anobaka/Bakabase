import type { DataSyncMapView, DataSyncPeerCandidate } from "../api";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import { dataSyncApi, isRefusedHere } from "../api";
import { useDataSyncStore } from "../stores/dataSync";
import { isLive, syncPeersOf, withCandidates } from "../viewModels";

/** How often the page reads again while something waits on someone, and otherwise. */
export const LIVE_POLL_MS = 5_000;
export const IDLE_POLL_MS = 15_000;

const asError = (cause: unknown) => (cause instanceof Error ? cause : new Error(String(cause)));

/**
 * Everything the data sync page shows: the overview, and the map view's one record per device —
 * its link, its grant to read this device, this device's own request to it — with the requests
 * other devices filed. Read again every 5 s while a request, a link waiting for access or review,
 * or a task is live, and every 15 s otherwise — only while the page is visible. A 403 `HostOnly`
 * on the overview means this window may not use data sync: `refused`, and nothing more is asked.
 */
export function useDataSyncPageData() {
  const overview = useDataSyncStore((state) => state.overview);
  const overviewError = useDataSyncStore((state) => state.error);
  const reach = useDataSyncStore((state) => state.reach);
  const load = useDataSyncStore((state) => state.load);
  const [map, setMap] = useState<DataSyncMapView>();
  const [error, setError] = useState<Error>();
  const [candidates, setCandidates] = useState<DataSyncPeerCandidate[]>();
  const [version, setVersion] = useState(0);
  const [loaded, setLoaded] = useState(false);
  const mounted = useRef(true);
  const refused = reach === "refused";

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
    };
  }, []);

  /** Reads everything again. A failure leaves what was read last, with the error beside it. */
  const readAll = useCallback(async () => {
    await Promise.all([
      load(),
      dataSyncApi.map().then(
        (value) => {
          if (!mounted.current) return;
          setMap(value);
          setError(undefined);
        },
        // Refused by the gate: the page says so once, from the overview.
        (cause) => mounted.current && !isRefusedHere(cause) && setError(asError(cause)),
      ),
      dataSyncApi.peers(false).then(
        (value) => mounted.current && setCandidates(value),
        () => undefined,
      ),
    ]);
    if (mounted.current) setLoaded(true);
  }, [load]);

  /**
   * What an action asks for once it is done: everything read again, and `version` moved on so
   * what reads further on its own (the definitions list) reads again too. The periodic reads do
   * not move it.
   */
  const reload = useCallback(async () => {
    await readAll();
    if (mounted.current) setVersion((current) => current + 1);
  }, [readAll]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const peers = useMemo(
    () => withCandidates(syncPeersOf(map?.peers), candidates),
    [map, candidates],
  );
  const live = isLive({
    peers,
    pendingRequests: map?.requests.length ?? overview?.pendingRequests ?? 0,
    activeTaskId: overview?.activeTaskId,
  });

  useEffect(() => {
    if (refused) return;
    const timer = setInterval(
      () => {
        if (typeof document === "undefined" || !document.hidden) void readAll();
      },
      live ? LIVE_POLL_MS : IDLE_POLL_MS,
    );

    return () => clearInterval(timer);
  }, [live, refused, readAll]);

  return {
    overview,
    overviewError,
    refused,
    loaded,
    map,
    error,
    candidates,
    peers,
    version,
    reload,
  };
}

export type DataSyncPageData = ReturnType<typeof useDataSyncPageData>;
