import { create } from "zustand";

import { RuntimeMode } from "@/sdk/constants.ts";
import BApi from "@/sdk/BApi";
import { Api } from "@/sdk/Api";

interface IAppContext {
  listeningAddresses: string[];
  apiEndpoint?: string;
  apiEndpoints?: string[];
  bApi2: Api<unknown> | null;
  runtimeMode: RuntimeMode;
  update: (
    payload: Partial<IAppContext> & { serverAddresses?: string[] },
  ) => void;
}

export const useAppContextStore = create<IAppContext>((set, get) => ({
  listeningAddresses: [],
  bApi2: null,
  runtimeMode: RuntimeMode.Dev,
  update: (payload) => {
    let bApi2: Api<unknown>;
    const addresses = payload.serverAddresses ?? [];

    if (addresses.length > 1) {
      bApi2 = new Api({ baseUrl: addresses[1] });
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
