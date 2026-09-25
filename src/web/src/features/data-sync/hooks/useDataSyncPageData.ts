import type {
  DataSyncAccessRequestView,
  DataSyncLinkView,
  DataSyncMapView,
  DataSyncPeerCandidate,
  DataSyncReaderView,
} from "../api";
import type { DataSyncRefresh } from "./useDataSyncActions";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import { dataSyncApi, isRefusedHere } from "../api";
import { useDataSyncStore } from "../stores/dataSync";
import { isLive, syncPeersOf, withCandidates } from "../viewModels";

/** How often the page reads again while something waits on someone, and otherwise. */
export const LIVE_POLL_MS = 5_000;
export const IDLE_POLL_MS = 15_000;

interface Source<T> {
  value?: T;
  error?: Error;
}

const asError = (cause: unknown) => (cause instanceof Error ? cause : new Error(String(cause)));

/**
 * Everything the data sync page shows, each source read on its own: one that fails leaves the
 * others on the page, with its error next to what it would have shown. Read again every 5 s
 * while a request, a link waiting for access or review, or a task is live, and every 15 s
 * otherwise — only while the page is visible. A 403 `HostOnly` on the overview means this
 * window may not use data sync: `refused`, and nothing more is asked.
 */
export function useDataSyncPageData() {
  const overview = useDataSyncStore((state) => state.overview);
  const overviewError = useDataSyncStore((state) => state.error);
  const reach = useDataSyncStore((state) => state.reach);
  const load = useDataSyncStore((state) => state.load);
  const [links, setLinks] = useState<Source<DataSyncLinkView[]>>({});
  const [map, setMap] = useState<Source<DataSyncMapView>>({});
  const [requests, setRequests] = useState<Source<DataSyncAccessRequestView[]>>({});
  const [readers, setReaders] = useState<Source<DataSyncReaderView[]>>({});
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

  /** Reads every source again: they all read the same server state. */
  const readAll = useCallback(async () => {
    const read = <T>(
      request: () => Promise<T>,
      set: (update: (current: Source<T>) => Source<T>) => void,
    ) =>
      request().then(
        (value) => mounted.current && set(() => ({ value })),
        (cause) => {
          // Refused by the gate: the page says so once, from the overview. Anything else is
          // said next to what it would have shown, which stays as it was last read.
          if (mounted.current && !isRefusedHere(cause))
            set((current) => ({ value: current.value, error: asError(cause) }));
        },
      );

    await Promise.all([
      load(),
      read(dataSyncApi.links, setLinks),
      read(dataSyncApi.map, setMap),
      read(dataSyncApi.requests, setRequests),
      read(dataSyncApi.readers, setReaders),
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
  const reload = useCallback(
    async (_sources?: DataSyncRefresh[]) => {
      await readAll();
      if (mounted.current) setVersion((current) => current + 1);
    },
    [readAll],
  );

  useEffect(() => {
    void reload();
  }, [reload]);

  const peers = useMemo(
    () =>
      withCandidates(
        syncPeersOf({ links: links.value, map: map.value, readers: readers.value }),
        candidates,
      ),
    [links.value, map.value, readers.value, candidates],
  );
  const live = isLive({
    peers,
    pendingRequests: requests.value?.length ?? overview?.pendingRequests ?? 0,
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
    links,
    map,
    requests,
    readers,
    candidates,
    peers,
    version,
    reload,
  };
}

export type DataSyncPageData = ReturnType<typeof useDataSyncPageData>;
