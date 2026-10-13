import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { automaticPlaybackPreferences } from "./playbackPreferences";
import PlaybackQuickActions from "./PlaybackQuickActions";

import PlayControl, { type PlayControlPortalProps } from ".";

import { DataOrigin, DataStatus } from "@/sdk/constants";

const fixture = vi.hoisted(() => ({
  desktop: false,
  ready: true,
  preferences: {} as any,
  groups: [] as any[],
  browser: vi.fn(),
  native: vi.fn(),
  portal: vi.fn(),
  save: vi.fn(),
  after: vi.fn(),
  first: false,
}));

vi.mock("./usePlaybackPreferences", () => ({
  usePlaybackPreferences: () => ({
    preferences: fixture.preferences,
    ready: fixture.ready,
    save: fixture.save,
  }),
}));
vi.mock("./NoPlayableFilesModal", () => ({ default: () => null }));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: () => ({ data: { resource: { autoSelectFirstPlayableFile: fixture.first } } }),
}));
vi.mock("@/stores/remoteAccess", () => ({ useUserSideActionsRunHere: () => fixture.desktop }));
vi.mock("@/hooks/usePlayableItemResolution", () => ({
  usePlayableItemResolution: () => ({
    groups: fixture.groups,
    overallStatus: "ready",
    triggerDiscovery: vi.fn(),
  }),
}));
vi.mock("@/hooks/useResourceBrowserPlayer", () => ({
  useResourceBrowserPlayer: () => fixture.browser,
}));
vi.mock("@/hooks/useOpenResourceDirectory", () => ({
  useOpenResourceDirectory: () => ({ open: vi.fn(), label: "openFolder" }),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: fixture.portal }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    resource: { playResourceItem: fixture.native, markResourceAsPlayed: vi.fn() },
    file: { getRawFileUrl: () => "/file/raw" },
  },
}));
vi.mock("@/components/BusinessConstants", () => ({ default: { pathSeparator: "/" } }));
vi.mock("@/components/utils", () => ({
  standardizePath: (path: string) => path,
  splitPathIntoSegments: (path: string) => path.split("/"),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, visible }: any) => (visible ? <div role="dialog">{children}</div> : null),
  Button: ({ children, onPress, isDisabled, ...props }: any) => (
    <button aria-label={props["aria-label"]} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Checkbox: () => null,
  Select: () => null,
  Tooltip: ({ children }: any) => children,
  toast: { success: vi.fn(), danger: vi.fn() },
}));

let portal: PlayControlPortalProps;
const Portal = (props: PlayControlPortalProps) => {
  portal = props;

  return (
    <>
      <button onClick={() => props.onPlaySource(props.sources[0]!.source)}>main</button>
      <button onClick={props.onChoosePlayback}>choose</button>
      <button onClick={props.onPlayInBrowser}>browser</button>
      {props.onPlayVideos && <button onClick={props.onPlayVideos}>videos</button>}
    </>
  );
};
const resource = {
  id: 42,
  isFile: true,
  path: "/album/001.jpg",
  displayName: "Album",
  dataStates: [],
} as any;
const fs = (paths: string[]) => ({
  origin: DataOrigin.FileSystem,
  status: DataStatus.Ready,
  items: paths.map((key) => ({ origin: DataOrigin.FileSystem, key })),
});
const steam = {
  origin: DataOrigin.Steam,
  status: DataStatus.Ready,
  items: [{ origin: DataOrigin.Steam, key: "123" }],
};
const show = (value = resource) =>
  render(<PlayControl PortalComponent={Portal} afterPlaying={fixture.after} resource={value} />);

beforeEach(() => {
  vi.clearAllMocks();
  fixture.desktop = false;
  fixture.ready = true;
  fixture.first = false;
  fixture.preferences = automaticPlaybackPreferences();
  fixture.groups = [fs([resource.path])];
  fixture.browser.mockResolvedValue(undefined);
  fixture.native.mockResolvedValue({ code: 0 });
  fixture.save.mockResolvedValue(undefined);
});
afterEach(cleanup);

