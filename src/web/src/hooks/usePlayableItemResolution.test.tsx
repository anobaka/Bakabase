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
  it("replaces cached paths with current SSE data and treats empty/error discoveries as finished", async () => {
    const value = { ...resource(1), playableItems: files("/old/video.mkv").playableItems };
    const { result } = renderHook(() => usePlayableItemResolution(value));

    expect(result.current.groups[0]!.status).toBe(DataStatus.NotStarted);
    await act(async () => callback(0)(files("/new/cover.jpg", "/new/movie.mp4")));
    expect(result.current.groups[0]).toMatchObject({
      status: DataStatus.Ready,
      items: files("/new/cover.jpg", "/new/movie.mp4").playableItems,
    });
    await act(async () => callback(0)(null, "missing directory"));
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
