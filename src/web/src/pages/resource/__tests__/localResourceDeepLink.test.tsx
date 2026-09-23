import { StrictMode } from "react";
import { cleanup, render, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useLocalResourceDeepLink } from "../useLocalResourceDeepLink";

import { federationPeerApi } from "@/features/federation/peerApi";

const { createPortal, notifyError } = vi.hoisted(() => ({
  createPortal: vi.fn(),
  notifyError: vi.fn(),
}));

vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@/components/Resource/components/DetailModal", () => ({ default: () => null }));
vi.mock("@/features/federation/peerApi", () => ({ federationPeerApi: { status: vi.fn() } }));
vi.mock("react-hot-toast", () => ({ default: { error: notifyError } }));
vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (state: unknown) => unknown) => selector({ isLocal: true }),
  useIsPureClient: () => false,
}));
function Probe() {
  useLocalResourceDeepLink();

  return null;
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(federationPeerApi.status).mockResolvedValue({
    identity: { nodeId: "local", libraryEpoch: "current", name: "PC" },
    peers: [],
    requests: [],
    browsingEnabled: false,
    sharingEnabled: false,
    requirePairing: false,
    remoteAccessMode: 0,
  });
});
afterEach(cleanup);

describe("explicit handoff to local management", () => {
  it("opens once under StrictMode only after the current owner and epoch match", async () => {
    render(
      <StrictMode>
        <MemoryRouter initialEntries={["/resource?inspect=1&node=local&epoch=current"]}>
          <Probe />
        </MemoryRouter>
      </StrictMode>,
    );
    await waitFor(() => expect(createPortal).toHaveBeenCalledTimes(1));
    expect(createPortal).toHaveBeenCalledWith(expect.any(Function), { id: 1 });
  });
  it("refuses an old epoch even if the same numeric ID now exists in the restored local library", async () => {
    render(
      <MemoryRouter initialEntries={["/resource?inspect=1&node=local&epoch=old"]}>
        <Probe />
      </MemoryRouter>,
    );
    await waitFor(() =>
      expect(notifyError).toHaveBeenCalledWith("federation.error.LibraryEpochChanged"),
    );
    expect(createPortal).not.toHaveBeenCalled();
  });
  it("does not resolve a bare numeric deep link or change ordinary local searches", () => {
    render(
      <MemoryRouter initialEntries={["/resource?inspect=1"]}>
        <Probe />
      </MemoryRouter>,
    );
    expect(federationPeerApi.status).not.toHaveBeenCalled();
    expect(createPortal).not.toHaveBeenCalled();
  });
});
