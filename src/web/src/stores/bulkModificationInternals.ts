import type { StandardValueType } from "@/sdk/constants";

import { create } from "zustand";

type BulkModificationInternals = {
  disabledPropertyKeys: Record<number, number[]>;
  supportedStandardValueTypes: StandardValueType[];
  update: (payload: Partial<Omit<BulkModificationInternals, "update">>) => void;
};

export const useBulkModificationInternalsStore =
  create<BulkModificationInternals>((set, get) => ({
    disabledPropertyKeys: {},
    supportedStandardValueTypes: [],
    update: (payload) => set((state) => ({ ...state, ...payload })),
  }));
