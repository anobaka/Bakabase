import type { FederationStatus } from "../types";

import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

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
