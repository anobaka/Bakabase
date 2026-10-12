import { beforeEach, describe, expect, it, vi } from "vitest";

import { useDevicePlaybackStore } from "./usePlaybackPreferences";
import { automaticPlaybackPreferences, PLAYBACK_PREFERENCE_KEY } from "./playbackPreferences";

const api = vi.hoisted(() => ({ get: vi.fn(), save: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: {
    player: { getDevicePlaybackPreferences: api.get, setDevicePlaybackPreferences: api.save },
  },
}));
vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: vi.fn(),
  useUserSideActionsRunHere: vi.fn(),
}));

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  useDevicePlaybackStore.setState({
    scope: undefined,
    initialized: false,
    preferences: automaticPlaybackPreferences(),
  });
  api.get.mockResolvedValue({ code: 0, data: automaticPlaybackPreferences() });
  api.save.mockResolvedValue({ code: 0 });
});

describe("device preference persistence", () => {
  it("reads and saves browser preferences locally without requesting the server's device settings", async () => {
    localStorage.setItem(
      PLAYBACK_PREFERENCE_KEY,
      JSON.stringify({ ...automaticPlaybackPreferences(), image: { mode: "ask" } }),
    );
    await useDevicePlaybackStore.getState().load("browser");
    expect(useDevicePlaybackStore.getState().preferences.image.mode).toBe("ask");
    await useDevicePlaybackStore
      .getState()
      .save("video", { mode: "native", nativeSchemeId: "iina-macos" });
    expect(JSON.parse(localStorage.getItem(PLAYBACK_PREFERENCE_KEY)!).video).toEqual({
      mode: "native",
      nativeSchemeId: "iina-macos",
    });
    expect(api.get).not.toHaveBeenCalled();
    expect(api.save).not.toHaveBeenCalled();
  });

  it("coalesces desktop loads across all cards, and writes the current-device endpoint", async () => {
    let done!: (value: unknown) => void;

    api.get.mockReturnValue(
      new Promise((resolve) => {
        done = resolve;
      }),
    );
    const first = useDevicePlaybackStore.getState().load("desktop");
    const second = useDevicePlaybackStore.getState().load("desktop");

    expect(api.get).toHaveBeenCalledTimes(1);
    expect(useDevicePlaybackStore.getState().initialized).toBe(false);
    done({ code: 0, data: { ...automaticPlaybackPreferences(), video: { mode: "browser" } } });
    await Promise.all([first, second]);
    await useDevicePlaybackStore.getState().save("text", { mode: "ask" });
    expect(api.save).toHaveBeenCalledWith(
      expect.objectContaining({ version: 1, text: { mode: "ask" }, video: { mode: "browser" } }),
    );
    expect(localStorage.getItem(PLAYBACK_PREFERENCE_KEY)).toBeNull();
  });

  it("preserves configured desktop playback when an older optional endpoint is unavailable", async () => {
    api.get.mockRejectedValue(new Error("not available"));
    await useDevicePlaybackStore.getState().load("desktop");
    expect(useDevicePlaybackStore.getState().initialized).toBe(true);
    expect(useDevicePlaybackStore.getState().preferences).toEqual(automaticPlaybackPreferences());
  });

  it("does not remember a failed save and serializes successful edits to different media kinds", async () => {
    await useDevicePlaybackStore.getState().load("desktop");
    api.save.mockResolvedValueOnce({ code: 500, message: "full disk" });
    await expect(useDevicePlaybackStore.getState().save("video", { mode: "ask" })).rejects.toThrow(
      "full disk",
    );
    expect(useDevicePlaybackStore.getState().preferences.video.mode).toBe("automatic");
    await Promise.all([
      useDevicePlaybackStore.getState().save("image", { mode: "browser" }),
      useDevicePlaybackStore.getState().save("video", { mode: "ask" }),
    ]);
    expect(api.save.mock.lastCall![0]).toMatchObject({
      image: { mode: "browser" },
      video: { mode: "ask" },
    });
    expect(useDevicePlaybackStore.getState().preferences).toMatchObject({
      image: { mode: "browser" },
      video: { mode: "ask" },
    });
  });

  it("leaves defaults unchanged when browser persistence refuses", async () => {
    await useDevicePlaybackStore.getState().load("browser");
    vi.spyOn(Storage.prototype, "setItem").mockImplementationOnce(() => {
      throw new Error("quota");
    });
    await expect(useDevicePlaybackStore.getState().save("image", { mode: "ask" })).rejects.toThrow(
      "quota",
    );
    expect(useDevicePlaybackStore.getState().preferences.image.mode).toBe("automatic");
  });

  it("ignores an outdated desktop GET that returns after switching to browser scope", async () => {
    let done!: (value: unknown) => void;

    api.get.mockReturnValue(
      new Promise((resolve) => {
        done = resolve;
      }),
    );
    const desktop = useDevicePlaybackStore.getState().load("desktop");

    await useDevicePlaybackStore.getState().load("browser");
    done({ code: 0, data: { ...automaticPlaybackPreferences(), video: { mode: "ask" } } });
    await desktop;
    expect(useDevicePlaybackStore.getState()).toMatchObject({
      scope: "browser",
      initialized: true,
    });
    expect(useDevicePlaybackStore.getState().preferences.video.mode).toBe("automatic");
  });
});
