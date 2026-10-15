import type { FederationStatus } from "./types";

import { federationRequest, jsonBody } from "./transport";
import { notifyFederationChanged } from "./statusEvents";

const prefix = "/peers";

/** Installation identity used by management and definitions synchronization. */
export const federationPeerApi = {
  status: (signal?: AbortSignal) => federationRequest<FederationStatus>(prefix, { signal }),
  resetIdentity: async (asNewNode: boolean, replaceInstallIdentity = false) => {
    const result = await federationRequest<unknown>(
      `${prefix}/identity/reset`,
      jsonBody({ asNewNode, replaceInstallIdentity }),
    );

    notifyFederationChanged("reset");

    return result;
  },
  /** The name other devices see; null restores this computer's name. */
  setName: (name: string | null, expectedName?: string) =>
    federationRequest<unknown>(`${prefix}/name`, jsonBody({ name, expectedName }, "PUT")),
};
