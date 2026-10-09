import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useRemoteAccessStore } from "../remoteAccess";

import { ClientMode, RemoteAccessMode } from "@/sdk/constants";

const { context, observe, env } = vi.hoisted(() => ({
  context: vi.fn(),
  observe: vi.fn(),
  env: { apiEndpoint: "" },
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    remoteAccess: { getRemoteAccessContext: context, observeRemoteAccessAddress: observe },
  },
}));
vi.mock("@/core/clientApi", () => ({
  clientApi: { status: vi.fn().mockResolvedValue({ host: "console", servers: [] }) },
}));
vi.mock("@/config/env", () => ({ default: env }));

const initial = useRemoteAccessStore.getState();
let serial = 0;
const known = (extra: Record<string, unknown> = {}) => ({
  code: 0,
  data: {
    isLocal: false,
    clientMode: ClientMode.RemoteBrowser,
    mode: RemoteAccessMode.Unrestricted,
    serverReachable: true,
    ...extra,
  },
});

beforeEach(() => {
  vi.clearAllMocks();
  useRemoteAccessStore.setState(initial, true);
  env.apiEndpoint = `https://nas-${++serial}.example:8443`;
  context.mockResolvedValue(known());
  observe.mockResolvedValue({ code: 0 });
});
afterEach(() => vi.useRealTimers());

describe("startup address observation", () => {
  it("collects after known context without visiting devices, coalesces repeat loads and refreshes settings", async () => {
    await Promise.all([
      useRemoteAccessStore.getState().load(),
      useRemoteAccessStore.getState().load(),
    ]);

    expect(observe).toHaveBeenCalledTimes(1);
    expect(observe).toHaveBeenCalledWith(
      { address: env.apiEndpoint },
      expect.objectContaining({ showErrorToast: false, signal: expect.any(AbortSignal) }),
    );
    expect(useRemoteAccessStore.getState()).toMatchObject({
      context: "known",
      addressCandidatesRevision: 1,
    });
  });

  it("does not collect when context failed, access is unpaired, or the desktop API is loopback", async () => {
    context.mockRejectedValueOnce(new Error("offline"));
    await useRemoteAccessStore.getState().load();
    context.mockResolvedValueOnce(known({ mode: RemoteAccessMode.Enabled }));
    await useRemoteAccessStore.getState().load();
    env.apiEndpoint = "http://127.0.0.1:34567";
    context.mockResolvedValueOnce(known({ clientMode: ClientMode.AllInOne, isLocal: true }));
    await useRemoteAccessStore.getState().load();
    expect(observe).not.toHaveBeenCalled();
  });

  it("uses the paired relay's actual server address and skips a relay without one", async () => {
    context.mockResolvedValueOnce(
      known({ clientMode: ClientMode.PureClient, mode: RemoteAccessMode.Enabled, paired: true }),
    );
    await useRemoteAccessStore.getState().load();
    expect(observe).not.toHaveBeenCalled();
    context.mockResolvedValueOnce(
      known({
        clientMode: ClientMode.PureClient,
        mode: RemoteAccessMode.Enabled,
        paired: true,
        serverAddress: "https://actual-upstream.example:9443",
      }),
    );
    await useRemoteAccessStore.getState().load();
    expect(observe).toHaveBeenCalledWith(
      { address: "https://actual-upstream.example:9443" },
      expect.anything(),
    );
  });

  it("leaves context usable and stops a hung observation after three seconds", async () => {
    vi.useFakeTimers();
    observe.mockImplementationOnce(
      (_, { signal }: { signal: AbortSignal }) =>
        new Promise((_, reject) => {
          signal.addEventListener("abort", () => reject(new Error("aborted")));
        }),
    );
    const loading = useRemoteAccessStore.getState().load();

    await Promise.resolve();
    expect(useRemoteAccessStore.getState().context).toBe("known");
    await vi.advanceTimersByTimeAsync(3000);
    await loading;
    expect(useRemoteAccessStore.getState().addressCandidatesRevision).toBe(0);
    expect(observe.mock.calls[0][1].signal.aborted).toBe(true);
  });
});
