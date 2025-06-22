import BApi, { BApi as BApiType } from '@/sdk/BApi';

interface IAppContext {
  listeningAddresses: string[];
  apiEndpoint?: string;
  apiEndpoints?: string[];
  bApi2: BApiType;
}
export default {
  state: {
    listeningAddresses: [],
    bApi2: BApi,
  } as IAppContext,

  // 定义改变该模型状态的纯函数
  reducers: {
    update(state, payload) {
      let bApi2: BApiType;
      const addresses = payload.serverAddresses ?? [];
      if (addresses.length > 1) {
        bApi2 = new BApiType(addresses[1]);
      } else {
        bApi2 = BApi;
      }

      return {
        ...state,
        ...payload,
        bApi2,
      };
    },
  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({
  }),
};
