import type { FederationStatus, PairingRequest, PairingResult } from "../types";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { FederationError } from "../transport";
import DevicesPage from "../DevicesPage";
import { federationPeerApi } from "../peerApi";
import { useFederationStatus } from "../hooks/useFederationStatus";

import BApi from "@/sdk/BApi";

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
/** Library sharing is where most of this suite happens; the other tabs are named. */
const SHARING = "/federation/devices?section=sharing";
/** The add form, open even when devices are listed. */
const CONNECT = "/federation/devices?section=connect";
const renderPage = (entry = SHARING) =>
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
    renderPage(CONNECT);
    fireEvent.click(screen.getByText("federation.discovery.scan"));
    fireEvent.click(await screen.findByText("federation.discovery.use"));
    expect(screen.getByDisplayValue("http://candidate")).toBeInTheDocument();
    expect(federationPeerApi.connect).not.toHaveBeenCalled();
  });

  it("offers no identity action before the reader says what happened", () => {
    renderPage("/federation/devices?section=advanced");
    expect(screen.getByText("federation.identity.question")).toBeInTheDocument();
    expect(screen.queryByText("federation.identity.reset")).not.toBeInTheDocument();
    expect(screen.queryByText("federation.identity.restore")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("radio", { name: "federation.identity.copied.title" }));
    expect(screen.getByText("federation.identity.copied.removes")).toBeInTheDocument();
    expect(screen.getByText("federation.identity.copied.keeps")).toBeInTheDocument();
    expect(screen.getByText("federation.identity.reset")).toBeInTheDocument();
    expect(screen.queryByText("federation.identity.restore")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("radio", { name: "federation.identity.restored.title" }));
    expect(screen.getByText("federation.identity.restored.after")).toBeInTheDocument();
    expect(screen.getByText("federation.identity.restore")).toBeInTheDocument();
    expect(screen.queryByText("federation.identity.reset")).not.toBeInTheDocument();
  });
  it("requires explicit confirmation before making a copied installation a new device", async () => {
    renderPage("/federation/devices?section=advanced");
    fireEvent.click(screen.getByRole("radio", { name: "federation.identity.copied.title" }));
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.identity.reset"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("federation.identity.confirm");
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    // A copy: the install's own identity goes too.
    await waitFor(() => expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(true, true));
    expect(federationPeerApi.sharing).not.toHaveBeenCalled();
  });
  it("opens recovery help from configuration without resetting and restores with the original node identity", async () => {
    renderPage("/federation/devices?section=identity");
    const flow = screen.getByTestId("identity-recovery");

    expect(flow).toBeVisible();
    await waitFor(() => expect(flow).toHaveFocus());
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("radio", { name: "federation.identity.restored.title" }));
    fireEvent.click(screen.getByText("federation.identity.restore"));
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent("federation.identity.restoreConfirm");
    // A backup brings back who could manage this device too, and this leaves them alone.
    expect(dialog).toHaveTextContent("federation.identity.restoreWarning");
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(false));
    expect(federationPeerApi.sharing).not.toHaveBeenCalled();
  });
  it("reports a failed recovery without claiming a reset or disabling sharing in a separate request", async () => {
    vi.mocked(federationPeerApi.resetIdentity).mockRejectedValueOnce(
      new Error("Storage unavailable"),
    );
    renderPage("/federation/devices?section=advanced");
    fireEvent.click(screen.getByRole("radio", { name: "federation.identity.restored.title" }));
    fireEvent.click(screen.getByText("federation.identity.restore"));
    fireEvent.click(screen.getByText("federation.confirm"));
    expect(await screen.findByText("Storage unavailable")).toBeInTheDocument();
    expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(false);
    expect(federationPeerApi.sharing).not.toHaveBeenCalled();
    expect(
      screen.getByRole("button", { name: "federation.identity.restore", hidden: true }),
    ).not.toBeDisabled();
  });
  it("shows the device ID in Advanced, with a way to copy it", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);

    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    try {
      renderPage("/federation/devices?section=advanced");
      const block = within(screen.getByTestId("device-id"));

      expect(block.getByText("local")).toBeInTheDocument();
      // Named for what it copies — an ID, not an address — and says how it went.
      fireEvent.click(block.getByRole("button", { name: "federation.devices.id.copy" }));
      await waitFor(() => expect(writeText).toHaveBeenCalledWith("local"));
      expect(await block.findByRole("status")).toHaveTextContent("federation.copied");
    } finally {
      Reflect.deleteProperty(navigator, "clipboard");
    }
  });
});

