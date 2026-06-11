/**
 * Persistence helpers for the batch-play context menu: remember the last
 * player the user picked and whether all files of each resource should be
 * included. Kept pure (storage injected) so they are unit-testable.
 */

export type StoredLastPlayer = {
  key: string;
  name: string;
};

export const LAST_PLAYER_STORAGE_KEY = "bakabase.batchPlay.lastPlayer";
export const INCLUDE_ALL_FILES_STORAGE_KEY = "bakabase.batchPlay.includeAllFiles";

type ReadableStorage = Pick<Storage, "getItem">;
type WritableStorage = Pick<Storage, "setItem">;

export function readLastPlayer(storage: ReadableStorage): StoredLastPlayer | undefined {
  try {
    const raw = storage.getItem(LAST_PLAYER_STORAGE_KEY);

    if (!raw) return undefined;
    const parsed = JSON.parse(raw);

    if (
      typeof parsed?.key === "string" &&
      parsed.key.length > 0 &&
      typeof parsed?.name === "string"
    ) {
      return { key: parsed.key, name: parsed.name };
    }

    return undefined;
  } catch {
    return undefined;
  }
}

export function writeLastPlayer(storage: WritableStorage, player: StoredLastPlayer) {
  try {
    storage.setItem(LAST_PLAYER_STORAGE_KEY, JSON.stringify(player));
  } catch {
    // Storage may be unavailable (private mode); remembering is best-effort.
  }
}

export function readIncludeAllFiles(storage: ReadableStorage): boolean {
  try {
    return storage.getItem(INCLUDE_ALL_FILES_STORAGE_KEY) === "true";
  } catch {
    return false;
  }
}

export function writeIncludeAllFiles(storage: WritableStorage, value: boolean) {
  try {
    storage.setItem(INCLUDE_ALL_FILES_STORAGE_KEY, String(value));
  } catch {
    // Best-effort.
  }
}
