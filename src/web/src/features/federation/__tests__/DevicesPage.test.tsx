import type { FederationStatus, PairingRequest, PairingResult } from "../types";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { FederationError } from "../transport";
import DevicesPage from "../DevicesPage";
import { federationPeerApi } from "../peerApi";
import { useFederationStatus } from "../hooks/useFederationStatus";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    // Keys as text, plus the requester address so tests can see where it is surfaced.
    t: (key: string, options?: { address?: string }) =>
      options?.address ? `${key} ${options.address}` : key,
    i18n: { language: "en", changeLanguage: vi.fn() },
  }),
}));
vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (state: unknown) => unknown) =>
    selector({ initialized: true, isLocal: true }),
  useIsPureClient: () => false,
}));
vi.mock("../peerApi", () => ({
  federationPeerApi: {
    status: vi.fn(),
    browsing: vi.fn(),
    connect: vi.fn(),
    discover: vi.fn(),
    claim: vi.fn(),
    cancelRequest: vi.fn(),
    setName: vi.fn(),
    remove: vi.fn(),
    revoke: vi.fn(),
    forget: vi.fn(),
    enable: vi.fn(),
    mappings: vi.fn(),
    mappingRoots: vi.fn(),
    sharing: vi.fn(),
    invite: vi.fn(),
    decide: vi.fn(),
    resetIdentity: vi.fn(),
  },
}));
vi.mock("../hooks/useFederationStatus", () => ({ useFederationStatus: vi.fn() }));
// The management sections have their own suites (ManagementSections.test.tsx). Here they
// sit idle: nothing managed, remote access off, so they never compete with the sharing
// controls these tests drive.
vi.mock("../serverApi", () => ({
  managedServerApi: {
    list: vi.fn().mockResolvedValue({ available: true, servers: [], requests: [] }),
  },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    remoteAccess: {
      getRemoteAccessSettings: vi.fn().mockResolvedValue({
        code: 0,
        data: {
          mode: 0,
          addresses: [],
          allowLiveTranscode: false,
          requirePairing: false,
          devices: [],
          pendingRequests: [],
        },
      }),
    },
  },
}));
const status: FederationStatus = {
  identity: { nodeId: "local", libraryEpoch: "epoch", name: "This PC" },
  browsingEnabled: true,
  sharingEnabled: false,
  remoteAccessMode: 0,
  requirePairing: false,
  peers: [
    {
      nodeId: "remote",
      label: "Other PC",
      address: "http://other",
      enabled: true,
      connectionState: "Unknown",
      outboundGrant: { grantId: "outgoing-grant", revision: 1 },
      inboundGrant: { grantId: "incoming-grant", revision: 1 },
      pathMappings: [{ sourceRootId: "root", localPath: "/Volumes/Old" }],
    },
  ],
  requests: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  vi.mocked(useFederationStatus).mockReturnValue({
    status,
    loading: false,
    error: undefined,
    refresh: vi.fn().mockResolvedValue(undefined),
  });
  vi.mocked(federationPeerApi.mappingRoots).mockResolvedValue([
    { sourceRootId: "root", name: "Movies" },
  ]);
});
afterEach(cleanup);
const renderPage = (entry = "/federation/devices") =>
  render(
    <MemoryRouter initialEntries={[entry]}>
      <DevicesPage />
    </MemoryRouter>,
  );

