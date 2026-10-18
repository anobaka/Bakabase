import type { ManagementSource } from "../api";

import { cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useResourceCoverProperties } from "../useResourceCoverProperties";

const mocks = vi.hoisted(() => ({
  owner: undefined as ManagementSource | undefined,
  local: vi.fn(),
  remote: vi.fn(),
}));

vi.mock("../resourceContext", () => ({
  useResourceApi: () => ({
    customProperty: { getCustomPropertyByKeys: mocks.owner ? mocks.remote : mocks.local },
  }),
  useResourceOwner: () => mocks.owner,
  useResourceOwnerKey: () =>
    mocks.owner ? JSON.stringify([mocks.owner.serverId, mocks.owner.libraryEpoch]) : "local",
}));

beforeEach(() => {
  mocks.owner = undefined;
  vi.clearAllMocks();
  mocks.local.mockResolvedValue({ data: [{ id: 101, options: { choices: [] } }] });
  mocks.remote.mockResolvedValue({ data: [{ id: 101, options: { tags: [] } }] });
});
afterEach(cleanup);

describe("resource cover property metadata", () => {
  it("shares one metadata request across cards even if display keys are ordered differently", async () => {
    const first = renderHook(() => useResourceCoverProperties([101, 102]));
    const second = renderHook(() => useResourceCoverProperties([102, 101]));

    await waitFor(() =>
      expect(first.result.current.data?.get(101)?.options).toEqual({ choices: [] }),
    );
    expect(second.result.current.data).toBe(first.result.current.data);
    expect(mocks.local).toHaveBeenCalledExactlyOnceWith(
      { ids: [101, 102] },
      { showErrorToast: false },
    );
  });

  it("isolates matching property IDs by resource owner and library epoch", async () => {
    const local = renderHook(() => useResourceCoverProperties([101]));

    await waitFor(() =>
      expect(local.result.current.data?.get(101)?.options).toEqual({ choices: [] }),
    );
    mocks.owner = {
      serverId: "remote",
      libraryEpoch: "one",
      available: true,
      isLocal: false,
    } as ManagementSource;
    const remote = renderHook(() => useResourceCoverProperties([101]));

    await waitFor(() =>
      expect(remote.result.current.data?.get(101)?.options).toEqual({ tags: [] }),
    );
    expect(mocks.remote).toHaveBeenCalledTimes(1);
    mocks.owner = { ...mocks.owner, libraryEpoch: "two" };
    mocks.remote.mockResolvedValue({
      data: [{ id: 101, options: { choices: [{ color: "red" }] } }],
    });
    remote.rerender();
    await waitFor(() => expect(mocks.remote).toHaveBeenCalledTimes(2));
    await waitFor(() =>
      expect(remote.result.current.data?.get(101)?.options).toEqual({
        choices: [{ color: "red" }],
      }),
    );
  });

  it("does not request property definitions when no reference property is displayed", () => {
    renderHook(() => useResourceCoverProperties([]));
    expect(mocks.local).not.toHaveBeenCalled();
    expect(mocks.remote).not.toHaveBeenCalled();
  });
});
