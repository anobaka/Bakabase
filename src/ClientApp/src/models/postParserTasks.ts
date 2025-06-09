
import _ from 'lodash';
import type { BTask } from '@/core/models/BTask';
import type { PostParserTask } from '@/core/models/PostParserTask';

export default {
  state: [] as PostParserTask[],

  // 定义改变该模型状态的纯函数
  reducers: {
    setState: (prevState, tasks: BTask[]) => {
      return _.sortBy(tasks, x => x.createdAt);
    },
    update: (prevState: PostParserTask[], task: PostParserTask) => {
      const idx = prevState.findIndex((t) => t.id == task.id);
      let newState = prevState.slice();
      if (idx > -1) {
        newState[idx] = task;
      } else {
        newState.push(task);
        newState = _.sortBy(newState, x => x.id);
      }
      return newState;
    },
    delete: (prevState: PostParserTask[], taskId: number) => prevState.filter((t) => t.id !== taskId),
    deleteAll: (prevState: PostParserTask[]) => [] as PostParserTask[],
  },

  // 定义处理该模型副作用的函数
  effects: (dispatch) => ({
  }),
};
