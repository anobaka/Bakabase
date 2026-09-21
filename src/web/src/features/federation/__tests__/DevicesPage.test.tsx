import type { FederationStatus } from "../types";

import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { FederationError } from "../transport";
import { saveConnectionHintDraft, CONNECTION_HINT_DRAFT_KEY } from "../migration";
import DevicesPage from "../DevicesPage";
import { federationPeerApi } from "../peerApi";
import { useFederationStatus } from "../hooks/useFederationStatus";

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
const renderPage = () =>
  render(
    <MemoryRouter>
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
  it("never changes remote-access settings unless the explicit checkbox was selected", async () => {
    renderPage();
    fireEvent.click(screen.getByText("federation.sharing.start"));
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

  it("imports a hint file without network or authorization side effects", async () => {
    renderPage();
    const file = new File(["{}"], "hints.json", { type: "application/json" });

    Object.defineProperty(file, "text", {
      value: async () =>
        JSON.stringify({
          format: "bakabase-client-connection-hints",
          version: 1,
          servers: [
            {
              name: "Imported PC",
              address: "http://imported",
              pathMappings: [{ serverPath: "D:/Movies", localPath: "/Volumes/Movies" }],
              deviceKey: "discard",
            },
          ],
        }),
    });
    fireEvent.change(screen.getByLabelText("federation.migration.chooseFile"), {
      target: { files: [file] },
    });
    fireEvent.click(await screen.findByText("federation.discovery.use"));
    expect(screen.getByDisplayValue("http://imported")).toBeInTheDocument();
    expect(screen.getByText(/D:\/Movies/)).toBeInTheDocument();
    expect(federationPeerApi.connect).not.toHaveBeenCalled();
    expect(federationPeerApi.mappings).not.toHaveBeenCalled();
    expect(document.body.textContent).not.toContain("discard");
  });

  it("requires explicit confirmation before resetting a cloned installation identity", async () => {
    renderPage();
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.identity.reset"));
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.resetIdentity).toHaveBeenCalledOnce());
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
  it("restores a migration preview after remount, deduplicates repeated imports and clears only the draft", async () => {
    const file = new File(["{}"], "hints.json", { type: "application/json" });

    Object.defineProperty(file, "text", {
      value: async () =>
        JSON.stringify({
          format: "bakabase-client-connection-hints",
          version: 1,
          servers: [
            { name: "Imported", address: "http://imported", pathMappings: [], deviceKey: "secret" },
          ],
        }),
    });
    const page = renderPage();
    const importFile = () =>
      fireEvent.change(screen.getByLabelText("federation.migration.chooseFile"), {
        target: { files: [file] },
      });

    importFile();
    await screen.findByText("Imported");
    importFile();
    await waitFor(() => expect(screen.getAllByText("Imported")).toHaveLength(1));
    page.unmount();
    renderPage();
    expect(screen.getAllByText("Imported")).toHaveLength(1);
    expect(federationPeerApi.connect).not.toHaveBeenCalled();
    localStorage.setItem("unrelated", "keep");
    expect(JSON.stringify(localStorage)).not.toContain("secret");
    fireEvent.click(screen.getByText("federation.migration.clearDraft"));
    expect(screen.queryByText("Imported")).not.toBeInTheDocument();
    expect(localStorage.getItem("unrelated")).toBe("keep");
  });
});

describe("mapping and migration recovery", () => {
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
  it("keeps the restored draft intact if the next import is invalid", async () => {
    saveConnectionHintDraft({
      format: "bakabase-client-connection-hints",
      version: 1,
      servers: [{ name: "Saved PC", address: "http://saved", pathMappings: [] }],
    });
    const before = localStorage.getItem(CONNECTION_HINT_DRAFT_KEY);

    renderPage();
    const file = new File(["invalid"], "bad.json");

    Object.defineProperty(file, "text", { value: async () => "invalid" });
    fireEvent.change(screen.getByLabelText("federation.migration.chooseFile"), {
      target: { files: [file] },
    });
    expect(await screen.findByRole("alert")).toHaveTextContent("InvalidConnectionHints");
    expect(screen.getByText("Saved PC")).toBeInTheDocument();
    expect(localStorage.getItem(CONNECTION_HINT_DRAFT_KEY)).toBe(before);
  });
});
