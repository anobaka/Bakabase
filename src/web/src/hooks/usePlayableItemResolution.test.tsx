import { act, cleanup, renderHook } from "@testing-library/react";
import { useLayoutEffect } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { usePlayableItemResolution } from "./usePlayableItemResolution";

import { DataOrigin, DataStatus, ResourceDataType } from "@/sdk/constants";

const subscribe = vi.hoisted(() => vi.fn());

vi.mock("@/services/ResourceDiscoveryChannel", () => ({ resourceDiscoveryChannel: { subscribe } }));

const resource = (id: number, reloadToken = 0) =>
  ({
    id,
    reloadToken,
    path: `/resource/${id}`,
    dataStates: [
      {
        origin: DataOrigin.FileSystem,
        dataType: ResourceDataType.PlayableItem,
        status: DataStatus.NotStarted,
      },
    ],
    playableItems: [],
  }) as any;
const files = (...paths: string[]) => ({
  playableItems: paths.map((key) => ({ origin: DataOrigin.FileSystem, key })),
});
const callback = (index: number) =>
  subscribe.mock.calls[index][3] as (data: any, error?: string) => void;

beforeEach(() => {
  vi.clearAllMocks();
  subscribe.mockImplementation(() => Promise.resolve(vi.fn()));
});
afterEach(cleanup);

describe("effective playable items from async discovery", () => {
  it("never exposes old SSE files on the first render of a different resource", async () => {
    const snapshots: { id: number; paths: string[]; status: DataStatus }[] = [];
    const { rerender } = renderHook(
      ({ value }) => {
        const state = usePlayableItemResolution(value);

        snapshots.push({
          id: value.id,
          paths: state.groups[0]!.items.map((item) => item.key),
          status: state.groups[0]!.status,
        });

        return state;
      },
      { initialProps: { value: resource(1) } },
    );

    await act(async () => callback(0)(files("/old/video.mkv")));
    snapshots.length = 0;
    rerender({ value: resource(2) });
    expect(snapshots[0]).toEqual({ id: 2, paths: [], status: DataStatus.NotStarted });
    expect(snapshots.every((snapshot) => !snapshot.paths.includes("/old/video.mkv"))).toBe(true);
  });

  it("isolates a new path on the same resource before passive cleanup and ignores its old callback", async () => {
    const complete = vi.fn();
    const snapshots: { path: string; paths: string[] }[] = [];
    const oldPath = resource(1).path;
    const newPath = "/resource/moved";
    const { result, rerender } = renderHook(
      ({ value }) => {
        const state = usePlayableItemResolution(value, { onDiscoveryComplete: complete });

        snapshots.push({ path: value.path, paths: state.groups[0]!.items.map((item) => item.key) });
        // Layout runs before the previous passive subscription effect is cleaned up.
        useLayoutEffect(() => {
          if (value.path === newPath) callback(0)(files("/late/old.mp4"));
        }, [value.path]);

        return state;
      },
      { initialProps: { value: resource(1) } },
    );

    await act(async () => callback(0)(files("/old/video.mkv")));
    complete.mockClear();
    snapshots.length = 0;
    rerender({ value: { ...resource(1), path: newPath } });
    expect(snapshots[0]).toEqual({ path: newPath, paths: [] });
    expect(complete).not.toHaveBeenCalled();
    expect(result.current.groups[0]!.items).toEqual([]);
    expect(subscribe).toHaveBeenCalledTimes(2);
    await act(async () => callback(0)(files(`${oldPath}/late-again.mp4`)));
    expect(complete).not.toHaveBeenCalled();
    await act(async () => callback(1)(files(`${newPath}/current.mp4`)));
    expect(result.current.groups[0]!.items).toEqual(files(`${newPath}/current.mp4`).playableItems);
  });
  it("replaces cached paths with current SSE data and distinguishes failed discovery from a successful empty result", async () => {
    const value = { ...resource(1), playableItems: files("/old/video.mkv").playableItems };
    const { result } = renderHook(() => usePlayableItemResolution(value));

    expect(result.current.groups[0]!.status).toBe(DataStatus.NotStarted);
    await act(async () => callback(0)(files("/new/cover.jpg", "/new/movie.mp4")));
    expect(result.current.groups[0]).toMatchObject({
      status: DataStatus.Ready,
      items: files("/new/cover.jpg", "/new/movie.mp4").playableItems,
    });
    await act(async () => callback(0)(null, "missing directory"));
    expect(result.current.groups[0]).toMatchObject({
      status: DataStatus.Failed,
      items: [],
      error: "missing directory",
    });
    expect(result.current.overallStatus).toBe("failed");
    await act(async () => result.current.triggerDiscovery(DataOrigin.FileSystem));
    await act(async () => callback(1)(files()));
    expect(result.current.groups[0]).toMatchObject({ status: DataStatus.Ready, items: [] });
    expect(result.current.overallStatus).toBe("not-found");
  });

  it("ignores callbacks from a previous resource while new discovery still propagates", async () => {
    const complete = vi.fn();
    const { result, rerender } = renderHook(
      ({ value }) => usePlayableItemResolution(value, { onDiscoveryComplete: complete }),
      { initialProps: { value: resource(1) } },
    );

    rerender({ value: resource(2) });
    await act(async () => callback(0)(files("/old/video.mkv")));
    expect(complete).not.toHaveBeenCalled();
    expect(result.current.groups[0]!.items).toEqual([]);
    await act(async () => callback(1)(files("/new/video.mp4")));
    expect(complete).toHaveBeenCalledWith(
      2,
      DataOrigin.FileSystem,
      files("/new/video.mp4"),
      undefined,
    );
    expect(result.current.groups[0]!.items).toEqual(files("/new/video.mp4").playableItems);
  });

  it("ignores a completed discovery from the old reload and releases late subscription handles", async () => {
    let resolveOld!: (unsubscribe: () => void) => void;
    const oldUnsubscribe = vi.fn();

    subscribe.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveOld = resolve;
        }),
    );
    const { result, rerender } = renderHook(({ value }) => usePlayableItemResolution(value), {
      initialProps: { value: resource(1) },
    });

    rerender({ value: resource(1, 1) });
    await act(async () => {
      callback(0)(files("/old/video.mkv"));
      resolveOld(oldUnsubscribe);
    });
    expect(oldUnsubscribe).toHaveBeenCalledOnce();
    expect(result.current.groups[0]!.items).toEqual([]);
    await act(async () => callback(1)(files("/new/cover.jpg")));
    expect(result.current.groups[0]!.items).toEqual(files("/new/cover.jpg").playableItems);
  });

  it("unsubscribes a handle that arrives after unmount without delivering late completion", async () => {
    let done!: (unsubscribe: () => void) => void;
    const unsubscribe = vi.fn();
    const complete = vi.fn();

    subscribe.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          done = resolve;
        }),
    );
    const view = renderHook(() =>
      usePlayableItemResolution(resource(1), { onDiscoveryComplete: complete }),
    );

    view.unmount();
    await act(async () => {
      callback(0)(files("/old/video.mkv"));
      done(unsubscribe);
    });
    expect(unsubscribe).toHaveBeenCalledOnce();
    expect(complete).not.toHaveBeenCalled();
  });
});

