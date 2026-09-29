import { describe, expect, it } from "vitest";

import { managedServerOutcomeCode, proxyFakeAddressKey, proxyResolvesItself } from "../proxy";

import { ManagedServerOutcome } from "@/sdk/constants";

describe("what to say when a proxy on this computer is in the way", () => {
  it.each([
    ["http://nas.example.com:34567", true],
    ["nas.example-ddns.net:34567", true],
    ["https://user@home.example.org./path", true],
    ["http://jaxs-Mac-mini.local:34567", false],
    ["NAS.LOCAL.:34567", false],
    ["http://PC1:34567", false],
    ["PC1", false],
    ["http://198.18.0.29:34567", false],
    ["192.168.1.5:34567", false],
    ["http://[fe80::1]:34567", false],
    ["fe80::1", false],
    ["", false],
    [undefined, false],
  ])("%s names its device by a domain the proxy resolves itself: %s", (address, domain) => {
    // As the server tells them apart (ProxyFakeAddresses.ProxyResolvesItself).
    expect(proxyResolvesItself(address)).toBe(domain);
  });

  it("words a domain's fix apart from a LAN name's", () => {
    expect(proxyFakeAddressKey("http://nas.example.com:34567")).toBe(
      "federation.error.ProxyFakeAddressDomain",
    );
    expect(proxyFakeAddressKey("http://nas.local:34567")).toBe("federation.error.ProxyFakeAddress");
    expect(
      managedServerOutcomeCode(ManagedServerOutcome.ProxyFakeAddress, "nas.example.com:34567"),
    ).toBe("ManagedServerProxyFakeAddressDomain");
    expect(managedServerOutcomeCode(ManagedServerOutcome.ProxyFakeAddress, "nas.local:34567")).toBe(
      "ManagedServerProxyFakeAddress",
    );
    // Every other outcome is worded by itself alone.
    expect(
      managedServerOutcomeCode(ManagedServerOutcome.Unreachable, "nas.example.com:34567"),
    ).toBe("ManagedServerUnreachable");
  });
});
