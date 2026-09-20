import type { FederatedQueryPage, LocalFederatedQuery } from "../types";

import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { useFederatedQuery } from "../hooks/useFederatedQuery";
import { FederationError } from "../transport";

const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((yes, no) => {
    resolve = yes;
    reject = no;
  });

  return { promise, resolve, reject };
};
const query: LocalFederatedQuery = {
  nodeIds: ["a", "b"],
  query: { queryContractVersion: 1, sort: "NameAsc" },
  pageSize: 50,
};
const page = (sessionId: string, nodeId = "a"): FederatedQueryPage => ({
  sessionId,
  items: [
    {
      ref: { nodeId, libraryEpoch: "epoch", resourceId: 1 },
      ownerLabel: nodeId,
      displayName: sessionId,
      sourceKinds: [],
      fileAvailability: "MetadataOnly",
      playbackCapabilities: [],
    },
  ],
  nextCursor: "cursor",
  expiresInMs: 60_000,
  participants: [{ nodeId, libraryEpoch: "epoch", totalCount: 1 }],
  omittedNodes: [{ nodeId: "b", code: "Offline", retryable: true }],
  totalWithinParticipants: 1,
  coverageComplete: false,
});
const createApi = () => ({
  create: vi.fn(),
  page: vi.fn(),
  release: vi.fn().mockResolvedValue(undefined),
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("federated query snapshots", () => {
  it("aborts and releases a replaced snapshot and ignores its late response", async () => {
    const api = createApi();
    const old = deferred<FederatedQueryPage>();

    api.create.mockReturnValueOnce(old.promise).mockResolvedValueOnce(page("new", "b"));
    const { result } = renderHook(() => useFederatedQuery(api));
    let pending!: Promise<void>;

    act(() => {
      pending = result.current.search(query);
    });
    const signal = api.create.mock.calls[0][1] as AbortSignal;

    await act(() => result.current.search({ ...query, nodeIds: ["b"] }));
    expect(signal.aborted).toBe(true);
    await act(async () => {
      old.resolve(page("old"));
      await pending;
    });
    expect(result.current.state.pages.map((p) => p.sessionId)).toEqual(["new"]);
    expect(api.release).toHaveBeenCalledWith("old");
    expect(result.current.state.requestedNodeIds).toEqual(["b"]);
  });

  it("cancel does not allow a late create to restore results", async () => {
    const api = createApi();
    const pending = deferred<FederatedQueryPage>();

    api.create.mockReturnValue(pending.promise);
    const { result } = renderHook(() => useFederatedQuery(api));
    let request!: Promise<void>;

    act(() => {
      request = result.current.search(query);
    });
    act(() => result.current.cancel());
    await act(async () => {
      pending.resolve(page("cancelled"));
      await request;
    });
    expect(result.current.state.pages).toEqual([]);
    expect(result.current.state.cancelled).toBe(true);
    expect(result.current.state.busy).toBeUndefined();
    expect(api.release).toHaveBeenCalledWith("cancelled");
  });

  it("retains the received page and its partial coverage after a page failure", async () => {
    const api = createApi();

    api.create.mockResolvedValue(page("first"));
    api.page.mockRejectedValue(new FederationError("QuerySessionInterrupted", "", 409));
    const { result, unmount } = renderHook(() => useFederatedQuery(api));

    await act(() => result.current.search(query));
    await act(() => result.current.nextPage());
    expect(result.current.state.pages).toEqual([page("first")]);
    expect(result.current.state.error).toMatchObject({ code: "QuerySessionInterrupted" });
    unmount();
    expect(api.release).toHaveBeenCalledWith("first");
  });

  it("never appends an old page to a new query with the same numeric resource ID", async () => {
    const api = createApi();
    const oldPage = deferred<FederatedQueryPage>();

    api.create.mockResolvedValueOnce(page("old", "a")).mockResolvedValueOnce(page("new", "b"));
    api.page.mockReturnValue(oldPage.promise);
    const { result } = renderHook(() => useFederatedQuery(api));

    await act(() => result.current.search(query));
    let pending!: Promise<void>;

    act(() => {
      pending = result.current.nextPage();
    });
    await act(() => result.current.search(query));
    await act(async () => {
      oldPage.resolve(page("old", "a"));
      await pending;
    });
    expect(result.current.state.pages).toEqual([page("new", "b")]);
    expect(api.release).toHaveBeenCalledWith("old");
  });

  it("does not fetch expired cursors", async () => {
    const api = createApi();

    api.create.mockResolvedValue({ ...page("expired"), expiresInMs: -1 });
    const { result } = renderHook(() => useFederatedQuery(api));

    await act(() => result.current.search(query));
    await act(() => result.current.nextPage());
    expect(api.page).not.toHaveBeenCalled();
    expect(result.current.state.error).toMatchObject({ code: "QuerySessionExpired" });
  });

  it("releases the snapshot on pagehide and requires a new search after a cached-page restore", async () => {
    const api = createApi();

    api.create.mockResolvedValue(page("leaving"));
    const { result } = renderHook(() => useFederatedQuery(api));

    await act(() => result.current.search(query));
    act(() => {
      window.dispatchEvent(new Event("pagehide"));
    });
    expect(api.release).toHaveBeenCalledWith("leaving");
    act(() => {
      window.dispatchEvent(new PageTransitionEvent("pageshow", { persisted: true }));
    });
    expect(result.current.state.cancelled).toBe(true);
    await act(() => result.current.nextPage());
    expect(api.page).not.toHaveBeenCalled();
  });
});