describe("device permission workflows", () => {
  it("revoking another device targets the inbound grant; forgetting access targets the outbound node", async () => {
    renderPage();
    fireEvent.click(screen.getByText("federation.devices.revoke"));
    expect(federationPeerApi.revoke).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.revoke).toHaveBeenCalledWith("incoming-grant"));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
    fireEvent.click(screen.getByText("federation.devices.forget"));
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.forget).toHaveBeenCalledWith("remote"));
  });
  it("does not mark rejected path mapping saves as saved or throw away the edited path", async () => {
    vi.mocked(federationPeerApi.mappings).mockRejectedValue(new Error("Offline"));
    renderPage();
    fireEvent.click(screen.getByText("federation.mappings.title"));
    fireEvent.change(screen.getByDisplayValue("/Volumes/Old"), {
      target: { value: "/Volumes/New" },
    });
    fireEvent.click(screen.getByText("federation.save"));
    expect(federationPeerApi.mappings).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.mappings.replace"));
    expect(await screen.findByText("Offline")).toBeInTheDocument();
    expect(screen.getByDisplayValue("/Volumes/New")).toBeInTheDocument();
    expect(screen.getByText("federation.save")).not.toBeDisabled();
    expect(screen.queryByText("federation.mappings.saved")).not.toBeInTheDocument();
  });
  it("never changes remote-access settings when the user clears the checkbox", async () => {
    renderPage();
    const remote = screen.getByRole("checkbox", { name: "federation.sharing.configureRemote" });

    expect(remote).toBeChecked();
    fireEvent.click(remote);
    fireEvent.click(screen.getByText("federation.sharing.start"));
    expect(screen.getByRole("alertdialog")).not.toHaveTextContent("confirmWithRemote");
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.sharing).toHaveBeenCalledWith(true, false));
  });
  it("discovering a candidate only fills its address; no grant exchange starts", async () => {
    vi.mocked(federationPeerApi.discover).mockResolvedValue([
      { nodeId: "candidate", name: "New PC", address: "http://candidate" },
    ]);
    renderPage();
    fireEvent.click(screen.getByText("federation.discovery.scan"));
    fireEvent.click(await screen.findByText("federation.discovery.use"));
    expect(screen.getByDisplayValue("http://candidate")).toBeInTheDocument();
    expect(federationPeerApi.connect).not.toHaveBeenCalled();
  });

  it("requires explicit confirmation before resetting a cloned installation identity", async () => {
    renderPage();
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.identity.reset"));
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(true));
    expect(federationPeerApi.sharing).not.toHaveBeenCalled();
  });
  it("opens recovery help from configuration without resetting and restores with the original node identity", async () => {
    renderPage("/federation/devices?section=identity");
    expect(screen.getByText("federation.identity.title").closest("details")).toHaveAttribute(
      "open",
    );
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.identity.restore"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("federation.identity.restoreConfirm");
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(false));
    expect(federationPeerApi.sharing).not.toHaveBeenCalled();
  });
  it("reports a failed recovery without claiming a reset or disabling sharing in a separate request", async () => {
    vi.mocked(federationPeerApi.resetIdentity).mockRejectedValueOnce(
      new Error("Storage unavailable"),
    );
    renderPage();
    fireEvent.click(screen.getByText("federation.identity.restore"));
    fireEvent.click(screen.getByText("federation.confirm"));
    expect(await screen.findByText("Storage unavailable")).toBeInTheDocument();
    expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(false);
    expect(federationPeerApi.sharing).not.toHaveBeenCalled();
    expect(
      screen.getByRole("button", { name: "federation.identity.restore", hidden: true }),
    ).not.toBeDisabled();
  });
});

