import { managedServerApi } from "./serverApi";
import { FederationError } from "./transport";

import { ClientApiError, clientApi, LOCAL_SWITCHER_TARGET } from "@/core/clientApi";

/** Where "Manage devices…" lands, on the device the window belongs to. */
export const DEVICES_ROUTE = "/federation/devices";

/**
 * Sections of the devices page a link can land on with `?section=`. `management` is where
 * a "wants to manage this device" notification leads (the Service links it by that name);
 * `servers` is where the window's server switcher does.
 */
export type DevicesSection = "identity" | "servers" | "management";

/** The devices page, optionally brought to one of its sections. */
export const devicesRoute = (section?: DevicesSection) =>
  section ? `${DEVICES_ROUTE}?section=${section}` : DEVICES_ROUTE;

/**
 * A server's Configuration page, whose Remote access section holds "Access from other
 * devices" and "Only serve paired devices".
 *
 * The place to change who may manage a server shown in this window: its devices page is
 * this device's own (`localNodeOnly`, hidden from the menu while the window shows another
 * server), but the configuration page is the server's, in whatever version it runs.
 */
export const CONFIGURATION_ROUTE = "/configuration";

/**
 * Puts an SPA route on a URL the server handed back.
 *
 * The SPA routes on the hash, so the route is appended here rather than sent to the
 * server as `path`: a path would land on the server as a request path, and the relay
 * appends its one-time navigation token as a query *after* it — both of which need the
 * route outside the hash.
 *
 * Works for either kind of destination. This device's own origin carries no ticket, so the
 * fragment simply stays. A relay spends its ticket on a small landing page that moves on
 * with `location.replace` to the same address without the ticket, and that page appends
 * `location.hash` itself — a script navigation does not carry the fragment over the way a
 * redirect's `Location` would, which is why the page has to. The fragment never reaches
 * the server either way: browsers do not send it.
 */
export const withRoute = (url: string, route?: string) => {
  if (!route) return url;
  const base = url.split("#")[0];

  return `${base}#${route.startsWith("/") ? route : `/${route}`}`;
};

/**
 * Sends the whole window elsewhere — a different origin, not a route change, since each
 * server's UI is its own bundle behind its own relay port.
 *
 * Only http(s): the URL comes from this device's own app, but a navigation is the one
 * place a malformed answer would turn into script, so it is checked rather than trusted.
 */
export const navigateTo = (url: string) => {
  const target = new URL(url, window.location.href);

  if (target.protocol !== "http:" && target.protocol !== "https:") {
    throw new Error(`Refusing to navigate to ${target.protocol} URL`);
  }
  window.location.assign(url);
};

/**
 * From this device's own window: show a server it manages, optionally landing on one of
 * its routes (see {@link withRoute}).
 */
export async function openManagedServer(serverId: string, route?: string) {
  const { url } = await managedServerApi.open(serverId);

  navigateTo(withRoute(url, route));
}

/**
 * The console refuses in its own envelope. Said as a {@link FederationError} so a switch
 * that failed inside the console reads like one that failed from this device's own window:
 * `RelayUnavailable` is the same condition from either side, and both get the same words.
 */
const asFederationError = (cause: unknown) =>
  cause instanceof ClientApiError && cause.reason
    ? new FederationError(cause.reason, cause.message, cause.status, cause.status === 503)
    : cause;

/**
 * From inside the console: show another target, or this device itself with `"local"`,
 * optionally landing on one of its routes (see {@link withRoute}).
 */
export async function openConsoleTarget(id: string, route?: string) {
  let url: string;

  try {
    ({ url } = await clientApi.switcher.open(id));
  } catch (cause) {
    throw asFederationError(cause);
  }

  navigateTo(withRoute(url, route));
}

/** From inside the console: back to this device's own window, e.g. its devices page. */
export const openLocalView = (route?: string) => openConsoleTarget(LOCAL_SWITCHER_TARGET, route);
