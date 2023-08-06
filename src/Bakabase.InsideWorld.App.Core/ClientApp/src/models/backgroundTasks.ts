import { createModel } from "ice";

export default createModel(
  {
    state: [],

    // 定义改变该模型状态的纯函数
    reducers: {
      setState: (prevState, tasks) => {
        console.log('background tasks changed', tasks)
        return tasks.slice().sort((a, b) => b.startDt.localeCompare(a.startDt));
      },
      update: (prevState, task) => {
        const idx = prevState.findIndex((t) => t.id == task.id);
        if (idx > -1) {
          prevState[idx] = task;
        } else {
          prevState.push(task);
        }
        return prevState.sort((a, b) => b.startDt.localeCompare(a.startDt));
      },
    },

    // 定义处理该模型副作用的函数
    effects: (dispatch) => ({
    }),
  }
)
