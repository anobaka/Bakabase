import type { UpdaterStatus } from "@/sdk/constants";

import { create } from "zustand";

interface AppUpdaterState {
  status?: UpdaterStatus;
  percentage?: number;
  error?: string;
  update: (payload: Partial<Omit<AppUpdaterState, "update">>) => void;
}

export const useAppUpdaterStateStore = create<AppUpdaterState>((set, get) => ({
  status: undefined,
  percentage: undefined,
  error: undefined,
  update: (payload) => set((state) => ({ ...state, ...payload })),
}));
