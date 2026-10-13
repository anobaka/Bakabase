import type { PathMapping, Peer } from "../types";

import envConfig from "@/config/env";

export type MappingDraft = {
  version: 1;
  mappings: PathMapping[];
  grantId?: string;
  grantRevision?: number;
};
export const mappingDraftKey = (nodeId: string) =>
  `bakabase:federation:mapping-draft:v1:${encodeURIComponent(new URL(envConfig.apiEndpoint || window.location.origin, window.location.origin).href)}:${encodeURIComponent(nodeId)}`;
export const readMappingDraft = (nodeId: string): MappingDraft | undefined => {
  try {
    const draft = JSON.parse(localStorage.getItem(mappingDraftKey(nodeId)) || "null");

    if (
      draft?.version !== 1 ||
      !Array.isArray(draft.mappings) ||
      draft.mappings.length > 5000 ||
      !draft.mappings.every(
        (row: any) => typeof row?.sourceRootId === "string" && typeof row?.localPath === "string",
      )
    )
      return;

    return {
      version: 1,
      mappings: draft.mappings.map((row: PathMapping) => ({
        sourceRootId: row.sourceRootId,
        localPath: row.localPath,
      })),
      grantId: typeof draft.grantId === "string" ? draft.grantId : undefined,
      grantRevision: typeof draft.grantRevision === "number" ? draft.grantRevision : undefined,
    };
  } catch {
    return undefined;
  }
};
export const draftSharingChanged = (draft: MappingDraft, peer: Peer) =>
  draft.grantId !== peer.outboundGrant?.grantId ||
  draft.grantRevision !== peer.outboundGrant?.revision;
export const writeMappingDraft = (peer: Peer, mappings: PathMapping[]) =>
  localStorage.setItem(
    mappingDraftKey(peer.nodeId),
    JSON.stringify({
      version: 1,
      mappings: mappings.map(({ sourceRootId, localPath }) => ({ sourceRootId, localPath })),
      grantId: peer.outboundGrant?.grantId,
      grantRevision: peer.outboundGrant?.revision,
    }),
  );
export const clearMappingDraft = (nodeId: string) =>
  localStorage.removeItem(mappingDraftKey(nodeId));
