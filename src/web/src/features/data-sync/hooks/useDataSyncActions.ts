import { useEffect, useRef, useState } from "react";

/** What an action asks to be read again once it is done. */
export type DataSyncRefresh = "dataSync" | "sharing";

export interface DataSyncConfirmation {
  title: string;
  description: string;
  warning?: string;
  action: () => Promise<unknown>;
  refresh: DataSyncRefresh[];
}

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

/** What an action said, held by the page rather than by the section that ran it. */
export interface DataSyncNoticeState {
  value?: string;
  set: (value?: string) => void;
}

/** Told when an action begins and when it is over, listings re-read and all. */
export interface DataSyncActionHooks {
  /** Before its control is disabled while it runs, which moves the browser's focus off it. */
  onStart?: () => void;
  /** Once it is over, whether it succeeded or not, and even when the section is gone by then. */
  onEnd?: () => void;
}

/**
 * Data sync's own copy of the device map's action contract (`usePanelActions`): one action at
 * a time, the affected listings re-read afterwards, a failure shown next to what failed — in
 * the confirmation when there is one, so it stays with the decision just made.
 */
export function useDataSyncActions(
  onChanged: (sources: DataSyncRefresh[]) => Promise<unknown> | void,
  held?: DataSyncNoticeState,
  hooks?: DataSyncActionHooks,
) {
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const mounted = useRef(true);
  const [error, setError] = useState<Error>();
  const [ownNotice, setOwnNotice] = useState<string>();
  const notice = held ? held.value : ownNotice;
  const setNotice = held ? held.set : setOwnNotice;
  const [confirmation, setConfirmation] = useState<DataSyncConfirmation>();
  const [confirmationError, setConfirmationError] = useState<Error>();
  const latest = useRef(onChanged);
  const latestHooks = useRef(hooks);

  latest.current = onChanged;
  latestHooks.current = hooks;

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
    };
  }, []);

  const run = async (
    operation: () => Promise<unknown>,
    refresh: DataSyncRefresh[],
    onError: (cause: Error) => void = setError,
  ) => {
    if (busyRef.current) return false;
    busyRef.current = true;
    const { onEnd } = latestHooks.current ?? {};

    latestHooks.current?.onStart?.();
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    setConfirmationError(undefined);
    try {
      await operation();
      if (mounted.current && refresh.length) await latest.current(refresh);

      return true;
    } catch (cause) {
      if (mounted.current) onError(cause instanceof Error ? cause : new Error(String(cause)));

      return false;
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
      onEnd?.();
    }
  };

  const confirm = (next: DataSyncConfirmation) => {
    setConfirmationError(undefined);
    setConfirmation(next);
  };

  const confirmCurrent = () => {
    if (!confirmation) return;
    const { action, refresh } = confirmation;

    void run(
      async () => {
        await action();
        if (mounted.current) setConfirmation(undefined);
      },
      refresh,
      setConfirmationError,
    );
  };

  const cancelConfirmation = () => {
    setConfirmation(undefined);
    setConfirmationError(undefined);
  };

  /** Clears what the last device's actions said, when another device is shown. */
  const reset = () => {
    setError(undefined);
    setNotice(undefined);
  };

  return {
    busy,
    error,
    setError,
    notice,
    setNotice,
    confirmation,
    confirmationError,
    run,
    confirm,
    confirmCurrent,
    cancelConfirmation,
    reset,
    mounted,
  };
}

export type DataSyncActions = ReturnType<typeof useDataSyncActions>;
