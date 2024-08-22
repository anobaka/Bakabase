import type { IwFsEntryTask } from '@/core/models/FileExplorer/Entry';

export default {
  state: [] as IwFsEntryTask[],

  // 定义改变该模型状态的纯函数
  reducers: {
    // setState: (prevState, tasks) => tasks,
    remove: (prevState, path) => {
      console.log('remove');
      return prevState.filter((t) => t.path != path);
    },
    update: (prevState, task) => {
      console.log('update');
      const idx = prevState.findIndex((t) => t.path == task.path);
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
