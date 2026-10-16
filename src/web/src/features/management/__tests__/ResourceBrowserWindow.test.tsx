import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import ResourceBrowserWindow from "../ResourceBrowserWindow";
import { DataOrigin } from "@/sdk/constants";
import type { Resource } from "@/core/models/Resource";

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
  default: ({ entries, defaultActiveIndex }: any) => (
    <div>
      <span data-testid="active">{entries[defaultActiveIndex]?.path}</span>
      {entries.map((entry: any) => (
        <span key={entry.path}>{entry.mediaUrl}</span>
      ))}
    </div>
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
        source={source}
        resource={resource}
        initialPath="/root/211.jpg"
        initialPage={{
          items: [item("210.jpg"), item("211.jpg")],
          offset: 200,
          hasMore: true,
          nextOffset: 300,
        }}
        options={{}}
      />,
    );
    expect(screen.getByTestId("active")).toHaveTextContent("211.jpg");
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
  });

  it("keeps the scope selector available when configured rules match no files", async () => {
    render(
      <ResourceBrowserWindow
        source={source}
        resource={resource}
        initialPage={{ items: [], offset: 0, hasMore: false, playableFilesConfigured: true }}
        options={{ scope: "configured" }}
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
