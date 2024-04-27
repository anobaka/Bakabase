import type { ResourceProperty, SearchOperation, StandardValueType } from '@/sdk/constants';
import type { SignalRData } from '@/components/SignalR/models';

interface Options {
  resource: {
    reservedResourcePropertyAndValueTypeMap: Record<ResourceProperty, StandardValueType>;
    customPropertyValueSearchOperationsMap: Record<StandardValueType, SearchOperation[]>;
  };
}
export default {
  state: {} as SignalRData<Options>,

  // 定义改变该模型状态的纯函数
  reducers: {
    update(state, payload) {
      console.log(state, payload);
      return {
        ...state,
        ...payload,
        initialized: true,
      };
    },
  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({

  }),
};
