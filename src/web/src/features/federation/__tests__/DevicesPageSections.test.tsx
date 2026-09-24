import type { FederationStatus, ManagedServersView } from "../types";

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
import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/*
 * Landing on a section of the devices page (`?section=`), and what the page offers where
 * the rest of it is not available. The Service's "wants to manage this device"
 * notification links to `?section=management`; the window's switcher to `?section=servers`.
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
    expect(within(accessSection()).getByRole("heading", { level: 2 })).toHaveTextContent(
      "federation.management.title federation.management.self",
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

  it("moves nothing without a section", async () => {
    renderPage("/federation/devices");
    expect(await within(accessSection()).findByText("Laptop")).toBeInTheDocument();
    await waitFor(() => expect(managedServerApi.list).toHaveBeenCalled());
    expect(scrollIntoView).not.toHaveBeenCalled();
    expect(accessSection()).not.toHaveFocus();
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

  it("in the retired client, which is paired: still explains the move, and answers too", async () => {
    setStore({
      initialized: true,
      isLocal: false,
      clientMode: ClientMode.PureClient,
      clientHost: "legacy",
      serverName: "NAS",
    });
    renderPage("/federation/devices?section=management");
    expect(screen.getByText("federation.migration.intro")).toBeInTheDocument();
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
 * The access section reads its own settings. These are the two moments they change under
 * it while the page stays open, and both have to show at once — not on the next poll.
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
    renderPage("/federation/devices");
    expect(
      await within(accessSection()).findByText("federation.management.status.off"),
    ).toBeInTheDocument();
    const before = settingsReads();

    fireEvent.click(screen.getByRole("button", { name: "federation.sharing.start" }));
    fireEvent.click(within(screen.getByRole("alertdialog")).getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.sharing).toHaveBeenCalledWith(true, true));
    // Within the default wait of a second — far short of the section's own idle poll.
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
