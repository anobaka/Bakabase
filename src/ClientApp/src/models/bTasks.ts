import type { BTask } from '@/core/models/BTask';

export default {
  state: [] as BTask[],

  // 定义改变该模型状态的纯函数
  reducers: {
    setState: (prevState, tasks) => tasks.slice(),
    remove: (prevState, id) => {
      console.log('remove');
      return prevState.filter((t) => t.id != id);
    },
    update: (prevState, task) => {
      console.log('update');
      const idx = prevState.findIndex((t) => t.id == task.id);
      if (idx > -1) {
        prevState[idx] = task;
      } else {
        prevState.push(task);
      }
      return prevState;
    },
  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({
  }),
};
