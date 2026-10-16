import { act, cleanup, render, renderHook, screen } from "@testing-library/react";
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
vi.mock("@/features/playback/RemotePathMappingHint", () => ({
  default: ({ filePath, source }: any) => (
    <span data-source={source?.serverId ?? "current"} data-testid="mapping-hint">
      {filePath}
    </span>
  ),
}));
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
  cleanup();
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
    expect(mocks.createWindow.mock.calls[0][2].persistenceKey).toBe("media-player");
    expect(mocks.history).toHaveBeenCalledWith(13, { item: "/film/main.mkv" });
    expect(mocks.createWindow.mock.calls[0][1]).toMatchObject({
      resourceScope: true,
      configuredPaths: ["/film/main.mkv"],
    });
    expect(mocks.createWindow.mock.calls[0][1].entries).toHaveLength(4);
    render(mocks.createWindow.mock.calls[0][1].renderFooterExtra("/film/main.mkv"));
    expect(screen.getByTestId("mapping-hint")).toHaveTextContent("/film/main.mkv");
    expect(screen.getByTestId("mapping-hint")).toHaveAttribute("data-source", "current");
  });
  it("passes an explicit candidate scope and alternative action without silently filtering whole-resource defaults", async () => {
    const alternative = vi.fn();
    const { result } = renderHook(() => useResourceBrowserPlayer());

    await act(() =>
      result.current(resource, undefined, { scope: "configured", onOpenAlternative: alternative }),
    );
    expect(mocks.createWindow.mock.calls[0][1]).toMatchObject({
      initialScope: "configured",
      configuredPaths: ["/film/main.mkv"],
      onOpenAlternative: alternative,
    });
    expect(mocks.createWindow.mock.calls[0][1].entries).toHaveLength(4);
    expect(alternative).not.toHaveBeenCalled();
  });
  it("does not turn an explicit candidate-only request into whole-directory browsing when discovery fails", async () => {
    mocks.located.mockRejectedValue(new Error("discovery unavailable"));
    const { result } = renderHook(() => useResourceBrowserPlayer());

    await act(() => result.current(resource, undefined, { scope: "configured" }));
    expect(mocks.createWindow).not.toHaveBeenCalled();
    expect(mocks.danger).toHaveBeenCalledWith("mediaPlayer.scope.failed");
  });
  it("records the actual configured selection rather than an initial path excluded by the requested scope", async () => {
    const { result } = renderHook(() => useResourceBrowserPlayer());

    await act(() => result.current(resource, "/film/cover.jpg", { scope: "configured" }));
    expect(mocks.history).toHaveBeenCalledWith(13, { item: "/film/main.mkv" });
    expect(mocks.createWindow.mock.calls[0][1].defaultActiveIndex).toBe(3);
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