describe("independent browsing and safe mapping edits", () => {
  it("turns off browsing without changing sharing, pairings or mappings", async () => {
    renderPage();
    fireEvent.click(screen.getByText("federation.browsing.disable"));
    await waitFor(() => expect(federationPeerApi.browsing).toHaveBeenCalledWith(false));
    expect(federationPeerApi.sharing).not.toHaveBeenCalled();
    expect(federationPeerApi.forget).not.toHaveBeenCalled();
    expect(federationPeerApi.revoke).not.toHaveBeenCalled();
    expect(federationPeerApi.mappings).not.toHaveBeenCalled();
    expect(screen.getByText("federation.devices.known")).toBeInTheDocument();
  });
  it.each(["keep", "replace"])(
    "requires an explicit %s decision before changing an existing mapping",
    async (choice) => {
      vi.mocked(federationPeerApi.mappings).mockResolvedValue(undefined);
      renderPage();
      fireEvent.click(screen.getByText("federation.mappings.title"));
      fireEvent.change(screen.getByDisplayValue("/Volumes/Old"), {
        target: { value: "/Volumes/New" },
      });
      fireEvent.click(screen.getByText("federation.save"));
      expect(federationPeerApi.mappings).not.toHaveBeenCalled();
      expect(screen.getByRole("alertdialog")).toHaveTextContent("/Volumes/Old → /Volumes/New");
      fireEvent.click(screen.getByText(`federation.mappings.${choice}`));
      await waitFor(() =>
        expect(federationPeerApi.mappings).toHaveBeenCalledWith(
          "remote",
          [
            {
              sourceRootId: "root",
              localPath: choice === "keep" ? "/Volumes/Old" : "/Volumes/New",
            },
          ],
          status.peers[0].pathMappings,
        ),
      );
    },
  );
});

describe("mapping recovery", () => {
  it("requires a new review when the saved mapping changes during confirmation", async () => {
    vi.mocked(federationPeerApi.mappings).mockResolvedValue(undefined);
    const page = renderPage();

    fireEvent.click(screen.getByText("federation.mappings.title"));
    fireEvent.change(screen.getByDisplayValue("/Volumes/Old"), {
      target: { value: "/Volumes/New" },
    });
    fireEvent.click(screen.getByText("federation.save"));
    const updated = [{ sourceRootId: "root", localPath: "/Volumes/Concurrent" }];

    vi.mocked(useFederationStatus).mockReturnValue({
      status: { ...status, peers: [{ ...status.peers[0], pathMappings: updated }] },
      loading: false,
      error: undefined,
      refresh: vi.fn(),
    });
    page.rerender(
      <MemoryRouter>
        <DevicesPage />
      </MemoryRouter>,
    );
    expect(screen.getByText("federation.mappings.changedDuringReview")).toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.mappings.replace"));
    expect(federationPeerApi.mappings).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.mappings.replace"));
    await waitFor(() =>
      expect(federationPeerApi.mappings).toHaveBeenCalledWith(
        "remote",
        [{ sourceRootId: "root", localPath: "/Volumes/New" }],
        updated,
      ),
    );
  });
  it("refreshes current mappings after an atomic conflict and keeps the proposed path for review", async () => {
    const refresh = vi.fn().mockResolvedValue(undefined);

    vi.mocked(useFederationStatus).mockReturnValue({
      status,
      loading: false,
      error: undefined,
      refresh,
    });
    vi.mocked(federationPeerApi.mappings).mockRejectedValue(
      new FederationError("PathMappingsChanged", "Mappings changed", 409),
    );
    renderPage();
    fireEvent.click(screen.getByText("federation.mappings.title"));
    fireEvent.change(screen.getByDisplayValue("/Volumes/Old"), {
      target: { value: "/Volumes/New" },
    });
    fireEvent.click(screen.getByText("federation.save"));
    fireEvent.click(screen.getByText("federation.mappings.replace"));
    await screen.findByText("Mappings changed");
    await waitFor(() => expect(refresh).toHaveBeenCalled());
    expect(screen.getByDisplayValue("/Volumes/New")).toBeInTheDocument();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(screen.queryByText("federation.mappings.saved")).not.toBeInTheDocument();
  });
});

const pairingRequest = (overrides: Partial<PairingRequest> = {}): PairingRequest => ({
  requestId: "request",
  nodeId: "requester",
  nodeName: "Laptop",
  direction: "incoming",
  status: "awaitingApproval",
  expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
  replacesExistingAccess: false,
  offersReciprocalAccess: false,
  ...overrides,
});
const withRequests = (requests: PairingRequest[]) => {
  const refresh = vi.fn().mockResolvedValue(undefined);

  vi.mocked(useFederationStatus).mockReturnValue({
    status: { ...status, requests },
    loading: false,
    error: undefined,
    refresh,
  });

  return refresh;
};

