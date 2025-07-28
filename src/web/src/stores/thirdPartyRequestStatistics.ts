import type { components } from "@/sdk/BApi2";

import { create } from "zustand";

type ThirdPartyRequestStatistics =
  components["schemas"]["Bakabase.InsideWorld.Models.Models.Aos.ThirdPartyRequestStatistics"];

interface ThirdPartyRequestStatisticsState {
  statistics: ThirdPartyRequestStatistics[];
  setStatistics: (statistics: ThirdPartyRequestStatistics[]) => void;
  updateStatistics: (statistics: ThirdPartyRequestStatistics[]) => void;
}

export const useThirdPartyRequestStatisticsStore =
  create<ThirdPartyRequestStatisticsState>((set) => ({
    statistics: [],
    setStatistics: (statistics) => set({ statistics: statistics.slice() }),
    updateStatistics: (statistics) => set({ statistics: statistics.slice() }),
  }));
