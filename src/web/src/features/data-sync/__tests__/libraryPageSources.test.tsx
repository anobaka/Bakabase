import type { FederationStatus } from "@/features/federation/types";

import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import LibraryPage from "@/features/federation/LibraryPage";
import { useFederatedQuery } from "@/features/federation/hooks/useFederatedQuery";
import { useFederationStatus } from "@/features/federation/hooks/useFederationStatus";

/*
 * Hook H-lib (spec §11.3, F76): the library page lists as sources only the devices it shares a
 * library with, one way or the other. A device paired only to sync definitions has no library
 * here, and is left out rather than shown as a source it may not read.
 */

vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (state: unknown) => unknown) =>
    selector({ initialized: true, isLocal: true }),
  useIsPureClient: () => false,
}));
vi.mock("@/features/federation/peerApi", () => ({ federationPeerApi: { browsing: vi.fn() } }));
vi.mock("@/features/federation/components/ResourceDetail", () => ({ default: () => null }));
vi.mock("@/features/federation/hooks/useFederatedQuery", () => ({ useFederatedQuery: vi.fn() }));
vi.mock("@/features/federation/hooks/useFederationStatus", () => ({
  useFederationStatus: vi.fn(),
}));

const peer = (
  nodeId: string,
  label: string,
  grants: Partial<FederationStatus["peers"][number]>,
) => ({
  nodeId,
  label,
  address: `http://${nodeId}`,
  enabled: true,
  connectionState: "Online",
  pathMappings: [],
  ...grants,
});

const status: FederationStatus = {
  identity: { nodeId: "local", libraryEpoch: "epoch", name: "This PC" },
  browsingEnabled: true,
  sharingEnabled: true,
  remoteAccessMode: 0,
  requirePairing: true,
  peers: [
    peer("reads", "Library PC", { outboundGrant: { grantId: "out", revision: 1 } }),
    peer("reader", "Reader PC", { inboundGrant: { grantId: "in", revision: 1 } as never }),
    peer("definitions", "Definitions only PC", {}),
  ],
  requests: [],
} as FederationStatus;

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  vi.mocked(useFederationStatus).mockReturnValue({
    status,
    loading: false,
    error: undefined,
    refresh: vi.fn(),
  });
  vi.mocked(useFederatedQuery).mockReturnValue({
    state: { pages: [], requestedNodeIds: [] },
    search: vi.fn().mockResolvedValue(undefined),
    nextPage: vi.fn(),
    cancel: vi.fn(),
    reset: vi.fn(),
  });
});
afterEach(cleanup);

describe("library sources", () => {
  it("lists only devices with a library grant either way", () => {
    render(
      <MemoryRouter initialEntries={["/federation?scope=selected"]}>
        <LibraryPage />
      </MemoryRouter>,
    );

    expect(screen.getByRole("checkbox", { name: /Library PC/ })).toBeEnabled();
    // It reads this device's library: listed, not readable from here.
    expect(screen.getByRole("checkbox", { name: /Reader PC/ })).toBeDisabled();
    expect(screen.queryByRole("checkbox", { name: /Definitions only PC/ })).toBeNull();
  });
});
