import type { UpdaterStatus } from '@/sdk/constants';

export default {
  state: {

  } as {
    status: UpdaterStatus;
    percentage: number;
    error?: string;
  },

  // 定义改变该模型状态的纯函数
  reducers: {

  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({
  }),
};
