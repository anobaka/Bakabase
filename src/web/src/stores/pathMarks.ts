import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { create } from "zustand";

interface PathMarksState {
  marks: Map<number, BakabaseAbstractionsModelsDomainPathMark>;
  deletedMarkIds: Set<number>;
  updateMark: (mark: BakabaseAbstractionsModelsDomainPathMark) => void;
  setMarks: (marks: BakabaseAbstractionsModelsDomainPathMark[]) => void;
  getMark: (id: number) => BakabaseAbstractionsModelsDomainPathMark | undefined;
  removeMark: (id: number) => void;
}

export const usePathMarksStore = create<PathMarksState>((set, get) => ({
  marks: new Map(),
  deletedMarkIds: new Set(),
  updateMark: (mark) =>
    set((state) => {
      // A delayed push cannot recreate a row after its physical-delete notification.
      // A fresh full snapshot can introduce a new/restored row with the same id.
      if (mark.id == null || state.deletedMarkIds.has(mark.id)) return state;
      const newMarks = new Map(state.marks);

      newMarks.set(mark.id, mark);

      return { marks: newMarks };
    }),
  setMarks: (marks) =>
    set((state) => {
      const newMarks = new Map<number, BakabaseAbstractionsModelsDomainPathMark>();

      for (const mark of marks) {
        if (mark.id != null) {
          newMarks.set(mark.id, mark);
        }
      }

      const deletedMarkIds = new Set([...state.deletedMarkIds].filter((id) => !newMarks.has(id)));

      return { marks: newMarks, deletedMarkIds };
    }),
  getMark: (id) => get().marks.get(id),
  removeMark: (id) =>
    set((state) => {
      const newMarks = new Map(state.marks);

      newMarks.delete(id);
      const deletedMarkIds = new Set(state.deletedMarkIds);

      deletedMarkIds.add(id);

      return { marks: newMarks, deletedMarkIds };
    }),
}));

// Selector to get a mark by id with automatic re-render on change
export const selectMarkById = (id: number) => (state: PathMarksState) => state.marks.get(id);
