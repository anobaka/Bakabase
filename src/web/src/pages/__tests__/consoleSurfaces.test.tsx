import type { ReactNode } from "react";
import type { IMenuItem } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AntdMenu from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu";
import { routesMenuConfig } from "@/components/routesMenuConfig";
import ClientConnectionPage from "@/pages/client-connection";
import LogPage from "@/pages/log";
import ClientAppInfo from "@/pages/configuration/components/AppInfo/ClientAppInfo";
import MigrationNotice from "@/features/federation/components/MigrationNotice";
import { clientApi } from "@/core/clientApi";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/*
 * Pages that belong to the retired thin client, seen from the desktop app's console — the
 * relay that shows a managed server in this device's window. Both answer as PureClient;
 * the console has none of the thin client's connection, update or migration routes (409 /
 * 404), so each of these pages has to know which of the two it is in.
 */

// Every page is imported (see the menu below), and some reach into BApi as they load; any
// path through it is a function answering nothing. None of it is called by these tests.
vi.mock("@/sdk/BApi", () => {
  const anything = (): unknown =>
    new Proxy(() => Promise.resolve({ code: 0 }), {
      get: (_, key) => (key === "then" ? undefined : anything()),
    });

  return { default: anything() };
});
// The real menu, built from the real route config — the console's flags live there, and a
// hand-written copy would keep passing after they were deleted. Only the client group is
// rendered: the rest of the menu is not under test.
vi.mock(
  "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig",
  async (importOriginal) => {
    const real = await importOriginal<{ asideMenuConfig: IMenuItem[] }>();

    return {
      ...real,
      asideMenuConfig: real.asideMenuConfig.filter((item) => item.name === "menu.client"),
    };
  },
);
vi.mock("@/core/clientApi", () => ({
  LOCAL_SWITCHER_TARGET: "local",
  clientApi: {
    status: vi.fn(),
    appInfo: vi.fn(),
    migrationHints: vi.fn(),
    exportMigrationHints: vi.fn(),
    switcher: { list: vi.fn(), open: vi.fn() },
    updater: { state: vi.fn(), newVersion: vi.fn(), start: vi.fn(), restart: vi.fn() },
  },
}));
vi.mock("@/pages/log/ServerLog", () => ({ default: () => <p>server log</p> }));
vi.mock("@/pages/log/ClientLog", () => ({ default: () => <p>client log</p> }));
vi.mock("@/pages/client-connection/ServerUpdateNotice", () => ({ default: () => null }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/Changelog", () => ({ ChangelogButton: () => null }));
vi.mock("@/pages/configuration/components/SettingsSection", () => ({
  default: ({ title }: { title: string }) => <section>{title}</section>,
}));
vi.mock("@/components/bakaui", () => {
  const Pass = ({ children }: { children?: ReactNode }) => <>{children}</>;

  return {
    Button: ({ children, onPress }: { children: ReactNode; onPress?: () => void }) => (
      <button type="button" onClick={onPress}>
        {children}
      </button>
    ),
    Tabs: ({ children }: { children: ReactNode }) => <div role="tablist">{children}</div>,
    Tab: ({ title }: { title: string }) => <span role="tab">{title}</span>,
    Chip: Pass,
    Input: () => null,
    Modal: Pass,
    Snippet: Pass,
    Divider: () => null,
    Progress: () => null,
    Tooltip: Pass,
  };
});

const initialState = useRemoteAccessStore.getState();
const assign = vi.fn();

const pureClient = (clientHost: "console" | "legacy") =>
  useRemoteAccessStore.setState({
    initialized: true,
    isLocal: false,
    clientMode: ClientMode.PureClient,
    serverReachable: true,
    clientHost,
    serverName: "NAS",
  });

const renderIn = (node: ReactNode) => render(<MemoryRouter>{node}</MemoryRouter>);

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(clientApi.status).mockResolvedValue({
    clientVersion: "2.5.0",
    deviceName: "Desk",
    platform: 1,
    serverReachable: true,
    implementedUserMachineRoutes: [],
    servers: [],
  } as never);
  vi.mocked(clientApi.appInfo).mockResolvedValue({ version: "2.5.0", available: true });
  vi.mocked(clientApi.updater.state).mockResolvedValue({} as never);
  vi.mocked(clientApi.updater.newVersion).mockResolvedValue({} as never);
  vi.stubGlobal("location", { ...window.location, href: "http://127.0.0.1:34650/", assign });
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  useRemoteAccessStore.setState(initialState, true);
});