describe("playback routing on the current device", () => {
  it("defaults browser images to direct web viewing and selects local files ahead of host-only sources", async () => {
    fixture.groups = [steam, fs([resource.path])];
    show();
    expect(portal.sources[0]!.source).toBe(DataOrigin.FileSystem);
    expect(portal.playbackCaption).toBe("resource.playOnThisDevice.viewInBrowser");
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.browser).toHaveBeenCalledWith(resource, resource.path);
    expect(fixture.native).not.toHaveBeenCalled();
    expect(fixture.portal).not.toHaveBeenCalled();
  });

  it("opens multi-image folders as a gallery rather than a file-selection dialog", async () => {
    const folder = { ...resource, isFile: false, path: "/album" };

    fixture.groups = [fs(["/album/001.jpg", "/album/002.png"])];
    show(folder);
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.browser).toHaveBeenCalledWith(folder, undefined);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(fixture.portal).not.toHaveBeenCalled();
  });

  it("preserves configured desktop image playback and managed-server source API routing", async () => {
    fixture.desktop = true;
    show();
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: resource.path,
    });
    expect(fixture.browser).not.toHaveBeenCalled();
    expect(fixture.after).toHaveBeenCalledTimes(1);
    cleanup();
    fixture.groups = [steam, fs([resource.path])];
    show();
    expect(portal.sources[0]!.source).toBe(DataOrigin.Steam);
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.native).toHaveBeenLastCalledWith(42, { origin: DataOrigin.Steam, key: "123" });
  });

  it("honors a desktop web preference while retaining the configured-player alternative", async () => {
    fixture.desktop = true;
    fixture.preferences.image = { mode: "browser" };
    fixture.groups = [steam, fs([resource.path])];
    show();
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.browser).toHaveBeenCalledWith(resource, resource.path);
    fireEvent.click(screen.getByText("choose"));
    const choice = fixture.portal.mock.lastCall![1];

    expect(choice.kind).toBe("image");
    expect(choice.onRememberChoice).toBe(fixture.save);
    await act(async () => choice.onPlayNative());
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: resource.path,
    });
  });

  it("asks without opening either player when the media default requests a choice", () => {
    fixture.preferences.image = { mode: "ask" };
    show();
    fireEvent.click(screen.getByText("main"));
    expect(fixture.portal).toHaveBeenCalledTimes(1);
    expect(fixture.portal.mock.lastCall![1]).toMatchObject({
      filePath: resource.path,
      kind: "image",
      onPlayNative: undefined,
    });
    expect(fixture.browser).not.toHaveBeenCalled();
    expect(fixture.native).not.toHaveBeenCalled();
  });

  it.each([true, false])(
    "keeps pathless Steam resources usable without creating a file chooser (desktop=%s)",
    async (desktop) => {
      fixture.desktop = desktop;
      fixture.groups = [steam];
      show({ ...resource, path: undefined });
      expect(portal.browserPlaybackAvailable).toBe(false);
      fireEvent.click(screen.getByText("choose"));
      expect(fixture.portal).not.toHaveBeenCalled();
      await act(async () => fireEvent.click(screen.getByText("main")));
      if (desktop)
        expect(fixture.native).toHaveBeenCalledWith(42, { origin: DataOrigin.Steam, key: "123" });
      else expect(fixture.native).not.toHaveBeenCalled();
      expect(fixture.browser).not.toHaveBeenCalled();
    },
  );

  it("offers explicit browser preview even for desktop-native defaults and waits for preferences before main playback", async () => {
    fixture.desktop = true;
    fixture.ready = false;
    show();
    fireEvent.click(screen.getByText("main"));
    expect(fixture.native).not.toHaveBeenCalled();
    expect(portal.playbackReady).toBe(false);
    fixture.ready = true;
    cleanup();
    show();
    await act(async () => fireEvent.click(screen.getByText("browser")));
    expect(fixture.browser).toHaveBeenCalledWith(resource, undefined);
    expect(fixture.native).not.toHaveBeenCalled();
  });
});

