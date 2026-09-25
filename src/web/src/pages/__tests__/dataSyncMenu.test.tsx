import type { IMenuItem } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig";
import type { RouteMenuItem } from "@/components/routesMenuConfig";

import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import AntdMenu from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu";
import { routesMenuConfig } from "@/components/routesMenuConfig";
import { DATA_SYNC_ROUTE } from "@/features/data-sync/routes";
import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/*
 * Data sync is one page under System. Unlike the multi-device group it is not this device's
 * own: the page works in the desktop app's window showing a server it manages, and in a
 * browser on an Unrestricted server, so the entry is shown in each of them.
 */

// Every page is imported with the route config, and some reach into BApi as they load.
vi.mock("@/sdk/BApi", () => {
  const anything = (): unknown =>
    new Proxy(() => Promise.resolve({ code: 0 }), {
      get: (_, key) => (key === "then" ? undefined : anything()),
    });

  return { default: anything() };
});
// The real menu from the real route config; only the System group is rendered.
vi.mock(
  "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig",
  async (importOriginal) => {
    const real = await importOriginal<{ asideMenuConfig: IMenuItem[] }>();

    return {
      ...real,
      asideMenuConfig: real.asideMenuConfig.filter((item) => item.name === "menu.system"),
    };
  },
);
vi.mock("@/core/clientApi", () => ({
  LOCAL_SWITCHER_TARGET: "local",
  clientApi: { status: vi.fn(), switcher: { list: vi.fn(), open: vi.fn() } },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));

const initialState = useRemoteAccessStore.getState();

afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initialState, true);
});

const flatten = (items: RouteMenuItem[]): RouteMenuItem[] =>
  items.flatMap((item) => [item, ...flatten(item.children ?? [])]);

describe("the data sync menu entry", () => {
  it("is a page under System, right after Configuration, for every window", () => {
    const system = routesMenuConfig.find((route) => route.name === "menu.system");
    const names = system?.children?.map((route) => route.name) ?? [];
    const entry = system?.children?.find((route) => route.name === "menu.dataSync");

    expect(names.indexOf("menu.dataSync")).toBe(names.indexOf("menu.configuration") + 1);
    expect(entry).toMatchObject({ path: DATA_SYNC_ROUTE, layout: "basic", menu: true });
    expect(entry?.component).toBeDefined();
    expect(entry?.localNodeOnly).toBeFalsy();
    expect(entry?.pureClientOnly).toBeFalsy();
    expect(system?.localNodeOnly).toBeFalsy();
    expect(system?.pureClientOnly).toBeFalsy();
    // Registered once.
    expect(
      flatten(routesMenuConfig).filter((route) => route.path === DATA_SYNC_ROUTE),
    ).toHaveLength(1);
  });

  it.each([
    ["this device's own window", { isLocal: true, clientMode: ClientMode.AllInOne }],
    [
      "the window showing a server this device manages",
      {
        isLocal: false,
        clientMode: ClientMode.PureClient,
        clientHost: "console" as const,
        serverReachable: true,
      },
    ],
    [
      "a browser on an Unrestricted server",
      {
        isLocal: false,
        clientMode: ClientMode.RemoteBrowser,
        mode: RemoteAccessMode.Unrestricted,
      },
    ],
  ])("is shown in %s", async (_, state) => {
    useRemoteAccessStore.setState({ initialized: true, ...state });
    render(
      <MemoryRouter>
        <AntdMenu collapsed={false} />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getAllByText("menu.system")[0]);

    // The menu renders an item more than once (it measures for overflow); presence is enough.
    expect(await screen.findAllByText("menu.dataSync")).not.toHaveLength(0);
  });
});
