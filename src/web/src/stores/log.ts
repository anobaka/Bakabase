import { create } from "zustand";

interface LogState {
  logs: any[];
  update: (current: any[]) => void;
}

export const useLogStore = create<LogState>((set, get) => ({
  logs: [],
  update: (current) => {
    // Implement your update logic here if needed
    set({ logs: current });
  },
}));
