import { create } from "zustand";

import { RemoteAccessMode } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

interface IRemoteAccessState {
  /** False until the first answer from the server arrives. */
  initialized: boolean;
  /**
   * Whether this browser is on the machine running Bakabase. Answered by the
   * server from the connection itself, not guessed from the URL — opening
   * `http://192.168.1.5:34567` on the host is still local, and a reverse proxy
   * would make any URL-based guess wrong anyway.
   */
  isLocal: boolean;
  mode: RemoteAccessMode;
  load: () => Promise<void>;
}

export const useRemoteAccessStore = create<IRemoteAccessState>((set) => ({
  initialized: false,
  // Assume local until told otherwise: the desktop app is the overwhelmingly
  // common case, and it must not flicker through a "remote" rendering on start.
  isLocal: true,
  mode: RemoteAccessMode.Disabled,
  load: async () => {
    try {
      const rsp = await BApi.remoteAccess.getRemoteAccessContext();
      const data = rsp.data;

      if (data) {
        set({
          initialized: true,
          isLocal: data.isLocal ?? true,
          mode: data.mode ?? RemoteAccessMode.Disabled,
        });
      }
    } catch {
      // An older backend, or a request that failed on a flaky LAN. Staying
      // local-by-default keeps the desktop app working; a genuinely remote
      // device would have been refused by the gate long before this point.
      set({ initialized: true });
    }
  },
}));

/**
 * True when the UI is being used from another device, so host-only actions
 * (launching a player, opening a folder in the file manager) would run on the
 * wrong machine and must be replaced or disabled.
 */
export const useIsRemoteClient = () =>
  useRemoteAccessStore((state) => state.initialized && !state.isLocal);
