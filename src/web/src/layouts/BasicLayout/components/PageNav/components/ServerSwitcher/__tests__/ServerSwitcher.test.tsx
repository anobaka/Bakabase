import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ServerSwitcher from "..";

import { ClientMode, ManagedServerState } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

// The store is real; only its loader's dependency is stubbed, since each test sets the
// state it needs directly.
vi.mock("@/sdk/BApi", () => ({ default: {} }));

/** Keys the translation files answer for; none unless a test is about one. */
const translated = vi.hoisted(() => new Set<string>());

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string) => key,
    i18n: { language: "en", changeLanguage: vi.fn(), exists: (key: string) => translated.has(key) },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));

type Reply = { status?: number; body: unknown };
type Route_ = (url: string, init?: RequestInit) => Reply | undefined;

const initialState = useRemoteAccessStore.getState();
const assign = vi.fn();
let routes: Route_[] = [];
const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input);

  for (const route of routes) {
    const reply = route(url, init);

    if (reply) {
      const status = reply.status ?? 200;

      return {
        ok: status >= 200 && status < 300,
        status,
        statusText: String(status),
        json: async () => reply.body,
      } as Response;
    }
  }
  throw new Error(`Unexpected request ${init?.method ?? "GET"} ${url}`);
});

const on =
  (method: string, path: string, body: unknown, status?: number): Route_ =>
  (url, init) =>
    (init?.method ?? "GET") === method && url === path ? { body, status } : undefined;

const server = (overrides: Record<string, unknown> = {}) => ({
  serverId: "nas",
  name: "NAS",
  address: "http://192.168.1.5:34567",
  pairedAt: "2026-09-01T00:00:00Z",
  pathMappings: [],
  state: ManagedServerState.Unknown,
  importedFromLegacyClient: false,
  ...overrides,
});

const requestsTo = (method: string, path: string) =>
  fetchMock.mock.calls.filter(
    ([input, init]) => String(input) === path && (init?.method ?? "GET") === method,
  );

function LocationProbe() {
  const location = useLocation();

  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>;
}

const renderSwitcher = (collapsed = false) =>
  render(
    <MemoryRouter initialEntries={["/resource"]}>
      <ServerSwitcher collapsed={collapsed} />
      <Routes>
        <Route element={<LocationProbe />} path="*" />
      </Routes>
    </MemoryRouter>,
  );

const trigger = () => screen.getByRole("button", { name: "federation.switcher.label" });
const openMenu = async () => {
  await act(async () => {
    fireEvent.click(trigger());
  });

  return screen.getByRole("menu");
};

beforeEach(() => {
  routes = [];
  translated.clear();
  fetchMock.mockClear();
  assign.mockReset();
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("location", { ...window.location, href: "http://localhost:5173/", assign });
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  useRemoteAccessStore.setState(initialState, true);
});

