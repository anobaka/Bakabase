import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PlaybackQuickActions from "./PlaybackQuickActions";
import { ResourceCardPlaybackButton, ResourceDetailPlaybackButton } from "./PlaybackButtons";

import PlayControl, { type PlayControlPortalProps } from ".";

import { ClientMode, DataOrigin, DataStatus, ResourceSource } from "@/sdk/constants";

const fixture = vi.hoisted(() => ({
  desktop: false,
  appWindow: undefined as boolean | undefined,
  groups: [] as any[],
  browser: vi.fn(),
  native: vi.fn(),
  portal: vi.fn(),
  after: vi.fn(),
  first: false,
  external: vi.fn(),
  retry: vi.fn(),
  serverId: undefined as string | undefined,
  managing: false,
  opening: undefined as { mode: string } | undefined,
  overrideReady: true,
  overrideUnavailable: false,
  reloadOverrides: vi.fn(),
  overrideTarget: vi.fn(),
}));

vi.mock("@/utils/clientSurface", () => ({
  isDesktopAppWindow: () => fixture.appWindow ?? fixture.desktop,
}));

vi.mock("@/utils/openExternalUrl", () => ({ openExternalUrl: fixture.external }));
vi.mock("./NoPlayableFilesModal", () => ({ default: () => null }));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: () => ({ data: { resource: { autoSelectFirstPlayableFile: fixture.first } } }),
}));
vi.mock("@/stores/remoteAccess", () => ({
  useUserSideActionsRunHere: () => fixture.desktop,
  useRemoteAccessStore: (selector: (value: unknown) => unknown) =>
    selector({
      context: "known",
      serverId: fixture.serverId,
      serverName: "Library server",
      clientMode: fixture.managing
        ? ClientMode.PureClient
        : fixture.desktop
          ? ClientMode.AllInOne
          : ClientMode.RemoteBrowser,
    }),
}));
vi.mock("@/features/playback/resourceProfiles", () => ({
  useSourceResourceProfile: (target: string | undefined, resourceId: number) => {
    fixture.overrideTarget(target, resourceId);

    return {
      ready: fixture.overrideReady,
      unavailable: fixture.overrideUnavailable,
      reload: fixture.reloadOverrides,
      snapshot: { openingMode: fixture.opening?.mode },
      choice: () => (target ? fixture.opening : undefined),
    };
  },
  ResourceProfileLink: ({ compact, target }: any) => (
    <button data-compact={compact} data-target={target}>
      override settings
    </button>
  ),
}));
vi.mock("@/hooks/usePlayableItemResolution", () => ({
  usePlayableItemResolution: () => ({
    groups: fixture.groups,
    overallStatus: "ready",
    triggerDiscovery: fixture.retry,
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
  Dropdown: ({ children }: any) => <div>{children}</div>,
  DropdownTrigger: ({ children }: any) => children,
  DropdownMenu: ({ children }: any) => <div role="menu">{children}</div>,
  DropdownItem: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
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
      {props.discoveryFailed && (
        <button onClick={props.onDiscoveryFailure}>discovery failure</button>
      )}
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
  global.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
  vi.spyOn(window, "open").mockImplementation(() => null);
  fixture.desktop = false;
  fixture.appWindow = undefined;
  fixture.serverId = undefined;
  fixture.managing = false;
  fixture.opening = undefined;
  localStorage.clear();
  fixture.overrideReady = true;
  fixture.overrideUnavailable = false;
  fixture.reloadOverrides.mockResolvedValue(undefined);
  fixture.first = false;
  fixture.groups = [fs([resource.path])];
  fixture.browser.mockResolvedValue(undefined);
  fixture.native.mockResolvedValue({ code: 0 });
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("playback routing on the current device", () => {
  it("offers desktop guidance for discovered files without opening it automatically", () => {
    render(<PlayControl PortalComponent={ResourceCardPlaybackButton} resource={resource} />);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByText("resource.desktopPlayback.action")).toBeInTheDocument();
    expect(fixture.native).not.toHaveBeenCalled();
    expect(fixture.browser).not.toHaveBeenCalled();
  });
  it.each([DataStatus.Ready, DataStatus.Failed, DataStatus.NotStarted])(
    "does not offer desktop installation when file discovery has no candidates (%s)",
    (status) => {
      fixture.groups = [{ origin: DataOrigin.FileSystem, status, items: [] }];
      render(<PlayControl PortalComponent={ResourceCardPlaybackButton} resource={resource} />);
      expect(screen.queryByText("resource.desktopPlayback.action")).not.toBeInTheDocument();
    },
  );
  it("allows guidance in the chooser only after file candidates have been found", () => {
    show();
    fireEvent.click(screen.getByText("choose"));
    expect(fixture.portal.mock.calls.at(-1)![1].offerDesktopGuidance).toBe(true);
    cleanup();
    fixture.groups = [fs([])];
    show();
    fireEvent.click(screen.getByText("choose"));
    expect(fixture.portal.mock.calls.at(-1)![1].offerDesktopGuidance).toBe(false);
  });
  it("asks on an initial override read failure and still allows explicit browser/native choices", async () => {
    fixture.desktop = true;
    fixture.managing = true;
    fixture.serverId = "library";
    fixture.overrideReady = false;
    fixture.overrideUnavailable = true;
    show();
    fireEvent.click(screen.getByText("main"));
    expect(screen.getByText("resource.playbackPolicy.rulesUnavailable")).toBeInTheDocument();
    expect(fixture.native).not.toHaveBeenCalled();
    expect(fixture.browser).not.toHaveBeenCalled();
    await act(async () => fireEvent.click(screen.getByText("resource.playbackPolicy.retryRules")));
    expect(fixture.reloadOverrides).toHaveBeenCalledOnce();
    await act(async () => fireEvent.click(screen.getByText("browser")));
    expect(fixture.browser).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByText("choose"));
    await act(async () => fixture.portal.mock.calls.at(-1)![1].onPlayNative());
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: resource.path,
      explicitChoice: true,
    });
  });
  it("keeps website and Steam source actions independent of pending playback rules", async () => {
    fixture.overrideReady = false;
    fixture.overrideUnavailable = true;
    fixture.groups = [
      steam,
      {
        origin: DataOrigin.DLsite,
        status: DataStatus.Ready,
        items: [{ origin: DataOrigin.DLsite, key: "RJ123456" }],
      },
    ];
    show({ ...resource, sourceLinks: [{ source: ResourceSource.DLsite, sourceKey: "RJ123456" }] });
    await act(async () => portal.onPlaySource(DataOrigin.Steam));
    expect(window.open).toHaveBeenCalledWith("steam://rungameid/123", "_self");
    await act(async () => portal.originalSourceActions![0]!.onOpen());
    expect(fixture.external).toHaveBeenCalled();
    expect(fixture.native).not.toHaveBeenCalled();
  });
  it("uses target-specific native preferences on a managed app and keeps explicit web available", async () => {
    fixture.desktop = true;
    fixture.managing = true;
    fixture.serverId = "library";
    fixture.opening = { mode: "native" };
    show();
    expect(fixture.overrideTarget).toHaveBeenCalledWith("server:library", 42);
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: resource.path,
      explicitChoice: true,
    });
    expect(fixture.browser).not.toHaveBeenCalled();
    await act(async () => fireEvent.click(screen.getByText("browser")));
    expect(fixture.browser).toHaveBeenCalledOnce();
    expect(fixture.overrideTarget).toHaveBeenCalledWith("server:library", 42);
  });
  it("does not apply cross-device overrides to the app's own resource profiles", async () => {
    fixture.desktop = true;
    fixture.serverId = "self";
    fixture.opening = { mode: "browser" };
    show();
    expect(fixture.overrideTarget).toHaveBeenCalledWith(undefined, 42);
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.native).toHaveBeenCalledOnce();
    expect(fixture.browser).not.toHaveBeenCalled();
  });
  it("waits for the target's saved rules before performing a default action", () => {
    fixture.serverId = "library";
    fixture.overrideReady = false;
    show();
    expect(portal.playbackReady).toBe(false);
    fireEvent.click(screen.getByText("main"));
    expect(fixture.browser).not.toHaveBeenCalled();
    expect(fixture.native).not.toHaveBeenCalled();
  });
  it("defaults an ordinary localhost browser to web while retaining an explicit local native choice", async () => {
    fixture.desktop = true;
    fixture.appWindow = false;
    show();
    expect(portal.browserPlaybackIsDefault).toBe(true);
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.browser).toHaveBeenCalledOnce();
    expect(fixture.native).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("choose"));
    await act(async () => fixture.portal.mock.lastCall![1].onPlayNative());
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: resource.path,
      explicitChoice: true,
    });
  });

  it("retains an explicit native preference and offers choices when the requested launch fails", async () => {
    fixture.desktop = true;
    fixture.managing = true;
    fixture.serverId = "library";
    fixture.opening = { mode: "native" };
    fixture.native.mockResolvedValue({ code: 409, message: "Player unavailable" });
    show();
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: resource.path,
      explicitChoice: true,
    });
    expect(fixture.portal.mock.lastCall![1]).toMatchObject({
      unavailableReason: "resource.playbackPolicy.nativeFailed",
      filePath: resource.path,
    });
    expect(fixture.opening).toEqual({ mode: "native" });
    expect(fixture.browser).not.toHaveBeenCalled();
  });

  it.each([
    { desktop: false, path: "/album/001.jpg", mode: "native" },
    { desktop: false, path: "/album/movie.mkv", mode: "ask" },
    { desktop: true, path: "/album/001.jpg", mode: "browser" },
    { desktop: true, path: "/album/game.exe", mode: "ask" },
  ])(
    "ignores legacy media preferences (desktop=$desktop, path=$path, saved=$mode)",
    async ({ desktop, path, mode }) => {
      fixture.desktop = desktop;
      localStorage.setItem(
        "bakabase.playback.device.preferences.v1",
        JSON.stringify({
          version: 1,
          ...Object.fromEntries(
            ["image", "text", "audio", "video", "other"].map((kind) => [
              kind,
              { mode, nativeSchemeId: "potplayer-windows" },
            ]),
          ),
        }),
      );
      const storage = vi.spyOn(Storage.prototype, "getItem");

      fixture.groups = [fs([path])];
      show({ ...resource, path });
      await act(async () => fireEvent.click(screen.getByText("main")));
      expect(storage).not.toHaveBeenCalledWith("bakabase.playback.device.preferences.v1");
      if (desktop) {
        expect(fixture.native).toHaveBeenCalledWith(42, {
          origin: DataOrigin.FileSystem,
          key: path,
        });
        expect(fixture.browser).not.toHaveBeenCalled();
      } else {
        expect(fixture.browser).toHaveBeenCalledOnce();
        expect(fixture.native).not.toHaveBeenCalled();
      }
      expect(fixture.portal).not.toHaveBeenCalled();
    },
  );

  it("routes a failed browser preview's alternative to that precise file and hides unsupported browser-only alternatives", async () => {
    show();
    await act(async () => fireEvent.click(screen.getByText("browser")));
    const options = fixture.browser.mock.lastCall![2];

    expect(options.canOpenAlternative("/album/cover.jpg")).toBe(false);
    expect(options.canOpenAlternative("/album/video.mkv")).toBe(true);
    act(() => options.onOpenAlternative("/album/video.mkv"));
    expect(fixture.portal.mock.lastCall![1]).toMatchObject({
      filePath: "/album/video.mkv",
      isMixed: false,
    });
    expect(fixture.native).not.toHaveBeenCalled();
  });
  it("defaults browser images to direct web viewing and selects local files ahead of host-only sources", async () => {
    fixture.groups = [steam, fs([resource.path])];
    show();
    expect(portal.sources[0]!.source).toBe(DataOrigin.FileSystem);
    expect(portal.playbackCaption).toBe("resource.playOnThisDevice.viewInBrowser");
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.browser).toHaveBeenCalledWith(
      resource,
      resource.path,
      expect.objectContaining({
        onOpenAlternative: expect.any(Function),
        canOpenAlternative: expect.any(Function),
      }),
    );
    expect(fixture.native).not.toHaveBeenCalled();
    expect(fixture.portal).not.toHaveBeenCalled();
  });

  it("opens multi-image folders as a gallery rather than a file-selection dialog", async () => {
    const folder = { ...resource, isFile: false, path: "/album" };

    fixture.groups = [fs(["/album/001.jpg", "/album/002.png"])];
    show(folder);
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.browser).toHaveBeenCalledWith(
      folder,
      undefined,
      expect.objectContaining({
        onOpenAlternative: expect.any(Function),
        canOpenAlternative: expect.any(Function),
      }),
    );
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

  it("honors the source resource profile web mode while retaining the configured-player alternative", async () => {
    fixture.desktop = true;
    fixture.managing = true;
    fixture.serverId = "library";
    fixture.opening = { mode: "browser" };
    fixture.groups = [steam, fs([resource.path])];
    show();
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.browser).toHaveBeenCalledWith(
      resource,
      resource.path,
      expect.objectContaining({
        onOpenAlternative: expect.any(Function),
        canOpenAlternative: expect.any(Function),
      }),
    );
    fireEvent.click(screen.getByText("choose"));
    const choice = fixture.portal.mock.lastCall![1];

    expect(choice.onRememberChoice).toBeUndefined();
    await act(async () => choice.onPlayNative());
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      explicitChoice: true,
      key: resource.path,
    });
  });

  it("asks when the matching source resource profile requests a choice", () => {
    fixture.desktop = true;
    fixture.managing = true;
    fixture.serverId = "library";
    fixture.opening = { mode: "ask" };
    show();
    fireEvent.click(screen.getByText("main"));
    expect(fixture.portal).toHaveBeenCalledTimes(1);
    expect(fixture.portal.mock.lastCall![1]).toMatchObject({
      filePath: resource.path,
      onPlayNative: expect.any(Function),
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

  it("offers one-time browser preview without changing normal desktop playback", async () => {
    fixture.desktop = true;
    show();
    await act(async () => fireEvent.click(screen.getByText("browser")));
    expect(fixture.browser).toHaveBeenCalledWith(resource, undefined, expect.anything());
    expect(fixture.native).not.toHaveBeenCalled();
    expect(portal.playbackReady).toBe(true);
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: resource.path,
    });
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

  it.each([true, false])(
    "does not add opening settings beside the main action (web default: %s)",
    (browserDefault) => {
      render(
        <PlaybackQuickActions
          {...props({
            browserPlaybackAvailable: true,
            browserPlaybackIsDefault: browserDefault,
            playbackReady: true,
            sources: [fs([resource.path])].map((g) => ({ source: g.origin, items: g.items })),
            onChoosePlayback: vi.fn(),
            onPlayInBrowser: vi.fn(),
          })}
        />,
      );
      expect(screen.queryByLabelText("resource.playback.browserQuickAction")).toBeNull();
      expect(screen.queryByLabelText("resource.playback.chooseMethod")).toBeNull();
      expect(screen.queryByText("override settings")).toBeNull();
      expect(screen.queryByRole("button")).toBeNull();
    },
  );

  it("ports hover actions outside the cover without changing direct play or stealing focus", () => {
    const folder = vi.fn();
    const source = vi.fn();
    const videos = vi.fn();
    const original = vi.fn();
    const { container } = render(
      <ResourceCardPlaybackButton
        {...props({
          sources: [
            { source: DataOrigin.FileSystem, items: [] },
            { source: DataOrigin.Steam, items: [] },
          ],
          onPlaySource: source,
          onOpenFolder: folder,
          onPlayVideos: videos,
          originalSourceActions: [{ key: "source", label: "Original webpage", onOpen: original }],
        })}
      />,
    );
    const primary = container.querySelector("button")!;
    const anchor = screen.getByRole("group", { name: "resource.playOnThisDevice.moreOptions" });

    fireEvent.pointerEnter(anchor, { pointerType: "mouse" });
    const menu = screen.getByRole("menu");

    expect(container).not.toContainElement(menu);
    expect(document.activeElement).toBe(document.body);
    fireEvent.click(primary);
    expect(source).toHaveBeenCalledWith(DataOrigin.FileSystem);
    fireEvent.pointerEnter(anchor, { pointerType: "mouse" });
    fireEvent.click(screen.getByRole("menuitem", { name: "Open folder" }));
    fireEvent.pointerEnter(anchor, { pointerType: "mouse" });
    fireEvent.click(screen.getByRole("menuitem", { name: "resource.playback.playVideos" }));
    fireEvent.pointerEnter(anchor, { pointerType: "mouse" });
    fireEvent.click(screen.getByRole("menuitem", { name: "Original webpage" }));
    expect(folder).toHaveBeenCalledOnce();
    expect(videos).toHaveBeenCalledOnce();
    expect(original).toHaveBeenCalledOnce();
    expect(screen.queryByLabelText("resource.playback.browserQuickAction")).toBeNull();
    expect(screen.queryByLabelText("resource.playback.chooseMethod")).toBeNull();
    expect(screen.queryByText("override settings")).toBeNull();
    const touchMenu = screen.getByRole("button", { name: "resource.playOnThisDevice.moreOptions" });

    fireEvent.pointerEnter(anchor, { pointerType: "touch" });
    fireEvent.pointerDown(touchMenu, { pointerType: "touch" });
    act(() => touchMenu.focus());
    fireEvent.pointerUp(touchMenu, { pointerType: "touch" });
    fireEvent.click(touchMenu);
    expect(menu).toHaveClass("szh-menu--state-open");
    fireEvent.click(touchMenu);
    expect(menu).toHaveClass("szh-menu--state-closed");
  });

  it("lets the keyboard enter and dismiss actions while discovery is pending", async () => {
    const open = vi.fn();

    render(
      <ResourceCardPlaybackButton
        {...props({ status: "loading", fsDiscoveryStatus: "loading", onOpenFolder: open })}
      />,
    );
    const group = screen.getByRole("group", { name: "resource.playOnThisDevice.moreOptions" });

    expect(group).toHaveAttribute("tabindex", "0");
    act(() => group.focus());
    expect(group).toHaveFocus();
    fireEvent.keyDown(group, { key: "Tab" });
    const folder = screen.getByRole("menuitem", { name: "Open folder" });

    await waitFor(() => expect(folder).toHaveFocus());
    fireEvent.keyDown(folder, { key: "Escape" });
    expect(screen.getByRole("menu", { hidden: true })).toHaveClass("szh-menu--state-closed");
    expect(group).toHaveFocus();
    fireEvent.keyDown(group, { key: "ArrowDown" });
    fireEvent.click(screen.getByRole("menuitem", { name: "Open folder" }));
    expect(open).toHaveBeenCalledOnce();
  });

  it("closes when keyboard focus leaves the anchor without entering the menu", () => {
    render(
      <>
        <button>Outside</button>
        <ResourceCardPlaybackButton
          {...props({ sources: [{ source: DataOrigin.FileSystem, items: [] }] })}
        />
      </>,
    );
    const anchor = screen.getByRole("group", { name: "resource.playOnThisDevice.moreOptions" });

    act(() => anchor.querySelector("button")!.focus());
    expect(screen.getByRole("menu")).toHaveClass("szh-menu--state-open");
    fireEvent.keyDown(anchor.querySelector("button")!, { key: "Escape" });
    expect(screen.getByRole("menu", { hidden: true })).toHaveClass("szh-menu--state-closed");
    expect(anchor.querySelector("button")).toHaveFocus();
    fireEvent.pointerEnter(anchor, { pointerType: "mouse" });
    fireEvent.keyDown(anchor.querySelector("button")!, { key: "Tab", shiftKey: true });
    act(() => screen.getByText("Outside").focus());
    expect(screen.getByRole("menu", { hidden: true })).toHaveClass("szh-menu--state-closed");
  });

  it("keeps the menu open across the hover gap and closes after leaving both surfaces", () => {
    vi.useFakeTimers();
    try {
      render(
        <ResourceCardPlaybackButton
          {...props({ sources: [{ source: DataOrigin.FileSystem, items: [] }] })}
        />,
      );
      const anchor = screen.getByRole("group", { name: "resource.playOnThisDevice.moreOptions" });

      fireEvent.pointerEnter(anchor, { pointerType: "mouse" });
      const menu = screen.getByRole("menu");

      fireEvent.pointerLeave(anchor, { pointerType: "mouse" });
      fireEvent.pointerEnter(menu.parentElement!, { pointerType: "mouse" });
      act(() => vi.advanceTimersByTime(200));
      expect(menu).toHaveClass("szh-menu--state-open");
      const hoveredItem = screen.getByRole("menuitem", { name: "Open folder" });

      fireEvent.pointerMove(hoveredItem, { pointerType: "mouse" });
      act(() => vi.advanceTimersByTime(200));
      expect(hoveredItem).toHaveFocus();
      fireEvent.pointerLeave(menu.parentElement!, { pointerType: "mouse" });
      act(() => vi.advanceTimersByTime(200));
      expect(menu).toHaveClass("szh-menu--state-closed");
    } finally {
      vi.useRealTimers();
    }
  });

  it.each([
    { x: 1190, y: 730, side: "left" },
    { x: 10, y: 730, side: "right" },
    { x: 1190, y: 10, side: "left" },
    { x: 10, y: 10, side: "right" },
  ])("keeps the actual portaled menu inside the viewport at $x,$y", ({ x, y, side }) => {
    const rect = (left: number, top: number, width: number, height: number) => ({
      x: left,
      y: top,
      left,
      top,
      width,
      height,
      right: left + width,
      bottom: top + height,
      toJSON: () => ({}),
    });

    vi.spyOn(document.documentElement, "clientWidth", "get").mockReturnValue(1280);
    vi.spyOn(window, "innerHeight", "get").mockReturnValue(800);
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function () {
      if (this.getAttribute("role") === "group") return rect(x, y, 40, 40);
      if (this.getAttribute("role") === "menu") return rect(0, 0, 220, 180);

      return rect(0, 0, 0, 0);
    });
    render(
      <ResourceCardPlaybackButton
        {...props({ sources: [{ source: DataOrigin.FileSystem, items: [] }] })}
      />,
    );
    fireEvent.pointerEnter(
      screen.getByRole("group", { name: "resource.playOnThisDevice.moreOptions" }),
      { pointerType: "mouse" },
    );
    const menu = screen.getByRole("menu");
    const left = Number.parseFloat(menu.style.left);
    const top = Number.parseFloat(menu.style.top);

    expect(menu).toHaveClass(`szh-menu--dir-${side}`);
    expect(left).toBeGreaterThanOrEqual(8);
    expect(top).toBeGreaterThanOrEqual(8);
    expect(left + 220).toBeLessThanOrEqual(1272);
    expect(top + 180).toBeLessThanOrEqual(792);
  });
});

describe("mixed media and selecting individual videos", () => {
  const folder = { ...resource, isFile: false, path: "/album" };
  const mixed = ["/album/001.jpg", "/album/movie.mkv"];

  it("keeps the mixed collection's player rule when selecting a concrete file", async () => {
    fixture.desktop = true;
    fixture.managing = true;
    fixture.serverId = "library";
    fixture.opening = { mode: "native" };
    fixture.groups = [fs(mixed)];
    show(folder);
    fireEvent.click(screen.getByText("main"));
    await act(async () => fireEvent.click(screen.getByText("movie.mkv")));
    expect(fixture.native).toHaveBeenLastCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: mixed[1],
      explicitChoice: true,
    });
    fixture.native.mockClear();
    fireEvent.click(screen.getByText("videos"));
    await act(async () => fixture.portal.mock.lastCall![1].onPlayNative());
    expect(fixture.native).toHaveBeenLastCalledWith(42, {
      origin: DataOrigin.FileSystem,
      key: mixed[1],
      explicitChoice: true,
    });
    await act(async () => fireEvent.click(screen.getByText("browser")));
    expect(fixture.browser).toHaveBeenCalledOnce();
  });

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
    "defaults the full browser mixed collection to web regardless of first file",
    async ({ paths }) => {
      fixture.first = true;
      fixture.groups = [steam, fs(paths)];
      show(folder);
      expect(portal.sources[0]!.source).toBe(DataOrigin.FileSystem);
      expect(portal.browserPlaybackIsDefault).toBe(true);
      await act(async () => fireEvent.click(screen.getByText("main")));
      expect(fixture.browser).toHaveBeenCalledWith(
        folder,
        undefined,
        expect.objectContaining({
          onOpenAlternative: expect.any(Function),
          canOpenAlternative: expect.any(Function),
        }),
      );
      expect(fixture.native).not.toHaveBeenCalled();
      expect(fixture.portal).not.toHaveBeenCalled();
    },
  );

  it.each([{ paths: mixed }, { paths: [...mixed].reverse() }])(
    "uses the configured desktop playback API for the chosen mixed file regardless of first file",
    async ({ paths }) => {
      fixture.desktop = true;
      fixture.groups = [fs(paths)];
      show(folder);
      expect(portal.browserPlaybackIsDefault).toBe(false);
      expect(portal.playbackCaption).toBe("resource.playOnThisDevice.desktopPlayer");
      fireEvent.click(screen.getByText("main"));
      expect(screen.getByRole("dialog")).toBeInTheDocument();
      expect(fixture.native).not.toHaveBeenCalled();
      await act(async () => fireEvent.click(screen.getByText("movie.mkv")));
      expect(fixture.native).toHaveBeenCalledWith(42, {
        origin: DataOrigin.FileSystem,
        key: mixed[1],
      });
      expect(fixture.browser).not.toHaveBeenCalled();
      expect(fixture.portal).not.toHaveBeenCalled();
    },
  );

  it.each([{ paths: mixed }, { paths: [...mixed].reverse() }])(
    "retains desktop auto-select-first for the configured playable mixed items",
    async ({ paths }) => {
      fixture.desktop = true;
      fixture.first = true;
      fixture.groups = [fs(paths)];
      show(folder);
      await act(async () => fireEvent.click(screen.getByText("main")));
      expect(fixture.native).toHaveBeenCalledWith(42, {
        origin: DataOrigin.FileSystem,
        key: paths[0],
      });
      expect(fixture.browser).not.toHaveBeenCalled();
      expect(screen.queryByRole("dialog")).toBeNull();
    },
  );

  it("retains configured source priority on desktop even when filesystem discovery contains mixed media", async () => {
    fixture.desktop = true;
    fixture.groups = [steam, fs(mixed)];
    show(folder);
    expect(portal.sources[0]!.source).toBe(DataOrigin.Steam);
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.native).toHaveBeenCalledWith(42, { origin: DataOrigin.Steam, key: "123" });
    expect(fixture.browser).not.toHaveBeenCalled();
  });

  it("honors a source profile web mode for a mixed collection", async () => {
    fixture.desktop = true;
    fixture.managing = true;
    fixture.serverId = "library";
    fixture.opening = { mode: "browser" };
    fixture.groups = [fs(mixed)];
    show(folder);
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.browser).toHaveBeenCalledWith(
      folder,
      undefined,
      expect.objectContaining({
        onOpenAlternative: expect.any(Function),
        canOpenAlternative: expect.any(Function),
      }),
    );
    expect(fixture.native).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("offers the configured desktop mixed action from the chooser and resolves a concrete item before playing", async () => {
    fixture.desktop = true;
    fixture.managing = true;
    fixture.serverId = "library";
    fixture.opening = { mode: "ask" };
    fixture.groups = [fs(mixed)];
    show(folder);
    fireEvent.click(screen.getByText("main"));
    const choice = fixture.portal.mock.lastCall![1];

    expect(choice).toMatchObject({ isMixed: true, onPlayNative: expect.any(Function) });
    await act(async () => choice.onPlayNative());
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(fixture.native).not.toHaveBeenCalled();
    await act(async () => fireEvent.click(screen.getByText("001.jpg")));
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      explicitChoice: true,
      key: mixed[0],
    });
    expect(fixture.browser).not.toHaveBeenCalled();
  });

  it("honors a source profile native mode, passing only the selected mixed item to the desktop API", async () => {
    fixture.desktop = true;
    fixture.managing = true;
    fixture.serverId = "library";
    fixture.opening = { mode: "native" };
    fixture.groups = [fs(mixed)];
    show(folder);
    fireEvent.click(screen.getByText("main"));
    expect(fixture.browser).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    await act(async () => fireEvent.click(screen.getByText("movie.mkv")));
    expect(fixture.native).toHaveBeenCalledWith(42, {
      origin: DataOrigin.FileSystem,
      explicitChoice: true,
      key: mixed[1],
    });
  });

  it.each([true, false])(
    "offers explicit choices for a mixed collection without creating a saved rule (desktop=%s)",
    (desktop) => {
      fixture.desktop = desktop;
      fixture.groups = [fs(mixed)];
      show(folder);
      fireEvent.click(screen.getByText("choose"));
      expect(fixture.portal.mock.lastCall![1]).toMatchObject({
        isMixed: true,
        onPlayVideos: expect.any(Function),
      });
      expect(fixture.portal.mock.lastCall![1].onRememberChoice).toBeUndefined();
      expect(fixture.browser).not.toHaveBeenCalled();
      expect(fixture.native).not.toHaveBeenCalled();
    },
  );

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
        isMixed: false,
      });
      expect(screen.queryByRole("dialog")).toBeNull();
      await act(async () => choice.onPlayInBrowser());
      expect(fixture.browser).toHaveBeenCalledWith(
        folder,
        mixed[1],
        expect.objectContaining({
          onOpenAlternative: expect.any(Function),
          canOpenAlternative: expect.any(Function),
        }),
      );
      if (desktop) {
        await act(async () => choice.onPlayNative());
        expect(fixture.native).toHaveBeenCalledWith(42, {
          origin: DataOrigin.FileSystem,
          explicitChoice: true,
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

describe("discovery failure remains separate from unavailable playback", () => {
  it("retains a working source and explains the failed file scan with a retry", async () => {
    fixture.desktop = true;
    fixture.groups = [steam, { ...fs([]), status: DataStatus.Failed, error: "NAS offline" }];
    show();
    expect(portal.status).toBe("ready");
    expect(portal.fsDiscoveryStatus).toBe("failed");
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.native).toHaveBeenCalledWith(42, { origin: DataOrigin.Steam, key: "123" });
    fireEvent.click(screen.getByText("discovery failure"));
    expect(screen.getByText("NAS offline")).toBeTruthy();
    fireEvent.click(screen.getByText("resource.playbackActions.retryDiscovery"));
    expect(fixture.retry).toHaveBeenCalledWith(DataOrigin.FileSystem);
    expect(screen.queryByText("NAS offline")).toBeNull();
  });
  it("does not present a failed scan as an empty resource", () => {
    fixture.groups = [{ ...fs([]), status: DataStatus.Failed }];
    show();
    expect(portal.status).toBe("failed");
    expect(portal.hasPath).toBe(true);
    expect(portal.browserPlaybackAvailable).toBe(true);
    act(() => portal.onNotFound());
    expect(screen.getByText("resource.playbackActions.discoveryFailureHint")).toBeTruthy();
    expect(fixture.portal).not.toHaveBeenCalled();
  });
});

describe("downloaded external resources use ordinary file playback", () => {
  const sourceLinks = [
    { source: ResourceSource.DLsite, sourceKey: "RJ123456" },
    { source: ResourceSource.ExHentai, sourceKey: "123/abcdef" },
    { source: ResourceSource.Steam, sourceKey: "123" },
  ];
  const oldSources = [
    {
      origin: DataOrigin.DLsite,
      status: DataStatus.Ready,
      items: [{ origin: DataOrigin.DLsite, key: "RJ123456" }],
    },
    {
      origin: DataOrigin.ExHentai,
      status: DataStatus.Ready,
      items: [{ origin: DataOrigin.ExHentai, key: "123/abcdef" }],
    },
  ];

  it.each([false, true])(
    "ignores stale website candidates and uses the same file defaults (app=%s)",
    async (desktop) => {
      fixture.desktop = desktop;
      fixture.groups = [...oldSources, fs([resource.path])];
      const value = { ...resource, sourceLinks };

      show(value);
      expect(portal.sources.map((source) => source.source)).toEqual([DataOrigin.FileSystem]);
      await act(async () => fireEvent.click(screen.getByText("main")));
      if (desktop)
        expect(fixture.native).toHaveBeenCalledWith(42, {
          origin: DataOrigin.FileSystem,
          key: resource.path,
        });
      else expect(fixture.browser).toHaveBeenCalledWith(value, resource.path, expect.anything());
      expect(fixture.external).not.toHaveBeenCalled();
      expect(portal.originalSourceActions).toHaveLength(3);
      act(() => portal.originalSourceActions![0]!.onOpen());
      expect(fixture.external).toHaveBeenCalledWith(
        "https://www.dlsite.com/maniax/work/=/product_id/RJ123456.html",
        !desktop,
      );
    },
  );

  it("applies target overrides to a bound DLsite resource without special origin routing", async () => {
    fixture.desktop = true;
    fixture.managing = true;
    fixture.serverId = "library";
    fixture.opening = { mode: "browser" };
    fixture.groups = [...oldSources, fs([resource.path])];
    show({ ...resource, sourceLinks });
    await act(async () => fireEvent.click(screen.getByText("main")));
    expect(fixture.browser).toHaveBeenCalledOnce();
    expect(fixture.native).not.toHaveBeenCalled();
    expect(fixture.external).not.toHaveBeenCalled();
  });

  it("does not treat cached original pages as playable when no files are bound", () => {
    fixture.groups = oldSources;
    show({ ...resource, path: undefined, sourceLinks });
    expect(portal.status).toBe("not-found");
    expect(portal.sources).toEqual([]);
    act(() => portal.onNotFound());
    expect(fixture.portal.mock.lastCall![1]).toMatchObject({ resourceId: 42, hasPath: false });
    expect(fixture.external).not.toHaveBeenCalled();
    expect(fixture.native).not.toHaveBeenCalled();
    expect(fixture.browser).not.toHaveBeenCalled();
  });

  it("keeps original source navigation on the browser device even when the server has a GUI", () => {
    fixture.desktop = true;
    fixture.appWindow = false;
    show({ ...resource, sourceLinks });
    act(() => portal.originalSourceActions![0]!.onOpen());
    expect(fixture.external).toHaveBeenCalledWith(
      "https://www.dlsite.com/maniax/work/=/product_id/RJ123456.html",
      true,
    );
    expect(fixture.native).not.toHaveBeenCalled();
  });

  describe.each([
    { name: "card", component: ResourceCardPlaybackButton },
    { name: "details", component: ResourceDetailPlaybackButton },
  ])("real $name playback portal", ({ component }) => {
    it.each([
      { name: "pending", status: DataStatus.NotStarted },
      { name: "failed", status: DataStatus.Failed },
      { name: "empty", status: DataStatus.Ready },
    ])("keeps original pages separate while discovery is $name", ({ status }) => {
      fixture.overrideReady = false;
      fixture.overrideUnavailable = true;
      fixture.groups = [...oldSources, { ...fs([]), status }];
      render(<PlayControl PortalComponent={component} resource={{ ...resource, sourceLinks }} />);
      const originals = screen.getAllByText("resource.playbackActions.openOriginal");

      expect(originals).toHaveLength(3);
      fireEvent.click(originals[0]!);
      expect(fixture.external).toHaveBeenCalledOnce();
      expect(fixture.native).not.toHaveBeenCalled();
      expect(fixture.browser).not.toHaveBeenCalled();
      const buttons = screen.getAllByRole("button");
      const main = buttons[0]!;

      expect(main).not.toHaveAccessibleName("openFolder");
      expect(main).not.toHaveAccessibleName("resource.playbackActions.openOriginal");
      if (status === DataStatus.NotStarted) expect(main).toBeDisabled();
      else {
        fireEvent.click(main);
        if (status === DataStatus.Failed)
          expect(
            screen.getByText("resource.playbackActions.discoveryFailureHint"),
          ).toBeInTheDocument();
        else
          expect(fixture.portal.mock.lastCall![1]).toMatchObject({ resourceId: 42, hasPath: true });
      }
      expect(fixture.external).toHaveBeenCalledOnce();
    });
    it("preserves a no-files main button and original links for a pathless resource", () => {
      fixture.groups = oldSources;
      render(
        <PlayControl
          PortalComponent={component}
          resource={{ ...resource, path: undefined, sourceLinks }}
        />,
      );
      fireEvent.click(screen.getAllByRole("button")[0]!);
      expect(fixture.portal.mock.lastCall![1]).toMatchObject({ hasPath: false });
      expect(screen.getAllByText("resource.playbackActions.openOriginal")).toHaveLength(3);
      expect(fixture.external).not.toHaveBeenCalled();
    });
  });
});

it("requests Steam on the browser device without asking the remote host to launch it", () => {
  fixture.groups = [steam];
  show({ ...resource, path: undefined });
  fireEvent.click(screen.getByText("main"));
  expect(window.open).toHaveBeenCalledWith("steam://rungameid/123", "_self");
  expect(fixture.native).not.toHaveBeenCalled();
  expect(portal.sourceActionLabel?.(DataOrigin.Steam)).toBe("resource.playbackActions.openSteam");
});
