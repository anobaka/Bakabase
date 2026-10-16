import type { ManagedPathMappingSnapshot } from "./components/ManagedServerPathMappings";
import type { ManagedServerPathMapping } from "./types";
import type { ManagedMappingTreeLoader } from "./components/ManagedMappingTree";

import { managedServerApi } from "./serverApi";
import { FederationError } from "./transport";

import { ClientApiError, clientApi } from "@/core/clientApi";

/** The connection whose mappings are edited, never an address inferred from a resource path. */
export type ManagedPathMappingTarget = { serverId: string; kind: "manager" | "relay" };

export const PATH_MAPPINGS_SAVED_EVENT = "bakabase:path-mappings-saved";

export function notifyPathMappingsSaved(target: ManagedPathMappingTarget) {
  window.dispatchEvent(new CustomEvent(PATH_MAPPINGS_SAVED_EVENT, { detail: target }));
}

const mappingError = (error: unknown) =>
  error instanceof ClientApiError && error.reason
    ? new FederationError(error.reason, "", error.status)
    : error;

export async function loadManagedPathMappingTarget(
  target: ManagedPathMappingTarget,
  signal?: AbortSignal,
): Promise<ManagedPathMappingSnapshot> {
  if (target.kind === "relay") {
    const status = await clientApi.status(signal).catch((error) => {
      throw mappingError(error);
    });
    const server = status.servers.find(
      (server) => server.serverId === target.serverId && server.isActive,
    );

    if (status.activeServerId !== target.serverId || !server)
      throw new FederationError("UnknownServer", "", 404);

    return {
      serverId: server.serverId,
      name: server.serverName,
      pairedAt: server.pairedAt,
      pathMappings: server.pathMappings,
    };
  }

  const view = await managedServerApi.list(false, signal);
  const server = view.servers.find((server) => server.serverId === target.serverId);

  if (!server) throw new FederationError("UnknownServer", "", 404);

  return server;
}

const loadRelayTree: ManagedMappingTreeLoader = async (serverId, options, signal) => {
  const page = await clientApi.mappingTree(serverId, options, signal).catch((error) => {
    throw mappingError(error);
  });

  return { ...page, nextCursor: page.nextCursor ?? undefined };
};

export const mappingTreeLoader = (target: ManagedPathMappingTarget): ManagedMappingTreeLoader =>
  target.kind === "relay" ? loadRelayTree : managedServerApi.mappingTree;

export async function saveManagedPathMappings(
  target: ManagedPathMappingTarget,
  mappings: ManagedServerPathMapping[],
  expectedMappings: ManagedServerPathMapping[],
) {
  try {
    const result =
      target.kind === "relay"
        ? await clientApi.setPathMappings(target.serverId, mappings, expectedMappings)
        : await managedServerApi.setPathMappings(target.serverId, mappings, expectedMappings);

    // A connection removed after loading the form must not look like a successful save:
    // the user still needs their draft if they pair that device again.
    if (!result.changed) throw new FederationError("UnknownServer", "", 404);

    return result;
  } catch (error) {
    // Both transports use the same editor and localised conflict explanation.
    throw mappingError(error);
  }
}
