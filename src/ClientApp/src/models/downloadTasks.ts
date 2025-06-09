import type { DownloadTask } from '@/core/models/DownloadTask';

export default {
  state: [] as DownloadTask[],

  // 定义改变该模型状态的纯函数
  reducers: {
    setState: (prevState, tasks) => tasks.slice(),
    update: (prevState, task) => {
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
