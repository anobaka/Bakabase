import _ from 'lodash';
import type { BTask } from '@/core/models/BTask';

export default {
  state: [] as BTask[],

  // 定义改变该模型状态的纯函数
  reducers: {
    setState: (prevState, tasks: BTask[]) => {
      return _.sortBy(tasks, x => x.createdAt);
    },
    remove: (prevState, id) => {
      return prevState.filter((t) => t.id != id);
    },
    update: (prevState: BTask[], task: BTask) => {
      const idx = prevState.findIndex((t) => t.id == task.id);
      let newState = prevState.slice();
      if (idx > -1) {
        newState[idx] = task;
      } else {
        newState.push(task);
        newState = _.sortBy(newState, x => x.createdAt);
      }
      return newState;
    },
  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({
  }),
};
