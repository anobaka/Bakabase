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

/** Managed servers the reader should look at: revoked, or another device at their address. */
export const troubledServers = (data: DevicesData) =>
  (data.servers?.servers ?? []).filter(
    (server) =>
      server.state === ManagedServerState.Revoked ||
      server.state === ManagedServerState.WrongServer,
  );
