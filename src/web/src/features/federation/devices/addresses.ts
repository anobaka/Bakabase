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
 * Addresses are deduplicated by their full normalized URL. A mapped port or HTTPS
 * reverse proxy on the same host remains a distinct way to reach this instance.
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
  source?: string;
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

const sourceOrder: Record<string, number> = {
  configured: 0,
  browser: 1,
  deployment: 2,
  interface: 3,
};

/** Keep distinct schemes and ports; a recommendation belongs to a URL, not an entire host. */
export function deviceAddresses(addresses: readonly RemoteAccessAddress[]): DeviceAddress[] {
  const serverRecommends = addresses.some((address) => address.recommended != null);
  const byUrl = new Map<string, DeviceAddress>();

  for (const address of addresses) {
    let url: URL;

    try {
      url = new URL(address.url);
    } catch {
      continue;
    }
    const normalized = url.href.replace(/\/$/, "");
    const existing = byUrl.get(normalized);

    if (existing) {
      existing.recommended ||= address.recommended === true;
      continue;
    }
    byUrl.set(normalized, {
      url: normalized,
      host: url.hostname,
      interfaceName: address.interfaceName,
      kind: kindOf(address, url.hostname),
      recommended: address.recommended === true,
      source: address.source,
    });
  }
  const rows = [...byUrl.values()];
  const recommended = serverRecommends
    ? rows.find((row) => row.recommended)
    : rows.find((row) => row.kind === "lan");

  for (const row of rows) row.recommended = row === recommended;

  return rows
    .map((row, index) => ({ row, index }))
    .sort(
      (x, y) =>
        Number(y.row.recommended) - Number(x.row.recommended) ||
        (sourceOrder[x.row.source ?? "interface"] ?? 3) -
          (sourceOrder[y.row.source ?? "interface"] ?? 3) ||
        kindOrder[x.row.kind] - kindOrder[y.row.kind] ||
        x.index - y.index,
    )
    .map(({ row }) => row);
}

/** Addresses another device can usually reach; the rest are folded away. */
export const isReachableKind = (kind: AddressKind) => kind !== "virtual" && kind !== "linkLocal";
