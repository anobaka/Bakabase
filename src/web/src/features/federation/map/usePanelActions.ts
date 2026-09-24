import type { MapSource } from "./useDeviceMapData";

import { useEffect, useRef, useState } from "react";

import { FederationError } from "../transport";

export interface Confirmation {
  title: string;
  description: string;
  warning?: string;
  action: () => Promise<unknown>;
  /** What to re-read once it is done. */
  refresh: MapSource[];
}

/**
 * What an action said when it succeeded, held by the page rather than the panel: an action
 * can turn the record the panel shows into another (a request into a device, a device found
 * nearby into a request) or take it off the map, and what it said must outlive that.
 */
export interface NoticeState {
  value?: string;
  set: (value?: string) => void;
}

/** Told when an action begins and when it is over, listings re-read and all. */
export interface ActionHooks {
  /**
   * Before anything else happens — before its button is disabled while it runs, which moves
   * the browser's focus off that button at once.
   */
  onStart?: () => void;
  /** Once it is over, whether it succeeded or not, and even when the panel is gone by then. */
  onEnd?: () => void;
}

/**
 * The panel's actions, the way every multi-device section runs its own: one at a time, the
 * affected listings re-read afterwards, a failure shown next to what failed — in the
 * confirmation dialog when there is one, so it stays with the decision just made.
 */
export function usePanelActions(
  onChanged: (sources: MapSource[]) => Promise<unknown> | void,
  held?: NoticeState,
  hooks?: ActionHooks,
) {
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const mounted = useRef(true);
  const [error, setError] = useState<Error>();
  const [ownNotice, setOwnNotice] = useState<string>();
  const notice = held ? held.value : ownNotice;
  const setNotice = held ? held.set : setOwnNotice;
  const [confirmation, setConfirmation] = useState<Confirmation>();
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
    refresh: MapSource[],
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
      if (mounted.current) {
        onError(cause instanceof Error ? cause : new Error(String(cause)));
        // Mappings changed elsewhere: show the current ones to decide against.
        if (cause instanceof FederationError && cause.code === "PathMappingsChanged")
          await latest.current(["sharing"]);
      }

      return false;
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
      onEnd?.();
    }
  };

  const confirm = (next: Confirmation) => {
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

export type PanelActions = ReturnType<typeof usePanelActions>;