describe("compact playback alternatives", () => {
  const props = (overrides: Partial<PlayControlPortalProps>): PlayControlPortalProps => ({
    status: "ready",
    sources: [],
    hasPath: true,
    isFile: true,
    fsDiscoveryStatus: "ready",
    onPlaySource: vi.fn(),
    onOpenFolder: vi.fn(),
    openFolderLabel: "Open folder",
    onNotFound: vi.fn(),
    triggerFsDiscovery: vi.fn(),
    ...overrides,
  });

  it("avoids duplicating a browser main action and keeps the chooser reachable", () => {
    const choose = vi.fn();
    const browser = vi.fn();

    render(
      <PlaybackQuickActions
        {...props({
          browserPlaybackAvailable: true,
          browserPlaybackIsDefault: true,
          playbackReady: true,
          sources: [fs([resource.path])].map((g) => ({ source: g.origin, items: g.items })),
          onChoosePlayback: choose,
          onPlayInBrowser: browser,
        })}
      />,
    );
    expect(screen.queryByLabelText("resource.playback.browserQuickAction")).toBeNull();
    fireEvent.click(screen.getByLabelText("resource.playback.chooseMethod"));
    expect(choose).toHaveBeenCalledOnce();
    expect(browser).not.toHaveBeenCalled();
  });

  it("adds the explicit browser alternative beside a desktop-native main action", () => {
    const browser = vi.fn();

    render(
      <PlaybackQuickActions
        {...props({
          browserPlaybackAvailable: true,
          playbackReady: true,
          sources: [],
          onChoosePlayback: vi.fn(),
          onPlayInBrowser: browser,
        })}
      />,
    );
    fireEvent.click(screen.getByLabelText("resource.playback.browserQuickAction"));
    expect(browser).toHaveBeenCalledOnce();
  });
});

