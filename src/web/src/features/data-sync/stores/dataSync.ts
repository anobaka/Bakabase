import type { components } from "@/sdk/BApi2";

import { create } from "zustand";

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

interface DataSyncState {
  /** The last `/data-sync/overview` this window read; undefined until one has been read. */
  overview?: DataSyncOverview;
  /** The status line: the overview's first, then every hub `DataSyncStatus` push. */
  status?: DataSyncStatusView;
  /** The latest `DataSyncApplied` push; a new object on every push. */
  lastApplied?: DataSyncAppliedEvent;
  setOverview: (overview: DataSyncOverview) => void;
  setStatus: (status: DataSyncStatusView) => void;
  setApplied: (event: DataSyncAppliedEvent) => void;
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
  setOverview: (overview) => set({ overview, status: overview.status }),
  setStatus: (status) => set({ status }),
  setApplied: (event) => set({ lastApplied: event }),
  clear: () => set({ overview: undefined, status: undefined, lastApplied: undefined }),
}));
