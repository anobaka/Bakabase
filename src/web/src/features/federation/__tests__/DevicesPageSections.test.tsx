import type { FederationStatus, ManagedServersView } from "../types";
import type * as DataSyncApi from "@/features/data-sync/api";

import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, useNavigate } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DevicesPage from "../DevicesPage";
import { managedServerApi } from "../serverApi";
import { federationPeerApi } from "../peerApi";
import { useFederationStatus } from "../hooks/useFederationStatus";
import { REVEAL_HIGHLIGHT_MS } from "../hooks/useSectionReveal";

import BApi from "@/sdk/BApi";
import {
  ClientMode,
  DataSyncStatusLevel,
  ManagedServerState,
  RemoteAccessMode,
} from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { dataSyncApi } from "@/features/data-sync/api";
import { useDataSyncStore } from "@/features/data-sync/stores/dataSync";
import {
  mapPeer,
  mapView,
  overview as syncOverview,
  status as syncStatus,
} from "@/features/data-sync/__tests__/dataSyncFixtures";

/*
 * The devices page's sections: its nav, landing on a tab or a place inside one
 * (`?section=`), and what the page offers where the rest of it is not available. The
 * Service's "wants to manage this device" notification links to `?section=management`; the
 * window's switcher to `?section=servers`.
 *
 * The remote-access store is the real one, set directly: which window may administer the
 * server it shows is decided by its own selectors, and a copy of them here would keep
 * passing after the real ones changed.
 */

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options
        ? [key, ...Object.values(options).filter((value) => value !== undefined)].join(" ")
        : key,
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => false },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("../hooks/useFederationStatus", () => ({ useFederationStatus: vi.fn() }));
vi.mock("../peerApi", () => ({
  federationPeerApi: { status: vi.fn(), claim: vi.fn(), mappingRoots: vi.fn(), sharing: vi.fn() },
}));
vi.mock("../serverApi", () => ({ managedServerApi: { list: vi.fn() } }));
vi.mock("@/core/clientApi", () => ({
  LOCAL_SWITCHER_TARGET: "local",
  clientApi: { status: vi.fn(), switcher: { list: vi.fn(), open: vi.fn() } },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    remoteAccess: { getRemoteAccessSettings: vi.fn(), getRemoteAccessContext: vi.fn() },
  },
}));
// Data sync's own reader, which the page shares with the map; nothing to say unless a test says it.
vi.mock("@/features/data-sync/api", async (importOriginal) => ({
  ...(await importOriginal<typeof DataSyncApi>()),
  dataSyncApi: { map: vi.fn(), overview: vi.fn() },
}));

const initialStore = useRemoteAccessStore.getState();

/** The store as the context call would have left it for this kind of window. */
const setStore = (state: Partial<ReturnType<typeof useRemoteAccessStore.getState>>) =>
  useRemoteAccessStore.setState({ ...initialStore, ...state }, true);

const status: FederationStatus = {
  identity: { nodeId: "local", libraryEpoch: "epoch", name: "Desk" },
  browsingEnabled: true,
  sharingEnabled: false,
  remoteAccessMode: 0,
  requirePairing: false,
  peers: [],
  requests: [],
};
const emptyServers: ManagedServersView = { available: true, servers: [], requests: [] };

const laptopRequest = () => ({
  id: "req-1",
  deviceName: "Laptop",
  platform: 1,
  remoteAddress: "192.168.1.20",
  requestedAt: "2026-09-23 08:00:00.000",
  expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
});

const settingsResponse = (
  mode = RemoteAccessMode.Enabled,
  pendingRequests: ReturnType<typeof laptopRequest>[] = [laptopRequest()],
) => ({
  code: 0,
  data: {
    mode,
    addresses: [],
    allowLiveTranscode: false,
    requirePairing: mode !== RemoteAccessMode.Disabled,
    devices: [],
    pendingRequests,
  },
});

/** A promise the test resolves when it wants the answer to arrive. */
const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => (resolve = done));

  return { promise, resolve };
};