describe("mixed media and selecting individual videos", () => {
  const folder = { ...resource, isFile: false, path: "/album" };
  const mixed = ["/album/001.jpg", "/album/movie.mkv"];

  it("groups discovered files safely when their resource directory path is absent", async () => {
    fixture.desktop = true;
    fixture.groups = [fs(["/album/first.mp4", "/album/second.mp4"])];
    show({ ...folder, path: undefined });
    fireEvent.click(screen.getByText("main"));
    expect(screen.getByText("first.mp4")).toBeInTheDocument();
    await act(async () => fireEvent.click(screen.getByText("second.mp4")));
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: "/album/second.mp4",
    });
  });

  it.each([{ paths: mixed }, { paths: [...mixed].reverse() }])(
    "defaults the full desktop mixed collection to web regardless of first file",
    async ({ paths }) => {
      fixture.desktop = true;
      fixture.first = true;
      fixture.groups = [steam, fs(paths)];
      show(folder);
      expect(portal.sources[0]!.source).toBe(DataOrigin.FileSystem);
      expect(portal.browserPlaybackIsDefault).toBe(true);
      await act(async () => fireEvent.click(screen.getByText("main")));
      expect(fixture.browser).toHaveBeenCalledWith(folder, undefined);
      expect(fixture.native).not.toHaveBeenCalled();
      expect(fixture.portal).not.toHaveBeenCalled();
    },
  );

  it("retains an explicit mixed native preference, passing only the chosen item to the desktop API", async () => {
    fixture.desktop = true;
    fixture.preferences.other = { mode: "native" };
    fixture.groups = [fs(mixed)];
    show(folder);
    fireEvent.click(screen.getByText("main"));
    expect(fixture.browser).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    await act(async () => fireEvent.click(screen.getByText("movie.mkv")));
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: mixed[1],
    });
  });

  it.each([true, false])(
    "retains Ask for the entire mixed collection, and exposes video selection without guessing an AV scheme (desktop=%s)",
    (desktop) => {
      fixture.desktop = desktop;
      fixture.preferences.other = { mode: "ask" };
      fixture.groups = [fs(mixed)];
      show(folder);
      fireEvent.click(screen.getByText("main"));
      expect(fixture.portal.mock.lastCall![1]).toMatchObject({
        kind: "other",
        isMixed: true,
        onPlayVideos: expect.any(Function),
      });
      expect(fixture.browser).not.toHaveBeenCalled();
      expect(fixture.native).not.toHaveBeenCalled();
    },
  );

  it("asks for a concrete file for a browser-native mixed preference instead of applying a handler to the first file", () => {
    fixture.preferences.other = { mode: "native", nativeSchemeId: "potplayer-windows" };
    fixture.groups = [fs(mixed)];
    show(folder);
    fireEvent.click(screen.getByText("main"));
    expect(fixture.portal.mock.lastCall![1]).toMatchObject({ isMixed: true });
    expect(fixture.browser).not.toHaveBeenCalled();
    expect(fixture.native).not.toHaveBeenCalled();
  });

  it.each([true, false])(
    "directly selects a sole video, preserving original gallery for web and single filepath for native (desktop=%s)",
    async (desktop) => {
      fixture.desktop = desktop;
      fixture.groups = [fs(mixed)];
      show(folder);
      fireEvent.click(screen.getByText("videos"));
      const choice = fixture.portal.mock.lastCall![1];

      expect(choice).toMatchObject({
        resource: folder,
        filePath: mixed[1],
        kind: "video",
        isMixed: false,
      });
      expect(screen.queryByRole("dialog")).toBeNull();
      await act(async () => choice.onPlayInBrowser());
      expect(fixture.browser).toHaveBeenCalledWith(folder, mixed[1]);
      if (desktop) {
        await act(async () => choice.onPlayNative());
        expect(fixture.native).toHaveBeenCalledWith(42, {
          origin: DataOrigin.FileSystem,
          key: mixed[1],
        });
      } else expect(choice.onPlayNative).toBeUndefined();
    },
  );

  it("lists only videos when multiple exist, even when auto-select-first is enabled", () => {
    fixture.first = true;
    fixture.groups = [fs([...mixed, "/album/second.MP4", "/album/readme.txt"])];
    show(folder);
    fireEvent.click(screen.getByText("videos"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.queryByText("001.jpg")).toBeNull();
    expect(screen.queryByText("readme.txt")).toBeNull();
    expect(screen.getByText("movie.mkv")).toBeInTheDocument();
    expect(fixture.portal).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("second.MP4"));
    expect(fixture.portal.mock.lastCall![1]).toMatchObject({
      filePath: "/album/second.MP4",
      kind: "video",
      isMixed: false,
    });
    expect(fixture.native).not.toHaveBeenCalled();
    expect(fixture.browser).not.toHaveBeenCalled();
  });

  it("bounds a large video selector to 50 initial files and adds another page on request", () => {
    fixture.groups = [
      fs([
        "/album/cover.jpg",
        ...Array.from({ length: 1000 }, (_, index) => `/album/video-${index}.mp4`),
      ]),
    ];
    show(folder);
    fireEvent.click(screen.getByText("videos"));
    const rows = () => screen.queryAllByText(/^video-\d+\.mp4$/);

    expect(rows()).toHaveLength(50);
    expect(screen.queryByText("video-50.mp4")).toBeNull();
    expect(screen.getByText("resource.playback.videoCount")).toBeInTheDocument();
    fireEvent.click(screen.getByText("resource.playback.loadMoreVideos"));
    expect(rows()).toHaveLength(100);
    fireEvent.click(screen.getByText("video-99.mp4"));
    expect(fixture.portal.mock.lastCall![1]).toMatchObject({
      filePath: "/album/video-99.mp4",
      kind: "video",
    });
  });

  it("hides video actions during discovery and updates from completed effective sources, dropping absent/invalid video paths", () => {
    fixture.groups = [steam, { ...fs(mixed), status: DataStatus.NotStarted }];
    const view = show(folder);

    expect(portal.onPlayVideos).toBeUndefined();
    expect(portal.sources.some((source) => source.source === DataOrigin.FileSystem)).toBe(false);
    fixture.groups = [{ ...fs(mixed), status: DataStatus.Failed }];
    view.rerender(<PlayControl PortalComponent={Portal} resource={folder} />);
    expect(portal.onPlayVideos).toBeUndefined();
    fixture.groups = [fs([])];
    view.rerender(<PlayControl PortalComponent={Portal} resource={folder} />);
    expect(portal.onPlayVideos).toBeUndefined();
    fixture.groups = [fs([...mixed, "", "  ", undefined as unknown as string])];
    view.rerender(<PlayControl PortalComponent={Portal} resource={folder} />);
    expect(portal.onPlayVideos).toBeTypeOf("function");
    expect(portal.sources[0]!.items).toHaveLength(2);
    fixture.groups = [fs([mixed[0]!])];
    view.rerender(<PlayControl PortalComponent={Portal} resource={folder} />);
    expect(portal.onPlayVideos).toBeUndefined();
  });
});
