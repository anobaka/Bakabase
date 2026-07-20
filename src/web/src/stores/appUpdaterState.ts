import { UpdaterStatus } from "@/sdk/constants";

import { create } from "zustand";

interface AppUpdaterState {
  status?: UpdaterStatus;
  percentage?: number;
  error?: string;
  // Whether the user dismissed the failed-update banner for the current run.
  // In-memory only: a page reload / app restart resets it back to false.
  failureDismissed: boolean;
  update: (payload: Partial<Omit<AppUpdaterState, "update" | "dismissFailure">>) => void;
  dismissFailure: () => void;
}

export const useAppUpdaterStateStore = create<AppUpdaterState>((set, get) => ({
  status: undefined,
  percentage: undefined,
  error: undefined,
  failureDismissed: false,
  update: (payload) =>
    set((state) => {
      // A fresh, non-failed status means a new update cycle started, so a
      // previously dismissed failure should surface again if it fails anew.
      const failureDismissed =
        payload.status !== undefined && payload.status !== UpdaterStatus.Failed
          ? false
          : state.failureDismissed;

      return { ...state, ...payload, failureDismissed };
    }),
  dismissFailure: () => set({ failureDismissed: true }),
}));
