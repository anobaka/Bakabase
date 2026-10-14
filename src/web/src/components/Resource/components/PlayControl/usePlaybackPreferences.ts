import { useEffect } from "react";
import { create } from "zustand";

import {
  automaticPlaybackPreferences,
  normalizePlaybackPreferences,
  readBrowserPlaybackPreferences,
  PLAYBACK_PREFERENCE_KEY,
  type DevicePlaybackPreferences,
  type PlaybackKind,
  type PlaybackPreference,
} from "./playbackPreferences";

import BApi from "@/sdk/BApi";
import { useRemoteAccessStore, useUserSideActionsRunHere } from "@/stores/remoteAccess";
import { isDesktopAppWindow } from "@/utils/clientSurface";

type Scope = "browser" | "desktop";
let pending: { scope: Scope; promise: Promise<void> } | undefined;
let loadRevision = 0;
let saveTail = Promise.resolve();

export const useDevicePlaybackStore = create<{
  scope?: Scope;
  initialized: boolean;
  preferences: DevicePlaybackPreferences;
  load: (scope: Scope) => Promise<void>;
  save: (kind: PlaybackKind, preference: PlaybackPreference) => Promise<void>;
}>((set, get) => ({
  initialized: false,
  preferences: automaticPlaybackPreferences(),
  load: async (scope) => {
    if (get().initialized && get().scope === scope) return;
    if (pending?.scope === scope) return pending.promise;
    const revision = ++loadRevision;

    set({ scope, initialized: false });
    const promise = (async () => {
      let preferences = automaticPlaybackPreferences();

      if (scope === "browser") preferences = readBrowserPlaybackPreferences();
      else {
        try {
          const response = await BApi.player.getDevicePlaybackPreferences({
            showErrorToast: false,
          });

          if (!response.code) preferences = normalizePlaybackPreferences(response.data);
        } catch {
          // Older desktops keep their configured player behavior when this optional endpoint is absent.
        }
      }
      if (revision === loadRevision) set({ preferences, initialized: true });
    })();

    pending = { scope, promise };
    try {
      await promise;
    } finally {
      if (pending?.promise === promise) pending = undefined;
    }
  },
  save: (kind, preference) => {
    const requestedScope = get().scope;
    // Multiple open viewers can change different media defaults. Persist them in order
    // so the next full preferences document includes the preceding successful change.
    const operation = saveTail
      .catch(() => {})
      .then(async () => {
        const { scope, initialized, preferences } = get();

        if (!scope || !initialized || scope !== requestedScope)
          throw new Error("Playback preferences are not initialized");
        const next = normalizePlaybackPreferences({ ...preferences, [kind]: preference });

        if (scope === "browser")
          localStorage.setItem(PLAYBACK_PREFERENCE_KEY, JSON.stringify(next));
        else {
          const response = await BApi.player.setDevicePlaybackPreferences(next);

          if (response.code)
            throw new Error(response.message || "Could not save playback preferences");
        }
        if (get().scope === scope) set({ preferences: next });
      });

    saveTail = operation;

    return operation;
  },
}));

export const usePlaybackPreferences = () => {
  const nativeAvailable = useUserSideActionsRunHere();
  const desktop = nativeAvailable && isDesktopAppWindow();
  const contextInitialized = useRemoteAccessStore((state) => state.initialized);
  const store = useDevicePlaybackStore();
  const scope: Scope = desktop ? "desktop" : "browser";

  useEffect(() => {
    if (contextInitialized) void store.load(scope);
  }, [contextInitialized, scope, store.load]);

  return {
    ...store,
    desktop,
    ready: contextInitialized && store.initialized && store.scope === scope,
  };
};
