import { create } from "zustand";

interface LegacyInstallNoticePayload {
  path: string;
}

interface ILegacyInstallNoticeState {
  notice: LegacyInstallNoticePayload | null;
  set: (payload: LegacyInstallNoticePayload | null) => void;
  clear: () => void;
}

/**
 * Mirrors the server's <c>LegacyInstallAppDataDetected</c> hub push. Set on startup if
 * <c>&lt;install&gt;/current/AppData</c> is still populated. Cleared by the user dismissing.
 */
export const useLegacyInstallNoticeStore = create<ILegacyInstallNoticeState>((set) => ({
  notice: null,
  set: (payload) => set({ notice: payload }),
  clear: () => set({ notice: null }),
}));
