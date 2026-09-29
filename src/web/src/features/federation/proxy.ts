import { ManagedServerOutcome, ManagedServerOutcomeLabel } from "@/sdk/constants";

/*
 * What to say when a proxy on this computer is in the way of a device's address (Clash and the
 * like in fake-IP or TUN mode, answering names with 198.18.x.x addresses of their own).
 *
 * Where the address names the device by a `.local` name, a computer name or an IP address, the
 * proxy has no way to it and nothing is sent: the fix is `.local` names and LAN addresses set to
 * DIRECT. A domain the proxy resolves itself is connected to through it, and only said to be
 * behind it when that fails: the fix is then that domain. The server tells the two apart the same
 * way (`ProxyFakeAddresses.ProxyResolvesItself`).
 */

/** The host of an address as the devices pages show it: `http://host:port`, `host:port` or `host`. */
const hostOf = (address: string): string => {
  const authority = address
    .trim()
    .replace(/^[a-z][a-z0-9+.-]*:\/\//i, "")
    .split(/[/?#]/, 1)[0]!;
  const host = authority.slice(authority.lastIndexOf("@") + 1);

  if (host.startsWith("[")) return host.slice(1, Math.max(host.indexOf("]"), 1));

  // One colon is a port; more is an IPv6 address written bare.
  return host.split(":").length === 2 ? host.slice(0, host.indexOf(":")) : host;
};

/** Whether `address` names its device by a domain a proxy resolves itself. */
export const proxyResolvesItself = (address?: string | null): boolean => {
  if (!address) return false;

  const host = hostOf(address).replace(/\.+$/, "").toLowerCase();

  return (
    host.includes(".") &&
    !host.includes(":") &&
    !/^\d{1,3}(\.\d{1,3}){3}$/.test(host) &&
    !host.endsWith(".local")
  );
};

/** What a peer whose `connectionState` is `ProxyFakeAddress` is said to be, by its address. */
export const proxyFakeAddressKey = (address?: string | null) =>
  proxyResolvesItself(address)
    ? "federation.error.ProxyFakeAddressDomain"
    : "federation.error.ProxyFakeAddress";

/**
 * The error code a managed-server outcome is shown by (`federation.error.{code}`): for a proxy in
 * the way of a domain, its own wording.
 */
export const managedServerOutcomeCode = (outcome: ManagedServerOutcome, address?: string | null) =>
  `ManagedServer${ManagedServerOutcomeLabel[outcome] ?? outcome}${
    outcome === ManagedServerOutcome.ProxyFakeAddress && proxyResolvesItself(address)
      ? "Domain"
      : ""
  }`;
