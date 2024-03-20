import type { ResourceProperty, SearchOperation, StandardValueType } from '@/sdk/constants';

export default {
  state: {

  } as {
    resource: {
      reservedResourcePropertyAndValueTypeMap: Record<ResourceProperty, StandardValueType>;
      standardValueSearchOperationsMap: Record<StandardValueType, SearchOperation[]>;
    };
  },

  // 定义改变该模型状态的纯函数
  reducers: {

  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({
  }),
};
