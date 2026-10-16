import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ManagedPlaylistWindow from "../ManagedPlaylistWindow";
import { DataOrigin } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ request: vi.fn() }));
const t = (key: string) => key;
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t }) }));
vi.mock("../api", () => ({
  ownerClient: (source: any) => (operation: string, input: any) =>
    mocks.request(source, operation, input),
}));
vi.mock("../resourceApi", () => ({
  managedResourceAssetUrl: (source: any, id: number, key: string) =>
    `asset:${source.serverId}:${id}:${key}`,
}));
vi.mock("../playback", () => ({
  managedMediaUrl: (source: any, id: number, item: any) =>
    `media:${source.serverId}:${id}:${item.key}`,
}));
vi.mock("@/components/MediaPlayer", () => ({
  default: ({ entries }: any) => (
    <div>
      {entries.map((entry: any) => (
        <span key={entry.path}>{entry.mediaUrl}</span>
      ))}
    </div>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Spinner: () => <span>loading</span>,
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

beforeEach(() => {
  mocks.request.mockReset().mockImplementation(async (_source, operation, input) => {
    if (operation === "resourceGet")
      return { code: 0, data: [{ id: input.ids[0], hasLocalPath: input.ids[0] === 7 }] };
    const offset = Number(input.query.offset[0]);
    return {
      code: 0,
      data: {
        items: [{ origin: DataOrigin.FileSystem, key: `/file-${input.id}-${offset}.jpg` }],
        offset,
        hasMore: input.id === 7 && offset === 0,
        nextOffset: 100,
      },
    };
  });
});
afterEach(cleanup);

describe("owner playlist browser window", () => {
  it("pages resources in order, preserves the owner, and uses provider media when no local root exists", async () => {
    render(
      <ManagedPlaylistWindow source={source} items={[{ resourceId: 7 }, { resourceId: 8 }]} />,
    );
    await screen.findByText("asset:one:7:/file-7-0.jpg");
    await act(async () => fireEvent.click(screen.getByText("management.next")));
    expect(screen.getByText("asset:one:7:/file-7-100.jpg")).toBeTruthy();
    await act(async () => fireEvent.click(screen.getByText("management.next")));
    expect(screen.getByText("media:one:8:/file-8-0.jpg")).toBeTruthy();
    await act(async () => fireEvent.click(screen.getByText("management.previous")));
    expect(screen.getByText("asset:one:7:/file-7-100.jpg")).toBeTruthy();
    expect(
      mocks.request.mock.calls.every(
        ([owner]) => owner.serverId === "one" && owner.libraryEpoch === "epoch",
      ),
    ).toBe(true);
    expect(
      mocks.request.mock.calls
        .filter(([, operation]) => operation === "resourceBrowserFiles")
        .map(([, , input]) => [input.id, input.query.offset[0], input.query.pageSize[0]]),
    ).toEqual([
      [7, "0", "100"],
      [7, "100", "100"],
      [8, "0", "100"],
      [7, "100", "100"],
    ]);
  });

  it("does not request arbitrary file-only paths and allows explicitly continuing to a resource item", async () => {
    render(
      <ManagedPlaylistWindow
        source={source}
        items={[{ file: "/private/file.mp4" }, { resourceId: 8 }]}
      />,
    );
    expect(screen.getByRole("alert")).toHaveTextContent("management.playlist.fileItems");
    expect(mocks.request).not.toHaveBeenCalled();
    await act(async () => fireEvent.click(screen.getByText("management.next")));
    expect(screen.getByText("media:one:8:/file-8-0.jpg")).toBeTruthy();
    expect(
      mocks.request.mock.calls.some((call) => JSON.stringify(call).includes("/private/file.mp4")),
    ).toBe(false);
  });
});
