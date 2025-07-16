import { create } from "zustand";

import BApi, { BApi as BApiType } from "@/sdk/BApi";

interface IAppContext {
  listeningAddresses: string[];
  apiEndpoint?: string;
  apiEndpoints?: string[];
  bApi2: BApiType | null;
  update: (
    payload: Partial<IAppContext> & { serverAddresses?: string[] },
  ) => void;
}

export const useAppContextStore = create<IAppContext>((set, get) => ({
  listeningAddresses: [],
  bApi2: null,
  update: (payload) => {
    let bApi2: BApiType;
    const addresses = payload.serverAddresses ?? [];

    if (addresses.length > 1) {
      bApi2 = new BApiType(addresses[1]);
    } else {
      // 延迟导入 BApi 以避免循环依赖
      bApi2 = BApi;
    }
    set((state) => ({
      ...state,
      ...payload,
      bApi2,
    }));
  },
}));
