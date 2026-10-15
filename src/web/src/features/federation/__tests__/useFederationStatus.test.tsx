import type { FederationStatus } from "../types";

import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useFederationStatus } from "../hooks/useFederationStatus";
import { federationPeerApi } from "../peerApi";
import { notifyFederationChanged } from "../statusEvents";

vi.mock("../peerApi", () => ({ federationPeerApi: { status: vi.fn() } }));
const status: FederationStatus = {
  identity: { nodeId: "local", libraryEpoch: "epoch", name: "Local" },
  requirePairing: true,
  remoteAccessMode: 2,
  reachableAddresses: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
});
afterEach(cleanup);

describe("federation state notifications", () => {
  it("invalidates immediately after reset and ignores a superseded status response", async () => {
    let finishOld!: (value: FederationStatus) => void;
    const pending = new Promise<FederationStatus>((resolve) => {
      finishOld = resolve;
    });

    vi.mocked(federationPeerApi.status)
      .mockResolvedValueOnce(status)
      .mockReturnValueOnce(pending)
      .mockReturnValueOnce(new Promise(() => {}));
    const { result } = renderHook(useFederationStatus);

    await waitFor(() => expect(result.current.status).toEqual(status));
    act(() => {
      void result.current.refresh();
    });
    act(() => notifyFederationChanged("reset"));
    expect(result.current.status).toBeUndefined();
    await act(async () => finishOld(status));
    expect(result.current.status).toBeUndefined();
  });
  it("refreshes changed state without replacing it before the server responds", async () => {
    vi.mocked(federationPeerApi.status)
      .mockResolvedValueOnce(status)
      .mockReturnValueOnce(new Promise(() => {}));
    const { result } = renderHook(useFederationStatus);

    await waitFor(() => expect(result.current.status).toBeDefined());
    act(() => notifyFederationChanged());
    expect(result.current.status).toEqual(status);
    expect(federationPeerApi.status).toHaveBeenCalledTimes(2);
  });
  it("observes another window resetting identity and rejects malformed cross-window notifications", async () => {
    vi.mocked(federationPeerApi.status)
      .mockResolvedValueOnce(status)
      .mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(useFederationStatus);

    await waitFor(() => expect(result.current.status).toBeDefined());
    act(() =>
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: "federation.status-change",
          newValue: '{"sender":"other","kind":"invalid"}',
        }),
      ),
    );
    expect(result.current.status).toEqual(status);
    act(() =>
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: "federation.status-change",
          newValue: '{"sender":"other","kind":"reset"}',
        }),
      ),
    );
    expect(result.current.status).toBeUndefined();
  });
});

describe("quiet background refresh", () => {
  it("never shows loading or replaces the last good status with an error", async () => {
    let finish!: (value: FederationStatus) => void;

    vi.mocked(federationPeerApi.status)
      .mockResolvedValueOnce(status)
      .mockReturnValueOnce(
        new Promise((resolve) => {
          finish = resolve;
        }),
      )
      .mockRejectedValueOnce(new Error("offline"));
    const { result } = renderHook(useFederationStatus);

    await waitFor(() => expect(result.current.loading).toBe(false));
    let pending!: Promise<void>;

    act(() => {
      pending = result.current.refresh({ quiet: true });
    });
    expect(result.current.loading).toBe(false);
    await act(async () => {
      finish({ ...status, identity: { ...status.identity, name: "Updated" } });
      await pending;
    });
    expect(result.current.status?.identity.name).toBe("Updated");
    await act(async () => {
      await result.current.refresh({ quiet: true });
    });
    expect(result.current.error).toBeUndefined();
    expect(result.current.status?.identity.name).toBe("Updated");
    expect(result.current.loading).toBe(false);
  });
  it("yields to a refresh that is already in flight", async () => {
    let finish!: (value: FederationStatus) => void;

    vi.mocked(federationPeerApi.status)
      .mockResolvedValueOnce(status)
      .mockReturnValueOnce(
        new Promise((resolve) => {
          finish = resolve;
        }),
      );
    const { result } = renderHook(useFederationStatus);

    await waitFor(() => expect(result.current.status).toBeDefined());
    act(() => {
      void result.current.refresh();
    });
    await act(async () => {
      await result.current.refresh({ quiet: true });
    });
    expect(federationPeerApi.status).toHaveBeenCalledTimes(2);
    expect(result.current.loading).toBe(true);
    await act(async () => finish({ ...status, identity: { ...status.identity, name: "Updated" } }));
    expect(result.current.loading).toBe(false);
    expect(result.current.status?.identity.name).toBe("Updated");
  });
});
