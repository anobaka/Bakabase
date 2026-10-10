import { act, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useResourceBrowserPlayer } from "./useResourceBrowserPlayer";
import { DataOrigin } from "@/sdk/constants";
const mocks = vi.hoisted(() => ({
  createWindow: vi.fn(),
  files: vi.fn(),
  located: vi.fn(),
  history: vi.fn(),
  danger: vi.fn(),
  message: vi.fn(),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createWindow: mocks.createWindow }),
}));
vi.mock("@/components/MediaPlayer", () => ({ default: () => null }));
vi.mock("@/components/bakaui", () => ({ toast: { danger: mocks.danger, default: mocks.message } }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    file: { getAllFiles: mocks.files },
    resource: { getResourcePlayableItems: mocks.located, markResourceAsPlayed: mocks.history },
  },
}));
const resource = { id: 13, path: "/film", displayName: "Film", isFile: false } as any;
beforeEach(() => {
  vi.clearAllMocks();
  mocks.files.mockResolvedValue({
    code: 0,
    data: ["/film/a.ass", "/film/cover.jpg", "/film/sample.mkv", "/film/main.mkv"],
  });
  mocks.located.mockResolvedValue({
    code: 0,
    data: [{ origin: DataOrigin.FileSystem, key: "/film/main.mkv" }],
  });
  mocks.history.mockResolvedValue({ code: 0 });
});
describe("resource browser player", () => {
  it("starts the configured located file and records that same file rather than the first subtitle", async () => {
    const { result } = renderHook(() => useResourceBrowserPlayer());
    await act(() => result.current(resource));
    expect(mocks.createWindow.mock.calls[0][1].defaultActiveIndex).toBe(3);
    expect(mocks.history).toHaveBeenCalledWith(13, { item: "/film/main.mkv" });
  });
  it("preserves an explicit selected subtitle without re-running discovery", async () => {
    const { result } = renderHook(() => useResourceBrowserPlayer());
    await act(() => result.current(resource, "/film/a.ass"));
    expect(mocks.createWindow.mock.calls[0][1].defaultActiveIndex).toBe(0);
    expect(mocks.located).not.toHaveBeenCalled();
  });
  it("falls back to useful media if discovery fails and treats a resource file as its own entry", async () => {
    mocks.located.mockRejectedValue(new Error("discovery unavailable"));
    const { result } = renderHook(() => useResourceBrowserPlayer());
    await act(() => result.current(resource));
    expect(mocks.createWindow.mock.calls[0][1].defaultActiveIndex).toBe(2);
    await act(() => result.current({ ...resource, isFile: true, path: "/film/main.mkv" }));
    expect(mocks.files).toHaveBeenCalledTimes(1);
    expect(mocks.createWindow.mock.calls[1][1].entries.map((entry: any) => entry.path)).toEqual([
      "/film/main.mkv",
    ]);
  });
  it("reports file retrieval failure through the application's existing toast provider", async () => {
    mocks.files.mockRejectedValue(new Error("unreachable"));
    const { result } = renderHook(() => useResourceBrowserPlayer());
    await act(() => result.current(resource));
    expect(mocks.danger).toHaveBeenCalledWith("mediaPlayer.filesFailed");
    expect(mocks.createWindow).not.toHaveBeenCalled();
  });
});
