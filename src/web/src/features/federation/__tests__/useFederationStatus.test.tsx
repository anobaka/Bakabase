import type { FederationStatus } from "../types";

import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useFederationStatus } from "../hooks/useFederationStatus";
import { federationPeerApi } from "../peerApi";
import { notifyBrowsingChanged } from "../statusEvents";

vi.mock("../peerApi", () => ({ federationPeerApi: { status: vi.fn() } }));
const status: FederationStatus = {
  identity: { nodeId: "local", libraryEpoch: "epoch", name: "Local" },
  browsingEnabled: true,
  sharingEnabled: true,
  requirePairing: true,
  remoteAccessMode: 2,
  peers: [],
  requests: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
});
afterEach(cleanup);

describe("browsing status notifications", () => {
  it("disables immediately while refresh is pending, and ignores a superseded enabled response", async () => {
    let finishOld!: (value: FederationStatus) => void;
    const pending = new Promise<FederationStatus>((resolve) => {
      finishOld = resolve;
    });

    vi.mocked(federationPeerApi.status)
      .mockResolvedValueOnce(status)
      .mockReturnValueOnce(pending)
      .mockReturnValueOnce(new Promise(() => {}));
    const { result } = renderHook(useFederationStatus);

    await waitFor(() => expect(result.current.status?.browsingEnabled).toBe(true));
    act(() => {
      void result.current.refresh();
    });
    act(() => notifyBrowsingChanged(false));
    expect(result.current.status?.browsingEnabled).toBe(false);
    expect(result.current.status?.sharingEnabled).toBe(true);
    await act(async () => finishOld(status));
    expect(result.current.status?.browsingEnabled).toBe(false);
  });
  it("does not enable from a notification without an authoritative server response", async () => {
    vi.mocked(federationPeerApi.status)
      .mockResolvedValueOnce({ ...status, browsingEnabled: false })
      .mockReturnValueOnce(new Promise(() => {}));
    const { result } = renderHook(useFederationStatus);

    await waitFor(() => expect(result.current.status).toBeDefined());
    act(() => notifyBrowsingChanged(true));
    expect(result.current.status?.browsingEnabled).toBe(false);
    expect(federationPeerApi.status).toHaveBeenCalledTimes(2);
  });
  it("observes another window disabling browsing and rejects malformed cross-window notifications", async () => {
    vi.mocked(federationPeerApi.status)
      .mockResolvedValueOnce(status)
      .mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(useFederationStatus);

    await waitFor(() => expect(result.current.status).toBeDefined());
    act(() =>
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: "federation.status-change",
          newValue: '{"sender":"other","enabled":"false"}',
        }),
      ),
    );
    expect(result.current.status?.browsingEnabled).toBe(true);
    act(() =>
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: "federation.status-change",
          newValue: '{"sender":"other","enabled":false}',
        }),
      ),
    );
    expect(result.current.status?.browsingEnabled).toBe(false);
  });
});
