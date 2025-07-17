import { create } from "zustand";

import BApi, { BApi as BApiType } from "@/sdk/BApi";

interface IAppContext {
  listeningAddresses: string[];
  apiEndpoint?: string;
  apiEndpoints?: string[];
  bApi2: BApiType;
  update: (
    payload: Partial<IAppContext> & { serverAddresses?: string[] },
  ) => void;
}

export const useAppContextStore = create<IAppContext>((set, get) => ({
  listeningAddresses: [],
  bApi2: BApi,
  update: (payload) => {
    let bApi2: BApiType;
    const addresses = payload.serverAddresses ?? [];

    if (addresses.length > 1) {
      bApi2 = new BApiType(addresses[1]);
    } else {
      bApi2 = BApi;
    }
    set((state) => ({
      ...state,
      ...payload,
      bApi2,
    }));
  },
}));
