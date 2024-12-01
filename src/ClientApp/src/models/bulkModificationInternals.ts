import BApi, { BApi as BApiType } from '@/sdk/BApi';

type BulkModificationInternals = {
  disabledPropertyKeys: Record<number, number[]>;
};
export default {
  state: {
    disabledPropertyKeys: {},
  } as BulkModificationInternals,

  // 定义改变该模型状态的纯函数
  reducers: {
    update(state, payload) {
      return {
        ...state,
        ...payload,
      };
    },
  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({
  }),
};
