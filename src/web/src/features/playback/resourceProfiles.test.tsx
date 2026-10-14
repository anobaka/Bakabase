import {
  act,
  cleanup,
  fireEvent,
  render,
  renderHook,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  ResourceProfileLink,
  parseProfileSource,
  useSourceResourceProfile,
} from "./resourceProfiles";

import { ClientMode } from "@/sdk/constants";

const fixture = vi.hoisted(() => ({
  native: true,
  app: true,
  mode: 2,
  request: vi.fn(),
  open: vi.fn(),
}));

vi.mock("@/stores/remoteAccess", () => ({
  useUserSideActionsRunHere: () => fixture.native,
  useRemoteAccessStore: (selector: (state: any) => unknown) =>
    selector({ clientMode: fixture.mode }),
}));
vi.mock("@/utils/clientSurface", () => ({ isDesktopAppWindow: () => fixture.app }));
vi.mock("@/sdk/BApi", () => ({ default: { request: fixture.request } }));
vi.mock("@/features/federation/switching", () => ({ openLocalView: fixture.open }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("react-hot-toast", () => ({ default: { error: vi.fn() } }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isLoading, ...props }: any) => (
    <button aria-label={props["aria-label"]} disabled={isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Tooltip: ({ children }: any) => children,
}));
beforeEach(() => {
  vi.clearAllMocks();
  fixture.native = true;
  fixture.app = true;
  fixture.mode = ClientMode.PureClient;
  fixture.request.mockResolvedValue({
    code: 0,
    data: [{ resourceId: 42, openingMode: "native", profileIds: [7] }],
  });
  fixture.open.mockResolvedValue(undefined);
  localStorage.clear();
});
afterEach(cleanup);

describe("local resource profiles for another source", () => {
  it("batches mounted cards by source and reloads profiles when returning to the window", async () => {
    fixture.request.mockImplementation(async ({ body }: any) => ({
      code: 0,
      data: body.resourceIds.map((resourceId: number) => ({
        resourceId,
        openingMode: "native",
        profileIds: [7],
      })),
    }));
    const { result } = renderHook(() => [
      useSourceResourceProfile("server:mac", 1),
      useSourceResourceProfile("server:mac", 2),
      useSourceResourceProfile("server:mac", 3),
    ]);

    await waitFor(() => expect(result.current.every((value) => value.ready)).toBe(true));
    expect(fixture.request).toHaveBeenCalledTimes(1);
    expect(fixture.request).toHaveBeenCalledWith(
      expect.objectContaining({ body: { sourceDeviceId: "mac", resourceIds: [1, 2, 3] } }),
    );
    fireEvent(window, new Event("focus"));
    await waitFor(() => expect(fixture.request).toHaveBeenCalledTimes(2));
  });

  it("passes plain source identity, epoch and remote resource id without local executable config", async () => {
    const { result } = renderHook(() => useSourceResourceProfile("node:mac:epoch", 42));

    await waitFor(() => expect(result.current.ready).toBe(true));
    expect(fixture.request).toHaveBeenCalledWith(
      expect.objectContaining({
        path: "/player/resource-profiles",
        body: { sourceDeviceId: "mac", libraryEpoch: "epoch", resourceIds: [42] },
      }),
    );
    expect(result.current.choice()).toEqual({ mode: "native" });
  });
  it("keeps ordinary browsers on the web and ignores superseded per-kind browser storage", () => {
    fixture.app = false;
    localStorage.setItem(
      "bakabase.playback.overrides.v1",
      JSON.stringify([
        { target: "server:mac", choices: { video: { mode: "native", playerId: "PotPlayer" } } },
      ]),
    );
    const { result } = renderHook(() => useSourceResourceProfile("server:mac", 42));

    expect(result.current.ready).toBe(true);
    expect(result.current.choice()).toBeUndefined();
    expect(fixture.request).not.toHaveBeenCalled();
  });
  it("leaves local resource profiles to the existing local playback path", () => {
    const { result } = renderHook(() => useSourceResourceProfile(undefined, 42));

    expect(result.current.ready).toBe(true);
    expect(fixture.request).not.toHaveBeenCalled();
  });
  it("exposes retry when source matching fails rather than silently selecting a different application", async () => {
    fixture.request.mockRejectedValueOnce(new Error("offline"));
    const { result } = renderHook(() => useSourceResourceProfile("server:mac", 42));

    await waitFor(() => expect(result.current.unavailable).toBe(true));
    await act(async () => result.current.reload());
    expect(result.current.ready).toBe(true);
    expect(result.current.unavailable).toBe(false);
  });
  it("navigates out of a managed view to this device's ordinary resource profile page", async () => {
    render(<ResourceProfileLink compact resourceId={42} target="server:mac" />);
    fireEvent.click(screen.getByRole("button", { name: "resource.profilePlayback.configure" }));

    await waitFor(() =>
      expect(fixture.open).toHaveBeenCalledWith("/resource-profile?sourceDeviceId=mac"),
    );
    expect(fixture.request).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
  it("never offers local profile configuration to a plain browser viewing a server", () => {
    fixture.app = false;
    render(<ResourceProfileLink target="server:mac" />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
  it("keeps source targets separate", () => {
    expect(parseProfileSource("server:mac")).toEqual({ sourceDeviceId: "mac" });
    expect(parseProfileSource("node:mac:epoch")).toEqual({
      sourceDeviceId: "mac",
      libraryEpoch: "epoch",
    });
    expect(parseProfileSource(undefined)).toBeUndefined();
  });
});
