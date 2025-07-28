import { create } from "zustand";

interface IProgress {
  source: string;
  target: string;
  percentage: number;
  error?: string;
  moving: boolean;
}

interface FileMovingProgressesState {
  progresses: Record<string, IProgress>;
  setProgresses: (progresses: Record<string, IProgress>) => void;
  updateProgress: (progress: IProgress) => void;
}

export const useFileMovingProgressesStore = create<FileMovingProgressesState>(
  (set, get) => ({
    progresses: {},
    setProgresses: (progresses) => set({ progresses }),
    updateProgress: (progress) =>
      set((state) => {
        const newProgresses = {
          ...state.progresses,
          [progress.source]: progress,
        };

        return { progresses: newProgresses };
      }),
  }),
);
