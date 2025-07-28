import type IwFsEntryChangeEvent from "@/core/models/FileExplorer/IwFsEntryChangeEvent";

import { create } from "zustand";

interface IwFsEntryChangeEventsState {
  events: IwFsEntryChangeEvent[];
  addRange: (current: IwFsEntryChangeEvent[]) => void;
  clear: () => void;
}

export const useIwFsEntryChangeEventsStore = create<IwFsEntryChangeEventsState>(
  (set, get) => ({
    events: [{ path: "123" }] as IwFsEntryChangeEvent[],
    addRange: (current) =>
      set((state) => {
        // console.log(state.events, current);
        const newEvents = [...state.events, ...current];

        console.log("receiving new events", current, newEvents);

        return { events: newEvents };
      }),
    clear: () => {
      console.log("clearing events");
      set({ events: [] });
    },
  }),
);
