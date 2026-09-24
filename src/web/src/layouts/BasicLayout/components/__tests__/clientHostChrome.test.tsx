import type { ReactNode } from "react";

import { act, cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AppUpdateBanner from "../AppUpdateBanner";
import ClientTrayState from "../ClientTrayState";
import ClientVersionNotice from "../ClientVersionNotice";
import LegacyClientNotice from "../LegacyClientNotice";

import BApi from "@/sdk/BApi";
import { clientApi } from "@/core/clientApi";
import { BTaskStatus, ClientMode } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

vi.mock("@/sdk/BApi", () => ({
  default: {
    remoteAccess: { getRemoteAccessContext: vi.fn() },
    updater: {
      getNewAppVersion: vi.fn(),
      startUpdatingApp: vi.fn(),
      restartAndUpdateApp: vi.fn(),
    },
    app: { getAppInfo: vi.fn() },
  },
}));
vi.mock("@/core/clientApi", () => ({
  LOCAL_SWITCHER_TARGET: "local",
  clientApi: {
    status: vi.fn(),
    appInfo: vi.fn(),
    setTrayRunning: vi.fn(),
    updater: { state: vi.fn(), newVersion: vi.fn(), start: vi.fn(), restart: vi.fn() },
  },
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
  vi.mocked(clientApi.updater.newVersion).mockResolvedValue({ version: "9.9.9" } as never);
  vi.mocked(clientApi.updater.state).mockResolvedValue({} as never);
  vi.mocked(clientApi.updater.start).mockResolvedValue({} as never);
  vi.mocked(clientApi.appInfo).mockResolvedValue({ version: "2.5.0", available: true });
  vi.mocked(clientApi.setTrayRunning).mockResolvedValue({ applied: true });
  vi.mocked(BApi.updater.getNewAppVersion).mockResolvedValue({
    data: { version: "9.9.9" },
  } as never);
  vi.mocked(BApi.app.getAppInfo).mockResolvedValue({ data: { coreVersion: "2.6.0" } } as never);
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  useRemoteAccessStore.setState(initialState, true);
  useBTasksStore.setState({ tasks: [] } as never);
});

describe("telling the desktop app's console from the retired client", () => {
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
  it("takes a status without a host for the retired client", async () => {
    context({});
    status();
    await useRemoteAccessStore.getState().load();
    expect(useRemoteAccessStore.getState().clientHost).toBe("legacy");
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
      <LegacyClientNotice collapsed={false} />
      <ClientVersionNotice collapsed={false} />
      <AppUpdateBanner collapsed={false} />
      <ClientTrayState />
    </MemoryRouter>,
  );

const pureClient = (clientHost?: "console" | "legacy") =>
  useRemoteAccessStore.setState({
    initialized: true,
    isLocal: false,
    clientMode: ClientMode.PureClient,
    serverReachable: true,
    clientHost,
  });

describe("sidebar chrome per host", () => {
  it("hides every thin-client banner in the console and updates nothing", async () => {
    vi.useFakeTimers();
    pureClient("console");
    useBTasksStore.setState({ tasks: [{ status: BTaskStatus.Running }] } as never);
    renderChrome();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
    expect(screen.queryByTestId("legacy-client-notice")).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
    // Neither updater is touched: the client's does not exist here, and the forwarded one
    // would update the managed server behind the user's back.
    expect(clientApi.updater.newVersion).not.toHaveBeenCalled();
    expect(clientApi.updater.start).not.toHaveBeenCalled();
    expect(BApi.updater.getNewAppVersion).not.toHaveBeenCalled();
    expect(BApi.updater.startUpdatingApp).not.toHaveBeenCalled();
    expect(clientApi.appInfo).not.toHaveBeenCalled();
    expect(clientApi.setTrayRunning).not.toHaveBeenCalled();
  });

  it("waits for the host before offering anything client-specific", async () => {
    pureClient(undefined);
    renderChrome();
    await act(async () => {});
    expect(screen.queryByTestId("legacy-client-notice")).not.toBeInTheDocument();
    expect(clientApi.updater.newVersion).not.toHaveBeenCalled();
    expect(BApi.updater.getNewAppVersion).not.toHaveBeenCalled();
  });

  it("tells the retired client it is retired, and keeps its own updater", async () => {
    pureClient("legacy");
    renderChrome();
    const notice = screen.getByTestId("legacy-client-notice");

    expect(notice).toHaveTextContent("federation.legacyClient.notice");
    expect(notice.querySelector("a")).toHaveAttribute("href", "/other-devices");
    await act(async () => {});
    expect(clientApi.updater.newVersion).toHaveBeenCalled();
    expect(BApi.updater.getNewAppVersion).not.toHaveBeenCalled();
    // The version notice still compares the client with its server.
    expect(clientApi.appInfo).toHaveBeenCalled();
  });

  it("shows no retirement notice in the desktop app itself", async () => {
    useRemoteAccessStore.setState({
      initialized: true,
      isLocal: true,
      clientMode: ClientMode.AllInOne,
    });
    renderChrome();
    await act(async () => {});
    expect(screen.queryByTestId("legacy-client-notice")).not.toBeInTheDocument();
    expect(BApi.updater.getNewAppVersion).toHaveBeenCalled();
  });
});
