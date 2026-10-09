import type { BakabaseServiceModelsViewUserStorageRootsViewModel as StorageRoots } from "@/sdk/Api";

import { create } from "zustand";

import BApi from "@/sdk/BApi";

export const storageError = (cause: unknown, translate?: (key: string) => string): Error => {
  const response = cause as { message?: string; error?: { message?: string } } | undefined;
  const message = response?.error?.message || response?.message || "";
  const boundaryPrefix = "Choose a folder inside a mounted storage location.";

  // Translate only our policy's stable prefix. Preserve the path and actual OS errors.
  if (translate && message.startsWith(boundaryPrefix))
    return new Error(
      translate("fileExplorer.storage.pathRejected") + message.slice(boundaryPrefix.length),
    );

  return cause instanceof Error ? cause : new Error(message);
};

let pending: Promise<StorageRoots> | undefined;

export const useUserStorageStore = create<{
  settings?: StorageRoots;
  error?: Error;
  load: () => Promise<StorageRoots>;
}>((set, get) => ({
  load: async () => {
    if (get().settings) return get().settings!;
    if (pending) return pending;
    pending = (async () => {
      set({ error: undefined });
      try {
        const response = await BApi.file.getUserStorageRoots({ showErrorToast: false });

        if (response.code || !response.data) throw storageError(response);
        set({ settings: response.data });

        return response.data;
      } catch (cause) {
        const error = storageError(cause);

        set({ error });
        throw error;
      } finally {
        pending = undefined;
      }
    })();

    return pending;
  },
}));

/** UI navigation only; the server rechecks real paths and symlinks on every operation. */
export function isInsideStorageRoots(path: string, storage?: StorageRoots): boolean {
  if (!storage?.isRestricted) return true;
  const normalized = path.replace(/\/+$/, "");

  return storage.roots.some((root) => {
    const base = root.path.replace(/\/+$/, "");

    return normalized === base || normalized.startsWith(`${base}/`);
  });
}

export async function validateUserStoragePaths(paths: string[]): Promise<void> {
  const response = await BApi.file.validateUserStoragePaths({ paths }, { showErrorToast: false });

  if (response.code) throw storageError(response);
}
