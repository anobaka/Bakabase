import { afterEach, describe, expect, it, vi } from "vitest";

import {
  addressCandidateForContext,
  createAddressObserver,
  normalizeRemoteAccessAddress,
} from "../remoteAccessAddress";

import { ClientMode, RemoteAccessMode } from "@/sdk/constants";

const browser = {
  isLocal: false,
  clientMode: ClientMode.RemoteBrowser,
  mode: RemoteAccessMode.Unrestricted,
};
const origin = "http://192.168.3.23:34567";

afterEach(() => vi.useRealTimers());

describe("remote address candidates", () => {
  it("uses the effective API origin, including development URLs, mapped ports and protocol-relative endpoints", () => {
    expect(addressCandidateForContext(browser, "", origin)).toBe(origin);
    expect(
      addressCandidateForContext(browser, "http://nas.example:54321", "http://localhost:3000"),
    ).toBe("http://nas.example:54321");
    expect(addressCandidateForContext(browser, "//nas.example:8443", "https://app.example")).toBe(
      "https://nas.example:8443",
    );
    expect(addressCandidateForContext(browser, "/", origin)).toBe(origin);
    expect(normalizeRemoteAccessAddress(" HTTPS://NAS.EXAMPLE:443/ ")).toBe("https://nas.example");
  });

  it.each([
    "http://localhost:34567",
    "http://127.0.0.1:34567",
    "http://127.1:34567",
    "http://[::1]:34567",
    "http://0.0.0.0:34567",
    "http://[::]:34567",
    "http://169.254.1.2:34567",
    "http://198.18.0.1:34567",
    "http://host.docker.internal:34567",
    "ftp://nas.example",
    "https://user:secret@nas.example",
    "https://nas.example/base",
    "https://nas.example?token=secret",
    "https://nas.example#token",
    "not an address",
  ])("does not advertise %s", (address) => {
    expect(normalizeRemoteAccessAddress(address)).toBeUndefined();
  });

  it("requires actual management access and never substitutes the relay origin for its upstream", () => {
    expect(
      addressCandidateForContext({ ...browser, mode: RemoteAccessMode.Enabled }, "", origin),
    ).toBeUndefined();
    expect(
      addressCandidateForContext(
        { ...browser, mode: RemoteAccessMode.Enabled, paired: true },
        "",
        origin,
      ),
    ).toBe(origin);
    const relay = {
      ...browser,
      mode: RemoteAccessMode.Enabled,
      clientMode: ClientMode.PureClient,
      paired: true,
    };

    expect(addressCandidateForContext(relay, "", origin)).toBeUndefined();
    expect(
      addressCandidateForContext(
        { ...relay, serverAddress: "https://nas.example:8443" },
        "http://localhost:56789",
        "http://127.0.0.1:56789",
      ),
    ).toBe("https://nas.example:8443");
    expect(
      addressCandidateForContext({ ...relay, paired: false, serverAddress: origin }, "", origin),
    ).toBeUndefined();
    expect(
      addressCandidateForContext(
        { ...relay, serverReachable: false, serverAddress: origin },
        "",
        origin,
      ),
    ).toBeUndefined();
  });

  it("coalesces concurrent and repeated loads, remains silent on failure, and accepts a changed endpoint", async () => {
    vi.useFakeTimers();
    const post = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValue(true);
    const observe = createAddressObserver(post);

    expect(await Promise.all([observe(browser, "", origin), observe(browser, "", origin)])).toEqual(
      [false, false],
    );
    expect(await observe(browser, "", origin)).toBe(false);
    expect(post).toHaveBeenCalledTimes(1);
    expect(await observe(browser, "https://nas.example", origin)).toBe(true);
    expect(post).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(30_000);
    expect(await observe(browser, "", origin)).toBe(true);
    expect(post).toHaveBeenCalledTimes(3);
    expect(await observe(browser, "https://nas.example", origin)).toBe(false);
    await vi.advanceTimersByTimeAsync(60 * 60_000);
    expect(await observe(browser, "https://nas.example", origin)).toBe(true);
    expect(post).toHaveBeenCalledTimes(4);
  });
});