describe("the thin client's connection page", () => {
  it("in the console, points to this device's own devices page and touches no connection", async () => {
    pureClient("console");
    vi.mocked(clientApi.switcher.open).mockResolvedValue({ url: "http://localhost:34567/" });
    renderIn(<ClientConnectionPage />);
    expect(screen.getByText("clientConnection.managedByDesktop")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByText("federation.switcher.manageDevices"));
    });
    await waitFor(() =>
      expect(assign).toHaveBeenCalledWith(
        "http://localhost:34567/#/federation/devices?section=servers",
      ),
    );
    expect(clientApi.switcher.open).toHaveBeenCalledWith("local");
    expect(clientApi.status).not.toHaveBeenCalled();
    // The migration export is the retired client's; it is not offered here.
    expect(screen.queryByText("federation.migration.export")).not.toBeInTheDocument();
  });

  it("in the retired client, still manages its connection", async () => {
    pureClient("legacy");
    renderIn(<ClientConnectionPage />);
    await waitFor(() => expect(clientApi.status).toHaveBeenCalled());
    expect(screen.queryByText("clientConnection.managedByDesktop")).not.toBeInTheDocument();
  });
});

describe("the migration export", () => {
  it("is absent in the console, which already is the desktop app", () => {
    pureClient("console");
    const { container } = renderIn(<MigrationNotice />);

    expect(container).toBeEmptyDOMElement();
  });

  it("is offered in the retired client", () => {
    pureClient("legacy");
    renderIn(<MigrationNotice />);
    expect(screen.getByText("federation.migration.export")).toBeInTheDocument();
  });
});

describe("the client's own app info and updater", () => {
  it("is not read in the console", async () => {
    pureClient("console");
    renderIn(<ClientAppInfo />);
    await act(async () => {});
    expect(clientApi.appInfo).not.toHaveBeenCalled();
    expect(clientApi.updater.newVersion).not.toHaveBeenCalled();
    expect(clientApi.updater.state).not.toHaveBeenCalled();
  });

  it("is read in the retired client", async () => {
    pureClient("legacy");
    renderIn(<ClientAppInfo />);
    await waitFor(() => expect(clientApi.updater.newVersion).toHaveBeenCalled());
  });
});

describe("the thin client's menu group", () => {
  it("in the console, is about this computer and has no connection page", async () => {
    pureClient("console");
    renderIn(<AntdMenu collapsed={false} />);
    expect(screen.queryAllByText("menu.client")).toHaveLength(0);
    fireEvent.click(screen.getAllByText("menu.client.thisComputer")[0]);
    // The menu renders an item more than once (it measures for overflow); presence is enough.
    expect(await screen.findAllByText("menu.client.pathMapping")).not.toHaveLength(0);
    expect(screen.queryAllByText("menu.client.connection")).toHaveLength(0);
  });

  it("in the retired client, keeps its name and its connection page", async () => {
    pureClient("legacy");
    renderIn(<AntdMenu collapsed={false} />);
    expect(screen.queryAllByText("menu.client.thisComputer")).toHaveLength(0);
    fireEvent.click(screen.getAllByText("menu.client")[0]);
    expect(await screen.findAllByText("menu.client.connection")).not.toHaveLength(0);
  });
});

describe("the route config the menu is built from", () => {
  it("renames the client group in the console and hides only its connection page there", () => {
    const group = routesMenuConfig.find((route) => route.name === "menu.client");
    const child = (path: string) => group?.children?.find((route) => route.path === path);

    expect(group?.nameInConsole).toBe("menu.client.thisComputer");
    expect(child("/client-connection")?.hideInConsole).toBe(true);
    // This computer's path mappings are the console's as much as the client's.
    expect(child("/client-path-mapping")).toBeDefined();
    expect(child("/client-path-mapping")?.hideInConsole).toBeFalsy();
  });
});

describe("the log page's second log", () => {
  it("is not offered in the console, whose relay keeps this computer's log to itself", () => {
    pureClient("console");
    renderIn(<LogPage />);
    expect(screen.getByText("server log")).toBeInTheDocument();
    expect(screen.queryByText("client log")).not.toBeInTheDocument();
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
  });

  it("is the client's in the retired client", () => {
    pureClient("legacy");
    renderIn(<LogPage />);
    expect(screen.getByRole("tab", { name: "log.source.client" })).toBeInTheDocument();
  });
});