describe("independent browsing and safe mapping edits", () => {
  it("links to the Multi-device library by what the link does, apart from the panel's title", () => {
    renderPage();
    const panel = screen.getByTestId("browsing-switch").closest("#library-browsing")!;

    expect(within(panel as HTMLElement).getByRole("link")).toHaveAccessibleName(
      "federation.browsing.open",
    );
    expect(within(panel as HTMLElement).getByRole("link")).toHaveAttribute("href", "/federation");
  });
  it("turns off browsing without changing sharing, pairings or mappings", async () => {
    renderPage();
    fireEvent.click(screen.getByText("federation.browsing.disable"));
    await waitFor(() => expect(federationPeerApi.browsing).toHaveBeenCalledWith(false));
    expect(federationPeerApi.sharing).not.toHaveBeenCalled();
    expect(federationPeerApi.forget).not.toHaveBeenCalled();
    expect(federationPeerApi.revoke).not.toHaveBeenCalled();
    expect(federationPeerApi.mappings).not.toHaveBeenCalled();
    expect(screen.getByTestId("sharing-outbound")).toBeInTheDocument();
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
    // Stuck in the content's column, beside the nav rather than over it.
    const column = feedback.parentElement!;

    expect(column).toContainElement(screen.getByTestId("devices-panel"));
    expect(column).not.toContainElement(screen.getByTestId("devices-nav"));
    fireEvent.click(within(feedback).getByLabelText("federation.dismiss"));
    expect(screen.queryByTestId("federation-feedback")).not.toBeInTheDocument();
  });
  it("keeps what a link brings into view below it, and leaves a notice behind on the next link", () => {
    const FEEDBACK_HEIGHT = 48;
    const rect = vi
      .spyOn(HTMLElement.prototype, "getBoundingClientRect")
      .mockImplementation(function (this: HTMLElement) {
        const height = this.dataset.testid === "federation-feedback" ? FEEDBACK_HEIGHT : 0;

        return {
          x: 0,
          y: 0,
          top: 0,
          left: 0,
          right: 0,
          bottom: height,
          width: 0,
          height,
        } as DOMRect;
      });

    try {
      const request = pairingRequest({ requestId: "outgoing-request", direction: "outgoing" });
      const page = () => (
        <MemoryRouter initialEntries={[SHARING]}>
          <DevicesPage />
        </MemoryRouter>
      );
      const offset = () =>
        screen.getByTestId("devices-page").style.getPropertyValue("--devices-scroll-offset");

      withRequests([request]);
      const { rerender } = render(page());

      expect(offset()).toBe("16px");
      // The server claimed it in the background: the page says so, stuck at the top.
      withRequests([{ ...request, status: "granted" }]);
      rerender(page());
      expect(screen.getByTestId("federation-feedback")).toHaveTextContent(
        "federation.pair.granted",
      );
      expect(offset()).toBe(`${FEEDBACK_HEIGHT + 16}px`);
      // Every place a link or the nav brings into view keeps that much clear above it.
      const clear = "scroll-mt-[var(--devices-scroll-offset,1rem)]";

      expect(screen.getByRole("heading", { level: 2 })).toHaveClass(clear);
      expect(document.getElementById("library-connect")).toHaveClass(clear);
      expect(document.getElementById("library-share")).toHaveClass(clear);
      // The notice said how something went, where it went: the next link leaves it behind.
      fireEvent.click(
        within(screen.getByTestId("devices-nav")).getByRole("link", {
          name: /federation\.devices\.tab\.device/,
        }),
      );
      expect(screen.getByTestId("devices-panel")).toHaveAttribute("data-section", "device");
      expect(screen.queryByTestId("federation-feedback")).not.toBeInTheDocument();
      expect(offset()).toBe("16px");
    } finally {
      rect.mockRestore();
    }
  });
});