describe("confirmation dialog", () => {
  it("opens as a modal over the page, focuses Confirm and cancels on Escape", () => {
    renderPage();
    const revoke = screen.getByText("federation.devices.revoke");

    revoke.focus();
    fireEvent.click(revoke);
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog.parentElement).toHaveClass("fixed", "inset-0");
    expect(dialog.parentElement?.parentElement).toBe(document.body);
    expect(within(dialog).getByText("federation.confirm")).toHaveFocus();
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(federationPeerApi.revoke).not.toHaveBeenCalled();
    expect(revoke).toHaveFocus();
  });
  it("keeps a failed confirmed action inside the dialog", async () => {
    vi.mocked(federationPeerApi.revoke).mockRejectedValueOnce(new Error("Revoke failed"));
    renderPage();
    fireEvent.click(screen.getByText("federation.devices.revoke"));
    fireEvent.click(screen.getByText("federation.confirm"));
    const dialog = screen.getByRole("alertdialog");

    expect(await within(dialog).findByText("Revoke failed")).toBeInTheDocument();
    expect(screen.queryByTestId("federation-feedback")).not.toBeInTheDocument();
    expect(within(dialog).getByText("federation.confirm")).not.toBeDisabled();
  });
});

describe("action feedback", () => {
  it("shows errors in a sticky region that only exists while there is something to show", async () => {
    vi.mocked(federationPeerApi.browsing).mockRejectedValueOnce(new Error("Browsing failed"));
    renderPage();
    expect(screen.queryByTestId("federation-feedback")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.browsing.disable"));
    const feedback = await screen.findByTestId("federation-feedback");

    expect(feedback).toHaveClass("sticky", "top-0");
    expect(feedback).toHaveTextContent("Browsing failed");
    fireEvent.click(within(feedback).getByLabelText("federation.dismiss"));
    expect(screen.queryByTestId("federation-feedback")).not.toBeInTheDocument();
  });
});

describe("pairing requests", () => {
  it("asks this device to decide an incoming request and shows where it came from", async () => {
    withRequests([pairingRequest({ remoteAddress: "192.168.1.20", replacesExistingAccess: true })]);
    renderPage();
    expect(screen.getByText(/federation\.requests\.awaitingYourApproval/)).toBeInTheDocument();
    expect(screen.queryByText(/federation\.pair\.awaitingApproval/)).not.toBeInTheDocument();
    expect(screen.getByText("federation.requests.from 192.168.1.20")).toBeInTheDocument();
    expect(screen.getByText("federation.requests.replacesExisting")).toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.requests.approve"));
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent("federation.requests.approveConfirmFrom 192.168.1.20");
    expect(dialog).toHaveTextContent("federation.requests.replacesExisting");
    expect(federationPeerApi.decide).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.decide).toHaveBeenCalledWith("request", true));
  });
  it("does not invent an address or a replacement warning the server did not report", () => {
    withRequests([pairingRequest()]);
    renderPage();
    expect(screen.queryByText(/federation\.requests\.from/)).not.toBeInTheDocument();
    expect(screen.queryByText("federation.requests.replacesExisting")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.requests.approve"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("federation.requests.approveConfirm");
    expect(screen.getByRole("alertdialog")).not.toHaveTextContent("replacesExisting");
  });
  it("cancels an outgoing pending request and keeps the manual approval check", async () => {
    const refresh = withRequests([pairingRequest({ direction: "outgoing", requestId: "mine" })]);

    renderPage();
    expect(screen.getByText(/federation\.pair\.awaitingApproval/)).toBeInTheDocument();
    expect(screen.getByText("federation.requests.check")).toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.requests.cancel"));
    await waitFor(() => expect(federationPeerApi.cancelRequest).toHaveBeenCalledWith("mine"));
    await waitFor(() => expect(refresh).toHaveBeenCalledWith());
    expect(federationPeerApi.claim).not.toHaveBeenCalled();
    expect(federationPeerApi.decide).not.toHaveBeenCalled();
  });
});

