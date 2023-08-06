import type { ResourceTaskOperationOnComplete } from '@/sdk/constants';
import type { ResourceTaskType } from '@/sdk/constants';
import type ResourceTask from '@/core/models/ResourceTask';

export default {
  state: [] as ResourceTask[],

  // 定义改变该模型状态的纯函数
  reducers: {
    // setState: (prevState, tasks) => tasks,
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