const scrollIntoView = vi.fn();

function AgainButton({ to }: { to: string }) {
  const navigate = useNavigate();

  return (
    <button type="button" onClick={() => navigate(to)}>
      again
    </button>
  );
}

const renderPage = (entry: string) =>
  render(
    <MemoryRouter initialEntries={[entry]}>
      <DevicesPage />
      <AgainButton to={entry} />
    </MemoryRouter>,
  );

const accessSection = () => screen.getByTestId("management-access");

beforeEach(() => {
  vi.clearAllMocks();
  setStore({ initialized: true, isLocal: true, clientMode: ClientMode.AllInOne });
  vi.mocked(useFederationStatus).mockReturnValue({
    status,
    loading: false,
    error: undefined,
    refresh: vi.fn().mockResolvedValue(undefined),
  });
  vi.mocked(managedServerApi.list).mockResolvedValue(emptyServers);
  vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue(
    settingsResponse() as never,
  );
  Element.prototype.scrollIntoView = scrollIntoView;
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  delete (Element.prototype as Partial<Element>).scrollIntoView;
  useRemoteAccessStore.setState(initialStore, true);
  useDataSyncStore.getState().clear();
});

const settingsReads = () => vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mock.calls.length;

describe("landing on a section of this device's own devices page", () => {
  it("brings the management section into view once the list above it has loaded", async () => {
    const servers = deferred<ManagedServersView>();

    vi.mocked(managedServerApi.list).mockReturnValueOnce(servers.promise);
    renderPage("/federation/devices?section=management");
    // The access section has its settings, but the servers list above it has not
    // answered: arriving now would be undone the moment that list fills.
    expect(await within(accessSection()).findByText("Laptop")).toBeInTheDocument();
    expect(scrollIntoView).not.toHaveBeenCalled();
    expect(accessSection()).not.toHaveFocus();

    await act(async () => servers.resolve(emptyServers));
    await waitFor(() => expect(accessSection()).toHaveFocus());
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
    expect(scrollIntoView.mock.contexts[0]).toBe(accessSection());
    expect(accessSection()).toHaveAttribute("data-highlighted", "true");
    // A part of the Management tab, under the tab's own heading.
    expect(within(accessSection()).getByRole("heading", { level: 3 })).toHaveTextContent(
      "federation.management.title federation.management.self",
    );
    expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent(
      "federation.devices.tab.manage",
    );
  });

  it("lets the mark fade, and lands again when the same link is followed again", async () => {
    // Fake, but still moving on their own, so the waits below keep working.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    renderPage("/federation/devices?section=management");
    await waitFor(() => expect(accessSection()).toHaveAttribute("data-highlighted", "true"));
    act(() => {
      vi.advanceTimersByTime(REVEAL_HIGHLIGHT_MS);
    });
    expect(accessSection()).not.toHaveAttribute("data-highlighted");

    // A notification clicked while this page is open: same URL, a new navigation.
    (document.activeElement as HTMLElement | null)?.blur();
    fireEvent.click(screen.getByText("again"));
    await waitFor(() => expect(accessSection()).toHaveAttribute("data-highlighted", "true"));
    expect(accessSection()).toHaveFocus();
    expect(scrollIntoView).toHaveBeenCalledTimes(2);
  });

  it("brings the managed servers into view for the switcher's link", async () => {
    renderPage("/federation/devices?section=servers");
    const section = await screen.findByRole("region", { name: /federation\.servers\.title/ });

    await waitFor(() => expect(section).toHaveFocus());
    expect(section).toHaveAttribute("data-highlighted", "true");
    expect(accessSection()).not.toHaveAttribute("data-highlighted");
  });

  it("shows this device's tab without a section, and moves nothing", async () => {
    renderPage("/federation/devices");
    // What waits here is listed, and decided in the Management tab.
    const waiting = await screen.findByTestId("devices-waiting");

    expect(waiting).toHaveTextContent("federation.devices.waiting.manage Laptop");
    expect(within(waiting).getByRole("link")).toHaveAttribute(
      "href",
      "/federation/devices?section=management",
    );
    await waitFor(() => expect(managedServerApi.list).toHaveBeenCalled());
    expect(screen.queryByTestId("management-access")).not.toBeInTheDocument();
    expect(screen.getByTestId("devices-panel")).toHaveAttribute("data-section", "device");
    expect(scrollIntoView).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(document.body);
  });

  it("lands on a tab's heading for a tab's link, without marking anything", async () => {
    renderPage("/federation/devices?section=manage");
    const heading = screen.getByRole("heading", { level: 2 });

    expect(heading).toHaveTextContent("federation.devices.tab.manage");
    await waitFor(() => expect(heading).toHaveFocus());
    expect(await within(accessSection()).findByText("Laptop")).toBeInTheDocument();
    expect(accessSection()).not.toHaveAttribute("data-highlighted");
  });

  it("opens the form a link asks for: adding a device to manage", async () => {
    vi.mocked(managedServerApi.list).mockResolvedValue({
      available: true,
      servers: [
        {
          serverId: "nas",
          name: "NAS",
          address: "http://192.168.1.5:34567",
          pairedAt: "2026-09-01T00:00:00Z",
          pathMappings: [],
          state: 1,
        },
      ],
      requests: [],
    });
    const { unmount } = renderPage("/federation/devices?section=servers");

    // With a server listed, the form waits behind a button…
    expect(await screen.findByTestId("managed-server")).toBeInTheDocument();
    expect(screen.queryByLabelText("federation.servers.add.address")).not.toBeInTheDocument();
    unmount();
    // …which the `add-server` link opens, bringing it into view.
    renderPage("/federation/devices?section=add-server");
    const form = document.getElementById("managed-server-add")!;

    await waitFor(() => expect(form).toHaveFocus());
    expect(form).toHaveAttribute("data-highlighted", "true");
    // What a screen reader lands on is named, not an anonymous box.
    expect(form).toHaveAttribute("role", "group");
    expect(form).toHaveAccessibleName("federation.servers.add.title");
    expect(within(form).getByLabelText("federation.servers.add.address")).toBeInTheDocument();
  });

  it.each([
    ["connect", "library-connect", "federation.devices.add"],
    ["browsing", "library-browsing", "federation.browsing.title"],
    [
      "addresses",
      "device-addresses",
      "federation.devices.addresses.title federation.management.self",
    ],
  ])("names the place the %s link lands on by its heading", async (section, id, name) => {
    renderPage(`/federation/devices?section=${section}`);
    const place = document.getElementById(id)!;

    await waitFor(() => expect(place).toHaveFocus());
    expect(place).toHaveAccessibleName(name);
  });

  it("falls back to the tab's heading when the place a link names is not there", async () => {
    // The browse request the notification announced was decided elsewhere meanwhile.
    renderPage("/federation/devices?section=sharing-requests");
    const heading = screen.getByRole("heading", { level: 2 });

    expect(heading).toHaveTextContent("federation.devices.tab.sharing");
    await waitFor(() => expect(heading).toHaveFocus());
    expect(scrollIntoView).not.toHaveBeenCalledWith({ block: "start" });
  });
});