describe("background polling", () => {
  const advance = (ms: number) =>
    act(async () => {
      await vi.advanceTimersByTimeAsync(ms);
    });

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    vi.useRealTimers();
    Reflect.deleteProperty(document, "hidden");
  });

  it("claims only outgoing requests, without disabling controls or clearing feedback", async () => {
    const refresh = withRequests([
      pairingRequest({ requestId: "incoming" }),
      pairingRequest({ requestId: "outgoing", direction: "outgoing" }),
    ]);

    vi.mocked(federationPeerApi.claim).mockResolvedValue({
      outcome: "awaitingApproval",
    } as PairingResult);
    vi.mocked(federationPeerApi.browsing).mockRejectedValueOnce(new Error("Browsing failed"));
    renderPage();
    fireEvent.click(screen.getByText("federation.browsing.disable"));
    await advance(0);
    expect(screen.getByText("Browsing failed")).toBeInTheDocument();
    await advance(4000);
    expect(federationPeerApi.claim).toHaveBeenCalledOnce();
    expect(federationPeerApi.claim).toHaveBeenCalledWith("outgoing", expect.any(AbortSignal));
    expect(refresh).toHaveBeenCalledWith({ quiet: true });
    expect(screen.getByText("Browsing failed")).toBeInTheDocument();
    expect(screen.queryByText("federation.pair.awaitingApproval")).not.toBeInTheDocument();
    expect(screen.getByText("federation.requests.approve")).not.toBeDisabled();
  });
  it("reports a decided request without touching what the user is typing", async () => {
    const refresh = withRequests([pairingRequest({ direction: "outgoing" })]);

    vi.mocked(federationPeerApi.claim).mockResolvedValue({ outcome: "granted" } as PairingResult);
    renderPage();
    const code = screen.getByLabelText("federation.pair.code");

    fireEvent.change(code, { target: { value: "123456" } });
    await advance(4000);
    expect(screen.getByTestId("federation-feedback")).toHaveTextContent("federation.pair.granted");
    expect(code).toHaveValue("123456");
    expect(refresh).toHaveBeenCalledWith({ quiet: true });
  });
  it("backs off from a device whose claim failed and never shows that as an error", async () => {
    withRequests([pairingRequest({ direction: "outgoing" })]);
    vi.mocked(federationPeerApi.claim).mockRejectedValue(
      new FederationError("NodeUnreachable", "Unreachable", 502),
    );
    renderPage();
    await advance(4000);
    expect(federationPeerApi.claim).toHaveBeenCalledTimes(1);
    await advance(28_000);
    expect(federationPeerApi.claim).toHaveBeenCalledTimes(1);
    await advance(4000);
    expect(federationPeerApi.claim).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
  it("never overlaps a slow claim and keeps the page usable meanwhile", async () => {
    withRequests([pairingRequest({ direction: "outgoing" })]);
    vi.mocked(federationPeerApi.claim).mockReturnValue(new Promise(() => {}));
    renderPage();
    await advance(12_000);
    expect(federationPeerApi.claim).toHaveBeenCalledTimes(1);
    expect(screen.getByText("federation.requests.cancel")).not.toBeDisabled();
    expect(screen.getByText("federation.refresh")).not.toBeDisabled();
  });
  it("quietly re-reads status so new incoming requests appear, but not while hidden", async () => {
    const refresh = withRequests([pairingRequest()]);

    renderPage();
    await advance(4000);
    expect(federationPeerApi.claim).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();
    await advance(11_000);
    expect(refresh).toHaveBeenCalledOnce();
    expect(refresh).toHaveBeenCalledWith({ quiet: true });
    Object.defineProperty(document, "hidden", { configurable: true, get: () => true });
    await advance(30_000);
    expect(refresh).toHaveBeenCalledOnce();
  });
});

