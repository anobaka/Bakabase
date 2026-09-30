import type { DevicesData } from "./context";

import { ManagedServerState } from "@/sdk/constants";
import { millisecondsUntil } from "@/core/serverTime";

/** Whether a source has answered, either way. */
export function loaded(data: DevicesData, source: "sharing" | "servers" | "access") {
  switch (source) {
    case "sharing":
      return !!data.status || !!data.sharingError;
    case "servers":
      return !!data.servers || !!data.serversError;
    case "access":
      return !!data.access || !!data.accessError;
  }
}

/** Incoming library requests still waiting for this device's answer. */
export const waitingSharingRequests = (data: DevicesData) =>
  (data.status?.requests ?? []).filter(
    (request) =>
      request.direction === "incoming" &&
      request.status === "awaitingApproval" &&
      millisecondsUntil(request.expiresAt) > 0,
  );

/**
 * What data sync waits on this device for: decisions under "Needs you", first syncs ready to
 * review, and requests from other devices to answer — as the status the hub keeps current says.
 */
export const dataSyncWaiting = (data: DevicesData) => {
  const status = data.dataSyncStatus;

  return status ? status.openItems + status.linksToReview + status.pendingRequests : 0;
};

/** Managed servers the reader should look at: revoked, or another device at their address. */
export const troubledServers = (data: DevicesData) =>
  (data.servers?.servers ?? []).filter(
    (server) =>
      server.state === ManagedServerState.Revoked ||
      server.state === ManagedServerState.WrongServer,
  );
