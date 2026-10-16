import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import RemotePathMappingHint from "./RemotePathMappingHint";

import { ClientMode } from "@/sdk/constants";

const fixture = vi.hoisted(() => ({
  context: {} as any,
  load: vi.fn(),
  portal: vi.fn(),
}));

vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (select: (state: any) => unknown) => select(fixture.context),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: fixture.portal }),
}));
vi.mock("@/features/management/resourceContext", () => ({ useResourceOwner: () => undefined }));
vi.mock("@/features/federation/components/ManagedPathMappingsModal", () => ({
  default: () => null,
  loadManagedPathMappingTarget: fixture.load,
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
const source = {
  serverId: "nas",
  name: "NAS",
  libraryEpoch: "epoch",
  isLocal: false,
  available: true,
  contractVersion: 1,
};
let instance = 0;

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  fixture.context = {
    context: "known",
    clientMode: ClientMode.AllInOne,
    serverId: `controller-${++instance}`,
  };
  fixture.load.mockResolvedValue({
    serverId: "nas",
    pairedAt: "2026-10-16",
    pathMappings: [],
  });
});
afterEach(cleanup);
const configure = "resource.pathMappingHint.configure";
const dismiss = "resource.pathMappingHint.dismiss";

describe("quiet playback path mapping guidance", () => {
  it("opens the shared modal in place for an unmapped foreign file", async () => {
    const location = window.location.href;

    render(<RemotePathMappingHint filePath="/media/movie.mkv" source={source} />);
    fireEvent.click(await screen.findByRole("button", { name: configure }));
    expect(fixture.load).toHaveBeenCalledWith(
      { kind: "manager", serverId: "nas" },
      expect.any(AbortSignal),
    );
    expect(fixture.portal).toHaveBeenCalledWith(
      expect.any(Function),
      expect.objectContaining({ target: { kind: "manager", serverId: "nas" } }),
    );
    expect(window.location.href).toBe(location);
  });

  it.each([
    { context: "unknown", clientMode: ClientMode.AllInOne },
    { context: "known", clientMode: ClientMode.RemoteBrowser },
    { context: "known", clientMode: ClientMode.PureClient, clientHost: undefined },
  ])("does not claim native access in an unconfirmed/browser context: %o", async (context) => {
    fixture.context = { ...fixture.context, ...context };
    render(<RemotePathMappingHint filePath="/media/movie.mkv" source={source} />);
    expect(screen.queryByRole("button")).toBeNull();
    expect(fixture.load).not.toHaveBeenCalled();
  });

  it("does not fetch for ordinary local resources, closed dialogs or non-file actions", () => {
    render(
      <>
        <RemotePathMappingHint filePath="/media/movie.mkv" />
        <RemotePathMappingHint active={false} filePath="/media/movie.mkv" source={source} />
        <RemotePathMappingHint filePath="steam://run/10" source={source} />
        <RemotePathMappingHint filePath="https://example.com/movie" source={source} />
      </>,
    );
    expect(fixture.load).not.toHaveBeenCalled();
  });

  it("uses the controlling app's connection for a relay view, never a nested owner", async () => {
    fixture.context = {
      context: "known",
      clientMode: ClientMode.PureClient,
      clientHost: "console",
      ownDeviceId: "controller-relay",
      serverId: "nas",
    };
    render(
      <>
        <RemotePathMappingHint filePath="/media/movie.mkv" />
        <RemotePathMappingHint
          filePath="/media/other.mkv"
          source={{ ...source, serverId: "third" }}
        />
      </>,
    );
    expect(await screen.findAllByRole("button", { name: configure })).toHaveLength(1);
    expect(fixture.load).toHaveBeenCalledTimes(1);
    expect(fixture.load).toHaveBeenCalledWith(
      { kind: "relay", serverId: "nas" },
      expect.any(AbortSignal),
    );
  });

  it("checks this path against complete prefixes even when other mappings exist", async () => {
    fixture.load.mockResolvedValue({
      serverId: "nas",
      pairedAt: "now",
      pathMappings: [{ serverPath: "/media", localPath: "M:\\" }],
    });
    render(
      <>
        <RemotePathMappingHint filePath="/media/movie.mkv" source={source} />
        <RemotePathMappingHint filePath="/media-other/movie.mkv" source={source} />
      </>,
    );
    expect(await screen.findAllByRole("button", { name: configure })).toHaveLength(1);
    expect(fixture.load).toHaveBeenCalledTimes(1);
  });

  it("keeps failed reads silent, and rechecks when the user returns", async () => {
    fixture.load.mockRejectedValueOnce(new Error("offline"));
    render(<RemotePathMappingHint filePath="/media/movie.mkv" source={source} />);
    await waitFor(() => expect(fixture.load).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole("button")).toBeNull();
    fireEvent(window, new Event("focus"));
    expect(await screen.findByRole("button", { name: configure })).toBeInTheDocument();
  });

  it("dismisses all open instances and remembers the source without suppressing another device", async () => {
    const { unmount } = render(
      <>
        <RemotePathMappingHint filePath="/media/a.mkv" source={source} />
        <RemotePathMappingHint filePath="/media/b.mkv" source={source} />
      </>,
    );

    fireEvent.click((await screen.findAllByRole("button", { name: dismiss }))[0]!);
    expect(screen.queryByRole("button", { name: configure })).toBeNull();
    unmount();
    fixture.load.mockClear();
    render(
      <>
        <RemotePathMappingHint filePath="/media/c.mkv" source={source} />
        <RemotePathMappingHint
          filePath="/media/d.mkv"
          source={{ ...source, serverId: "another" }}
        />
      </>,
    );
    expect(await screen.findAllByRole("button", { name: configure })).toHaveLength(1);
    expect(fixture.load).toHaveBeenCalledTimes(1);
    expect(fixture.load.mock.calls[0]![0].serverId).toBe("another");
  });

  it("hides the hint after saving a covering mapping and ignores another target's event", async () => {
    render(<RemotePathMappingHint filePath="/media/movie.mkv" source={source} />);
    await screen.findByRole("button", { name: configure });
    fixture.load.mockResolvedValue({
      serverId: "nas",
      pairedAt: "now",
      pathMappings: [{ serverPath: "/media", localPath: "M:\\" }],
    });
    fireEvent(
      window,
      new CustomEvent("bakabase:path-mappings-saved", {
        detail: { serverId: "other", kind: "manager" },
      }),
    );
    expect(fixture.load).toHaveBeenCalledTimes(1);
    fireEvent(
      window,
      new CustomEvent("bakabase:path-mappings-saved", {
        detail: { serverId: "nas", kind: "manager" },
      }),
    );
    await waitFor(() => expect(screen.queryByRole("button", { name: configure })).toBeNull());
    expect(fixture.load).toHaveBeenCalledTimes(2);
  });
});
