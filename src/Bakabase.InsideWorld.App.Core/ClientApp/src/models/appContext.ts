
interface IAppContext {
  serverAddresses: string[];
}
export default {
  state: {
    serverAddresses: [],
  } as IAppContext,

  // 定义改变该模型状态的纯函数
  reducers: {

  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({
  }),
};
