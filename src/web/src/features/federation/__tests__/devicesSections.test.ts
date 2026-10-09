import type { DevicesData } from "../devices/context";
import type { DataSyncStatusView } from "@/features/data-sync/api";

import { describe, expect, it } from "vitest";

import { classifyAddress, deviceAddresses } from "../devices/addresses";
import { devicesAnchors, devicesTabs, resolveSection } from "../devices/sections";
import { devicesRoute } from "../switching";

import { RemoteAccessAddressKind } from "@/sdk/constants";
import { status as syncStatus } from "@/features/data-sync/__tests__/dataSyncFixtures";

/*
 * `?section=` is a contract with things outside the page — the Service's notifications,
 * the window's switcher, the configuration page, the map, the library and the help — so
 * every value they send has to keep landing where it did.
 */

describe("where a devices page link lands", () => {
  it.each([
    [null, "device", null, false],
    ["", "device", null, false],
    ["no-such-section", "device", null, false],
    ["device", "device", null, true],
    ["manage", "manage", null, true],
    ["sharing", "sharing", null, true],
    // Data sync's summary; everything else about it is on /data-sync.
    ["sync", "sync", null, true],
    ["advanced", "advanced", null, true],
    ["addresses", "device", "addresses", true],
    // The window's switcher.
    ["servers", "manage", "servers", true],
    ["add-server", "manage", "add-server", true],
    // The Service's "wants to manage this device" notification, the help and the map.
    ["management", "manage", "management", true],
    // The library while the Multi-device library is off, and its empty state.
    ["browsing", "sharing", "browsing", true],
    ["connect", "sharing", "connect", true],
    ["share", "sharing", "share", true],
    // The Service's "wants to browse this device" notification.
    ["sharing-requests", "sharing", "sharing-requests", true],
    // The configuration page's recovery link.
    ["identity", "advanced", "identity", true],
  ])("%s → the %s tab, at %s", (value, tab, anchor, explicit) => {
    expect(resolveSection(value)).toEqual({ tab, anchor, explicit });
  });

  it("names every place inside a tab once, and each by an element of the page", () => {
    const anchors = devicesTabs.flatMap((tab) => tab.anchors);

    expect(new Set(anchors).size).toBe(anchors.length);
    expect(anchors.sort()).toEqual(Object.keys(devicesAnchors).sort());
    expect(devicesAnchors.management.elementId).toBe("management-access");
    expect(devicesAnchors.servers.elementId).toBe("managed-servers");
    expect(devicesAnchors.identity.elementId).toBe("federation-identity");
  });

  it.each([
    [undefined, "/federation/devices"],
    ["manage", "/federation/devices?section=manage"],
    ["sync", "/federation/devices?section=sync"],
    ["servers", "/federation/devices?section=servers"],
    ["management", "/federation/devices?section=management"],
    ["sharing-requests", "/federation/devices?section=sharing-requests"],
    ["identity", "/federation/devices?section=identity"],
  ] as const)("devicesRoute(%s) is %s", (section, route) => {
    expect(devicesRoute(section)).toBe(route);
  });
});

describe("the data sync tab", () => {
  const sync = devicesTabs.find((tab) => tab.id === "sync")!;
  const withStatus = (patch?: Partial<DataSyncStatusView>) =>
    ({ dataSyncStatus: patch && syncStatus(patch) }) as unknown as DevicesData;

  it("stands before Advanced", () => {
    expect(devicesTabs.map((tab) => tab.id)).toEqual([
      "device",
      "manage",
      "sharing",
      "sync",
      "advanced",
    ]);
  });

  it("counts decisions, first syncs to review and requests to answer, and nothing else", () => {
    expect(sync.badge!(withStatus())).toBeUndefined();
    expect(sync.badge!(withStatus({ links: 3, readers: 2, linksWaiting: 1 }))).toBeUndefined();
    expect(sync.badge!(withStatus({ openItems: 2, linksToReview: 1, pendingRequests: 1 }))).toEqual(
      { count: 4, countKey: "federation.devices.nav.pendingSync", attention: false },
    );
  });
});

