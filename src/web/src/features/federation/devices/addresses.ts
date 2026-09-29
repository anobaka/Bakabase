import type { BakabaseServiceModelsViewRemoteAccessAddressViewModel as RemoteAccessAddress } from "@/sdk/Api";

import { RemoteAccessAddressKind } from "@/sdk/constants";

/*
 * Which of this device's addresses another device should type.
 *
 * The server lists every listening port on every interface that is up — a dozen addresses
 * on a computer with a VPN, a proxy's TUN adapter and a VM bridge. Every one stays in the
 * API (the device map needs all of this device's hosts to recognise it), but a person needs
 * one: the recommended address first, the other networks after it, and the adapters another
 * device cannot reach folded away and labelled.
 *
 * Every port reaches the same instance, so one row per host is enough, on the main port —
 * the first one the server lists, which is the port its discovery beacon advertises.
 *
 * The server says which kind of network each address is on and which one to recommend: only
 * it can see which interface has a default gateway, the mark of the network a router serves
 * that a VM's host-only adapter or an overlay lacks (`RemoteAccessAddressClassifier`). The
 * guess below, from the address and the interface's name alone, is for a server too old to say.
 */

export type AddressKind = "lan" | "vpn" | "virtual" | "linkLocal" | "unknown";

export interface DeviceAddress {
  url: string;
  host: string;
  interfaceName: string;
  kind: AddressKind;
  recommended: boolean;
}

/** Interface names of overlay networks and VPN clients (the server's list, by name only). */
const VPN_INTERFACE =
  /zerotier|^zt[0-9a-z]|^feth\d|wireguard|^wg\d|tailscale|openvpn|nordlynx|^ppp\d/i;

/** Interface names of bridges, container networks, hypervisors and proxy TUN adapters. */
const VIRTUAL_INTERFACE =
  /bridge|docker|^br-|veth|vethernet|vmnet|virbr|^vnic|^utun|^tun|^tap|hyper-v|virtualbox|vmware|parallels|host-only|wintun|clash/i;

const octets = (host: string) => {
  const parts = host.split(".");

  if (parts.length !== 4) return undefined;
  const numbers = parts.map((part) => (/^\d{1,3}$/.test(part) ? Number(part) : NaN));

  return numbers.every((value) => value >= 0 && value <= 255) ? numbers : undefined;
};

/**
 * What kind of network an IPv4 address is on, from the address and the interface's name.
 * Address ranges that say it outright come first: a Tailscale address is a VPN whatever its
 * adapter is called (`utun` on macOS), a proxy's benchmark range is never another device.
 */
export function classifyAddress(host: string, interfaceName = ""): AddressKind {
  const ip = octets(host);

  if (ip) {
    const [a, b] = ip;

    if (a === 169 && b === 254) return "linkLocal";
    if (a === 198 && (b === 18 || b === 19)) return "virtual";
    if (a === 100 && b >= 64 && b <= 127) return "vpn";
  }
  if (VPN_INTERFACE.test(interfaceName.trim())) return "vpn";
  if (VIRTUAL_INTERFACE.test(interfaceName.trim())) return "virtual";
  if (ip) {
    const [a, b] = ip;

    if (a === 10 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168)) return "lan";
  }

  return "unknown";
}

/** The server's words for the same kinds. */
const serverKind: Record<RemoteAccessAddressKind, AddressKind> = {
  [RemoteAccessAddressKind.Unknown]: "unknown",
  [RemoteAccessAddressKind.Lan]: "lan",
  [RemoteAccessAddressKind.Vpn]: "vpn",
  [RemoteAccessAddressKind.Virtual]: "virtual",
  [RemoteAccessAddressKind.LinkLocal]: "linkLocal",
};

/** The kind the server reported, else the guess; a value this page does not know is a guess too. */
const kindOf = (address: RemoteAccessAddress, host: string): AddressKind =>
  (address.kind != null ? serverKind[address.kind] : undefined) ??
  classifyAddress(host, address.interfaceName);

const kindOrder: Record<AddressKind, number> = {
  lan: 0,
  vpn: 1,
  unknown: 2,
  virtual: 3,
  linkLocal: 4,
};

const hostAndPort = (url: string) => {
  try {
    const parsed = new URL(url);

    return { host: parsed.hostname, port: parsed.port };
  } catch {
    return { host: url, port: "" };
  }
};

/**
 * One row per host on the main port, the recommended one first, then LAN, VPN, unknown,
 * virtual and link-local. The server's recommendation stands when it gives one (nothing
 * recommended included); from an older server, the first LAN address is, and nothing is when
 * there is none.
 */
export function deviceAddresses(addresses: readonly RemoteAccessAddress[]): DeviceAddress[] {
  if (!addresses.length) return [];
  const serverRecommends = addresses.some((address) => address.recommended != null);
  const recommendedHosts = new Set(
    addresses
      .filter((address) => address.recommended === true)
      .map((address) => hostAndPort(address.url).host),
  );
  const mainPort = hostAndPort(addresses[0].url).port;
  const byHost = new Map<string, DeviceAddress>();

  for (const address of addresses) {
    const { host, port } = hostAndPort(address.url);

    if (byHost.has(host) || port !== mainPort) continue;
    byHost.set(host, {
      url: address.url,
      host,
      interfaceName: address.interfaceName,
      kind: kindOf(address, host),
      recommended: false,
    });
  }
  // A host listed only on another port still gets a row, rather than vanishing.
  for (const address of addresses) {
    const { host } = hostAndPort(address.url);

    if (!byHost.has(host))
      byHost.set(host, {
        url: address.url,
        host,
        interfaceName: address.interfaceName,
        kind: kindOf(address, host),
        recommended: false,
      });
  }
  const rows = [...byHost.values()];
  const recommended = serverRecommends
    ? rows.find((row) => recommendedHosts.has(row.host))
    : rows.find((row) => row.kind === "lan");

  if (recommended) recommended.recommended = true;

  return rows
    .map((row, index) => ({ row, index }))
    .sort(
      (x, y) =>
        Number(y.row.recommended) - Number(x.row.recommended) ||
        kindOrder[x.row.kind] - kindOrder[y.row.kind] ||
        x.index - y.index,
    )
    .map(({ row }) => row);
}

/** Addresses another device can usually reach; the rest are folded away. */
export const isReachableKind = (kind: AddressKind) => kind !== "virtual" && kind !== "linkLocal";
