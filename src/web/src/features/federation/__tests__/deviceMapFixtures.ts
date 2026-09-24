import type {
  BakabaseServiceModelsViewRemoteAccessDeviceViewModel as PairedDevice,
  BakabaseServiceModelsViewRemoteAccessPendingRequestViewModel as ManagementRequest,
  BakabaseServiceModelsViewRemoteAccessSettingsViewModel as RemoteAccessSettings,
} from "@/sdk/Api";
import type {
  FederationStatus,
  ManagedServer,
  ManagedServerPendingRequest,
  ManagedServersView,
  PairingRequest,
  Peer,
} from "../types";

import {
  ManagedServerOutcome,
  ManagedServerState,
  RemoteAccessMode,
  RemoteDevicePlatform,
} from "@/sdk/constants";

/** Builders for the three listings the device map is drawn from. */

export const inTenMinutes = () => new Date(Date.now() + 10 * 60_000).toISOString();
export const aMinuteAgo = () => new Date(Date.now() - 60_000).toISOString();

export const grant = (grantId: string) => ({ grantId, revision: 1 });

export const peer = (nodeId: string, overrides: Partial<Peer> = {}): Peer => ({
  nodeId,
  label: nodeId.toUpperCase(),
  address: `http://192.168.1.${nodeId.length + 10}:34567`,
  enabled: true,
  connectionState: "Online",
  pathMappings: [],
  ...overrides,
});

export const sharingRequest = (
  nodeId: string,
  direction: "incoming" | "outgoing",
  overrides: Partial<PairingRequest> = {},
): PairingRequest => ({
  requestId: `${direction}-${nodeId}`,
  nodeId,
  nodeName: nodeId.toUpperCase(),
  direction,
  status: "awaitingApproval",
  expiresAt: inTenMinutes(),
  replacesExistingAccess: false,
  offersReciprocalAccess: false,
  ...overrides,
});

export const status = (overrides: Partial<FederationStatus> = {}): FederationStatus => ({
  identity: { nodeId: "self-node", libraryEpoch: "epoch", name: "Studio PC" },
  sharingEnabled: true,
  remoteAccessMode: RemoteAccessMode.Enabled,
  requirePairing: true,
  peers: [],
  requests: [],
  browsingEnabled: true,
  reachableAddresses: ["http://192.168.1.2:34567"],
  ...overrides,
});

export const server = (
  serverId: string,
  overrides: Partial<ManagedServer> = {},
): ManagedServer => ({
  serverId,
  name: serverId.toUpperCase(),
  address: `http://192.168.1.${serverId.length + 50}:34567`,
  pairedAt: "2026-09-01T00:00:00Z",
  pathMappings: [],
  state: ManagedServerState.Online,
  mode: RemoteAccessMode.Enabled,
  appVersion: "2.4.0",
  importedFromLegacyClient: false,
  ...overrides,
});

export const managementRequestOut = (
  requestId: string,
  overrides: Partial<ManagedServerPendingRequest> = {},
): ManagedServerPendingRequest => ({
  requestId,
  address: "http://192.168.1.90:34567",
  serverName: "Attic NAS",
  expiresAt: inTenMinutes(),
  outcome: ManagedServerOutcome.AwaitingApproval,
  active: true,
  ...overrides,
});

export const servers = (overrides: Partial<ManagedServersView> = {}): ManagedServersView => ({
  available: true,
  servers: [],
  requests: [],
  ...overrides,
});

export const manager = (
  id: string,
  name: string,
  overrides: Partial<PairedDevice> = {},
): PairedDevice => ({
  id,
  name,
  platform: RemoteDevicePlatform.Windows,
  createdAt: "2026-09-01T00:00:00Z",
  lastSeenAt: "2026-09-20T00:00:00Z",
  ...overrides,
});

export const managementRequestIn = (
  id: string,
  deviceName: string,
  overrides: Partial<ManagementRequest> = {},
): ManagementRequest => ({
  id,
  deviceName,
  platform: RemoteDevicePlatform.MacOS,
  remoteAddress: "192.168.1.77",
  requestedAt: "2026-09-24T00:00:00Z",
  expiresAt: inTenMinutes(),
  ...overrides,
});

export const access = (overrides: Partial<RemoteAccessSettings> = {}): RemoteAccessSettings => ({
  mode: RemoteAccessMode.Enabled,
  addresses: [],
  allowLiveTranscode: false,
  requirePairing: true,
  devices: [],
  pendingRequests: [],
  ...overrides,
});

/** Fisher–Yates with a fixed seed, so "any order" is still one reproducible order. */
export const shuffled = <T>(items: T[], seed = 7): T[] => {
  const copy = [...items];
  let state = seed;

  for (let index = copy.length - 1; index > 0; index--) {
    state = (state * 16807) % 2147483647;
    const other = state % (index + 1);

    [copy[index], copy[other]] = [copy[other], copy[index]];
  }

  return copy;
};
