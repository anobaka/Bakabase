import type {
  ManagedServer,
  ManagedServerDiscovery,
  ManagedServerPendingRequest,
  ManagedServersView,
} from "../types";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ManagedServersSection from "../components/ManagedServers";
import ManagementAccessSection from "../components/ManagementAccess";
import { FederationAccess } from "../components/common";
import { managedServerApi } from "../serverApi";
import { federationPeerApi } from "../peerApi";
import { FederationError } from "../transport";

import BApi from "@/sdk/BApi";
import { clientApi } from "@/core/clientApi";
import {
  ClientMode,
  ManagedServerOutcome,
  ManagedServerState,
  RemoteAccessMode,
} from "@/sdk/constants";

const store = vi.hoisted(() => ({
  state: {} as Record<string, unknown>,
  pureClient: false,
}));

vi.mock("react-i18next", () => ({
  // Keys as text, followed by the interpolated values, so a test can see what was said.
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options
        ? [key, ...Object.values(options).filter((value) => value !== undefined)].join(" ")
        : key,
    i18n: {
      language: "en",
      changeLanguage: vi.fn(),
      exists: (key: string) =>
        key.startsWith("federation.error.ManagedServer") ||
        key === "federation.error.ManagementUnavailable",
    },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (state: unknown) => unknown) => selector(store.state),
  useIsPureClient: () => store.pureClient,
}));
vi.mock("../serverApi", () => ({
  managedServerApi: {
    list: vi.fn(),
    discover: vi.fn(),
    probe: vi.fn(),
    pair: vi.fn(),
    cancelRequest: vi.fn(),
    forget: vi.fn(),
    setPathMappings: vi.fn(),
    open: vi.fn(),
    importLegacyClient: vi.fn(),
  },
}));
// Library sharing's discovery cannot see a server that does not share its library — the
// usual one to manage. Mocked only so a test fails loudly if the section reaches for it.
vi.mock("../peerApi", () => ({ federationPeerApi: { discover: vi.fn() } }));
vi.mock("@/core/clientApi", () => ({
  LOCAL_SWITCHER_TARGET: "local",
  clientApi: { switcher: { list: vi.fn(), open: vi.fn() } },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    remoteAccess: {
      getRemoteAccessSettings: vi.fn(),
      setRemoteAccessMode: vi.fn(),
      setRemoteAccessRequirePairing: vi.fn(),
      issueRemoteAccessPairingCode: vi.fn(),
      approveRemoteDevicePairingRequest: vi.fn(),
      rejectRemoteDevicePairingRequest: vi.fn(),
      revokeRemoteAccessDevice: vi.fn(),
    },
  },
}));

const assign = vi.fn();
const reloadContext = vi.fn();

const server = (overrides: Partial<ManagedServer> = {}): ManagedServer => ({
  serverId: "nas",
  name: "NAS",
  address: "http://192.168.1.5:34567",
  pairedAt: "2026-09-01T00:00:00Z",
  pathMappings: [],
  state: ManagedServerState.Online,
  mode: RemoteAccessMode.Enabled,
  importedFromLegacyClient: false,
  ...overrides,
});
const view = (overrides: Partial<ManagedServersView> = {}): ManagedServersView => ({
  available: true,
  servers: [],
  requests: [],
  ...overrides,
});
const pendingRequest = (
  requestId = "request-1",
  overrides: Partial<ManagedServerPendingRequest> = {},
): ManagedServerPendingRequest => ({
  requestId,
  address: "http://192.168.1.5:34567",
  serverName: "NAS",
  expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
  outcome: ManagedServerOutcome.AwaitingApproval,
  active: true,
  ...overrides,
});

/** A promise the test resolves when it wants the answer to arrive. */
const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => (resolve = done));

  return { promise, resolve };
};

/** What the list endpoint answers right now; tests move it on to simulate the other side. */
let listing: ManagedServersView;

