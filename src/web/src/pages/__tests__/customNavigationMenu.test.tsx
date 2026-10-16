import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import AntdMenu from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { useUiOptionsStore } from "@/stores/options";

vi.mock("@/sdk/BApi", () => {
  const anything = (): unknown =>
    new Proxy(() => Promise.resolve({ code: 0 }), {
      get: (_, key) => (key === "then" ? undefined : anything()),
    });

  return { default: anything() };
});
vi.mock("@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig", () => ({
  asideMenuConfig: [
    { name: "resource", path: "/resource" },
    { name: "tools", children: [{ name: "alpha", path: "/alpha" }] },
    {
      name: "thisComputer",
      pureClientOnly: true,
      children: [{ name: "pathMapping", path: "/path-mapping" }],
    },
  ],
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));

const remoteInitial = useRemoteAccessStore.getState();
const uiInitial = useUiOptionsStore.getState();

afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(remoteInitial, true);
  useUiOptionsStore.setState(uiInitial, true);
});

const knownCatalogIds = [
  "menu:resource",
  "directory:tools",
  "menu:alpha",
  "directory:thisComputer",
  "menu:pathMapping",
];
const LocationProbe = () => {
  const current = useLocation();

  return <output data-testid="location">{current.pathname}</output>;
};

describe("custom navigation rendering", () => {
  it("uses custom directories and navigates moved items by their real route", () => {
    useUiOptionsStore.setState({
      data: {
        ...uiInitial.data,
        navigationLayout: {
          version: 1,
          knownCatalogIds,
          directoryAliases: {},
          items: [
            {
              id: "custom:personal",
              kind: "directory",
              name: "My tools",
              hidden: false,
              children: [{ id: "menu:alpha", kind: "menu", hidden: false }],
            },
            { id: "menu:resource", kind: "menu", hidden: true },
          ],
        },
      },
    });
    render(
      <MemoryRouter initialEntries={["/resource"]}>
        <AntdMenu collapsed={false} />
        <LocationProbe />
      </MemoryRouter>,
    );
    expect(screen.getAllByText("My tools")).not.toHaveLength(0);
    expect(screen.queryByText("resource")).toBeNull();
    fireEvent.click(screen.getAllByText("My tools")[0]!);
    fireEvent.click(screen.getAllByText("alpha")[0]!);
    expect(screen.getByTestId("location").textContent).toBe("/alpha");
  });

  it("supports a root-level feature while preserving its fixed catalog label", () => {
    useUiOptionsStore.setState({
      data: {
        ...uiInitial.data,
        navigationLayout: {
          version: 1,
          knownCatalogIds,
          directoryAliases: {},
          items: [{ id: "menu:alpha", kind: "menu", name: "Attempted rename", hidden: false }],
        },
      },
    });
    render(
      <MemoryRouter>
        <AntdMenu collapsed={false} />
        <LocationProbe />
      </MemoryRouter>,
    );
    expect(screen.queryByText("Attempted rename")).toBeNull();
    fireEvent.click(screen.getAllByText("alpha")[0]!);
    expect(screen.getByTestId("location").textContent).toBe("/alpha");
  });

  it("keeps the original desktop capability after moving path mapping to the root", () => {
    useUiOptionsStore.setState({
      data: {
        ...uiInitial.data,
        navigationLayout: {
          version: 1,
          knownCatalogIds,
          directoryAliases: {},
          items: [{ id: "menu:pathMapping", kind: "menu", hidden: false }],
        },
      },
    });
    useRemoteAccessStore.setState({
      initialized: true,
      clientMode: ClientMode.RemoteBrowser,
      isLocal: true,
    });
    const { rerender } = render(
      <MemoryRouter>
        <AntdMenu collapsed={false} />
      </MemoryRouter>,
    );

    expect(screen.queryByText("pathMapping")).toBeNull();
    useRemoteAccessStore.setState({
      initialized: true,
      clientMode: ClientMode.PureClient,
      isLocal: false,
    });
    rerender(
      <MemoryRouter>
        <AntdMenu collapsed={false} />
      </MemoryRouter>,
    );
    expect(screen.getAllByText("pathMapping")).not.toHaveLength(0);
  });
});