describe("this device's own window", () => {
  beforeEach(() => {
    useRemoteAccessStore.setState({
      initialized: true,
      isLocal: true,
      clientMode: ClientMode.AllInOne,
      serverName: "Desk",
    });
  });

  it("lists this device and the managed servers, and switches the window to the one chosen", async () => {
    routes = [
      on("GET", "/federation/local/servers", {
        available: true,
        servers: [server()],
        requests: [],
      }),
      on("GET", "/federation/local/servers?probe=true", {
        available: true,
        servers: [server({ state: ManagedServerState.Offline })],
        requests: [],
      }),
      on("POST", "/federation/local/servers/nas/open", {
        url: "http://127.0.0.1:34650/?__bakabase_switch=token",
      }),
    ];
    renderSwitcher();
    expect(within(trigger()).getByText("Desk")).toBeInTheDocument();
    expect(screen.queryByText("federation.switcher.managing")).not.toBeInTheDocument();
    await waitFor(() => expect(requestsTo("GET", "/federation/local/servers")).toHaveLength(1));

    const menu = await openMenu();

    // Opening asks for fresh states.
    await waitFor(() =>
      expect(requestsTo("GET", "/federation/local/servers?probe=true")).toHaveLength(1),
    );
    const items = within(menu).getAllByRole("menuitem");

    expect(items[0]).toHaveTextContent("Desk");
    expect(items[0]).toHaveTextContent("federation.thisDevice");
    expect(items[0]).toHaveAttribute("aria-current", "true");
    await waitFor(() =>
      expect(within(menu).getByRole("menuitem", { name: /NAS/ })).toHaveTextContent(
        `federation.servers.state.${ManagedServerState.Offline}`,
      ),
    );
    const nasDot = within(within(menu).getByRole("menuitem", { name: /NAS/ })).getByTestId(
      "server-state-dot",
    );

    expect(nasDot).toHaveAttribute("data-state", "Offline");
    expect(nasDot).toHaveClass("bg-default-300");
    expect(within(items[0]).queryByTestId("server-state-dot")).not.toBeInTheDocument();
    expect(items[items.length - 1]).toHaveTextContent("federation.switcher.manageDevices");

    await act(async () => {
      fireEvent.click(within(menu).getByRole("menuitem", { name: /NAS/ }));
    });
    await waitFor(() =>
      expect(assign).toHaveBeenCalledWith("http://127.0.0.1:34650/?__bakabase_switch=token"),
    );
    expect(
      JSON.parse(String(requestsTo("POST", "/federation/local/servers/nas/open")[0][1]?.body)),
    ).toEqual({});
  });

  it("choosing this device only closes the menu", async () => {
    routes = [
      on("GET", "/federation/local/servers", { available: true, servers: [], requests: [] }),
    ];
    routes.push(
      on("GET", "/federation/local/servers?probe=true", {
        available: true,
        servers: [],
        requests: [],
      }),
    );
    renderSwitcher();
    const menu = await openMenu();

    expect(within(menu).getByText("federation.switcher.empty")).toBeInTheDocument();
    fireEvent.click(within(menu).getAllByRole("menuitem")[0]);
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
  });

  it("leads to the devices page in this window", async () => {
    routes = [
      on("GET", "/federation/local/servers", { available: true, servers: [], requests: [] }),
      on("GET", "/federation/local/servers?probe=true", {
        available: true,
        servers: [],
        requests: [],
      }),
    ];
    renderSwitcher();
    const menu = await openMenu();

    fireEvent.click(within(menu).getByRole("menuitem", { name: /manageDevices/ }));
    // Straight to the list of managed servers on this device's own page.
    expect(screen.getByTestId("location")).toHaveTextContent("/federation/devices?section=servers");
    await waitFor(() => expect(screen.queryByRole("menu")).not.toBeInTheDocument());
    expect(assign).not.toHaveBeenCalled();
  });

  it("says so in the menu when the managed servers cannot be read, and tries again", async () => {
    let fail = true;

    routes = [
      (url) =>
        url.startsWith("/federation/local/servers") && fail
          ? { status: 500, body: { code: "InternalError", message: "boom" } }
          : undefined,
      on("GET", "/federation/local/servers?probe=true", {
        available: true,
        servers: [server()],
        requests: [],
      }),
    ];
    renderSwitcher();
    await waitFor(() => expect(requestsTo("GET", "/federation/local/servers")).toHaveLength(1));
    const menu = await openMenu();

    // This device is still offered; the failure is said where the servers would be.
    expect(within(menu).getAllByRole("menuitem")[0]).toHaveTextContent("Desk");
    expect(await within(menu).findByRole("alert")).toHaveTextContent(
      "federation.switcher.listFailed",
    );
    expect(within(menu).queryByText("federation.switcher.empty")).not.toBeInTheDocument();
    fail = false;
    await act(async () => {
      fireEvent.click(within(menu).getByRole("menuitem", { name: "federation.retry" }));
    });
    expect(await within(menu).findByRole("menuitem", { name: /NAS/ })).toBeInTheDocument();
    expect(within(menu).queryByRole("alert")).not.toBeInTheDocument();
  });

  it("walks the menu with the keyboard and closes when focus leaves it", async () => {
    routes = [
      on("GET", "/federation/local/servers", {
        available: true,
        servers: [server()],
        requests: [],
      }),
      on("GET", "/federation/local/servers?probe=true", {
        available: true,
        servers: [server()],
        requests: [],
      }),
    ];
    renderSwitcher();
    await waitFor(() => expect(requestsTo("GET", "/federation/local/servers")).toHaveLength(1));
    await screen.findByRole("button", { name: "federation.switcher.label" });
    // ArrowUp on the button opens the menu at its last item.
    await act(async () => {
      fireEvent.keyDown(trigger(), { key: "ArrowUp" });
    });
    const menu = screen.getByRole("menu");
    const items = () => within(menu).getAllByRole("menuitem");

    expect(items()[items().length - 1]).toHaveFocus();
    fireEvent.keyDown(menu, { key: "Home" });
    expect(items()[0]).toHaveFocus();
    fireEvent.keyDown(menu, { key: "ArrowUp" });
    expect(items()[items().length - 1]).toHaveFocus();
    fireEvent.keyDown(menu, { key: "ArrowDown" });
    expect(items()[0]).toHaveFocus();
    fireEvent.keyDown(menu, { key: "End" });
    expect(items()[items().length - 1]).toHaveFocus();
    // Focus moving within the menu keeps it open; moving out of it closes it.
    fireEvent.blur(items()[0], { relatedTarget: items()[1] });
    expect(screen.getByRole("menu")).toBeInTheDocument();
    fireEvent.blur(items()[0], { relatedTarget: document.body });
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
  });

  it("says so when a server cannot be opened, and stays put", async () => {
    routes = [
      on("GET", "/federation/local/servers", {
        available: true,
        servers: [server()],
        requests: [],
      }),
      on("GET", "/federation/local/servers?probe=true", {
        available: true,
        servers: [server()],
        requests: [],
      }),
      on(
        "POST",
        "/federation/local/servers/nas/open",
        { code: "ServerNotManaged", message: "Not managed" },
        404,
      ),
    ];
    renderSwitcher();
    await waitFor(() => expect(requestsTo("GET", "/federation/local/servers")).toHaveLength(1));
    const menu = await openMenu();

    await act(async () => {
      fireEvent.click(await within(menu).findByRole("menuitem", { name: /NAS/ }));
    });
    expect(await within(menu).findByRole("alert")).toHaveTextContent(
      "federation.switcher.openFailed",
    );
    expect(assign).not.toHaveBeenCalled();
    expect(within(menu).getByRole("menuitem", { name: /NAS/ })).not.toBeDisabled();
  });

  it("says why a server could not be opened when there are words for the reason", async () => {
    translated.add("federation.error.RelayUnavailable");
    routes = [
      on("GET", "/federation/local/servers", {
        available: true,
        servers: [server()],
        requests: [],
      }),
      on("GET", "/federation/local/servers?probe=true", {
        available: true,
        servers: [server()],
        requests: [],
      }),
      on(
        "POST",
        "/federation/local/servers/nas/open",
        { code: "RelayUnavailable", message: "No loopback port is free.", retryable: true },
        503,
      ),
    ];
    renderSwitcher();
    const menu = await openMenu();

    await act(async () => {
      fireEvent.click(await within(menu).findByRole("menuitem", { name: /NAS/ }));
    });
    expect(await within(menu).findByRole("alert")).toHaveTextContent(
      "federation.error.RelayUnavailable",
    );
    expect(assign).not.toHaveBeenCalled();
  });

  it("closes on Escape and hands focus back to the button", async () => {
    routes = [
      on("GET", "/federation/local/servers", { available: true, servers: [], requests: [] }),
      on("GET", "/federation/local/servers?probe=true", {
        available: true,
        servers: [],
        requests: [],
      }),
    ];
    renderSwitcher();
    await openMenu();
    expect(trigger()).toHaveAttribute("aria-expanded", "true");
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(trigger()).toHaveFocus();
  });

  it("stays a menu when collapsed, showing the initial", async () => {
    routes = [
      on("GET", "/federation/local/servers", {
        available: true,
        servers: [server()],
        requests: [],
      }),
      on("GET", "/federation/local/servers?probe=true", {
        available: true,
        servers: [server()],
        requests: [],
      }),
    ];
    renderSwitcher(true);
    expect(trigger()).toHaveTextContent("D");
    expect(trigger()).toHaveAttribute("title", "Desk");
    const menu = await openMenu();

    expect(await within(menu).findByRole("menuitem", { name: /NAS/ })).toBeInTheDocument();
  });

  it("keeps the plain brand link where nothing can be managed", async () => {
    routes = [
      on("GET", "/federation/local/servers", { available: false, servers: [], requests: [] }),
    ];
    renderSwitcher();
    expect(await screen.findByRole("link", { name: "Bakabase" })).toHaveAttribute("href", "/");
    expect(
      screen.queryByRole("button", { name: "federation.switcher.label" }),
    ).not.toBeInTheDocument();
  });
});

