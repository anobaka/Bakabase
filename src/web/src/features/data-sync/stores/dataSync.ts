import type { components } from "@/sdk/BApi2";

import { create } from "zustand";

import { dataSyncApi, isRefusedHere } from "../api";

type Schemas = components["schemas"];

export type DataSyncOverview = Schemas["Bakabase.Modules.DataSync.Services.DataSyncOverview"];
export type DataSyncStatusView = Schemas["Bakabase.Modules.DataSync.Services.DataSyncStatusView"];

/**
 * The hub's `DataSyncApplied` push: which definitions an apply has just written, so a page
 * showing them (properties, extension groups) reads them again. The hub sends it as a plain
 * object, so it has no SDK schema.
 */
export interface DataSyncAppliedEvent {
  /** Kind ids, e.g. `customProperty`, `extensionGroup`. */
  kinds: string[];
  /** Local keys of the definitions that were written. */
  localKeys: string[];
}

/** The keys the UI hub pushes data sync's state under (`GetIncrementalData(key, data)`). */
export const DATA_SYNC_HUB_KEYS = {
  status: "DataSyncStatus",
  applied: "DataSyncApplied",
} as const;

/**
 * Whether this window may read data sync at all, as the server said: `refused` once the
 * remote-access gate answered 403 `HostOnly` (a LAN browser on a server outside Unrestricted
 * mode). Until the first answer it is `unknown`.
 */
export type DataSyncReach = "unknown" | "allowed" | "refused";

interface DataSyncState {
  /** The last `/data-sync/overview` this window read; undefined until one has been read. */
  overview?: DataSyncOverview;
  /** The status line: the overview's first, then every hub `DataSyncStatus` push. */
  status?: DataSyncStatusView;
  /** The latest `DataSyncApplied` push; a new object on every push. */
  lastApplied?: DataSyncAppliedEvent;
  reach: DataSyncReach;
  /** The last overview read that failed for another reason than the gate; cleared by a success. */
  error?: Error;
  setOverview: (overview: DataSyncOverview) => void;
  setStatus: (status: DataSyncStatusView) => void;
  setApplied: (event: DataSyncAppliedEvent) => void;
  /** Reads the overview again. Never throws: what went wrong is kept in `reach` and `error`. */
  load: () => Promise<DataSyncOverview | undefined>;
  clear: () => void;
}

/**
 * What this window knows about data sync on the server it shows. Drives the status
 * indicator and the page; filled by the page's overview read and the UI hub.
 */
export const useDataSyncStore = create<DataSyncState>((set) => ({
  overview: undefined,
  status: undefined,
  lastApplied: undefined,
  reach: "unknown",
  error: undefined,
  setOverview: (overview) => set({ overview, status: overview.status, reach: "allowed" }),
  setStatus: (status) => set({ status }),
  setApplied: (event) => set({ lastApplied: event }),
  load: async () => {
    try {
      const overview = await dataSyncApi.overview();

      if (!overview) return undefined;
      set({ overview, status: overview.status, reach: "allowed", error: undefined });

      return overview;
    } catch (cause) {
      if (isRefusedHere(cause)) set({ reach: "refused", error: undefined });
      else set({ error: cause instanceof Error ? cause : new Error(String(cause)) });

      return undefined;
    }
  },
  clear: () =>
    set({
      overview: undefined,
      status: undefined,
      lastApplied: undefined,
      reach: "unknown",
      error: undefined,
    }),
}));

const isStatus = (data: unknown): data is DataSyncStatusView =>
  typeof data === "object" &&
  data !== null &&
  typeof (data as DataSyncStatusView).level === "number" &&
  typeof (data as DataSyncStatusView).openItems === "number";

const isApplied = (data: unknown): data is DataSyncAppliedEvent =>
  typeof data === "object" &&
  data !== null &&
  Array.isArray((data as DataSyncAppliedEvent).kinds) &&
  Array.isArray((data as DataSyncAppliedEvent).localKeys);

/**
 * Takes one UI hub push — `UIHubConnection`'s `GetIncrementalData` hands every one here first.
 * Answers whether it was data sync's, so the hub's dispatch can stop there. Anything malformed
 * is dropped: the next overview read corrects the store.
 */
export const applyDataSyncHubData = (key: string, data: unknown): boolean => {
  const store = useDataSyncStore.getState();

  if (key === DATA_SYNC_HUB_KEYS.status) {
    if (isStatus(data)) store.setStatus(data);

    return true;
  }
  if (key === DATA_SYNC_HUB_KEYS.applied) {
    if (isApplied(data)) store.setApplied({ kinds: data.kinds, localKeys: data.localKeys });

    return true;
  }

  return false;
};
