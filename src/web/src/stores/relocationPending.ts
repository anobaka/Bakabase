import { create } from "zustand";

interface RelocationPendingPayload {
  target: string;
  mode: "UseTarget" | "CopyToEmpty" | "OverwriteTarget";
  expectedFiles: number;
  expectedTotalBytes: number;
}

interface IRelocationPendingState {
  pending: RelocationPendingPayload | null;
  set: (payload: RelocationPendingPayload | null) => void;
  clear: () => void;
}

/**
 * Mirrors the server's RelocationPending hub push. AppInfo subscribes and renders
 * a non-dismissable "restart now" modal when set.
 */
export const useRelocationPendingStore = create<IRelocationPendingState>((set) => ({
  pending: null,
  set: (payload) => set({ pending: payload }),
  clear: () => set({ pending: null }),
}));