describe("the console (the desktop app showing a managed server)", () => {
  const switcher = {
    code: 0,
    data: {
      currentId: "nas",
      targets: [
        { id: "local", name: "Desk", isLocal: true, isCurrent: false },
        { id: "nas", name: "NAS", isLocal: false, isCurrent: true },
        { id: "studio", name: "Studio", isLocal: false, isCurrent: false },
      ],
    },
  };

  beforeEach(() => {
    useRemoteAccessStore.setState({
      initialized: true,
      isLocal: false,
      clientMode: ClientMode.PureClient,
      clientHost: "console",
      serverName: "NAS",
      localName: "Desk",
    });
    routes = [on("GET", "/client/switcher", switcher)];
  });

  it("marks which server the window is managing", async () => {
    renderSwitcher();
    const button = trigger();

    expect(within(button).getByText("federation.switcher.managing")).toBeInTheDocument();
    expect(within(button).getByText("NAS")).toBeInTheDocument();
    await waitFor(() => expect(requestsTo("GET", "/client/switcher")).toHaveLength(1));
    // Management lists live on the device's own origin, never here.
    expect(fetchMock.mock.calls.some(([input]) => String(input).includes("/federation/"))).toBe(
      false,
    );
  });

  it("switches through the relay to another managed server", async () => {
    routes.push(
      on("POST", "/client/switcher/studio/open", {
        code: 0,
        data: { url: "http://127.0.0.1:34651/?__bakabase_switch=abc" },
      }),
    );
    renderSwitcher();
    const menu = await openMenu();
    const studio = await within(menu).findByRole("menuitem", { name: /Studio/ });

    expect(within(menu).getByRole("menuitem", { name: /NAS/ })).toHaveAttribute(
      "aria-current",
      "true",
    );
    expect(within(menu).getByRole("menuitem", { name: /Desk/ })).toHaveTextContent(
      "federation.thisDevice",
    );
    await act(async () => {
      fireEvent.click(studio);
    });
    await waitFor(() =>
      expect(assign).toHaveBeenCalledWith("http://127.0.0.1:34651/?__bakabase_switch=abc"),
    );
  });

  it("shows each server's state as the relay reports it, as this device's own window does", async () => {
    routes = [
      on("GET", "/client/switcher", {
        code: 0,
        data: {
          currentId: "nas",
          targets: [
            { id: "local", name: "Desk", isLocal: true, isCurrent: false },
            {
              id: "nas",
              name: "NAS",
              isLocal: false,
              isCurrent: true,
              state: ManagedServerState.Online,
            },
            {
              id: "studio",
              name: "Studio",
              isLocal: false,
              isCurrent: false,
              state: ManagedServerState.Offline,
            },
            {
              id: "old",
              name: "Old PC",
              isLocal: false,
              isCurrent: false,
              state: ManagedServerState.Revoked,
            },
            {
              id: "fresh",
              name: "Fresh PC",
              isLocal: false,
              isCurrent: false,
              state: ManagedServerState.Unknown,
            },
          ],
        },
      }),
    ];
    renderSwitcher();
    const menu = await openMenu();
    const item = (name: RegExp) => within(menu).findByRole("menuitem", { name });
    const dot = async (name: RegExp) => within(await item(name)).getByTestId("server-state-dot");

    expect(await dot(/NAS/)).toHaveAttribute("data-state", "Online");
    expect(await dot(/NAS/)).toHaveClass("bg-success");
    expect(await dot(/Studio/)).toHaveAttribute("data-state", "Offline");
    expect(await dot(/Studio/)).toHaveClass("bg-default-300");
    expect(await dot(/Old PC/)).toHaveAttribute("data-state", "Revoked");
    expect(await dot(/Old PC/)).toHaveClass("bg-danger");
    expect(await dot(/Fresh PC/)).toHaveAttribute("data-state", "Unknown");
    expect(await dot(/Fresh PC/)).toHaveClass("border");
    // The same words as this device's own window for the two that cannot be opened now.
    expect(await item(/Studio/)).toHaveTextContent(
      `federation.servers.state.${ManagedServerState.Offline}`,
    );
    expect(await item(/Old PC/)).toHaveTextContent(
      `federation.servers.state.${ManagedServerState.Revoked}`,
    );
    expect(await item(/NAS/)).not.toHaveTextContent("federation.servers.state.");
    // This device is shown by its icon, never a dot.
    expect(within(await item(/Desk/)).queryByTestId("server-state-dot")).not.toBeInTheDocument();
  });

  it("reads a state the relay does not report, or one this page does not know, as not checked", async () => {
    routes = [
      on("GET", "/client/switcher", {
        code: 0,
        data: {
          currentId: "nas",
          targets: [
            // An older desktop app says nothing about states.
            { id: "local", name: "Desk", isLocal: true, isCurrent: false },
            { id: "nas", name: "NAS", isLocal: false, isCurrent: true },
            // A newer one may know a state this page does not.
            { id: "studio", name: "Studio", isLocal: false, isCurrent: false, state: 9 },
          ],
        },
      }),
    ];
    renderSwitcher();
    const menu = await openMenu();

    for (const name of [/NAS/, /Studio/]) {
      const entry = await within(menu).findByRole("menuitem", { name });
      const dot = within(entry).getByTestId("server-state-dot");

      expect(dot).toHaveAttribute("data-state", "Unknown");
      expect(dot).toHaveClass("border", "border-default-300");
      expect(entry).not.toHaveTextContent("federation.servers.state.");
    }
  });

  it("goes back to this device's own devices page for management", async () => {
    routes.push(
      on("POST", "/client/switcher/local/open", {
        code: 0,
        data: { url: "http://localhost:34567/" },
      }),
    );
    renderSwitcher();
    const menu = await openMenu();

    await act(async () => {
      fireEvent.click(within(menu).getByRole("menuitem", { name: /manageDevices/ }));
    });
    await waitFor(() =>
      expect(assign).toHaveBeenCalledWith(
        "http://localhost:34567/#/federation/devices?section=servers",
      ),
    );
    // The route travels in the hash, never as a path the relay would have to resolve.
    expect(
      JSON.parse(String(requestsTo("POST", "/client/switcher/local/open")[0][1]?.body)),
    ).toEqual({});
  });

  it("still offers the way back to this device when the list cannot be read", async () => {
    routes = [
      on("GET", "/client/switcher", { code: 500 }, 500),
      on("POST", "/client/switcher/local/open", {
        code: 0,
        data: { url: "http://localhost:34567/" },
      }),
    ];
    renderSwitcher();
    const menu = await openMenu();
    const local = within(menu).getByRole("menuitem", { name: /Desk/ });

    // Not silent: the menu says the list is missing, and still offers the way back.
    expect(await within(menu).findByRole("alert")).toHaveTextContent(
      "federation.switcher.listFailed",
    );
    await act(async () => {
      fireEvent.click(local);
    });
    await waitFor(() => expect(assign).toHaveBeenCalledWith("http://localhost:34567/"));
  });

  it("still says which server it is managing when the menu is collapsed", async () => {
    renderSwitcher(true);
    const button = trigger();

    expect(button).toHaveTextContent("N");
    expect(button).toHaveAttribute("title", "federation.switcher.managingName");
    const menu = await openMenu();

    // The open menu spells out what the collapsed button can only abbreviate.
    expect(within(menu).getByText("federation.switcher.managingName")).toBeInTheDocument();
    expect(await within(menu).findByRole("menuitem", { name: /Studio/ })).toBeInTheDocument();
  });

  it("refuses a destination that is not a web page", async () => {
    routes.push(
      on("POST", "/client/switcher/studio/open", { code: 0, data: { url: "javascript:alert(1)" } }),
    );
    renderSwitcher();
    const menu = await openMenu();

    await act(async () => {
      fireEvent.click(await within(menu).findByRole("menuitem", { name: /Studio/ }));
    });
    expect(await within(menu).findByRole("alert")).toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();
  });

  it("says why the relay could not switch, in the same words as this computer's own window", async () => {
    translated.add("federation.error.RelayUnavailable");
    routes.push(
      on("POST", "/client/switcher/studio/open", { code: 503, message: "RelayUnavailable" }, 503),
    );
    renderSwitcher();
    const menu = await openMenu();

    await act(async () => {
      fireEvent.click(await within(menu).findByRole("menuitem", { name: /Studio/ }));
    });
    expect(await within(menu).findByRole("alert")).toHaveTextContent(
      "federation.error.RelayUnavailable",
    );
    expect(assign).not.toHaveBeenCalled();
    expect(within(menu).getByRole("menuitem", { name: /Studio/ })).not.toBeDisabled();
  });

  it("falls back to a general message for a refusal it has no words for", async () => {
    routes.push(on("POST", "/client/switcher/studio/open", { code: 500 }, 500));
    renderSwitcher();
    const menu = await openMenu();

    await act(async () => {
      fireEvent.click(await within(menu).findByRole("menuitem", { name: /Studio/ }));
    });
    expect(await within(menu).findByRole("alert")).toHaveTextContent(
      "federation.switcher.openFailed",
    );
  });
});

describe("windows with nothing to switch to", () => {
  it.each([
    [
      "a client not yet identified",
      { clientMode: ClientMode.PureClient, clientHost: undefined, isLocal: false },
    ],
    ["a browser on another device", { clientMode: ClientMode.RemoteBrowser, isLocal: false }],
  ])("keeps the plain brand link in %s and asks nothing", (_, state) => {
    useRemoteAccessStore.setState({ initialized: true, ...state } as never);
    renderSwitcher();
    expect(screen.getByRole("link", { name: "Bakabase" })).toHaveAttribute("href", "/");
    expect(screen.queryByTestId("server-switcher")).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("collapses the brand link to its initial", () => {
    useRemoteAccessStore.setState({
      initialized: true,
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
    });
    renderSwitcher(true);
    expect(screen.getByRole("link", { name: "B" })).toBeInTheDocument();
  });
});
