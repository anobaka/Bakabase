import { describe, expect, it } from "vitest";

import {
  INCLUDE_ALL_FILES_STORAGE_KEY,
  LAST_PLAYER_STORAGE_KEY,
  readIncludeAllFiles,
  readLastPlayer,
  writeIncludeAllFiles,
  writeLastPlayer,
} from "../batchPlayStorage";

const createStorage = (initial: Record<string, string> = {}) => {
  const map = new Map(Object.entries(initial));

  return {
    getItem: (key: string) => map.get(key) ?? null,
    setItem: (key: string, value: string) => {
      map.set(key, value);
    },
  };
};

describe("batchPlayStorage", () => {
  describe("readLastPlayer", () => {
    it("returns undefined when nothing is stored", () => {
      expect(readLastPlayer(createStorage())).toBeUndefined();
    });

    it("returns undefined for corrupted json", () => {
      const storage = createStorage({ [LAST_PLAYER_STORAGE_KEY]: "{not json" });

      expect(readLastPlayer(storage)).toBeUndefined();
    });

    it("returns undefined when the key field is missing or empty", () => {
      expect(
        readLastPlayer(
          createStorage({ [LAST_PLAYER_STORAGE_KEY]: JSON.stringify({ name: "VLC" }) }),
        ),
      ).toBeUndefined();
      expect(
        readLastPlayer(
          createStorage({ [LAST_PLAYER_STORAGE_KEY]: JSON.stringify({ key: "", name: "VLC" }) }),
        ),
      ).toBeUndefined();
    });

    it("round-trips a stored player", () => {
      const storage = createStorage();

      writeLastPlayer(storage, { key: "known|Vlc", name: "VLC media player" });

      expect(readLastPlayer(storage)).toEqual({ key: "known|Vlc", name: "VLC media player" });
    });

    it("does not throw when storage is unavailable", () => {
      const broken = {
        getItem: () => {
          throw new Error("denied");
        },
        setItem: () => {
          throw new Error("denied");
        },
      };

      expect(() => writeLastPlayer(broken, { key: "k", name: "n" })).not.toThrow();
      expect(readLastPlayer(broken)).toBeUndefined();
    });
  });

  describe("readIncludeAllFiles", () => {
    it("defaults to false", () => {
      expect(readIncludeAllFiles(createStorage())).toBe(false);
    });

    it("round-trips the toggle", () => {
      const storage = createStorage();

      writeIncludeAllFiles(storage, true);
      expect(readIncludeAllFiles(storage)).toBe(true);
      expect(storage.getItem(INCLUDE_ALL_FILES_STORAGE_KEY)).toBe("true");

      writeIncludeAllFiles(storage, false);
      expect(readIncludeAllFiles(storage)).toBe(false);
    });
  });
});
