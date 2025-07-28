import type { DependentComponentStatus } from "@/sdk/constants";

import { create } from "zustand";

interface IContext {
  id: string;
  name: string;
  description?: string;
  defaultLocation: string;
  status: DependentComponentStatus;
  isRequired: boolean;
  installationProgress: number;
  location?: string;
  version?: string;
  error?: string;
}

interface DependentComponentContextsState {
  contexts: IContext[];
  setContexts: (contexts: IContext[]) => void;
  updateContext: (context: IContext) => void;
}

export const useDependentComponentContextsStore =
  create<DependentComponentContextsState>((set, get) => ({
    contexts: [],
    setContexts: (contexts) => {
      console.log("dependent component context changed", contexts);
      set({
        contexts: contexts.slice().sort((a, b) => a.id.localeCompare(b.id)),
      });
    },
    updateContext: (context) =>
      set((state) => {
        const idx = state.contexts.findIndex((t) => t.id === context.id);
        let newContexts = state.contexts.slice();

        if (idx > -1) {
          newContexts[idx] = context;
        } else {
          newContexts.push(context);
        }

        return { contexts: newContexts };
      }),
  }));