describe("unreadable sharing state", () => {
  it("offers a confirmed reset as a new device even though no status could be loaded", async () => {
    const refresh = vi.fn().mockResolvedValue(undefined);

    vi.mocked(useFederationStatus).mockReturnValue({
      status: undefined,
      loading: false,
      error: new FederationError("SharingStateUnavailable", "Unreadable", 503),
      refresh,
    });
    renderPage();
    expect(screen.getByText("federation.recovery.title")).toBeInTheDocument();
    expect(screen.queryByText("federation.browsing.title")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.identity.reset"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("federation.recovery.confirm");
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(true));
    await waitFor(() => expect(refresh).toHaveBeenCalled());
  });
  it("does not offer a reset for other load failures", () => {
    vi.mocked(useFederationStatus).mockReturnValue({
      status: undefined,
      loading: false,
      error: new FederationError("InternalError", "Failed", 500),
      refresh: vi.fn(),
    });
    renderPage();
    expect(screen.queryByText("federation.recovery.title")).not.toBeInTheDocument();
    expect(screen.queryByText("federation.identity.reset")).not.toBeInTheDocument();
    expect(screen.getByTestId("federation-feedback")).toHaveTextContent("Failed");
  });
});

const withStatus = (patch: Partial<FederationStatus>) => {
  const refresh = vi.fn().mockResolvedValue(undefined);

  vi.mocked(useFederationStatus).mockReturnValue({
    status: { ...status, ...patch },
    loading: false,
    error: undefined,
    refresh,
  });

  return refresh;
};

describe("enabling sharing", () => {
  it("opens remote access by default when it is disabled, and says so before confirming", async () => {
    renderPage();
    expect(
      screen.getByRole("checkbox", { name: "federation.sharing.configureRemote" }),
    ).toBeChecked();
    fireEvent.click(screen.getByText("federation.sharing.start"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(
      "federation.sharing.confirmWithRemote",
    );
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.sharing).toHaveBeenCalledWith(true, true));
  });
  it.each([1, 2])("leaves remote-access mode %s alone unless asked", async (mode) => {
    withStatus({ remoteAccessMode: mode as FederationStatus["remoteAccessMode"] });
    renderPage();
    expect(
      screen.getByRole("checkbox", { name: "federation.sharing.configureRemote" }),
    ).not.toBeChecked();
    fireEvent.click(screen.getByText("federation.sharing.start"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("federation.sharing.confirm");
    expect(screen.getByRole("alertdialog")).not.toHaveTextContent("confirmWithRemote");
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.sharing).toHaveBeenCalledWith(true, false));
  });
});

