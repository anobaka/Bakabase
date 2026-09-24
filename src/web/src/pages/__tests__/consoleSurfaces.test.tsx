import type { ReactNode } from "react";
import type { IMenuItem } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig";

import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import AntdMenu from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu";
import { routesMenuConfig } from "@/components/routesMenuConfig";
import LogPage from "@/pages/log";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/*
 * Surfaces the desktop app's console — the relay that shows a managed server in this
 * device's window — shares with that server's own UI: the menu group about this computer,
 * and the log page, which there can only be the managed server's.
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
// hand-written copy would keep passing after they were deleted. Only the console's group is
// rendered: the rest of the menu is not under test.
vi.mock(
  "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig",
  async (importOriginal) => {
    const real = await importOriginal<{ asideMenuConfig: IMenuItem[] }>();

    return {
      ...real,
      asideMenuConfig: real.asideMenuConfig.filter(
        (item) => item.name === "menu.client.thisComputer",
      ),
    };
  },
);
vi.mock("@/core/clientApi", () => ({
  LOCAL_SWITCHER_TARGET: "local",
  clientApi: { status: vi.fn(), switcher: { list: vi.fn(), open: vi.fn() } },
}));
vi.mock("@/pages/log/ServerLog", () => ({ default: () => <p>server log</p> }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));

const initialState = useRemoteAccessStore.getState();

const pureClient = (clientHost?: "console") =>
  useRemoteAccessStore.setState({
    initialized: true,
    isLocal: false,
    clientMode: ClientMode.PureClient,
    serverReachable: true,
    clientHost,
    serverName: "NAS",
  });

const renderIn = (node: ReactNode) => render(<MemoryRouter>{node}</MemoryRouter>);

afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initialState, true);
});

describe("the menu group about this computer", () => {
  it("in the console, holds this computer's path mappings", async () => {
    pureClient("console");
    renderIn(<AntdMenu collapsed={false} />);
    fireEvent.click(screen.getAllByText("menu.client.thisComputer")[0]);
    // The menu renders an item more than once (it measures for overflow); presence is enough.
    expect(await screen.findAllByText("menu.client.pathMapping")).not.toHaveLength(0);
  });

  it("is absent from this device's own window", () => {
    useRemoteAccessStore.setState({
      initialized: true,
      isLocal: true,
      clientMode: ClientMode.AllInOne,
    });
    renderIn(<AntdMenu collapsed={false} />);
    expect(screen.queryAllByText("menu.client.thisComputer")).toHaveLength(0);
  });
});

describe("the route config the menu is built from", () => {
  it("keeps the group to managed servers and holds only this computer's path mappings", () => {
    const group = routesMenuConfig.find((route) => route.name === "menu.client.thisComputer");

    expect(group?.pureClientOnly).toBe(true);
    expect(group?.children?.map((route) => route.path)).toEqual(["/client-path-mapping"]);
  });
});

describe("the log page", () => {
  it("in the console, is the managed server's alone: its relay keeps this computer's log to itself", () => {
    pureClient("console");
    renderIn(<LogPage />);
    expect(screen.getByText("server log")).toBeInTheDocument();
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
  });
});
