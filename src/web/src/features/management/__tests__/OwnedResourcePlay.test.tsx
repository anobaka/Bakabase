import type { ManagementSource } from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import OwnedResourcePlay from "../OwnedResourcePlay";

import { DataOrigin } from "@/sdk/constants";

const fixture = vi.hoisted(() => ({
  request: vi.fn(),
  client: vi.fn(),
  native: vi.fn(),
  window: vi.fn(),
  profile: vi.fn(),
  app: false,
  bridge: false,
  first: false,
  choice: undefined as { mode: string } | undefined,
  ready: true,
  unavailable: false,
  reload: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({ default: {} }));
vi.mock("../api", async (original) => ({
  ...(await original<any>()),
  ownerClient: (source: any) => {
    fixture.client(source);

    return fixture.request;
  },
}));
vi.mock("../playback", async (original) => ({
  ...(await original<any>()),
  playManagedResource: fixture.native,
}));
vi.mock("@/features/playback/resourceProfiles", () => ({
  useSourceResourceProfile: (target: string, id: number) => {
    fixture.profile(target, id);

    return {
      choice: () => fixture.choice,
      ready: fixture.ready,
      unavailable: fixture.unavailable,
      reload: fixture.reload,
    };
  },
}));
vi.mock("@/stores/remoteAccess", () => ({ useUserSideActionsRunHere: () => fixture.bridge }));
vi.mock("@/utils/clientSurface", () => ({ isDesktopAppWindow: () => fixture.app }));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: (selector: any) =>
    selector({ data: { resource: { autoSelectFirstPlayableFile: fixture.first } } }),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createWindow: fixture.window }),
}));
vi.mock("@/components/MediaPlayer", () => ({ default: () => null }));
vi.mock("@/components/Window/bounds", () => ({
  initialWindowBounds: () => ({ x: 30, y: 30, width: 800, height: 600 }),
}));
vi.mock("@/components/Resource/components/PlayControl", () => ({
  default: () => <div>ordinary local playback</div>,
}));
vi.mock("@/components/Resource/components/PlayControl/PlaybackButtons", () => ({
  ResourceDetailPlaybackButton: () => null,
}));
vi.mock("@/components/Resource/components/PlayControl/DesktopPlaybackGuide", () => ({
  DesktopPlaybackGuideButton: () => <button>desktop guide</button>,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, isLoading, ...props }: any) => (
    <button aria-label={props["aria-label"]} disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Modal: ({ children, visible }: any) => (visible ? <div role="dialog">{children}</div> : null),
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
const source: ManagementSource = {
  serverId: "owner-a",
  name: "Owner A",
  libraryEpoch: "epoch-a",
  available: true,
  isLocal: false,
  contractVersion: 1,
};
const resource = { id: 42, path: "/album", isFile: false, displayName: "Album" } as any;
const files = [
  { origin: DataOrigin.FileSystem, key: "/album/cover.jpg" },
  { origin: DataOrigin.FileSystem, key: "/album/movie.mkv" },
];
const show = (owner = source) => render(<OwnedResourcePlay resource={resource} source={owner} />);
const clickPlay = () =>
  act(async () => fireEvent.click(screen.getByRole("button", { name: "common.action.play" })));

beforeEach(() => {
  vi.clearAllMocks();
  fixture.app = false;
  fixture.bridge = false;
  fixture.first = false;
  fixture.choice = undefined;
  fixture.ready = true;
  fixture.unavailable = false;
  fixture.request.mockImplementation(async (operation) => ({
    code: 0,
    data:
      operation === "resourceBrowserFiles"
        ? { items: files, offset: 0, hasMore: false, playableFilesConfigured: true }
        : files,
  }));
  fixture.native.mockResolvedValue(undefined);
});
afterEach(cleanup);

describe("playback bound to a managed resource owner", () => {
  it("keeps the original all-in-one control for local resources", () => {
    show({ ...source, isLocal: true });
    expect(screen.getByText("ordinary local playback")).toBeInTheDocument();
    expect(fixture.request).not.toHaveBeenCalled();
    expect(fixture.profile).not.toHaveBeenCalled();
  });
  it("discovers only on click and opens the owner's configured files in the ordinary web player", async () => {
    show();
    expect(fixture.request).not.toHaveBeenCalled();
    await clickPlay();
    expect(fixture.request).toHaveBeenCalledWith("resourceBrowserFiles", {
      id: 42,
      query: { offset: ["0"], pageSize: ["100"] },
    });
    expect(fixture.request).not.toHaveBeenCalledWith("resourcePlayableItems", expect.anything());
    expect(fixture.profile).toHaveBeenCalledWith("node:owner-a:epoch-a", 42);
    const props = fixture.window.mock.lastCall![1];

    expect(props.entries.map((entry: any) => entry.path)).toEqual(files.map((file) => file.key));
    for (const entry of props.entries) {
      const url = new URL(entry.mediaUrl, window.location.origin);

      expect(url.pathname).toContain("/management/owner-a/media");
      expect(url.searchParams.get("libraryEpoch")).toBe("epoch-a");
      expect(url.searchParams.get("resourceId")).toBe("42");
      expect(url.searchParams.get("key")).toBe(entry.path);
      expect(entry.thumbnailUrl).toBeNull();
    }
    expect(props.defaultActiveIndex).toBe(0);
    expect(props.onOpenAlternative).toBeUndefined();
    expect(fixture.native).not.toHaveBeenCalled();
  });
  it("keeps browser defaults even when the manager has a native bridge", async () => {
    fixture.bridge = true;
    show();
    await clickPlay();
    expect(fixture.window).toHaveBeenCalledOnce();
    expect(fixture.native).not.toHaveBeenCalled();
  });
  it("uses the app's source profile for an arbitrary mapped file without fixed media categories", async () => {
    fixture.app = fixture.bridge = true;
    fixture.choice = { mode: "native" };
    const game = { origin: DataOrigin.FileSystem, key: "/games/start.exe" };

    fixture.request.mockResolvedValue({ data: [game] });
    show();
    await clickPlay();
    expect(fixture.native).toHaveBeenCalledWith(source, 42, game, true);
    expect(fixture.window).not.toHaveBeenCalled();
  });
  it("keeps a mixed collection's configured app rule and sends only the selected file", async () => {
    fixture.app = fixture.bridge = true;
    show();
    await clickPlay();
    expect(fixture.native).not.toHaveBeenCalled();
    await act(async () => fireEvent.click(screen.getByText("/album/movie.mkv")));
    expect(fixture.native).toHaveBeenCalledWith(source, 42, files[1], false);
  });
  it("honors source profiles asking for web playback or a one-time choice", async () => {
    fixture.app = fixture.bridge = true;
    fixture.choice = { mode: "browser" };
    show();
    await clickPlay();
    expect(fixture.window).toHaveBeenCalledOnce();
    cleanup();
    fixture.window.mockClear();
    fixture.choice = { mode: "ask" };
    show();
    await clickPlay();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(fixture.native).not.toHaveBeenCalled();
    expect(fixture.window).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("resource.openingRules.browser"));
    expect(fixture.window).toHaveBeenCalledOnce();
  });
  it("offers explicit web fallback after native failure without changing the profile", async () => {
    fixture.app = fixture.bridge = true;
    fixture.choice = { mode: "native" };
    fixture.first = true;
    fixture.native.mockRejectedValue(new Error("Mapped NAS is unavailable"));
    show();
    await clickPlay();
    expect(screen.getByRole("alert")).toHaveTextContent("Mapped NAS is unavailable");
    expect(fixture.window).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("resource.openingRules.browser"));
    expect(fixture.window).toHaveBeenCalledOnce();
    expect(fixture.choice).toEqual({ mode: "native" });
  });
  it("does not confuse failed file discovery with an empty resource and allows retry", async () => {
    fixture.request.mockRejectedValueOnce(new Error("Owner offline"));
    show();
    await clickPlay();
    expect(screen.getByRole("alert")).toHaveTextContent("Owner offline");
    expect(screen.queryByText("management.play.noFiles")).toBeNull();
    await act(async () => fireEvent.click(screen.getByText("management.retry")));
    expect(fixture.window).toHaveBeenCalledOnce();
  });
  it("does not launch a stale result after another owner's same-numbered resource replaces the card", async () => {
    let resolve!: (value: unknown) => void;

    fixture.request.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done;
        }),
    );
    const view = show();

    fireEvent.click(screen.getByRole("button", { name: "common.action.play" }));
    view.rerender(
      <OwnedResourcePlay
        resource={resource}
        source={{ ...source, serverId: "owner-b", libraryEpoch: "epoch-b" }}
      />,
    );
    await act(async () => resolve({ data: files }));
    expect(fixture.window).not.toHaveBeenCalled();
    expect(fixture.native).not.toHaveBeenCalled();
  });
  it("keeps native launch busy until the launch result returns", async () => {
    fixture.app = fixture.bridge = true;
    fixture.first = true;
    let done!: () => void;

    fixture.native.mockReturnValueOnce(
      new Promise<void>((resolve) => {
        done = resolve;
      }),
    );
    show();
    fireEvent.click(screen.getByRole("button", { name: "common.action.play" }));
    await waitFor(() => expect(fixture.native).toHaveBeenCalled());
    expect(screen.getByRole("button", { name: "common.action.play" })).toBeDisabled();
    await act(async () => done());
    expect(screen.getByRole("button", { name: "common.action.play" })).toBeEnabled();
  });
  it("browses owner-resolved images when no native playable-file rule exists", async () => {
    fixture.request.mockResolvedValueOnce({
      data: { items: [files[0]], offset: 0, hasMore: false, playableFilesConfigured: false },
    });
    show();
    await clickPlay();
    expect(fixture.request).toHaveBeenNthCalledWith(1, "resourceBrowserFiles", {
      id: 42,
      query: { offset: ["0"], pageSize: ["100"] },
    });
    expect(fixture.window.mock.lastCall![1].entries[0].path).toBe(files[0].key);
    expect(fixture.window.mock.lastCall![1].onOpenAlternative).toBeUndefined();
    expect(fixture.native).not.toHaveBeenCalled();
  });
  it("does not replace an app's empty native rule with automatic browser playback", async () => {
    fixture.app = fixture.bridge = true;
    fixture.request.mockResolvedValueOnce({ data: [] }).mockResolvedValueOnce({
      data: { items: [files[0]], offset: 0, hasMore: false, playableFilesConfigured: false },
    });
    show();
    await clickPlay();
    expect(fixture.window).not.toHaveBeenCalled();
    expect(fixture.native).not.toHaveBeenCalled();
    expect(screen.queryByText("resource.playOnThisDevice.desktopPlayer")).toBeNull();
    fireEvent.click(screen.getByText("resource.openingRules.browser"));
    expect(fixture.window).toHaveBeenCalledOnce();
    await fixture.window.mock.lastCall![1].onOpenAlternative(files[0].key);
    expect(fixture.native).toHaveBeenCalledWith(source, 42, files[0], true);
  });
  it("keeps explicitly configured zero matches empty", async () => {
    fixture.request
      .mockResolvedValueOnce({
        data: { items: [], offset: 0, hasMore: false, playableFilesConfigured: true },
      })
      .mockResolvedValueOnce({ data: [] });
    show();
    await clickPlay();
    expect(screen.getByText("management.play.noFiles")).toBeInTheDocument();
    expect(fixture.window).not.toHaveBeenCalled();
    expect(fixture.native).not.toHaveBeenCalled();
  });
  it("shows paging for large browser collections and opens only the selected page", async () => {
    fixture.request
      .mockResolvedValueOnce({
        data: { items: [files[0]], offset: 0, nextOffset: 100, hasMore: true },
      })
      .mockResolvedValueOnce({ data: { items: [files[1]], offset: 100, hasMore: false } });
    show();
    await clickPlay();
    expect(screen.getByText("management.play.browserPageHelp")).toBeInTheDocument();
    expect(fixture.window).not.toHaveBeenCalled();
    await act(async () => fireEvent.click(screen.getByText("management.next")));
    expect(fixture.request).toHaveBeenLastCalledWith("resourceBrowserFiles", {
      id: 42,
      query: { offset: ["100"], pageSize: ["100"] },
    });
    expect(screen.getByText("management.next")).toBeDisabled();
    fireEvent.click(screen.getByText("management.play.openPage"));
    expect(fixture.window.mock.lastCall![1].entries).toHaveLength(1);
    expect(fixture.window.mock.lastCall![1].entries[0].path).toBe(files[1].key);
  });
});