describe("per-source discovery retry", () => {
  it("retries repeatedly, clears the old failure while pending, and ignores its late result", async () => {
    const { result } = renderHook(() => usePlayableItemResolution(resource(1)));

    await act(async () => callback(0)(null, "NAS unavailable"));
    await act(async () => result.current.triggerDiscovery(DataOrigin.FileSystem));
    expect(subscribe).toHaveBeenCalledTimes(2);
    expect(result.current.overallStatus).toBe("loading");
    expect(result.current.groups[0]?.error).toBeUndefined();
    await act(async () => callback(0)(files("/old.mp4")));
    expect(result.current.overallStatus).toBe("loading");
    await act(async () => callback(1)(null, "still unavailable"));
    await act(async () => result.current.triggerDiscovery(DataOrigin.FileSystem));
    expect(subscribe).toHaveBeenCalledTimes(3);
    await act(async () => callback(2)(files("/available.mp4")));
    expect(result.current.overallStatus).toBe("ready");
    expect(result.current.groups[0]?.items[0]?.key).toBe("/available.mp4");
  });

  it("keeps a ready independent source usable and does not resubscribe it on file retry", async () => {
    const value = resource(1);

    value.dataStates.push({
      origin: DataOrigin.Steam,
      dataType: ResourceDataType.PlayableItem,
      status: DataStatus.Ready,
    });
    value.playableItems = [{ origin: DataOrigin.Steam, key: "123" }];
    const { result } = renderHook(() => usePlayableItemResolution(value));

    await act(async () => callback(0)(null, "NAS unavailable"));
    expect(result.current.overallStatus).toBe("ready");
    expect(result.current.groups[0]?.origin).toBe(DataOrigin.Steam);
    await act(async () => result.current.triggerDiscovery(DataOrigin.FileSystem));
    expect(subscribe.mock.calls.every((call) => call[1] === DataOrigin.FileSystem)).toBe(true);
    expect(result.current.groups[0]?.items[0]?.key).toBe("123");
  });

  it("reports rejected subscriptions and permits a retry of a backend failed origin", async () => {
    const value = resource(1);

    value.dataStates[0].status = DataStatus.Failed;
    subscribe.mockRejectedValueOnce(new Error("connection refused"));
    const { result } = renderHook(() => usePlayableItemResolution(value));

    expect(result.current.overallStatus).toBe("failed");
    expect(subscribe).not.toHaveBeenCalled();
    await act(async () => result.current.triggerDiscovery(DataOrigin.FileSystem));
    expect(result.current.groups[0]?.error).toBe("connection refused");
    await act(async () => result.current.triggerDiscovery(DataOrigin.FileSystem));
    await act(async () => callback(1)(files("/video.mp4")));
    expect(result.current.overallStatus).toBe("ready");
  });
});

it("ignores legacy webpage playable states and cache while discovering downloaded files", async () => {
  const value = resource(1);

  for (const origin of [DataOrigin.DLsite, DataOrigin.ExHentai]) {
    value.dataStates.push({
      origin,
      dataType: ResourceDataType.PlayableItem,
      status: DataStatus.NotStarted,
    });
    value.playableItems.push({ origin, key: "old-website" });
  }
  const { result } = renderHook(() => usePlayableItemResolution(value));

  expect(subscribe).toHaveBeenCalledTimes(1);
  expect(result.current.overallStatus).toBe("loading");
  await act(async () =>
    callback(0)({
      playableItems: [
        ...files("/downloaded/game.exe").playableItems,
        { origin: DataOrigin.DLsite, key: "RJ123" },
      ],
    }),
  );
  expect(result.current.groups).toHaveLength(1);
  expect(result.current.groups[0]?.items).toEqual(files("/downloaded/game.exe").playableItems);
  await act(async () => result.current.triggerDiscovery(DataOrigin.DLsite));
  expect(subscribe).toHaveBeenCalledTimes(1);
});