const listReads = () => vi.mocked(managedServerApi.list).mock.calls.length;
/** Lets fake time pass, and everything it sets off settle. */
const elapse = (ms: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

beforeEach(() => {
  vi.clearAllMocks();
  store.state = { initialized: true, isLocal: true, load: reloadContext };
  store.pureClient = false;
  listing = view();
  vi.mocked(managedServerApi.list).mockImplementation(async () => listing);
  vi.stubGlobal("location", { ...window.location, href: "http://localhost:34567/", assign });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const renderServers = async () => {
  const rendered = render(
    <MemoryRouter>
      <ManagedServersSection />
    </MemoryRouter>,
  );

  await screen.findByText("federation.servers.add.title");
  await waitFor(() => expect(managedServerApi.list).toHaveBeenCalledWith(true));

  return rendered;
};
const addForm = () => {
  const address = screen.getByLabelText("federation.servers.add.address");

  return { address, code: screen.getByLabelText("federation.servers.add.code") };
};

describe("devices this one manages", () => {
  it("lists each server with its state, and only warns about an unrestricted one", async () => {
    listing = view({
      servers: [
        server({ mode: RemoteAccessMode.Unrestricted }),
        server({ serverId: "old", name: "Old PC", state: ManagedServerState.Revoked }),
      ],
    });
    await renderServers();
    const nas = await screen.findByRole("article", { name: "NAS" });

    expect(
      within(nas).getByText(`federation.servers.state.${ManagedServerState.Online}`),
    ).toBeInTheDocument();
    // The directions name the server's own Configuration page and its settings there, in
    // that page's own words — not the devices page, which is this computer's and is not in
    // the menu while the window shows another server.
    expect(within(nas).getByTestId("managed-server-unrestricted")).toHaveTextContent(
      [
        "federation.servers.unrestricted NAS",
        "menu.configuration",
        "configuration.remoteAccess.mode.label",
        "configuration.remoteAccess.mode.enabled",
        "configuration.remoteAccess.requirePairing.label",
      ].join(" "),
    );
    const old = screen.getByRole("article", { name: "Old PC" });

    expect(within(old).getByText("federation.servers.revokedTip Old PC")).toBeInTheDocument();
    expect(within(old).queryByText(/unrestricted/)).not.toBeInTheDocument();
    // A warning, never a change: nothing on either side is written.
    expect(BApi.remoteAccess.setRemoteAccessMode).not.toHaveBeenCalled();
    expect(BApi.remoteAccess.setRemoteAccessRequirePairing).not.toHaveBeenCalled();
    expect(managedServerApi.forget).not.toHaveBeenCalled();
    expect(managedServerApi.pair).not.toHaveBeenCalled();
  });

  it("says who answers at a server's address instead, and never offers to pair with it there", async () => {
    listing = view({
      servers: [
        server({
          state: ManagedServerState.WrongServer,
          answeredBy: { serverId: "studio", name: "Studio", isThisDevice: false },
        }),
        server({
          serverId: "desk",
          name: "Desk",
          address: "http://127.0.0.1:34570",
          state: ManagedServerState.WrongServer,
          answeredBy: { serverId: "this-device", name: "This Mac", isThisDevice: true },
        }),
      ],
    });
    await renderServers();
    const nas = await screen.findByRole("article", { name: "NAS" });

    expect(
      within(nas).getByText(`federation.servers.state.${ManagedServerState.WrongServer}`),
    ).toBeInTheDocument();
    // Names the address and who answers there, and points at finding the server where it
    // went — the address is not this server's any more, so nothing here pairs with it.
    expect(within(nas).getByTestId("managed-server-wrong-server")).toHaveTextContent(
      "federation.servers.wrongServerTip NAS http://192.168.1.5:34567 Studio federation.servers.add.discover",
    );
    expect(within(nas).queryByText(/revokedTip/)).not.toBeInTheDocument();

    const desk = screen.getByRole("article", { name: "Desk" });

    expect(within(desk).getByTestId("managed-server-wrong-server")).toHaveTextContent(
      "federation.servers.wrongServerThisDeviceTip Desk http://127.0.0.1:34570 federation.servers.add.discover",
    );
    expect(managedServerApi.pair).not.toHaveBeenCalled();
    expect(managedServerApi.forget).not.toHaveBeenCalled();
  });

  it("pairs with a code and shows the new server", async () => {
    vi.mocked(managedServerApi.pair).mockImplementation(async () => {
      listing = view({ servers: [server()] });

      return { outcome: ManagedServerOutcome.Ok, serverId: "nas", serverName: "NAS" };
    });
    await renderServers();
    const { address, code } = addForm();

    fireEvent.change(address, { target: { value: " http://192.168.1.5:34567 " } });
    fireEvent.change(code, { target: { value: "123456" } });
    fireEvent.click(screen.getByText("federation.servers.add.withCode"));
    expect(await screen.findByText("federation.servers.paired NAS")).toBeInTheDocument();
    expect(managedServerApi.pair).toHaveBeenCalledWith("http://192.168.1.5:34567", "123456");
    expect(await screen.findByRole("article", { name: "NAS" })).toBeInTheDocument();
    expect(address).toHaveValue("");
    expect(code).toHaveValue("");
  });

  it("files a request without a code, shows it waiting, and withdraws it quietly", async () => {
    vi.mocked(managedServerApi.pair).mockImplementation(async () => {
      listing = view({ requests: [pendingRequest()] });

      return {
        outcome: ManagedServerOutcome.AwaitingApproval,
        requestId: "request-1",
        serverName: "NAS",
      };
    });
    vi.mocked(managedServerApi.cancelRequest).mockImplementation(async () => {
      listing = view();

      return { changed: true };
    });
    await renderServers();
    fireEvent.change(addForm().address, { target: { value: "http://192.168.1.5:34567" } });
    fireEvent.click(screen.getByText("federation.servers.add.request"));
    expect(await screen.findByText("federation.servers.requested NAS")).toBeInTheDocument();
    expect(managedServerApi.pair).toHaveBeenCalledWith("http://192.168.1.5:34567", undefined);
    const requests = await screen.findByTestId("managed-server-requests");

    expect(requests).toHaveTextContent("federation.servers.waiting NAS 10");
    fireEvent.click(within(requests).getByText("federation.servers.cancelRequest"));
    await waitFor(() => expect(managedServerApi.cancelRequest).toHaveBeenCalledWith("request-1"));
    await waitFor(() =>
      expect(screen.queryByTestId("managed-server-requests")).not.toBeInTheDocument(),
    );
    // Withdrawing it is not news about the other device.
    expect(screen.queryByText(/requestClosed/)).not.toBeInTheDocument();
  });

  it("keeps re-reading while a request waits and names the device once approved", async () => {
    vi.useFakeTimers();
    listing = view({ requests: [pendingRequest()] });
    render(
      <MemoryRouter>
        <ManagedServersSection />
      </MemoryRouter>,
    );
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(screen.getByTestId("managed-server-requests")).toBeInTheDocument();
    const before = vi.mocked(managedServerApi.list).mock.calls.length;

    listing = view({ servers: [server()] });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(vi.mocked(managedServerApi.list).mock.calls.length).toBeGreaterThan(before);
    expect(screen.getByText("federation.servers.approved NAS")).toBeInTheDocument();
    expect(screen.getByRole("article", { name: "NAS" })).toBeInTheDocument();
    // Nothing left to wait for: the polling stops.
    const settled = vi.mocked(managedServerApi.list).mock.calls.length;

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });
    expect(vi.mocked(managedServerApi.list).mock.calls.length).toBe(settled);
  });

  it("says when a request ended without an approval", async () => {
    vi.useFakeTimers();
    listing = view({ requests: [pendingRequest()] });
    render(
      <MemoryRouter>
        <ManagedServersSection />
      </MemoryRouter>,
    );
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    listing = view();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(screen.getByText("federation.servers.requestClosed NAS")).toBeInTheDocument();
  });

  it("keeps waiting through a failed attempt, and shows the approval when it comes", async () => {
    vi.useFakeTimers();
    listing = view({ requests: [pendingRequest()] });
    render(
      <MemoryRouter>
        <ManagedServersSection />
      </MemoryRouter>,
    );
    await elapse(0);
    expect(screen.getByTestId("managed-server-requests")).toHaveTextContent(
      "federation.servers.waiting NAS",
    );

    // The NAS drops off the network for one claim. The app keeps asking; so does the page.
    listing = view({
      requests: [pendingRequest("request-1", { outcome: ManagedServerOutcome.Unreachable })],
    });
    await elapse(5000);
    const row = screen.getByTestId("managed-server-requests");

    expect(row).toHaveTextContent("federation.servers.retrying NAS");
    // Not an answer: no final "nothing answered", and the wait can still be cancelled.
    expect(row).not.toHaveTextContent("federation.error.ManagedServerUnreachable");
    expect(within(row).getByText("federation.servers.cancelRequest")).toBeInTheDocument();
    expect(within(row).queryByText("federation.servers.dismiss")).not.toBeInTheDocument();
    const before = listReads();

    // Somebody approves on the NAS; the app saves it and the request is gone.
    listing = view({ servers: [server()] });
    await elapse(5000);
    expect(listReads()).toBeGreaterThan(before);
    expect(screen.getByText("federation.servers.approved NAS")).toBeInTheDocument();
    expect(screen.getByRole("article", { name: "NAS" })).toBeInTheDocument();
    expect(screen.queryByTestId("managed-server-requests")).not.toBeInTheDocument();
  });

  it("shows how a finished request ended, offers to dismiss it, and stops asking", async () => {
    vi.useFakeTimers();
    vi.mocked(managedServerApi.cancelRequest).mockImplementation(async () => {
      listing = view();

      return { changed: true };
    });
    listing = view({ requests: [pendingRequest()] });
    render(
      <MemoryRouter>
        <ManagedServersSection />
      </MemoryRouter>,
    );
    await elapse(0);
    listing = view({
      requests: [
        pendingRequest("request-1", {
          outcome: ManagedServerOutcome.RequestRejected,
          active: false,
        }),
      ],
    });
    await elapse(5000);
    const row = screen.getByTestId("managed-server-requests");

    expect(row).toHaveTextContent("federation.error.ManagedServerRequestRejected");
    expect(within(row).queryByText("federation.servers.cancelRequest")).not.toBeInTheDocument();
    // Said in the row; a notice on top would say it twice.
    expect(screen.queryByText(/requestClosed/)).not.toBeInTheDocument();
    const settled = listReads();

    await elapse(60_000);
    expect(listReads()).toBe(settled);

    fireEvent.click(within(row).getByText("federation.servers.dismiss"));
    await elapse(0);
    expect(managedServerApi.cancelRequest).toHaveBeenCalledWith("request-1");
    expect(screen.queryByTestId("managed-server-requests")).not.toBeInTheDocument();
    expect(screen.queryByText(/requestClosed/)).not.toBeInTheDocument();
  });

  it("opens a server by sending the window to the URL the app returned", async () => {
    listing = view({ servers: [server()] });
    vi.mocked(managedServerApi.open).mockResolvedValue({
      url: "http://127.0.0.1:34650/?__bakabase_switch=token",
    });
    await renderServers();
    fireEvent.click(await screen.findByText("federation.servers.open"));
    await waitFor(() =>
      expect(assign).toHaveBeenCalledWith("http://127.0.0.1:34650/?__bakabase_switch=token"),
    );
    expect(managedServerApi.open).toHaveBeenCalledWith("nas");
  });

  it("opens an unrestricted server on its own Configuration page, through its relay", async () => {
    listing = view({ servers: [server({ mode: RemoteAccessMode.Unrestricted })] });
    vi.mocked(managedServerApi.open).mockResolvedValue({
      url: "http://127.0.0.1:34650/?__bakabase_switch=token",
    });
    await renderServers();
    const warning = await screen.findByTestId("managed-server-unrestricted");

    fireEvent.click(
      within(warning).getByRole("button", {
        name: "federation.servers.openConfiguration menu.configuration",
      }),
    );
    // The route rides in the fragment, after the ticket: the relay's landing page keeps it
    // when it drops the ticket, and the server never sees it.
    await waitFor(() =>
      expect(assign).toHaveBeenCalledWith(
        "http://127.0.0.1:34650/?__bakabase_switch=token#/configuration",
      ),
    );
    expect(managedServerApi.open).toHaveBeenCalledWith("nas");
    // Only a way there: nothing on the server is changed from this computer.
    expect(BApi.remoteAccess.setRemoteAccessMode).not.toHaveBeenCalled();
    expect(BApi.remoteAccess.setRemoteAccessRequirePairing).not.toHaveBeenCalled();
  });

  it("says so when the server's Configuration page cannot be opened, and stays", async () => {
    listing = view({ servers: [server({ mode: RemoteAccessMode.Unrestricted })] });
    vi.mocked(managedServerApi.open).mockRejectedValue(
      new FederationError("RelayUnavailable", "no port", 503, true),
    );
    await renderServers();
    const warning = await screen.findByTestId("managed-server-unrestricted");

    fireEvent.click(within(warning).getByRole("button"));
    expect(await screen.findByRole("alert")).toHaveTextContent("RelayUnavailable");
    expect(assign).not.toHaveBeenCalled();
    expect(screen.getByRole("article", { name: "NAS" })).toBeInTheDocument();
  });

  it("stops managing a server only after confirmation", async () => {
    listing = view({ servers: [server()] });
    vi.mocked(managedServerApi.forget).mockImplementation(async () => {
      listing = view();

      return { changed: true };
    });
    await renderServers();
    fireEvent.click(await screen.findByText("federation.servers.forget"));
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent("federation.servers.forgetConfirm NAS");
    expect(managedServerApi.forget).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByText("federation.confirm"));
    await waitFor(() => expect(managedServerApi.forget).toHaveBeenCalledWith("nas"));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
    expect(screen.queryByRole("article", { name: "NAS" })).not.toBeInTheDocument();
    expect(screen.getByText("federation.servers.empty")).toBeInTheDocument();
  });

  it.each([
    [{ found: true, imported: 2, skipped: 1 }, "federation.servers.import.done 2 1"],
    [{ found: true, imported: 0, skipped: 3 }, "federation.servers.import.nothingNew 3"],
    [{ found: false, imported: 0, skipped: 0 }, "federation.servers.import.notFound"],
  ])("imports from Bakabase Client and says what happened (%o)", async (result, message) => {
    vi.mocked(managedServerApi.importLegacyClient).mockResolvedValue(result);
    await renderServers();
    fireEvent.click(screen.getByText("federation.servers.import.action"));
    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(managedServerApi.importLegacyClient).toHaveBeenCalledOnce();
  });

  it("saves this computer's path mappings for a server, sent whole", async () => {
    listing = view({
      servers: [server({ pathMappings: [{ serverPath: "/data/old", localPath: "/Volumes/Old" }] })],
    });
    vi.mocked(managedServerApi.setPathMappings).mockResolvedValue({ changed: true });
    await renderServers();
    const card = await screen.findByRole("article", { name: "NAS" });

    fireEvent.click(within(card).getByText("federation.servers.mappings.title"));
    fireEvent.click(within(card).getByLabelText("federation.servers.mappings.remove"));
    fireEvent.click(within(card).getByText("federation.servers.mappings.add"));
    const save = within(card).getByText("federation.servers.mappings.save");

    expect(save).toBeDisabled();
    fireEvent.change(within(card).getByLabelText("federation.servers.mappings.server"), {
      target: { value: " D:\\Media " },
    });
    fireEvent.change(within(card).getByLabelText("federation.servers.mappings.local"), {
      target: { value: "/Volumes/Media" },
    });
    fireEvent.click(save);
    await waitFor(() =>
      expect(managedServerApi.setPathMappings).toHaveBeenCalledWith("nas", [
        { serverPath: "D:\\Media", localPath: "/Volumes/Media" },
      ]),
    );
    expect(await screen.findByText("federation.servers.mappings.saved")).toBeInTheDocument();
  });

  it("explains an outcome the other device reported instead of pretending it worked", async () => {
    vi.mocked(managedServerApi.pair).mockResolvedValue({
      outcome: ManagedServerOutcome.CodeRejected,
    });
    await renderServers();
    fireEvent.change(addForm().address, { target: { value: "http://192.168.1.5:34567" } });
    fireEvent.change(addForm().code, { target: { value: "000000" } });
    fireEvent.click(screen.getByText("federation.servers.add.withCode"));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "federation.error.ManagedServerCodeRejected",
    );
    expect(addForm().code).toHaveValue("000000");
    expect(screen.queryByText(/federation\.servers\.paired/)).not.toBeInTheDocument();
  });

  it("finds nearby servers by their beacons, marks those already managed, and only fills an address", async () => {
    listing = view({ servers: [server()] });
    const answer = deferred<ManagedServerDiscovery>();

    vi.mocked(managedServerApi.discover).mockReturnValue(answer.promise);
    await renderServers();
    fireEvent.click(screen.getByRole("button", { name: "federation.servers.add.discover" }));
    // It listens for a few seconds, and says so meanwhile.
    const searching = screen.getByRole("button", { name: "federation.servers.add.discovering" });

    expect(searching).toBeDisabled();
    expect(searching).toHaveAttribute("aria-busy", "true");
    await act(async () =>
      answer.resolve({
        servers: [
          {
            serverId: "nas",
            name: "NAS",
            address: "http://192.168.1.5:34567",
            appVersion: "2.5.0",
            alreadyManaged: true,
          },
          {
            serverId: "studio",
            name: "Studio",
            address: "http://192.168.1.9:34567",
            appVersion: "2.5.1",
            alreadyManaged: false,
          },
        ],
      }),
    );
    const found = screen.getByTestId("managed-server-candidates");
    const nas = within(found).getByRole("group", { name: "NAS" });

    // Listed, so the search does not read as "not on this network" — but not offered again.
    expect(nas).toHaveTextContent("federation.servers.add.alreadyManaged");
    expect(within(nas).queryByText("federation.servers.add.useAddress")).not.toBeInTheDocument();
    const studio = within(found).getByRole("group", { name: "Studio" });

    expect(studio).toHaveTextContent("http://192.168.1.9:34567 · v2.5.1");
    fireEvent.click(within(studio).getByText("federation.servers.add.useAddress"));
    expect(addForm().address).toHaveValue("http://192.168.1.9:34567");
    expect(screen.getByRole("button", { name: "federation.servers.add.discover" })).toBeEnabled();
    expect(managedServerApi.discover).toHaveBeenCalledOnce();
    // Not library sharing's discovery, which cannot see a server that does not share.
    expect(federationPeerApi.discover).not.toHaveBeenCalled();
    expect(managedServerApi.pair).not.toHaveBeenCalled();
  });

  it("treats a server paired since the search as managed too", async () => {
    vi.mocked(managedServerApi.discover).mockResolvedValue({
      servers: [
        {
          serverId: "nas",
          name: "NAS",
          address: "http://192.168.1.5:34567",
          appVersion: "2.5.0",
          alreadyManaged: false,
        },
      ],
    });
    vi.mocked(managedServerApi.pair).mockImplementation(async () => {
      listing = view({ servers: [server()] });

      return { outcome: ManagedServerOutcome.Ok, serverId: "nas", serverName: "NAS" };
    });
    await renderServers();
    fireEvent.click(screen.getByText("federation.servers.add.discover"));
    fireEvent.click(await screen.findByText("federation.servers.add.useAddress"));
    fireEvent.change(addForm().code, { target: { value: "123456" } });
    fireEvent.click(screen.getByText("federation.servers.add.withCode"));
    const nas = await within(screen.getByTestId("managed-server-candidates")).findByText(
      "federation.servers.add.alreadyManaged",
    );

    expect(nas).toBeInTheDocument();
    expect(screen.queryByText("federation.servers.add.useAddress")).not.toBeInTheDocument();
  });

  it("says why a search could not run, and tries again on request", async () => {
    vi.mocked(managedServerApi.discover)
      .mockRejectedValueOnce(
        new FederationError(
          "ManagementUnavailable",
          "This installation cannot manage other servers.",
          404,
        ),
      )
      .mockResolvedValueOnce({ servers: [] });
    await renderServers();
    fireEvent.click(screen.getByText("federation.servers.add.discover"));
    const alert = await screen.findByRole("alert");

    expect(alert).toHaveTextContent("federation.error.ManagementUnavailable");
    fireEvent.click(within(alert).getByText("federation.retry"));
    expect(await screen.findByText("federation.servers.add.noneFound")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("stops listening when the section goes away", async () => {
    let signal: AbortSignal | undefined;

    vi.mocked(managedServerApi.discover).mockImplementation((given) => {
      signal = given;

      return new Promise(() => {});
    });
    const { unmount } = await renderServers();

    fireEvent.click(screen.getByText("federation.servers.add.discover"));
    expect(signal?.aborted).toBe(false);
    unmount();
    expect(signal?.aborted).toBe(true);
  });

  it("is absent where this installation cannot manage anything", async () => {
    listing = view({ available: false });
    render(
      <MemoryRouter>
        <ManagedServersSection />
      </MemoryRouter>,
    );
    await waitFor(() => expect(managedServerApi.list).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.queryByText("federation.servers.title")).not.toBeInTheDocument(),
    );
  });
});

const settings = (overrides: Record<string, unknown> = {}) =>
  vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue({
    code: 0,
    data: {
      mode: RemoteAccessMode.Disabled,
      addresses: [],
      allowLiveTranscode: false,
      requirePairing: false,
      devices: [],
      pendingRequests: [],
      ...overrides,
    },
  } as never);

/** Every call here reports its failure inline, never as a toast on top of that. */
const quiet = { showErrorToast: false };

const renderAccess = async (onChanged = vi.fn()) => {
  render(
    <MemoryRouter>
      <ManagementAccessSection onChanged={onChanged} />
    </MemoryRouter>,
  );
  await waitFor(() => expect(BApi.remoteAccess.getRemoteAccessSettings).toHaveBeenCalled());

  return onChanged;
};

describe("letting other devices manage this one", () => {
  beforeEach(() => {
    vi.mocked(BApi.remoteAccess.setRemoteAccessRequirePairing).mockResolvedValue({
      code: 0,
    } as never);
    vi.mocked(BApi.remoteAccess.setRemoteAccessMode).mockResolvedValue({ code: 0 } as never);
  });

  it("turns management on only when asked: pairing required first, then the mode", async () => {
    settings();
    const onChanged = await renderAccess();

    expect(await screen.findByText("federation.management.status.off")).toBeInTheDocument();
    // In this device's own window the section speaks of "this device".
    expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent(
      "federation.management.title federation.management.self",
    );
    fireEvent.click(screen.getByRole("button", { name: /^federation\.management\.enable/ }));
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent("federation.management.enableConfirm");
    expect(BApi.remoteAccess.setRemoteAccessMode).not.toHaveBeenCalled();
    expect(BApi.remoteAccess.setRemoteAccessRequirePairing).not.toHaveBeenCalled();
    settings({ mode: RemoteAccessMode.Enabled, requirePairing: true });
    fireEvent.click(within(dialog).getByText("federation.confirm"));
    await waitFor(() =>
      expect(BApi.remoteAccess.setRemoteAccessMode).toHaveBeenCalledWith(
        { mode: RemoteAccessMode.Enabled },
        quiet,
      ),
    );
    expect(BApi.remoteAccess.setRemoteAccessRequirePairing).toHaveBeenCalledWith(
      { require: true },
      quiet,
    );
    expect(
      vi.mocked(BApi.remoteAccess.setRemoteAccessRequirePairing).mock.invocationCallOrder[0],
    ).toBeLessThan(vi.mocked(BApi.remoteAccess.setRemoteAccessMode).mock.invocationCallOrder[0]);
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    expect(reloadContext).toHaveBeenCalled();
    expect(await screen.findByText("federation.management.status.paired")).toBeInTheDocument();
  });

  it("keeps reading its settings while remote access is off, so a change made elsewhere shows", async () => {
    vi.useFakeTimers();
    settings();
    render(
      <MemoryRouter>
        <ManagementAccessSection />
      </MemoryRouter>,
    );
    await elapse(0);
    expect(screen.getByText("federation.management.status.off")).toBeInTheDocument();
    // Turned on from the settings page, or by the sharing panel beside it.
    settings({ mode: RemoteAccessMode.Enabled, requirePairing: true });
    await elapse(15_000);
    expect(screen.getByText("federation.management.status.paired")).toBeInTheDocument();
  });

  it("reads its settings again at once when told they may have changed", async () => {
    settings();
    const { rerender } = render(
      <MemoryRouter>
        <ManagementAccessSection reloadKey="a" />
      </MemoryRouter>,
    );

    expect(await screen.findByText("federation.management.status.off")).toBeInTheDocument();
    // The same key again is not news.
    rerender(
      <MemoryRouter>
        <ManagementAccessSection reloadKey="a" />
      </MemoryRouter>,
    );
    expect(BApi.remoteAccess.getRemoteAccessSettings).toHaveBeenCalledTimes(1);
    settings({ mode: RemoteAccessMode.Enabled, requirePairing: true });
    rerender(
      <MemoryRouter>
        <ManagementAccessSection reloadKey="b" />
      </MemoryRouter>,
    );
    expect(await screen.findByText("federation.management.status.paired")).toBeInTheDocument();
    expect(BApi.remoteAccess.getRemoteAccessSettings).toHaveBeenCalledTimes(2);
  });

  it("explains an unrestricted mode and requires pairing only on an explicit click", async () => {
    settings({ mode: RemoteAccessMode.Unrestricted });
    await renderAccess();
    const warning = await screen.findByTestId("management-unrestricted");

    expect(warning).toHaveTextContent("federation.management.unrestricted");
    expect(BApi.remoteAccess.setRemoteAccessMode).not.toHaveBeenCalled();
    fireEvent.click(within(warning).getByText("federation.management.requirePairing"));
    const dialog = screen.getByRole("alertdialog");

    // Says that the mode itself changes, not only the pairing switch.
    expect(dialog).toHaveTextContent("federation.management.requirePairingConfirmUnrestricted");
    // Sitting at the device: nothing here loses access.
    expect(dialog).not.toHaveTextContent("federation.management.lockOutWarning");
    fireEvent.click(within(dialog).getByText("federation.confirm"));
    await waitFor(() =>
      expect(BApi.remoteAccess.setRemoteAccessMode).toHaveBeenCalledWith(
        { mode: RemoteAccessMode.Enabled },
        quiet,
      ),
    );
    expect(BApi.remoteAccess.setRemoteAccessRequirePairing).toHaveBeenCalledWith(
      { require: true },
      quiet,
    );
  });

  it("warns a browser that requiring pairing locks it out too", async () => {
    store.state = {
      initialized: true,
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
      mode: RemoteAccessMode.Unrestricted,
      serverName: "NAS",
      load: reloadContext,
    };
    settings({ mode: RemoteAccessMode.Unrestricted });
    await renderAccess();
    const warning = await screen.findByTestId("management-unrestricted");

    // Named, not "this device": the browser is not on it.
    expect(warning).toHaveTextContent("federation.management.unrestricted NAS");
    fireEvent.click(within(warning).getByText("federation.management.requirePairing"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(
      "federation.management.lockOutWarning",
    );
  });

  it("keeps a failed switch inside the dialog", async () => {
    settings();
    vi.mocked(BApi.remoteAccess.setRemoteAccessRequirePairing).mockResolvedValue({
      code: 500,
      message: "Settings are read-only",
    } as never);
    const onChanged = await renderAccess();

    fireEvent.click(await screen.findByRole("button", { name: /^federation\.management\.enable/ }));
    const dialog = screen.getByRole("alertdialog");

    fireEvent.click(within(dialog).getByText("federation.confirm"));
    expect(await within(dialog).findByText("Settings are read-only")).toBeInTheDocument();
    expect(BApi.remoteAccess.setRemoteAccessMode).not.toHaveBeenCalled();
    expect(onChanged).not.toHaveBeenCalled();
  });

  it("shows a refused request in the server's own words, once, without a toast", async () => {
    settings({ mode: RemoteAccessMode.Enabled, requirePairing: true });
    // BApi rejects with the whole response on an HTTP failure.
    vi.mocked(BApi.remoteAccess.issueRemoteAccessPairingCode).mockRejectedValue({
      status: 403,
      error: { code: 401, message: "This action runs on the machine hosting Bakabase." },
    });
    await renderAccess();
    fireEvent.click(await screen.findByText("federation.management.code.issue"));
    expect(
      await screen.findByText("This action runs on the machine hosting Bakabase."),
    ).toBeInTheDocument();
    expect(BApi.remoteAccess.issueRemoteAccessPairingCode).toHaveBeenCalledWith(quiet);
  });

  it("says when the settings cannot be read, and reads them again on request", async () => {
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockRejectedValueOnce({
      status: 403,
      error: { code: 401, message: "Not from here" },
    });
    settings({ mode: RemoteAccessMode.Enabled, requirePairing: true });
    const onSettled = vi.fn();

    render(
      <MemoryRouter>
        <ManagementAccessSection onSettled={onSettled} />
      </MemoryRouter>,
    );
    const alert = await screen.findByRole("alert");

    expect(alert).toHaveTextContent("Not from here");
    expect(screen.queryByText("federation.loading")).not.toBeInTheDocument();
    expect(onSettled).toHaveBeenCalledTimes(1);
    fireEvent.click(within(alert).getByText("federation.retry"));
    expect(await screen.findByText("federation.management.status.paired")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(onSettled).toHaveBeenCalledTimes(1);
  });

  it("approves a management request after confirming, and rejects without ceremony", async () => {
    const request = {
      id: "req-1",
      deviceName: "Laptop",
      platform: 1,
      remoteAddress: "192.168.1.20",
      requestedAt: "2026-09-23 08:00:00.000",
      expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
    };

    settings({ mode: RemoteAccessMode.Enabled, requirePairing: true, pendingRequests: [request] });
    vi.mocked(BApi.remoteAccess.approveRemoteDevicePairingRequest).mockResolvedValue({
      code: 0,
    } as never);
    vi.mocked(BApi.remoteAccess.rejectRemoteDevicePairingRequest).mockResolvedValue({
      code: 0,
    } as never);
    await renderAccess();
    const requests = await screen.findByTestId("management-requests");

    expect(requests).toHaveTextContent("192.168.1.20");
    fireEvent.click(within(requests).getByText("federation.management.requests.approve"));
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent(
      "federation.management.requests.approveConfirmFrom Laptop 192.168.1.20",
    );
    expect(BApi.remoteAccess.approveRemoteDevicePairingRequest).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByText("federation.confirm"));
    await waitFor(() =>
      expect(BApi.remoteAccess.approveRemoteDevicePairingRequest).toHaveBeenCalledWith(
        "req-1",
        quiet,
      ),
    );
    expect(
      await screen.findByText(
        "federation.management.requests.approved Laptop federation.management.self",
      ),
    ).toBeInTheDocument();
    fireEvent.click(within(requests).getByText("federation.management.requests.reject"));
    await waitFor(() =>
      expect(BApi.remoteAccess.rejectRemoteDevicePairingRequest).toHaveBeenCalledWith(
        "req-1",
        quiet,
      ),
    );
  });

  it("revokes a paired device after confirming, and issues a pairing code", async () => {
    settings({
      mode: RemoteAccessMode.Enabled,
      requirePairing: true,
      addresses: [{ url: "http://192.168.1.5:34567", interfaceName: "en0" }],
      devices: [{ id: "dev-1", name: "Studio", platform: 2, createdAt: "2026-09-01 00:00:00.000" }],
    });
    vi.mocked(BApi.remoteAccess.revokeRemoteAccessDevice).mockResolvedValue({ code: 0 } as never);
    vi.mocked(BApi.remoteAccess.issueRemoteAccessPairingCode).mockResolvedValue({
      code: 0,
      data: { code: "482913", expiresAt: new Date(Date.now() + 5 * 60_000).toISOString() },
    } as never);
    await renderAccess();
    expect(await screen.findByText("http://192.168.1.5:34567")).toBeInTheDocument();
    const devices = screen.getByTestId("management-devices");

    fireEvent.click(within(devices).getByText("federation.management.devices.revoke"));
    expect(BApi.remoteAccess.revokeRemoteAccessDevice).not.toHaveBeenCalled();
    // Someone else's device: no warning about this window.
    expect(screen.getByRole("alertdialog")).not.toHaveTextContent(
      "federation.management.devices.revokeSelfWarning",
    );
    fireEvent.click(within(screen.getByRole("alertdialog")).getByText("federation.confirm"));
    await waitFor(() =>
      expect(BApi.remoteAccess.revokeRemoteAccessDevice).toHaveBeenCalledWith("dev-1", quiet),
    );
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
    fireEvent.click(screen.getByText("federation.management.code.issue"));
    expect(await screen.findByText("482913")).toBeInTheDocument();
  });
});

describe("letting other devices manage the server a paired window shows", () => {
  beforeEach(() => {
    store.state = {
      initialized: true,
      isLocal: false,
      clientMode: ClientMode.PureClient,
      clientHost: "console",
      serverName: "NAS",
      localName: "Desk",
      ownDeviceId: "dev-desk",
      load: reloadContext,
    };
    store.pureClient = true;
  });

  it("names the server rather than this device, and marks this computer among its devices", async () => {
    settings({
      mode: RemoteAccessMode.Enabled,
      requirePairing: true,
      devices: [
        { id: "dev-desk", name: "Desk", platform: 2, createdAt: "2026-09-01 00:00:00.000" },
        { id: "dev-2", name: "Laptop", platform: 1, createdAt: "2026-09-02 00:00:00.000" },
      ],
    });
    vi.mocked(BApi.remoteAccess.revokeRemoteAccessDevice).mockResolvedValue({ code: 0 } as never);
    await renderAccess();
    expect(await screen.findByRole("heading", { level: 2 })).toHaveTextContent(
      "federation.management.title NAS",
    );
    const devices = await screen.findByTestId("management-devices");
    const rows = within(devices)
      .getAllByText("federation.management.devices.revoke")
      .map((button) => button.parentElement!);

    expect(rows[0]).toHaveTextContent("federation.management.devices.you");
    expect(rows[1]).not.toHaveTextContent("federation.management.devices.you");
    // Revoking this computer ends the session doing it; the dialog says so first.
    fireEvent.click(within(rows[0]).getByText("federation.management.devices.revoke"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(
      "federation.management.devices.revokeSelfWarning NAS",
    );
    expect(BApi.remoteAccess.revokeRemoteAccessDevice).not.toHaveBeenCalled();
    // A paired window keeps its access when pairing is required; no lock-out warning.
    fireEvent.click(within(screen.getByRole("alertdialog")).getByText("federation.cancel"));
  });
});

describe("multi-device pages inside the console", () => {
  it("offers the way back to this computer, on the same page", async () => {
    store.state = { initialized: true, isLocal: false, clientHost: "console" };
    store.pureClient = true;
    vi.mocked(clientApi.switcher.open).mockResolvedValue({ url: "http://localhost:34567/" });
    render(
      <MemoryRouter initialEntries={["/federation/devices"]}>
        <FederationAccess>
          <p>page</p>
        </FederationAccess>
      </MemoryRouter>,
    );
    expect(screen.queryByText("page")).not.toBeInTheDocument();
    expect(screen.getByText("federation.console.localOnly")).toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.console.switchToThisDevice"));
    await waitFor(() =>
      expect(assign).toHaveBeenCalledWith("http://localhost:34567/#/federation/devices"),
    );
    expect(clientApi.switcher.open).toHaveBeenCalledWith("local");
  });

  it("points back to this computer before the relay has said it is the console", () => {
    store.state = { initialized: true, isLocal: false, clientMode: ClientMode.PureClient };
    store.pureClient = true;
    render(
      <MemoryRouter>
        <FederationAccess>
          <p>page</p>
        </FederationAccess>
      </MemoryRouter>,
    );
    expect(screen.queryByText("page")).not.toBeInTheDocument();
    expect(screen.getByText("federation.console.localOnly")).toBeInTheDocument();
  });

  it("tells a browser on another device where these pages live", () => {
    store.state = { initialized: true, isLocal: false, clientMode: ClientMode.RemoteBrowser };
    store.pureClient = false;
    render(
      <MemoryRouter>
        <FederationAccess>
          <p>page</p>
        </FederationAccess>
      </MemoryRouter>,
    );
    expect(screen.queryByText("page")).not.toBeInTheDocument();
    expect(screen.getByText("federation.localOnly")).toBeInTheDocument();
    expect(screen.queryByText("federation.console.localOnly")).not.toBeInTheDocument();
  });
});
