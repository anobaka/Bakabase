import { createModel } from 'ice';
import type { DependentComponentStatus } from '@/sdk/constants';

interface IProgress {
  source: string;
  target: string;
  percentage: number;
  error?: string;
  moving: boolean;
}

export default createModel(
  {
    state: {} as Record<string, IProgress>,

    // 定义改变该模型状态的纯函数
    reducers: {
      setState: (prevState, progresses) => {
        return progresses;
      },
      update: (prevState, progress) => {
        prevState[progress.source] = progress;
        return prevState;
      },
    },

    // 定义处理该模型副作用的函数
    effects: (dispatch) => ({}),
  },
);
