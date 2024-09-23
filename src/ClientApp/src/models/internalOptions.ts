import type { ResourceProperty, SearchOperation, StandardValueConversionRule, StandardValueType } from '@/sdk/constants';
import type { SignalRData } from '@/components/SignalR/models';
import type { IProperty } from '@/components/Property/models';

interface Options {
  resource: {
    // reservedResourcePropertyAndValueTypesMap: Record<ResourceProperty, IProperty>;
    // internalResourcePropertyAndValueTypesMap: Record<ResourceProperty, IProperty>;

    reservedResourcePropertyDescriptorMap: Record<ResourceProperty, IProperty>;
    internalResourcePropertyDescriptorMap: Record<ResourceProperty, IProperty>;
    customPropertyValueSearchOperationsMap: Record<StandardValueType, SearchOperation[]>;
  };
  standardValueConversionRuleMap: Record<StandardValueType, Record<StandardValueType, StandardValueConversionRule>>;
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
