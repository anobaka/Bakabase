import type { BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo } from "@/sdk/Api";
import type {
  ClientPairingOutcome,
  RemoteDevicePlatform,
  ServerHandshakeOutcome,
  UpdaterStatus,
} from "@/sdk/constants";

/**
 * The thin client's own API, under `/client/`.
 *
 * Hand-written rather than generated, because `gen-sdk` builds from the server's
 * swagger and these endpoints are not the server's — they exist only in the client
 * process, answering questions about this machine. The enums below still come from
 * `constants.ts`: the codegen driver sees both sides and emits them, so the one thing
 * that could silently drift does not.
 *
 * Every call here is served by the forwarding layer on this origin. In any other
 * flavour they simply do not exist, which is why the pages that use them are shown
 * only when `clientMode` says PureClient.
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
  /** Route keys this build can run here, e.g. `GET /tool/open`. */
  implementedUserMachineRoutes: string[];
  servers: ClientKnownServer[];
}

export interface ClientHandshakeResult {
  outcome: ServerHandshakeOutcome;
  detail?: string;
  server?: {
    id: string;
    name: string;
    appVersion: string;
    mode: number;
    pairingSupported: boolean;
  };
}

export interface ClientPairingResult {
  outcome: ClientPairingOutcome;
  serverId?: string;
  serverName?: string;
  detail?: string;
}

export interface ClientPairingTicket {
  outcome: ClientPairingOutcome;
  requestId?: string;
  expiresAt?: string;
}

/**
 * One line the client wrote, or one line plus everything that followed it — a stack
 * trace belongs to the message it came from and arrives folded into it.
 */
export interface ClientLogEntry {
  /** As written, offset included. Absent for a fragment with no head, i.e. a rolled file's first lines. */
  timestamp?: string;
  level?: string;
  source?: string;
  message: string;
}

export interface ClientLogPage {
  /** False in a client that has never written a log — a first launch. */
  available: boolean;
  directory?: string;
  entries: ClientLogEntry[];
}

/**
 * The client's updater state. Shaped by the same C# types the server's updater uses, so
 * the frontend reads both with one set of enums.
 */
export interface ClientUpdaterState {
  status?: UpdaterStatus;
  percentage?: number;
  error?: string;
}

export type ClientVersionInfo =
  BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo;

/** The client's own paths. All absent in a host with no application data directory. */
export interface ClientAppInfo {
  version: string;
  available: boolean;
  dataDirectory?: string;
  logDirectory?: string;
  /** Where an installation of Locale Emulator has to sit for this client to use it. */
  componentsDirectory?: string;
}

const call = async <T>(path: string, init?: RequestInit): Promise<T> => {
  const rsp = await fetch(`/client${path}`, {
    ...init,
    headers: init?.body ? { "Content-Type": "application/json", ...init?.headers } : init?.headers,
  });

  if (!rsp.ok) {
    throw new Error(`${init?.method ?? "GET"} /client${path} failed with ${rsp.status}`);
  }

  const envelope = await rsp.json();

  return envelope.data as T;
};

const post = <T>(path: string, body?: unknown) =>
  call<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) });

export const clientApi = {
  status: () => call<ClientStatus>("/status"),
  /** Deliberately untyped until the strict migration whitelist validates it. */
  migrationHints: () => call<unknown>("/migration-hints"),
  /** A native save dialog is optional; older/headless clients keep browser downloads. */
  exportMigrationHints: async (): Promise<{ outcome: "saved" | "cancelled" | "unavailable" }> => {
    const response = await fetch("/client/migration-hints/export", { method: "POST" });

    if (response.status === 404) return { outcome: "unavailable" };
    if (!response.ok) throw new Error(`Connection hint export failed with ${response.status}`);
    const envelope = await response.json();
    const outcome = envelope?.data?.outcome;

    if (envelope.code !== 0 || !["saved", "cancelled", "unavailable"].includes(outcome))
      throw new Error("The client returned an invalid export result.");

    return { outcome };
  },

  /** Asks an address what it is. Never throws for an unreachable server — that is an answer. */
  connect: (address: string) => post<ClientHandshakeResult>("/connect", { address }),

  pairWithCode: (address: string, code: string) =>
    post<ClientPairingResult>("/pair/code", { address, code }),

  /** Files a request for an already-paired device to approve. */
  requestPairing: (address: string) => post<ClientPairingTicket>("/pair/request", { address }),

  /** Collects the credentials an approval produced. Answers "not yet" until somebody acts. */
  claimPairing: (address: string, requestId: string) =>
    post<ClientPairingResult>("/pair/claim", { address, requestId }),

  activateServer: (serverId: string) =>
    post<{ changed: boolean }>(`/servers/${encodeURIComponent(serverId)}/activate`),

  forgetServer: (serverId: string) =>
    call<{ changed: boolean }>(`/servers/${encodeURIComponent(serverId)}`, { method: "DELETE" }),

  setPathMappings: (serverId: string, mappings: ClientPathMapping[]) =>
    call<{ changed: boolean }>(`/servers/${encodeURIComponent(serverId)}/path-mappings`, {
      method: "PUT",
      body: JSON.stringify({ mappings }),
    }),

  /**
   * The client's own log, newest first.
   *
   * A tail rather than a query: the client keeps no log database, so there is nothing
   * to page through, and `take` is capped server-side.
   */
  log: (query: { take?: number; level?: string; contains?: string } = {}) => {
    const search = new URLSearchParams();

    if (query.take) search.set("take", String(query.take));
    if (query.level) search.set("level", query.level);
    if (query.contains) search.set("contains", query.contains);

    const suffix = search.toString();

    return call<ClientLogPage>(`/log${suffix ? `?${suffix}` : ""}`);
  },

  /**
   * Where this client keeps its own things. `BApi.app.getAppInfo` is forwarded and
   * describes the server, whose paths belong to another machine and cannot be opened
   * from here.
   */
  appInfo: () => call<ClientAppInfo>("/app/info"),

  /**
   * Reveals one of this client's own directories, named rather than pathed: a page that
   * could name a path could name any path, so the client resolves the name itself.
   */
  openDirectory: (directory: "data" | "log" | "components") =>
    post<{ opened: boolean }>(`/app/open?directory=${directory}`),

  /**
   * Shows the client's log directory in this machine's file manager.
   *
   * Takes no path, and is not `/tool/open`: that route translates the server path it is
   * given, which for a directory this process owns on this disk means refusing it.
   */
  openLogDirectory: () => post<{ opened: boolean }>("/log/open"),

  /**
   * Tells this machine's tray icon whether the server is working.
   *
   * In the all-in-one the task manager sets the icon directly — same process. Here the
   * tasks are on the server and the tray is on this desk, and the window is already
   * holding the server's live task feed, so it reports what it sees rather than having
   * the client open a second connection to learn the same thing.
   */
  setTrayRunning: (running: boolean) => post<{ applied: boolean }>("/tray", { running }),

  /**
   * The client updating itself. Separate from `BApi.updater`, which is forwarded and
   * therefore updates the server — two products in one window.
   */
  updater: {
    state: () => call<ClientUpdaterState>("/updater/state"),
    newVersion: () => call<ClientVersionInfo>("/updater/new-version"),
    start: () => post<ClientUpdaterState>("/updater/update"),
    restart: () => post<ClientUpdaterState>("/updater/restart"),
  },
};
