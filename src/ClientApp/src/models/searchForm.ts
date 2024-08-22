export default {
  state: {
    orders: [],
    // CategoryId - Ids
    mediaLibraryIdGroups: {},
    addDts: [],
    releaseDts: [],
    fileCreateDts: [],
    fileModifyDts: [],
  },

  // 定义改变该模型状态的纯函数
  reducers: {
    patch(prevState, payload) {
      const model = {
        ...prevState,
        ...payload,
      };
      console.log('patching search form', { ...prevState }, 'with', { ...payload });
      console.log('search form after patching', model);
      return model;
    },
    replace(prevState, payload) {
      console.log('replacing search form with', payload);
      return payload;
    },
  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({
    // async getUserInfo() {
    //   await delay(1000);
    //   dispatch.user.update({
    //     name: 'taobao',
    //     id: '123',
    //   });
    // },
  }),
};
