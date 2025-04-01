import { createModel } from 'ice';
import _ from 'lodash';
import type { BTask } from '@/core/models/BTask';

export default createModel(
  {
    state: [] as BTask[],

    // 定义改变该模型状态的纯函数
    reducers: {
      setState: (prevState, tasks) => {
        console.log('background tasks changed', tasks);
        return _.sortBy(tasks, x => x.startedAt ?? '2099');
      },
      update: (prevState, task) => {
        const idx = prevState.findIndex((t) => t.id == task.id);
        if (idx > -1) {
          prevState[idx] = task;
        } else {
          prevState.push(task);
        }
        return _.sortBy(prevState, x => x.startedAt ?? '2099');
      },
    },

    // 定义处理该模型副作用的函数
    effects: (dispatch) => ({
    }),
  },
);
