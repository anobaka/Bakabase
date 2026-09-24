import type { ReactNode } from "react";

import { act, cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AppUpdateBanner from "../AppUpdateBanner";

import BApi from "@/sdk/BApi";
import { clientApi } from "@/core/clientApi";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

vi.mock("@/sdk/BApi", () => ({
  default: {
    remoteAccess: { getRemoteAccessContext: vi.fn() },
    updater: {
      getNewAppVersion: vi.fn(),
      startUpdatingApp: vi.fn(),
      restartAndUpdateApp: vi.fn(),
    },
  },
}));
vi.mock("@/core/clientApi", () => ({
  LOCAL_SWITCHER_TARGET: "local",
  clientApi: { status: vi.fn() },
}));
vi.mock("@/components/Changelog", () => ({ useChangelogModal: () => vi.fn() }));
vi.mock("@/components/bakaui", () => ({
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Button: ({ children, onPress }: { children: ReactNode; onPress?: () => void }) => (
    <button type="button" onClick={onPress}>
      {children}
    </button>
  ),
  Progress: () => <div role="progressbar" />,
  Spinner: () => <span />,
}));

const initialState = useRemoteAccessStore.getState();
const context = (overrides: Record<string, unknown>) =>
  vi.mocked(BApi.remoteAccess.getRemoteAccessContext).mockResolvedValue({
    code: 0,
    data: {
      isLocal: false,
      mode: 1,
      paired: true,
      clientMode: ClientMode.PureClient,
      serverReachable: true,
      serverName: "NAS",
      cookieCaptureAvailable: true,
      ...overrides,
    },
  } as never);
const status = (overrides: Record<string, unknown> = {}) =>
  vi.mocked(clientApi.status).mockResolvedValue({
    clientVersion: "2.5.0",
    deviceName: "Desk",
    platform: 1,
    serverReachable: true,
    implementedUserMachineRoutes: [],
    servers: [],
    ...overrides,
  } as never);

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(BApi.updater.getNewAppVersion).mockResolvedValue({
    data: { version: "9.9.9" },
  } as never);
});
afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initialState, true);
});

describe("recognising the desktop app's console", () => {
  it("recognises the console and remembers this device's name", async () => {
    context({});
    status({ host: "console", localName: "Desk" });
    await useRemoteAccessStore.getState().load();
    expect(useRemoteAccessStore.getState()).toMatchObject({
      clientMode: ClientMode.PureClient,
      clientHost: "console",
      localName: "Desk",
    });
  });
  it("remembers which of the server's paired devices this window is", async () => {
    context({});
    status({
      host: "console",
      localName: "Desk",
      activeServerId: "nas",
      servers: [
        {
          serverId: "nas",
          baseAddress: "http://192.168.1.5:34567",
          pairedAt: "2026-09-01T00:00:00Z",
          deviceId: "dev-desk",
          isActive: true,
          pathMappings: [],
        },
      ],
    });
    await useRemoteAccessStore.getState().load();
    expect(useRemoteAccessStore.getState().ownDeviceId).toBe("dev-desk");

    // Gone again once the window is this device's own.
    context({ isLocal: true, clientMode: ClientMode.AllInOne });
    await useRemoteAccessStore.getState().load();
    expect(useRemoteAccessStore.getState().ownDeviceId).toBeUndefined();
  });
  it("takes a status without a host for nothing it knows how to drive", async () => {
    context({});
    status();
    await useRemoteAccessStore.getState().load();
    expect(useRemoteAccessStore.getState().clientHost).toBeUndefined();
    expect(useRemoteAccessStore.getState().localName).toBeUndefined();
  });
  it("never asks outside PureClient", async () => {
    context({ isLocal: true, clientMode: ClientMode.AllInOne });
    await useRemoteAccessStore.getState().load();
    expect(clientApi.status).not.toHaveBeenCalled();
    expect(useRemoteAccessStore.getState().clientHost).toBeUndefined();
  });
  it("leaves the host unknown when the status cannot be read", async () => {
    context({});
    vi.mocked(clientApi.status).mockRejectedValue(new Error("gone"));
    await useRemoteAccessStore.getState().load();
    expect(useRemoteAccessStore.getState()).toMatchObject({
      initialized: true,
      clientMode: ClientMode.PureClient,
    });
    expect(useRemoteAccessStore.getState().clientHost).toBeUndefined();
  });
});

const renderChrome = () =>
  render(
    <MemoryRouter>
      <AppUpdateBanner collapsed={false} />
    </MemoryRouter>,
  );

const pureClient = (clientHost?: "console") =>
  useRemoteAccessStore.setState({
    initialized: true,
    isLocal: false,
    clientMode: ClientMode.PureClient,
    serverReachable: true,
    clientHost,
  });

describe("the sidebar's update banner", () => {
  it("updates nothing in the console", async () => {
    pureClient("console");
    renderChrome();
    await act(async () => {});
    expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
    // The forwarded updater would update the managed server behind the user's back.
    expect(BApi.updater.getNewAppVersion).not.toHaveBeenCalled();
    expect(BApi.updater.startUpdatingApp).not.toHaveBeenCalled();
  });

  it("updates nothing while the relay has not yet said it is the console", async () => {
    pureClient(undefined);
    renderChrome();
    await act(async () => {});
    expect(BApi.updater.getNewAppVersion).not.toHaveBeenCalled();
    expect(BApi.updater.startUpdatingApp).not.toHaveBeenCalled();
  });

  it("is the desktop app's own updater in its own window", async () => {
    useRemoteAccessStore.setState({
      initialized: true,
      isLocal: true,
      clientMode: ClientMode.AllInOne,
    });
    renderChrome();
    await act(async () => {});
    expect(BApi.updater.getNewAppVersion).toHaveBeenCalled();
  });
});
