import type { FederationStatus } from "../types";
import type { QueryState } from "../hooks/useFederatedQuery";

import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import LibraryPage from "../LibraryPage";
import { useFederatedQuery } from "../hooks/useFederatedQuery";
import { useFederationStatus } from "../hooks/useFederationStatus";
import { FederationError } from "../transport";

vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (state: unknown) => unknown) =>
    selector({ initialized: true, isLocal: true }),
  useIsPureClient: () => false,
}));
vi.mock("../hooks/useFederatedQuery", () => ({ useFederatedQuery: vi.fn() }));
vi.mock("../hooks/useFederationStatus", () => ({ useFederationStatus: vi.fn() }));
const status: FederationStatus = {
  identity: { nodeId: "local", libraryEpoch: "epoch", name: "This PC" },
  sharingEnabled: false,
  remoteAccessMode: 0,
  requirePairing: false,
  peers: [
    {
      nodeId: "offline",
      label: "Sleeping PC",
      address: "http://sleeping",
      enabled: true,
      connectionState: "Offline",
      outboundGrant: { grantId: "grant", revision: 1 },
      pathMappings: [],
    },
    {
      nodeId: "unauthorized",
      label: "Unpaired PC",
      address: "http://unpaired",
      enabled: true,
      connectionState: "Unknown",
      pathMappings: [],
    },
  ],
  requests: [],
};
const search = vi.fn().mockResolvedValue(undefined);
const state: QueryState = { pages: [], requestedNodeIds: ["local", "offline"] };
const setState = (state: QueryState) =>
  vi
    .mocked(useFederatedQuery)
    .mockReturnValue({ state, search, nextPage: vi.fn(), cancel: vi.fn(), reset: vi.fn() });

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  vi.mocked(useFederationStatus).mockReturnValue({
    status,
    loading: false,
    error: undefined,
    refresh: vi.fn(),
  });
  setState(state);
});
afterEach(cleanup);

describe("visible query coverage", () => {
  it("keeps offline authorized sources in All so the coordinator can report missing coverage", async () => {
    render(
      <MemoryRouter initialEntries={["/federation?scope=all"]}>
        <LibraryPage />
      </MemoryRouter>,
    );
    await waitFor(() =>
      expect(search).toHaveBeenCalledWith(
        expect.objectContaining({ nodeIds: ["local", "offline"] }),
      ),
    );
    expect(screen.getByText("federation.readOnly")).toBeInTheDocument();
  });
  it("explains zero results with omitted sources as partial, never as an empty combined library", () => {
    setState({
      ...state,
      pages: [
        {
          sessionId: "partial",
          items: [],
          expiresInMs: 30_000,
          participants: [{ nodeId: "local", libraryEpoch: "epoch", totalCount: 0 }],
          omittedNodes: [{ nodeId: "offline", code: "Offline", retryable: true }],
          totalWithinParticipants: 0,
          coverageComplete: false,
        },
      ],
    });
    render(
      <MemoryRouter>
        <LibraryPage />
      </MemoryRouter>,
    );
    expect(screen.getByText("federation.empty.partial")).toBeInTheDocument();
    expect(screen.queryByText("federation.empty.title")).not.toBeInTheDocument();
    expect(screen.getByText(/Sleeping PC/)).toBeInTheDocument();
  });
  it("shows each failed source when no participant could complete", () => {
    setState({
      ...state,
      error: new FederationError("NoParticipants", "No sources completed", 503, true, undefined, [
        { nodeId: "offline", code: "Offline", retryable: true },
      ]),
    });
    render(
      <MemoryRouter>
        <LibraryPage />
      </MemoryRouter>,
    );
    expect(screen.getByRole("alert")).toHaveTextContent("No sources completed");
    expect(screen.getByText(/Sleeping PC/)).toBeInTheDocument();
    expect(screen.queryByText("federation.empty.title")).not.toBeInTheDocument();
  });
});
