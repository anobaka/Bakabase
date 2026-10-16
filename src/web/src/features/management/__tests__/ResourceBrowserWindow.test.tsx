import type { Resource } from "@/core/models/Resource";

import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import ResourceBrowserWindow from "../ResourceBrowserWindow";

import { DataOrigin } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ request: vi.fn() }));

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("../api", () => ({ ownerClient: () => mocks.request }));
vi.mock("../playback", () => ({
  managedMediaUrl: (source: any, id: number, item: any) =>
    `media:${source.serverId}:${id}:${item.key}`,
}));
vi.mock("../resourceApi", () => ({
  managedResourceAssetUrl: (source: any, id: number, key: string) =>
    `asset:${source.serverId}:${id}:${key}`,
}));
vi.mock("@/components/MediaPlayer", () => ({
  default: ({ entries, defaultActiveIndex, renderFooterExtra }: any) => (
    <div>
      <span data-testid="active">{entries[defaultActiveIndex]?.path}</span>
      {entries[defaultActiveIndex] && renderFooterExtra(entries[defaultActiveIndex].path)}
      {entries.map((entry: any) => (
        <span key={entry.path}>{entry.mediaUrl}</span>
      ))}
    </div>
  ),
}));
vi.mock("@/features/playback/RemotePathMappingHint", () => ({
  default: ({ filePath, source }: any) => (
    <span data-source={source?.serverId} data-testid="mapping-hint">
      {filePath}
    </span>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
}));
const source = {
  serverId: "one",
  libraryEpoch: "epoch",
  name: "One",
  isLocal: false,
  available: true,
  contractVersion: 1,
};
const resource = { id: 7, path: "/root", hasLocalPath: true, isFile: false } as Resource;
const item = (name: string) => ({ origin: DataOrigin.FileSystem, key: `/root/${name}` });

afterEach(() => {
  cleanup();
  mocks.request.mockReset();
});

describe("managed resource browser window", () => {
  it("renders the selected file's bounded page and switches scope using the owner request", async () => {
    render(
      <ResourceBrowserWindow
        initialPage={{
          items: [item("210.jpg"), item("211.jpg")],
          offset: 200,
          hasMore: true,
          nextOffset: 300,
        }}
        initialPath="/root/211.jpg"
        options={{}}
        resource={resource}
        source={source}
      />,
    );
    expect(screen.getByTestId("active")).toHaveTextContent("211.jpg");
    expect(screen.getByTestId("mapping-hint")).toHaveTextContent("/root/211.jpg");
    expect(screen.getByTestId("mapping-hint")).toHaveAttribute("data-source", "one");
    expect(screen.getByText("asset:one:7:/root/211.jpg")).toBeTruthy();
    mocks.request.mockResolvedValueOnce({
      data: {
        items: [item("video.mp4")],
        offset: 0,
        hasMore: false,
        playableFilesConfigured: true,
      },
    });
    await act(async () =>
      fireEvent.change(screen.getByRole("combobox"), { target: { value: "configured" } }),
    );
    expect(mocks.request).toHaveBeenCalledWith("resourceBrowserFiles", {
      id: 7,
      query: { offset: ["0"], pageSize: ["100"], scope: ["configured"] },
    });
    expect(screen.getByText("media:one:7:/root/video.mp4")).toBeTruthy();
    expect(screen.getByTestId("mapping-hint")).toHaveTextContent("/root/video.mp4");
  });

  it("keeps the scope selector available when configured rules match no files", async () => {
    render(
      <ResourceBrowserWindow
        initialPage={{ items: [], offset: 0, hasMore: false, playableFilesConfigured: true }}
        options={{ scope: "configured" }}
        resource={resource}
        source={source}
      />,
    );
    mocks.request.mockResolvedValueOnce({
      data: { items: [item("comic.jpg")], offset: 0, hasMore: false },
    });
    await act(async () =>
      fireEvent.change(screen.getByRole("combobox"), { target: { value: "resource" } }),
    );
    expect(mocks.request.mock.calls[0][1].query.scope).toEqual(["all"]);
    expect(screen.getByText("asset:one:7:/root/comic.jpg")).toBeTruthy();
  });
});