describe("the devices page's nav", () => {
  const nav = () => screen.getByRole("navigation", { name: "federation.devices.nav.label" });
  const navLink = (tab: string) => nav().querySelector<HTMLAnchorElement>(`a[data-tab="${tab}"]`)!;

  it("lists the tabs in order as links, marks the current one, and moves focus to its heading", async () => {
    renderPage("/federation/devices");
    const links = within(nav()).getAllByRole("link");

    expect(links.map((link) => link.getAttribute("data-tab"))).toEqual([
      "device",
      "manage",
      "sharing",
      "sync",
      "advanced",
    ]);
    expect(links.map((link) => link.getAttribute("href"))).toEqual([
      "/federation/devices?section=device",
      "/federation/devices?section=manage",
      "/federation/devices?section=sharing",
      "/federation/devices?section=sync",
      "/federation/devices?section=advanced",
    ]);
    expect(navLink("device")).toHaveAttribute("aria-current", "page");
    fireEvent.click(navLink("sharing"));
    expect(navLink("sharing")).toHaveAttribute("aria-current", "page");
    expect(navLink("device")).not.toHaveAttribute("aria-current");
    const heading = screen.getByRole("heading", { level: 2 });

    expect(heading).toHaveTextContent("federation.devices.tab.sharing");
    await waitFor(() => expect(heading).toHaveFocus());
  });

  it("counts the requests waiting in a tab, in words as well as the number", async () => {
    vi.mocked(useFederationStatus).mockReturnValue({
      status: {
        ...status,
        requests: [
          {
            requestId: "browse-1",
            nodeId: "laptop",
            nodeName: "Laptop",
            direction: "incoming",
            status: "awaitingApproval",
            expiresAt: new Date(Date.now() + 60_000).toISOString(),
            replacesExistingAccess: false,
            offersReciprocalAccess: false,
          },
        ],
      },
      loading: false,
      error: undefined,
      refresh: vi.fn().mockResolvedValue(undefined),
    });
    renderPage("/federation/devices");
    await waitFor(() =>
      expect(navLink("manage")).toHaveTextContent("federation.devices.nav.pendingManage 1"),
    );
    expect(navLink("sharing")).toHaveTextContent("federation.devices.nav.pendingShare 1");
    // The number itself is hidden from screen readers, which hear the words instead.
    expect(navLink("sharing").querySelector('[aria-hidden="true"]:not(svg)')).toHaveTextContent(
      "1",
    );
    expect(navLink("device")).not.toHaveTextContent("pending");
    // The device tab lists the same requests: the badge is never the only way to learn it.
    expect(screen.getByTestId("devices-waiting")).toHaveTextContent(
      "federation.devices.waiting.browse Laptop",
    );
  });

  it("flags a tab that needs a look", async () => {
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue(
      settingsResponse(RemoteAccessMode.Unrestricted, []) as never,
    );
    renderPage("/federation/devices");
    await waitFor(() =>
      expect(navLink("manage")).toHaveTextContent("federation.devices.nav.attention"),
    );
    expect(screen.getByTestId("devices-waiting")).toHaveTextContent(
      "federation.devices.waiting.unrestricted",
    );
  });

  it("sums data sync up in its own tab, counts what waits for it here, and leads to its page", async () => {
    vi.mocked(dataSyncApi.map).mockResolvedValue(mapView({ peers: [mapPeer("nas", "NAS")] }));
    vi.mocked(dataSyncApi.overview).mockResolvedValue(
      syncOverview({
        openInboxItems: 2,
        status: syncStatus({ level: DataSyncStatusLevel.NeedsYou, openItems: 2, linksToReview: 1 }),
      }),
    );
    renderPage("/federation/devices?section=sync");
    const panel = await screen.findByTestId("devices-sync");

    expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent(
      "federation.devices.tab.sync",
    );
    await waitFor(() => expect(panel).toHaveTextContent("dataSync.status.NeedsYou 2"));
    expect(panel).toHaveTextContent("dataSync.sharing.isOn");
    // NAS is received from and reads this device.
    expect(
      within(panel)
        .getAllByRole("definition")
        .map((value) => value.textContent),
    ).toEqual(["1", "1"]);
    expect(within(panel).getByRole("link", { name: "dataSync.link.openPage" })).toHaveAttribute(
      "href",
      "/data-sync",
    );
    // Two decisions and a first sync to review wait here: the nav counts them in words too.
    expect(navLink("sync")).toHaveTextContent("federation.devices.nav.pendingSync 3");
    // And the device tab lists them, leading here.
    fireEvent.click(navLink("device"));
    expect(
      within(await screen.findByTestId("devices-waiting")).getByRole("link", {
        name: "federation.devices.waiting.sync 3",
      }),
    ).toHaveAttribute("href", "/federation/devices?section=sync");
  });

  it("offers nothing to manage where this installation cannot manage anything", async () => {
    vi.mocked(managedServerApi.list).mockResolvedValue({
      available: false,
      servers: [],
      requests: [],
    });
    renderPage("/federation/devices");
    const chooser = await screen.findByTestId("devices-chooser");

    await waitFor(() =>
      expect(
        within(chooser)
          .getAllByRole("link")
          .map((link) => link.getAttribute("href")),
      ).toEqual(["/federation/devices?section=connect"]),
    );
    fireEvent.click(navLink("manage"));
    expect(await screen.findByTestId("management-access")).toBeInTheDocument();
    expect(screen.queryByText("federation.servers.title")).not.toBeInTheDocument();
  });
});

