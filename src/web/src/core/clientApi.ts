import type { ManagedServerState, RemoteDevicePlatform } from "@/sdk/constants";

/**
 * The console's own API, under `/client/`: what a managed server's UI, shown in the desktop
 * app's window through that app's relay, is told about the window it is in.
 *
 * Hand-written rather than generated, because `gen-sdk` builds from the server's swagger
 * and these endpoints are not the server's — they exist only in the relay, answering
 * questions about this machine. Every call here is served on this origin when `clientMode`
 * says PureClient; in any other flavour they simply do not exist.
 *
 * The API grew out of the removed thin client's, which is why the relay still answers that
 * program's connection routes (409 `ManagedByHost`) for a managed server's older UI.
 */

export interface ClientPathMapping {
  serverPath: string;
  localPath: string;
}

export interface ClientKnownServer {
  serverId: string;
  serverName?: string;
  baseAddress: string;
  pairedAt: string;
  lastConnectedAt?: string;
  deviceId: string;
  isActive: boolean;
  pathMappings: ClientPathMapping[];
}

export interface ClientStatus {
  clientVersion: string;
  deviceName: string;
  platform: RemoteDevicePlatform;
  activeServerId?: string;
  serverReachable: boolean;
  /** Route keys the relay can run here, e.g. `GET /tool/open`. */
  implementedUserMachineRoutes: string[];
  servers: ClientKnownServer[];
  /**
   * Which program answers `/client/*`: `"console"` is the desktop app's relay showing a
   * server it manages. Absent from anything else, which this UI then treats as nothing it
   * knows how to drive.
   */
  host?: ClientHostKind;
  /** The name of the device whose window this is (not the server's). */
  localName?: string;
}

/** The value `/client/status` reports in `host`. */
export type ClientHostKind = "console";

/** One place the desktop app's window can show: itself, or a server it manages. */
export interface ClientSwitcherTarget {
  /** `"local"` for the device the window belongs to; otherwise the server's id. */
  id: string;
  name: string;
  isLocal: boolean;
  isCurrent: boolean;
  /**
   * How the server was when last checked — the same states `/federation/local/servers`
   * reports in this device's own window. Absent for `"local"`, and from a desktop app
   * that predates the field; either way the entry reads as not checked.
   */
  state?: ManagedServerState;
}

export interface ClientSwitcher {
  currentId: string;
  /** This device first, then every managed server. */
  targets: ClientSwitcherTarget[];
}

/** The id the console uses for the device whose window this is. */
export const LOCAL_SWITCHER_TARGET = "local";

/**
 * A `/client/*` route that answered with a failure status.
 *
 * The console refuses in the same envelope it answers in — `{ code, message }`, with a
 * stable name as the message (`RelayUnavailable`, `UnknownServer`) — and `reason` keeps
 * that name, so a page can say what went wrong rather than only that something did.
 * Absent when the body said nothing usable.
 */
export class ClientApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly reason?: string,
  ) {
    super(message);
    this.name = "ClientApiError";
  }
}

const call = async <T>(path: string, init?: RequestInit): Promise<T> => {
  const rsp = await fetch(`/client${path}`, {
    ...init,
    headers: init?.body ? { "Content-Type": "application/json", ...init?.headers } : init?.headers,
  });

  if (!rsp.ok) {
    let reason: string | undefined;

    try {
      const refusal = await rsp.json();

      if (typeof refusal?.message === "string" && refusal.message) reason = refusal.message;
    } catch {
      // No body, or not JSON: the status is all there is.
    }

    throw new ClientApiError(
      `${init?.method ?? "GET"} /client${path} failed with ${rsp.status}`,
      rsp.status,
      reason,
    );
  }

  const envelope = await rsp.json();

  return envelope.data as T;
};

const post = <T>(path: string, body?: unknown) =>
  call<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) });

export const clientApi = {
  status: () => call<ClientStatus>("/status"),

  setPathMappings: (serverId: string, mappings: ClientPathMapping[]) =>
    call<{ changed: boolean }>(`/servers/${encodeURIComponent(serverId)}/path-mappings`, {
      method: "PUT",
      body: JSON.stringify({ mappings }),
    }),

  /** Where else this window can go: this device, and every other server it manages. */
  switcher: {
    list: () => call<ClientSwitcher>("/switcher"),
    /**
     * Where the window should navigate to show `id`. For `"local"` that is this device's
     * own origin — the same one its window starts on, so its browser storage is the same.
     */
    open: (id: string, path?: string) =>
      post<{ url: string }>(
        `/switcher/${encodeURIComponent(id)}/open`,
        path === undefined ? {} : { path },
      ),
  },
};
