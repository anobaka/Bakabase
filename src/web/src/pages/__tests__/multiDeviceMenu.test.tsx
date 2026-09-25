import type { IMenuItem } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig";
import type { RouteMenuItem } from "@/components/routesMenuConfig";

import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import AntdMenu from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu";
import { routesMenuConfig } from "@/components/routesMenuConfig";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/*
 * The multi-device mode (「多设备互联」, "Multi-device") is one menu group: the merged library, the devices
 * page and the device map. Every page of it belongs to this device's own window.
 */

// Every page is imported with the route config, and some reach into BApi as they load.
vi.mock("@/sdk/BApi", () => {
  const anything = (): unknown =>
    new Proxy(() => Promise.resolve({ code: 0 }), {
      get: (_, key) => (key === "then" ? undefined : anything()),
    });

  return { default: anything() };
});
// The real menu from the real route config; only the multi-device group is rendered.
vi.mock(
  "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig",
  async (importOriginal) => {
    const real = await importOriginal<{ asideMenuConfig: IMenuItem[] }>();

    return {
      ...real,
      asideMenuConfig: real.asideMenuConfig.filter((item) => item.name === "federation.mode"),
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

describe("the multi-device menu group", () => {
  it("holds the library, the devices page and the device map, all this device's own", () => {
    const group = routesMenuConfig.find((route) => route.name === "federation.mode");

    expect(group?.localNodeOnly).toBe(true);
    expect(group?.children?.map((route) => [route.name, route.path, route.localNodeOnly])).toEqual([
      ["federation.title", "/federation", true],
      ["federation.devices.title", "/federation/devices", true],
      ["federation.map.title", "/federation/map", true],
    ]);
    // Moved into the group, not duplicated: every route is registered once, at its old path.
    const paths = flatten(routesMenuConfig)
      .map((route) => route.path)
      .filter((path) => path?.startsWith("/federation"));

    expect(paths).toEqual(["/federation", "/federation/devices", "/federation/map"]);
  });

  it("is in this device's own window", async () => {
    useRemoteAccessStore.setState({
      initialized: true,
      isLocal: true,
      clientMode: ClientMode.AllInOne,
    });
    render(
      <MemoryRouter>
        <AntdMenu collapsed={false} />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getAllByText("federation.mode")[0]);

    expect(await screen.findAllByText("federation.map.title")).not.toHaveLength(0);
    expect(screen.getAllByText("federation.devices.title")).not.toHaveLength(0);
    expect(screen.getAllByText("federation.title")).not.toHaveLength(0);
  });

  it("is absent where the window shows a server this device manages, and from a browser", () => {
    for (const state of [
      { isLocal: false, clientMode: ClientMode.PureClient },
      { isLocal: false, clientMode: ClientMode.RemoteBrowser },
    ]) {
      useRemoteAccessStore.setState({ initialized: true, ...state });
      render(
        <MemoryRouter>
          <AntdMenu collapsed={false} />
        </MemoryRouter>,
      );
      expect(screen.queryAllByText("federation.mode")).toHaveLength(0);
      cleanup();
    }
  });
});