describe("the devices page where the rest of it is not available", () => {
  it("in the desktop app showing a managed server: answers for that server, and offers the way back", async () => {
    setStore({
      initialized: true,
      isLocal: false,
      clientMode: ClientMode.PureClient,
      clientHost: "console",
      serverName: "NAS",
      localName: "Desk",
    });
    renderPage("/federation/devices?section=management");
    expect(screen.getByText("federation.console.localOnly")).toBeInTheDocument();
    expect(screen.getByText("federation.console.switchToThisDevice")).toBeInTheDocument();
    // The request waiting on the server this window shows, where it is approved.
    await waitFor(() => expect(accessSection()).toHaveFocus());
    expect(within(accessSection()).getByRole("heading", { level: 2 })).toHaveTextContent(
      "federation.management.title NAS",
    );
    expect(within(accessSection()).getByText("Laptop")).toBeInTheDocument();
    // This device's own lists belong to its own window and are not asked for here.
    expect(managedServerApi.list).not.toHaveBeenCalled();
    expect(useFederationStatus).not.toHaveBeenCalled();
  });

  it("while the relay has not yet said it is the console: already points back, and answers too", async () => {
    setStore({
      initialized: true,
      isLocal: false,
      clientMode: ClientMode.PureClient,
      clientHost: undefined,
      serverName: "NAS",
    });
    renderPage("/federation/devices?section=management");
    expect(screen.getByText("federation.console.localOnly")).toBeInTheDocument();
    await waitFor(() => expect(accessSection()).toHaveFocus());
  });

  it("in a browser on an unrestricted server, which may decide it", async () => {
    setStore({
      initialized: true,
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
      mode: RemoteAccessMode.Unrestricted,
      serverName: "NAS",
    });
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue(
      settingsResponse(RemoteAccessMode.Unrestricted) as never,
    );
    renderPage("/federation/devices?section=management");
    expect(screen.getByText("federation.localOnly")).toBeInTheDocument();
    expect(await screen.findByTestId("management-unrestricted")).toHaveTextContent(
      "federation.management.unrestricted NAS",
    );
  });

  it("not in a browser a paired-only server merely lets browse", async () => {
    setStore({
      initialized: true,
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
      mode: RemoteAccessMode.Enabled,
    });
    renderPage("/federation/devices?section=management");
    expect(screen.getByText("federation.localOnly")).toBeInTheDocument();
    expect(screen.queryByTestId("management-access")).not.toBeInTheDocument();
    expect(BApi.remoteAccess.getRemoteAccessSettings).not.toHaveBeenCalled();
  });
});