describe("which of this device's addresses to type", () => {
  it.each([
    ["192.168.1.5", "en0", "lan"],
    ["10.0.0.2", "Ethernet", "lan"],
    ["172.20.1.3", "eth0", "lan"],
    ["100.101.1.2", "utun7", "vpn"],
    ["100.64.0.1", "Tailscale", "vpn"],
    ["198.18.0.1", "utun4", "virtual"],
    ["169.254.3.4", "en5", "linkLocal"],
    ["192.168.128.1", "bridge100", "virtual"],
    ["172.17.0.1", "docker0", "virtual"],
    ["172.28.16.1", "vEthernet (WSL)", "virtual"],
    ["192.168.56.1", "VirtualBox Host-Only Network", "virtual"],
    ["10.211.55.2", "vnic0", "virtual"],
    ["10.147.17.3", "ZeroTier One [8056c2e21c000001]", "vpn"],
    ["8.8.8.8", "en0", "unknown"],
  ] as const)("%s on %s is %s", (host, interfaceName, kind) => {
    expect(classifyAddress(host, interfaceName)).toBe(kind);
  });

  it("keeps every distinct port, recommends the first LAN address, and orders the rest", () => {
    const rows = deviceAddresses([
      { url: "http://198.18.0.1:34567", interfaceName: "utun4" },
      { url: "http://198.18.0.1:5000", interfaceName: "utun4" },
      { url: "http://169.254.3.4:34567", interfaceName: "en5" },
      { url: "http://100.101.1.2:34567", interfaceName: "utun7" },
      { url: "http://192.168.1.5:34567", interfaceName: "en0" },
      { url: "http://192.168.1.5:5000", interfaceName: "en0" },
      { url: "http://10.0.0.2:34567", interfaceName: "en1" },
    ]);

    expect(rows.map((row) => [row.url, row.kind, row.recommended])).toEqual([
      ["http://192.168.1.5:34567", "lan", true],
      ["http://192.168.1.5:5000", "lan", false],
      ["http://10.0.0.2:34567", "lan", false],
      ["http://100.101.1.2:34567", "vpn", false],
      ["http://198.18.0.1:34567", "virtual", false],
      ["http://198.18.0.1:5000", "virtual", false],
      ["http://169.254.3.4:34567", "linkLocal", false],
    ]);
  });

  it("deduplicates complete normalized URLs without discarding mapped ports or HTTPS", () => {
    const rows = deviceAddresses([
      {
        url: "https://NAS.EXAMPLE:443/",
        interfaceName: "",
        source: "configured",
        recommended: true,
      },
      { url: "https://nas.example", interfaceName: "", source: "browser", recommended: false },
      { url: "http://nas.example:34567", interfaceName: "", source: "browser", recommended: false },
      {
        url: "https://nas.example:8443",
        interfaceName: "",
        source: "deployment",
        recommended: false,
      },
      {
        url: "http://nas.example:5000",
        interfaceName: "eth0",
        source: "interface",
        recommended: false,
      },
    ]);

    expect(rows.map(({ url, source, recommended }) => [url, source, recommended])).toEqual([
      ["https://nas.example", "configured", true],
      ["http://nas.example:34567", "browser", false],
      ["https://nas.example:8443", "deployment", false],
      ["http://nas.example:5000", "interface", false],
    ]);
  });

  it("takes the server's kinds and recommendation over its own guess", () => {
    // Only the server sees the gateway: an unnamed host-only adapter listed first is not the
    // network a router serves, and the server says which one is.
    const rows = deviceAddresses([
      {
        url: "http://10.37.129.2:34567",
        interfaceName: "en8",
        kind: RemoteAccessAddressKind.Unknown,
        recommended: false,
      },
      {
        url: "http://192.168.1.5:34567",
        interfaceName: "en0",
        kind: RemoteAccessAddressKind.Lan,
        recommended: true,
      },
      {
        url: "http://192.168.1.5:5000",
        interfaceName: "en0",
        kind: RemoteAccessAddressKind.Lan,
        recommended: true,
      },
      {
        url: "http://10.0.0.9:34567",
        interfaceName: "en1",
        kind: RemoteAccessAddressKind.Virtual,
        recommended: false,
      },
    ]);

    expect(rows.map((row) => [row.url, row.kind, row.recommended])).toEqual([
      ["http://192.168.1.5:34567", "lan", true],
      ["http://192.168.1.5:5000", "lan", false],
      ["http://10.37.129.2:34567", "unknown", false],
      ["http://10.0.0.9:34567", "virtual", false],
    ]);
  });

  it("recommends nothing the server did not, even a LAN address", () => {
    const rows = deviceAddresses([
      {
        url: "http://192.168.1.5:34567",
        interfaceName: "en0",
        kind: RemoteAccessAddressKind.Lan,
        recommended: false,
      },
    ]);

    expect(rows.map((row) => row.recommended)).toEqual([false]);
  });

  it("recommends nothing when no address is on a local network", () => {
    const rows = deviceAddresses([{ url: "http://198.18.0.1:34567", interfaceName: "utun4" }]);

    expect(rows.map((row) => row.recommended)).toEqual([false]);
    expect(deviceAddresses([])).toEqual([]);
  });
});
