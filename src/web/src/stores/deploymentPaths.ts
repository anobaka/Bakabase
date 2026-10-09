import { create } from "zustand";

import BApi from "@/sdk/BApi";

type DeploymentPaths = NonNullable<Awaited<ReturnType<typeof BApi.app.getDeploymentPaths>>["data"]>;

interface DeploymentPathsState {
  data?: DeploymentPaths;
  loaded: boolean;
  loading: boolean;
  load: () => Promise<void>;
}

/** Display metadata belongs to this server origin and remains fixed until its container restarts. */
export const useDeploymentPathsStore = create<DeploymentPathsState>((set, get) => ({
  loaded: false,
  loading: false,
  load: async () => {
    if (get().loaded || get().loading) return;
    set({ loading: true });
    try {
      const response = await BApi.app.getDeploymentPaths({ showErrorToast: false });

      set({
        data: response.code ? undefined : (response.data ?? undefined),
        loaded: !response.code && !!response.data,
      });
    } catch {
      // Older servers and unavailable metadata still have a valid server-side path to show.
      set({ data: undefined });
    } finally {
      set({ loading: false });
    }
  },
}));
