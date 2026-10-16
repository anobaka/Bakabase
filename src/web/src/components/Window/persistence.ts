import type { WindowOptions } from "./types";

import { initialWindowBounds } from "./bounds";

export type WindowBounds = { x: number; y: number; width: number; height: number };
type BoundsStorage = Pick<Storage, "getItem" | "setItem">;

export const MEDIA_PLAYER_WINDOW_PERSISTENCE_KEY = "media-player";
const storagePrefix = "bakabase-window-bounds:";

const validBounds = (value: unknown): value is WindowBounds => {
  if (value == null || typeof value !== "object") return false;
  const bounds = value as WindowBounds;

  return (
    [bounds.x, bounds.y, bounds.width, bounds.height].every(
      (number) => typeof number === "number" && Number.isFinite(number),
    ) &&
    bounds.width > 0 &&
    bounds.height > 0
  );
};

export function readWindowBounds(key?: string, storage?: BoundsStorage): WindowBounds | undefined {
  if (!key) return undefined;
  try {
    const value = JSON.parse(
      (storage ?? window.localStorage).getItem(storagePrefix + key) ?? "null",
    );

    return value?.version === 1 && validBounds(value.bounds) ? value.bounds : undefined;
  } catch {
    // Private browsing, invalid old data, or a full/disabled store must not prevent opening.
    return undefined;
  }
}

export function writeWindowBounds(
  key: string | undefined,
  bounds: WindowBounds,
  storage?: BoundsStorage,
) {
  if (!key || !validBounds(bounds)) return;
  try {
    (storage ?? window.localStorage).setItem(
      storagePrefix + key,
      JSON.stringify({ version: 1, bounds }),
    );
  } catch {
    // Keeping a window usable is more important than remembering its geometry.
  }
}

export function restoredWindowBounds(
  options: WindowOptions | undefined,
  viewportWidth: number,
  viewportHeight: number,
) {
  const saved = readWindowBounds(options?.persistenceKey);

  return initialWindowBounds(
    saved
      ? {
          ...options,
          initialPosition: { x: saved.x, y: saved.y },
          initialSize: { width: saved.width, height: saved.height },
        }
      : options,
    viewportWidth,
    viewportHeight,
  );
}
