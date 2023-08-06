export default {
  state: {},

  // 定义改变该模型状态的纯函数
  reducers: {
    add: (prevState, icons) => {
      Object.keys(icons).forEach((e) => {
        prevState[e] = icons[e];
      });
    },
  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({
  }),
};
