import type { Confirmation, PanelActions } from "@/features/federation/map/usePanelActions";

/** What an action asks to be read again once it is done. */
export type DataSyncRefresh = "dataSync" | "sharing";

export type DataSyncConfirmation = Confirmation;

/**
 * What data sync's sections need from whoever shows them: the `/data-sync` page, or the
 * device map's details. Declared structurally, with method syntax, so the map's own
 * `PanelActions` fits it as it is (spec §11.1): the sections import nothing from the map.
 *
 * Every action goes through {@link run} or {@link confirm} — never through a busy state or a
 * dialog of the section's own — so one action runs at a time, focus is taken where the host
 * takes it, and a failure shows where the host shows failures.
 */
export interface DataSyncPanelActions {
  busy: boolean;
  mounted: { readonly current: boolean };
  /** Held by the host: survives the record it was about turning into another, or going away. */
  setNotice(value?: string): void;
  run(operation: () => Promise<unknown>, refresh: DataSyncRefresh[]): Promise<boolean>;
  confirm(confirmation: DataSyncConfirmation): void;
}

/**
 * What a dialog opened from a section needs as well: to say its own failures itself, rather
 * than where the host says them. Method syntax again, so the map's `PanelActions` fits.
 */
export interface DataSyncDialogActions extends DataSyncPanelActions {
  run(
    operation: () => Promise<unknown>,
    refresh: DataSyncRefresh[],
    onError?: (cause: Error) => void,
  ): Promise<boolean>;
}

/** Data sync's hosts run their actions with the device map's own hook, unchanged. */
export { usePanelActions as useDataSyncActions } from "@/features/federation/map/usePanelActions";

export type DataSyncActions = PanelActions;