/*
 * The page reads the remote-access settings for all its tabs. These are the two moments
 * they change under it while the page stays open, and both have to show at once — not on
 * the next poll.
 */
describe("the management section keeps up with the page around it", () => {
  it("reads its settings again when the sharing panel turns remote access on", async () => {
    // The page's status as the server would report it; `refresh` reads it again.
    let reported = status;

    vi.mocked(useFederationStatus).mockImplementation(() => {
      const [shown, setShown] = useState(reported);

      return {
        status: shown,
        loading: false,
        error: undefined,
        refresh: async () => setShown(reported),
      };
    });
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue(
      settingsResponse(RemoteAccessMode.Disabled, []) as never,
    );
    // Starting sharing with remote access off also opens it: Enabled, pairing required.
    vi.mocked(federationPeerApi.sharing).mockImplementation(async () => {
      reported = {
        ...status,
        sharingEnabled: true,
        remoteAccessMode: RemoteAccessMode.Enabled,
        requirePairing: true,
      };
      vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue(
        settingsResponse() as never,
      );

      return {};
    });
    renderPage("/federation/devices?section=sharing");
    await waitFor(() => expect(settingsReads()).toBeGreaterThan(0));
    const before = settingsReads();

    fireEvent.click(screen.getByRole("button", { name: "federation.sharing.start" }));
    fireEvent.click(within(screen.getByRole("alertdialog")).getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.sharing).toHaveBeenCalledWith(true, true));
    await waitFor(() => expect(settingsReads()).toBeGreaterThan(before));
    // Over in Management the new mode already shows — far short of the settings' idle poll.
    fireEvent.click(
      screen
        .getByRole("navigation", { name: "federation.devices.nav.label" })
        .querySelector<HTMLAnchorElement>('a[data-tab="manage"]')!,
    );
    expect(
      await within(accessSection()).findByText("federation.management.status.paired"),
    ).toBeInTheDocument();
    expect(within(accessSection()).getByText("Laptop")).toBeInTheDocument();
    expect(settingsReads()).toBeGreaterThan(before);
  });

  it("reads them again when the notification leads here while the page is open", async () => {
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue(
      settingsResponse(RemoteAccessMode.Enabled, []) as never,
    );
    renderPage("/federation/devices?section=management");
    await waitFor(() => expect(accessSection()).toHaveFocus());
    expect(within(accessSection()).queryByText("Laptop")).not.toBeInTheDocument();
    const before = settingsReads();

    // A laptop asks to manage this computer; its notification is followed.
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue(
      settingsResponse() as never,
    );
    (document.activeElement as HTMLElement | null)?.blur();
    fireEvent.click(screen.getByText("again"));
    expect(await within(accessSection()).findByText("Laptop")).toBeInTheDocument();
    expect(settingsReads()).toBe(before + 1);
    await waitFor(() => expect(accessSection()).toHaveFocus());
  });

  it("does the same for the server a managed window shows", async () => {
    setStore({
      initialized: true,
      isLocal: false,
      clientMode: ClientMode.PureClient,
      clientHost: "console",
      serverName: "NAS",
    });
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue(
      settingsResponse(RemoteAccessMode.Enabled, []) as never,
    );
    renderPage("/federation/devices?section=management");
    await waitFor(() => expect(accessSection()).toHaveFocus());
    const before = settingsReads();

    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue(
      settingsResponse() as never,
    );
    fireEvent.click(screen.getByText("again"));
    expect(await within(accessSection()).findByText("Laptop")).toBeInTheDocument();
    expect(settingsReads()).toBe(before + 1);
  });
});

