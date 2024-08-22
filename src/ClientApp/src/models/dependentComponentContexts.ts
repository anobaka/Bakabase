import { createModel } from 'ice';
import type { DependentComponentStatus } from '@/sdk/constants';

interface IContext {
  id: string;
  name: string;
  description?: string;
  defaultLocation: string;
  status: DependentComponentStatus;

  installationProgress: number;
  location?: string;
  version?: string;
  error?: string;
}

export default createModel(
  {
    state: [] as IContext[],

    // 定义改变该模型状态的纯函数
    reducers: {
      setState: (prevState, contexts) => {
        console.log('dependent component context changed', contexts);
        return contexts.slice().sort((a, b) => a.id.localeCompare(b.id));
      },
      update: (prevState, context) => {
        const idx = prevState.findIndex((t) => t.id == context.id);
        if (idx > -1) {
          prevState[idx] = context;
        } else {
          prevState.push(context);
        }
        return prevState;
      },
    },

    // 定义处理该模型副作用的函数
    effects: (dispatch) => ({}),
  },
);
