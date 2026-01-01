import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { create } from "zustand";

interface PathMarksState {
  marks: Map<number, BakabaseAbstractionsModelsDomainPathMark>;
  updateMark: (mark: BakabaseAbstractionsModelsDomainPathMark) => void;
  setMarks: (marks: BakabaseAbstractionsModelsDomainPathMark[]) => void;
  getMark: (id: number) => BakabaseAbstractionsModelsDomainPathMark | undefined;
  removeMark: (id: number) => void;
}

export const usePathMarksStore = create<PathMarksState>((set, get) => ({
  marks: new Map(),
  updateMark: (mark) =>
    set((state) => {
      if (mark.id == null) return state;
      const newMarks = new Map(state.marks);
      newMarks.set(mark.id, mark);
      return { marks: newMarks };
    }),
  setMarks: (marks) =>
    set(() => {
      const newMarks = new Map<number, BakabaseAbstractionsModelsDomainPathMark>();
      for (const mark of marks) {
        if (mark.id != null) {
          newMarks.set(mark.id, mark);
        }
      }
      return { marks: newMarks };
    }),
  getMark: (id) => get().marks.get(id),
  removeMark: (id) =>
    set((state) => {
      const newMarks = new Map(state.marks);
      newMarks.delete(id);
      return { marks: newMarks };
    }),
}));

// Selector to get a mark by id with automatic re-render on change
export const selectMarkById = (id: number) => (state: PathMarksState) =>
  state.marks.get(id);
