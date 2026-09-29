"use client";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { create } from "zustand";

import { Spinner, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { UpdaterStatus } from "@/sdk/constants";

interface RestartState {
  restarting: boolean;
  setRestarting: (restarting: boolean) => void;
}

const useRestartState = create<RestartState>((set) => ({
  restarting: false,
  setRestarting: (restarting) => set({ restarting }),
}));

/** Keep every local update entry point in one state while the native app hands over. */
export const useUpdateRestart = () => {
  const { t } = useTranslation();
  const restarting = useRestartState((state) => state.restarting);

  const restart = useCallback(async () => {
    if (useRestartState.getState().restarting) return;

    useRestartState.getState().setRestarting(true);

    try {
      // The process can exit while the request is in flight. An expected handoff
      // should not raise the SDK's generic network-error toast.
      const response = await BApi.updater.restartAndUpdateApp({ showErrorToast: false });

      if (response.code !== 0) {
        throw new Error(response.message);
      }
      // The native updater takes over from here. Keep the waiting message visible
      // until this window closes instead of exposing an active restart button again.
    } catch (error) {
      useRestartState.getState().setRestarting(false);
      toast.danger({
        title: t<string>("appUpdate.restartFailed"),
        description: error instanceof Error ? error.message : undefined,
      });
    }
  }, [t]);

  return { restarting, restart };
};

/** A remote server restart leaves this browser window open while its connection drops. */
const REMOTE_RESTART_STATUS_TIMEOUT_MS = 180_000;

export const useRemoteServerUpdateRestart = (status?: UpdaterStatus) => {
  const { t } = useTranslation();
  const [restarting, setRestarting] = useState(false);
  const [timedOut, setTimedOut] = useState(false);

  useEffect(() => {
    if (status !== UpdaterStatus.PendingRestart) {
      setRestarting(false);
      setTimedOut(false);
    }
  }, [status]);

  // The remote server may disappear before another updater status arrives. Once the
  // ordinary update window has elapsed, restore controls and state the uncertainty.
  useEffect(() => {
    if (!restarting) return;

    const timer = window.setTimeout(() => {
      setRestarting(false);
      setTimedOut(true);
    }, REMOTE_RESTART_STATUS_TIMEOUT_MS);

    return () => window.clearTimeout(timer);
  }, [restarting]);

  const restart = useCallback(async () => {
    if (restarting) return;

    setRestarting(true);
    setTimedOut(false);

    try {
      const response = await BApi.updater.restartAndUpdateApp({ showErrorToast: false });

      if (response.code !== 0) throw new Error(response.message);
    } catch (error) {
      setRestarting(false);
      toast.danger({
        title: t<string>("appUpdate.restartFailed"),
        description: error instanceof Error ? error.message : undefined,
      });
    }
  }, [restarting, t]);

  return { restarting, timedOut, restart };
};

/** Shown immediately, before the native shell replaces the window with update progress. */
export const UpdateRestartOverlay: React.FC = () => {
  const { t } = useTranslation();
  const restarting = useRestartState((state) => state.restarting);

  if (!restarting) return null;

  return (
    <div
      aria-busy="true"
      aria-live="polite"
      className="fixed inset-0 z-[9999] flex items-center justify-center bg-background/90 px-6"
      role="status"
    >
      <div className="flex max-w-md flex-col items-center gap-4 rounded-xl border border-default-200 bg-content1 px-8 py-7 text-center shadow-xl">
        <Spinner size="lg" />
        <h2 className="text-lg font-semibold">{t<string>("appUpdate.restarting")}</h2>
        <p className="text-sm text-foreground-500">{t<string>("appUpdate.restarting.detail")}</p>
      </div>
    </div>
  );
};