describe("pairing requests", () => {
  it("asks this device to decide an incoming request and shows where it came from", async () => {
    withRequests([pairingRequest({ remoteAddress: "192.168.1.20", replacesExistingAccess: true })]);
    renderPage();
    // The place the notification's link lands on is named by its heading.
    expect(screen.getByTestId("sharing-requests")).toHaveAccessibleName(
      "federation.requests.incomingTitle",
    );
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
  it("says what this device did with an incoming request it decided, never the requester's outcome", () => {
    withRequests([
      pairingRequest({ requestId: "allowed", nodeName: "Laptop", status: "granted" }),
      pairingRequest({ requestId: "refused", nodeName: "Tablet", status: "rejected" }),
      pairingRequest({ requestId: "mine", direction: "outgoing", status: "granted" }),
    ]);
    renderPage();
    const incoming = screen.getByTestId("sharing-requests");

    // Listed here until they expire: this device allowed one and rejected the other.
    expect(within(incoming).getByText("federation.requests.incomingGranted")).toBeInTheDocument();
    expect(within(incoming).getByText("federation.requests.incomingRejected")).toBeInTheDocument();
    // "You can now browse it" and "the other side rejected" are the requester's words.
    expect(within(incoming).queryByText(/federation\.pair\./)).not.toBeInTheDocument();
    expect(within(incoming).queryByText("federation.requests.approve")).not.toBeInTheDocument();
    // A request this device sent still says how the other side answered.
    expect(
      within(screen.getByTestId("sharing-outgoing-requests")).getByText("federation.pair.granted"),
    ).toBeInTheDocument();
  });
  it("cancels an outgoing pending request and keeps the manual approval check", async () => {
    const refresh = withRequests([pairingRequest({ direction: "outgoing", requestId: "mine" })]);

    renderPage();
    expect(screen.getByText(/federation\.pair\.awaitingApproval/)).toBeInTheDocument();
    expect(screen.getByText("federation.requests.check")).toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.requests.cancel"));
    await waitFor(() => expect(federationPeerApi.cancelRequest).toHaveBeenCalledWith("mine"));
    await waitFor(() => expect(refresh).toHaveBeenCalled());
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
    // Still waiting is not news: the request's own row says so, the feedback does not.
    expect(screen.getByTestId("federation-feedback")).not.toHaveTextContent(
      "federation.pair.awaitingApproval",
    );
    expect(screen.getByText("federation.requests.approve")).not.toBeDisabled();
  });
  it("reports a decided request without touching what the user is typing", async () => {
    const refresh = withRequests([pairingRequest({ direction: "outgoing" })]);

    vi.mocked(federationPeerApi.claim).mockResolvedValue({ outcome: "granted" } as PairingResult);
    renderPage(CONNECT);
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
    // A request waits for an answer here, so status is read again every five seconds.
    await advance(1000);
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
    fireEvent.click(screen.getByText("federation.recovery.reset"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("federation.recovery.confirm");
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    // Only the sharing state was lost: the install keeps its identity and paired devices.
    await waitFor(() => expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(true, false));
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
    expect(screen.queryByText("federation.recovery.reset")).not.toBeInTheDocument();
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
    // Remote access is already on: there is nothing to turn on with sharing.
    expect(
      screen.queryByRole("checkbox", { name: "federation.sharing.configureRemote" }),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.sharing.start"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("federation.sharing.confirm");
    expect(screen.getByRole("alertdialog")).not.toHaveTextContent("confirmWithRemote");
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.sharing).toHaveBeenCalledWith(true, false));
  });
});

describe("this device's address", () => {
  const writeText = vi.fn();
  /** What the server lists: every port on every interface, in its own order. */
  const listed = [
    { url: "http://198.18.0.1:34567", interfaceName: "utun4" },
    { url: "http://198.18.0.1:5000", interfaceName: "utun4" },
    { url: "http://192.168.1.5:34567", interfaceName: "en0" },
    { url: "http://192.168.1.5:5000", interfaceName: "en0" },
    { url: "http://100.101.1.2:34567", interfaceName: "utun7" },
    { url: "http://192.168.128.1:34567", interfaceName: "bridge100" },
    { url: "http://169.254.3.4:34567", interfaceName: "en5" },
  ];
  const withSettings = (mode: number, addresses = listed) =>
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue({
      code: 0,
      data: {
        mode,
        addresses,
        allowLiveTranscode: false,
        requirePairing: true,
        devices: [],
        pendingRequests: [],
      },
    } as never);

  beforeEach(() => {
    writeText.mockReset();
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
  });
  afterEach(() => Reflect.deleteProperty(navigator, "clipboard"));

  it("recommends one address, lists one row per host, and folds away what cannot be reached", async () => {
    withSettings(1);
    renderPage("/federation/devices");
    const list = await screen.findByTestId("device-addresses");

    expect(list).toHaveAttribute("data-context", "device");
    const recommended = within(list).getByTestId("device-address-recommended");

    expect(recommended).toHaveTextContent("http://192.168.1.5:34567");
    expect(recommended).toHaveTextContent("federation.devices.addresses.recommended");
    // A VPN address is shown, and says what it is; other ports of the same host are not.
    expect(within(list).getByText("http://100.101.1.2:34567")).toBeInTheDocument();
    expect(within(list).getByText(/federation\.devices\.addresses\.vpn/)).toBeInTheDocument();
    expect(within(list).queryByText(/:5000$/)).not.toBeInTheDocument();
    // Adapters another device cannot reach wait behind a disclosure, labelled.
    expect(within(list).queryByText("http://198.18.0.1:34567")).not.toBeInTheDocument();
    const more = within(within(list).getByTestId("device-address-more")).getByRole("button", {
      name: "federation.devices.addresses.more",
    });

    expect(more).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(more);
    expect(within(list).getByText("http://198.18.0.1:34567")).toBeInTheDocument();
    expect(within(list).getByText("http://192.168.128.1:34567")).toBeInTheDocument();
    expect(within(list).getByText("http://169.254.3.4:34567")).toBeInTheDocument();
    expect(within(list).getAllByText(/federation\.devices\.addresses\.virtual/)).toHaveLength(2);
    expect(within(list).getByText(/federation\.devices\.addresses\.linkLocal/)).toBeInTheDocument();
  });
  it("copies an address and says whether it could", async () => {
    writeText.mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error("denied"));
    withSettings(1);
    renderPage("/federation/devices");
    const recommended = await screen.findByTestId("device-address-recommended");

    // Named by what it copies, beside the word it shows.
    const button = within(recommended).getByRole("button", {
      name: "federation.copy http://192.168.1.5:34567",
    });
    const list = screen.getByTestId("device-addresses");
    const announced = within(list).getByRole("status");

    expect(announced.textContent).toBe("");
    fireEvent.click(button);
    await waitFor(() => expect(writeText).toHaveBeenCalledWith("http://192.168.1.5:34567"));
    expect(await within(recommended).findByText("federation.copied")).toBeInTheDocument();
    // …and says how it went where a screen reader hears it.
    expect(announced).toHaveTextContent("federation.copied");
    fireEvent.click(within(recommended).getByText("federation.copied"));
    expect(await within(recommended).findByText("federation.copyFailed")).toBeInTheDocument();
    expect(announced).toHaveTextContent("federation.copyFailed");
    // Named by its own title, once: by the place a link lands on, which holds the list.
    expect(list).not.toHaveAttribute("aria-label");
    expect(list).not.toHaveAttribute("role");
    expect(
      screen.getByRole("region", { name: "federation.devices.addresses.title" }),
    ).toContainElement(list);
  });
  it("names the place a link lands on while the addresses are still on their way", () => {
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockReturnValueOnce(
      new Promise(() => {}) as never,
    );
    renderPage("/federation/devices");
    const place = screen.getByRole("region", { name: "federation.devices.addresses.title" });

    expect(place).toHaveAttribute("id", "device-addresses");
    expect(place).toHaveTextContent("federation.loading");
  });
  it("greys the addresses out while remote access is off, and says where to turn it on", async () => {
    withSettings(0);
    renderPage("/federation/devices");
    const list = await screen.findByTestId("device-addresses");

    expect(list).toHaveTextContent("federation.devices.addresses.remoteOff");
    const link = within(list).getByRole("link", {
      name: "federation.devices.openManagementAccess",
    });

    expect(link).toHaveAttribute("href", "/federation/devices?section=management");
    // Only the addresses are greyed out: the warning and its link keep their contrast.
    expect(within(list).getByTestId("device-address-recommended")).toHaveClass("opacity-60");
    for (let element: HTMLElement | null = link; element; element = element.parentElement)
      expect(element).not.toHaveClass("opacity-60");
  });
  it("points at the network when no address was found", async () => {
    withSettings(1, []);
    renderPage("/federation/devices");
    expect(await screen.findByText("federation.devices.addresses.none")).toBeInTheDocument();
  });
  it("puts the recommended address next to a new share code", async () => {
    vi.mocked(federationPeerApi.invite).mockResolvedValue({
      code: "482913",
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });
    withSettings(1);
    withStatus({ sharingEnabled: true, remoteAccessMode: 1 });
    renderPage();
    const list = await screen.findByTestId("device-addresses");

    expect(list).toHaveAttribute("data-context", "sharing");
    expect(within(list).getByTestId("device-address-recommended")).toHaveTextContent(
      "http://192.168.1.5:34567",
    );
    // Where a new code will be said: there, and silent, before one is made.
    const announced = screen.getByTestId("invite-code");

    expect(announced).toHaveAttribute("role", "status");
    expect(announced.textContent).toBe("");
    fireEvent.click(screen.getByText("federation.sharing.issueCode"));
    expect(await screen.findByText("482913")).toBeInTheDocument();
    expect(list.parentElement?.parentElement).toContainElement(screen.getByText("482913"));
    // Said as it appears — what it is, the digits and until when — while the button that
    // made it keeps focus and its own name.
    expect(announced).toHaveTextContent(
      /^federation\.sharing\.codeLabel\s*482913\s*federation\.expires$/,
    );
  });
  it("explains that remote access must be turned on while sharing is on and it is off", () => {
    withStatus({ sharingEnabled: true, remoteAccessMode: 0 });
    renderPage();
    expect(screen.getByText("federation.sharing.remoteDisabled")).toBeInTheDocument();
    expect(screen.queryByTestId("device-addresses")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.sharing.enableRemote"));
    // Sharing is on already: the question is about remote access, and what it lets back in.
    expect(screen.getByRole("alertdialog")).toHaveTextContent(
      "federation.sharing.enableRemoteConfirm",
    );
    expect(screen.getByRole("alertdialog")).not.toHaveTextContent("confirmWithRemote");
  });
  it("offers no code while sharing is off", () => {
    renderPage();
    expect(screen.queryByText("federation.sharing.issueCode")).not.toBeInTheDocument();
  });
});

describe("device name", () => {
  const nameForm = () => within(screen.getByLabelText("federation.name.label").closest("form")!);

  it("renames this device and refreshes status", async () => {
    const refresh = withStatus({});

    renderPage("/federation/devices");
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
    renderPage("/federation/devices");
    fireEvent.click(screen.getByText("federation.name.edit"));
    fireEvent.click(screen.getByText("federation.name.reset"));
    await waitFor(() => expect(federationPeerApi.setName).toHaveBeenCalledWith(null));
  });
  it("keeps the edit open when the name is rejected", async () => {
    vi.mocked(federationPeerApi.setName).mockRejectedValueOnce(
      new FederationError("InvalidDeviceName", "Bad name", 400),
    );
    renderPage("/federation/devices");
    fireEvent.click(screen.getByText("federation.name.edit"));
    fireEvent.click(nameForm().getByText("federation.save"));
    expect(await screen.findByText("Bad name")).toBeInTheDocument();
    expect(screen.getByLabelText("federation.name.label")).toHaveValue("This PC");
  });
});

describe("connecting with share-back", () => {
  const connectTo = (address: string) => {
    // Suggested in the same form as the management form's, as the address list shows it.
    fireEvent.change(screen.getByPlaceholderText("http://192.168.1.5:34567"), {
      target: { value: address },
    });
    fireEvent.click(screen.getByText("federation.pair.request"));
  };
  const shareBack = () => screen.getByRole("checkbox", { name: /federation\.pair\.shareBack/ });

  it("offers read access back by default", async () => {
    vi.mocked(federationPeerApi.connect).mockResolvedValue({
      outcome: "awaitingApproval",
    } as PairingResult);
    renderPage(CONNECT);
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
    renderPage(CONNECT);
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
    // A device holding both grants is in both lists, each with that direction's actions.
    expect(screen.getAllByText("federation.devices.remove")).toHaveLength(2);
    fireEvent.click(
      within(screen.getByTestId("sharing-outbound")).getByText("federation.devices.remove"),
    );
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

describe("a peer this device cannot reach", () => {
  it("says when a proxy on this computer took over the peer's name, and nothing more otherwise", () => {
    vi.mocked(useFederationStatus).mockReturnValue({
      status: {
        ...status,
        peers: [
          { ...status.peers[0], connectionState: "ProxyFakeAddress" },
          {
            ...status.peers[0],
            nodeId: "switched-off",
            label: "Switched off",
            connectionState: "Offline",
          },
        ],
      },
      loading: false,
      error: undefined,
      refresh: vi.fn().mockResolvedValue(undefined),
    });
    renderPage();

    expect(screen.getByText("federation.connection.ProxyFakeAddress")).toBeInTheDocument();
    // The fix is on this computer, so it is said in full, once, next to that peer.
    expect(screen.getByTestId("peer-proxy")).toHaveTextContent("federation.error.ProxyFakeAddress");
    expect(screen.getAllByTestId("peer-proxy")).toHaveLength(1);
    expect(screen.getByText("federation.connection.Offline")).toBeInTheDocument();
  });

  it("names that domain as the fix when the proxy could not get through to a peer's domain", () => {
    vi.mocked(useFederationStatus).mockReturnValue({
      status: {
        ...status,
        peers: [
          {
            ...status.peers[0],
            address: "http://nas.example.com:34567",
            connectionState: "ProxyFakeAddress",
          },
        ],
      },
      loading: false,
      error: undefined,
      refresh: vi.fn().mockResolvedValue(undefined),
    });
    renderPage();

    expect(screen.getByTestId("peer-proxy").textContent).toBe(
      "federation.error.ProxyFakeAddressDomain",
    );
  });
});

describe("discovery", () => {
  it("explains how to become discoverable when the scan finds nothing", async () => {
    vi.mocked(federationPeerApi.discover).mockResolvedValue([]);
    renderPage(CONNECT);
    fireEvent.click(screen.getByText("federation.discovery.scan"));
    expect(await screen.findByText("federation.discovery.noneFound")).toBeInTheDocument();
    expect(screen.queryByText("federation.discovery.use")).not.toBeInTheDocument();
  });

  it("lists another computer answering with this device's own identity", async () => {
    // The server leaves this device itself out; what it lists under this id is a copy of its
    // data folder, and connecting to it is where the user learns so.
    vi.mocked(federationPeerApi.discover).mockResolvedValue([
      { nodeId: "local", name: "Other PC", address: "http://192.168.1.9:34567" },
    ]);
    renderPage(CONNECT);
    fireEvent.click(screen.getByText("federation.discovery.scan"));
    fireEvent.click(await screen.findByText("federation.discovery.use"));
    expect(screen.getByDisplayValue("http://192.168.1.9:34567")).toBeInTheDocument();
    expect(screen.queryByText("federation.discovery.noneFound")).not.toBeInTheDocument();
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