describe("the devices page's forms", () => {
  const listed = (state: ManagedServerState): ManagedServersView => ({
    available: true,
    servers: [
      {
        serverId: "nas",
        name: "NAS",
        address: "http://192.168.1.5:34567",
        pairedAt: "2026-09-01T00:00:00Z",
        pathMappings: [],
        state,
        answeredBy:
          state === ManagedServerState.WrongServer
            ? { serverId: "other", name: "Other", isThisDevice: false }
            : null,
      },
    ],
    requests: [],
  });

  it("keeps the add form open while a managed server has to be found again", async () => {
    // The card's tip sends the reader to the search in the add form: it has to be there.
    vi.mocked(managedServerApi.list).mockResolvedValue(listed(ManagedServerState.WrongServer));
    renderPage("/federation/devices?section=servers");

    expect(await screen.findByTestId("managed-server-wrong-server")).toHaveTextContent(
      "federation.servers.wrongServerTip",
    );
    expect(
      screen.getByRole("button", { name: "federation.servers.add.discover" }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("federation.servers.add.address")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "federation.servers.add.title" }),
    ).not.toBeInTheDocument();
  });

  it("moves the keyboard into the add form opened from its button, on Management", async () => {
    vi.mocked(managedServerApi.list).mockResolvedValue(listed(ManagedServerState.Online));
    renderPage("/federation/devices?section=manage");
    const toggle = await screen.findByRole("button", { name: "federation.servers.add.title" });

    expect(toggle).toHaveAttribute("aria-expanded", "false");
    toggle.focus();
    fireEvent.click(toggle);
    // Not the section's heading above the cards: the field the reader came to fill in.
    const field = screen.getByLabelText("federation.servers.add.address");

    await waitFor(() => expect(field).toHaveFocus());
    await act(async () => new Promise((done) => setTimeout(done, 10)));
    expect(field).toHaveFocus();
  });

  it("does the same on Library sharing", async () => {
    vi.mocked(useFederationStatus).mockReturnValue({
      status: {
        ...status,
        peers: [
          {
            nodeId: "nas",
            label: "NAS",
            address: "http://192.168.1.5:34567",
            enabled: true,
            connectionState: "Online",
            outboundGrant: { grantId: "grant", revision: 1 },
            pathMappings: [],
          },
        ],
      },
      loading: false,
      error: undefined,
      refresh: vi.fn().mockResolvedValue(undefined),
    });
    vi.mocked(federationPeerApi.mappingRoots).mockResolvedValue([]);
    renderPage("/federation/devices?section=sharing");
    const toggle = await screen.findByRole("button", { name: "federation.devices.add" });

    expect(toggle).toHaveAttribute("aria-expanded", "false");
    toggle.focus();
    fireEvent.click(toggle);
    const field = screen.getByLabelText("federation.pair.address");

    await waitFor(() => expect(field).toHaveFocus());
    await act(async () => new Promise((done) => setTimeout(done, 10)));
    expect(field).toHaveFocus();
  });

  describe("the address beside a share code", () => {
    const sharing = () =>
      vi.mocked(useFederationStatus).mockReturnValue({
        status: { ...status, sharingEnabled: true, remoteAccessMode: RemoteAccessMode.Enabled },
        loading: false,
        error: undefined,
        refresh: vi.fn().mockResolvedValue(undefined),
      });

    it("says it is loading while remote access's settings are on their way, never that none was found", async () => {
      const settings = deferred<unknown>();

      sharing();
      vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockReturnValue(
        settings.promise as never,
      );
      renderPage("/federation/devices?section=share");
      const list = await screen.findByTestId("device-addresses");

      expect(list).toHaveTextContent("federation.loading");
      expect(list).not.toHaveTextContent("federation.devices.addresses.none");
      const answer = settingsResponse(RemoteAccessMode.Enabled, []);

      await act(async () =>
        settings.resolve({
          ...answer,
          data: {
            ...answer.data,
            addresses: [{ url: "http://192.168.1.2:34567", interfaceName: "en0" }],
          },
        }),
      );
      expect(await within(list).findByText("http://192.168.1.2:34567")).toBeInTheDocument();
    });

    it("says why the settings could not be read, with a way to try again", async () => {
      sharing();
      vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockRejectedValue(new Error("down"));
      renderPage("/federation/devices?section=share");
      const list = await screen.findByTestId("device-addresses");

      await waitFor(() => expect(within(list).getByRole("alert")).toBeInTheDocument());
      expect(list).not.toHaveTextContent("federation.devices.addresses.none");
      vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue(
        settingsResponse() as never,
      );
      const before = settingsReads();

      fireEvent.click(within(list).getByRole("button", { name: "federation.retry" }));
      await waitFor(() => expect(settingsReads()).toBe(before + 1));
    });
  });
});