describe("this device's address", () => {
  const addresses = ["http://192.168.1.5:34567", "http://10.0.0.2:34567"];
  const writeText = vi.fn();

  beforeEach(() => {
    writeText.mockReset();
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
  });
  afterEach(() => Reflect.deleteProperty(navigator, "clipboard"));

  it("lists every reachable address next to the code, each with a copy action", async () => {
    writeText.mockResolvedValue(undefined);
    vi.mocked(federationPeerApi.invite).mockResolvedValue({
      code: "482913",
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });
    withStatus({ sharingEnabled: true, remoteAccessMode: 1, reachableAddresses: addresses });
    renderPage();
    const group = screen.getByRole("group", { name: "federation.sharing.addresses" });

    expect(within(group).getByText(addresses[0])).toBeInTheDocument();
    expect(within(group).getByText(addresses[1])).toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.sharing.issueCode"));
    expect(await screen.findByText("482913")).toBeInTheDocument();
    expect(group.parentElement).toContainElement(screen.getByText("482913"));
    fireEvent.click(within(group).getAllByText("federation.copy")[1]);
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(addresses[1]));
    expect(await within(group).findByText("federation.copied")).toBeInTheDocument();
  });
  it("says so when the address could not be copied", async () => {
    writeText.mockRejectedValue(new Error("denied"));
    withStatus({ sharingEnabled: true, remoteAccessMode: 1, reachableAddresses: addresses });
    renderPage();
    fireEvent.click(screen.getAllByText("federation.copy")[0]);
    expect(await screen.findByText("federation.copyFailed")).toBeInTheDocument();
  });
  it("explains that remote access must be enabled when no address is reachable", () => {
    withStatus({ sharingEnabled: true, remoteAccessMode: 0, reachableAddresses: undefined });
    renderPage();
    const group = screen.getByRole("group", { name: "federation.sharing.addresses" });

    expect(group).toHaveTextContent("federation.sharing.remoteDisabled");
    fireEvent.click(within(group).getByText("federation.sharing.configureRemote"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(
      "federation.sharing.confirmWithRemote",
    );
  });
  it("points at the network when remote access is on but no address was found", () => {
    withStatus({ sharingEnabled: true, remoteAccessMode: 1, reachableAddresses: [] });
    renderPage();
    expect(screen.getByText("federation.sharing.noAddress")).toBeInTheDocument();
    expect(screen.queryByText("federation.sharing.remoteDisabled")).not.toBeInTheDocument();
  });
  it("shows no address while sharing is off", () => {
    renderPage();
    expect(
      screen.queryByRole("group", { name: "federation.sharing.addresses" }),
    ).not.toBeInTheDocument();
  });
});

describe("device name", () => {
  const nameForm = () => within(screen.getByLabelText("federation.name.label").closest("form")!);

  it("renames this device and refreshes status", async () => {
    const refresh = withStatus({});

    renderPage();
    fireEvent.click(screen.getByText("federation.name.edit"));
    fireEvent.change(screen.getByLabelText("federation.name.label"), {
      target: { value: "  Study PC " },
    });
    fireEvent.click(nameForm().getByText("federation.save"));
    await waitFor(() => expect(federationPeerApi.setName).toHaveBeenCalledWith("Study PC"));
    await waitFor(() => expect(refresh).toHaveBeenCalled());
    expect(screen.queryByLabelText("federation.name.label")).not.toBeInTheDocument();
  });
  it("resets to the computer name", async () => {
    renderPage();
    fireEvent.click(screen.getByText("federation.name.edit"));
    fireEvent.click(screen.getByText("federation.name.reset"));
    await waitFor(() => expect(federationPeerApi.setName).toHaveBeenCalledWith(null));
  });
  it("keeps the edit open when the name is rejected", async () => {
    vi.mocked(federationPeerApi.setName).mockRejectedValueOnce(
      new FederationError("InvalidDeviceName", "Bad name", 400),
    );
    renderPage();
    fireEvent.click(screen.getByText("federation.name.edit"));
    fireEvent.click(nameForm().getByText("federation.save"));
    expect(await screen.findByText("Bad name")).toBeInTheDocument();
    expect(screen.getByLabelText("federation.name.label")).toHaveValue("This PC");
  });
});

describe("connecting with share-back", () => {
  const connectTo = (address: string) => {
    fireEvent.change(screen.getByPlaceholderText("192.168.1.5:34567"), {
      target: { value: address },
    });
    fireEvent.click(screen.getByText("federation.pair.request"));
  };
  const shareBack = () => screen.getByRole("checkbox", { name: /federation\.pair\.shareBack/ });

  it("offers read access back by default", async () => {
    vi.mocked(federationPeerApi.connect).mockResolvedValue({
      outcome: "awaitingApproval",
    } as PairingResult);
    renderPage();
    expect(shareBack()).toBeChecked();
    expect(shareBack()).toHaveAccessibleName(/federation\.pair\.shareBackTipRemote/);
    expect(screen.queryByText("federation.pair.directionTip")).not.toBeInTheDocument();
    connectTo("http://other:34567");
    await waitFor(() =>
      expect(federationPeerApi.connect).toHaveBeenCalledWith("http://other:34567", undefined, true),
    );
  });
  it("connects one way when the user clears it", async () => {
    vi.mocked(federationPeerApi.connect).mockResolvedValue({
      outcome: "awaitingApproval",
    } as PairingResult);
    withStatus({ remoteAccessMode: 1 });
    renderPage();
    expect(shareBack()).toHaveAccessibleName(/federation\.pair\.shareBackTip$/);
    fireEvent.click(shareBack());
    expect(screen.getByText("federation.pair.directionTip")).toBeInTheDocument();
    connectTo("http://other:34567");
    await waitFor(() =>
      expect(federationPeerApi.connect).toHaveBeenCalledWith(
        "http://other:34567",
        undefined,
        false,
      ),
    );
  });
});

describe("reciprocal requests", () => {
  it.each([
    ["192.168.1.20", "federation.requests.approveConfirmFromReciprocal 192.168.1.20"],
    [undefined, "federation.requests.approveConfirmReciprocal"],
  ])("tells the approver they will also read the requester (address %s)", (address, text) => {
    withRequests([pairingRequest({ remoteAddress: address, offersReciprocalAccess: true })]);
    renderPage();
    expect(screen.getByText("federation.requests.offersReciprocal")).toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.requests.approve"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(text);
  });
  it("does not promise reciprocal access the request did not offer", () => {
    withRequests([pairingRequest({ remoteAddress: "192.168.1.20" })]);
    renderPage();
    expect(screen.queryByText("federation.requests.offersReciprocal")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.requests.approve"));
    expect(screen.getByRole("alertdialog")).not.toHaveTextContent("Reciprocal");
  });
});

describe("removing a device", () => {
  it("removes both directions only after confirmation, separately from the one-way actions", async () => {
    renderPage();
    fireEvent.click(screen.getByText("federation.devices.remove"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("federation.devices.removeConfirm");
    expect(federationPeerApi.remove).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.remove).toHaveBeenCalledWith("remote"));
    expect(federationPeerApi.forget).not.toHaveBeenCalled();
    expect(federationPeerApi.revoke).not.toHaveBeenCalled();
    expect(screen.getByText("federation.devices.forget")).toBeInTheDocument();
    expect(screen.getByText("federation.devices.revoke")).toBeInTheDocument();
  });
});

describe("discovery", () => {
  it.each([
    ["nothing", []],
    ["only this device", [{ nodeId: "local", name: "This PC", address: "http://self" }]],
  ])("explains how to become discoverable when the scan finds %s", async (_, found) => {
    vi.mocked(federationPeerApi.discover).mockResolvedValue(found);
    renderPage();
    fireEvent.click(screen.getByText("federation.discovery.scan"));
    expect(await screen.findByText("federation.discovery.noneFound")).toBeInTheDocument();
    expect(screen.queryByText("federation.discovery.use")).not.toBeInTheDocument();
  });
});

describe("requests decided outside this page", () => {
  it("reports an outgoing request the server claimed in the background", () => {
    const request = {
      requestId: "outgoing-request",
      nodeId: "remote",
      nodeName: "Other PC",
      direction: "outgoing",
      status: "awaitingApproval",
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      replacesExistingAccess: false,
      offersReciprocalAccess: false,
    } satisfies PairingRequest;
    const refresh = vi.fn().mockResolvedValue(undefined);

    vi.mocked(useFederationStatus).mockReturnValue({
      status: { ...status, requests: [request] },
      loading: false,
      error: undefined,
      refresh,
    });
    const { rerender } = render(
      <MemoryRouter>
        <DevicesPage />
      </MemoryRouter>,
    );

    expect(screen.queryByText("federation.pair.granted")).not.toBeInTheDocument();
    vi.mocked(useFederationStatus).mockReturnValue({
      status: { ...status, requests: [{ ...request, status: "granted" }] },
      loading: false,
      error: undefined,
      refresh,
    });
    rerender(
      <MemoryRouter>
        <DevicesPage />
      </MemoryRouter>,
    );

    expect(screen.getByRole("status")).toHaveTextContent("federation.pair.granted");
  });
});
