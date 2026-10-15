import type { ManagedServer, ManagedServerPathMapping } from "../types";

import envConfig from "@/config/env";

export type ManagedMappingDraft = {
  version: 1;
  pairedAt: string;
  mappings: ManagedServerPathMapping[];
  baseMappings: ManagedServerPathMapping[];
};
export const mappingDraftKey = (serverId: string) =>
  `bakabase:management:mapping-draft:v1:${encodeURIComponent(new URL(envConfig.apiEndpoint || window.location.origin, window.location.origin).href)}:${encodeURIComponent(serverId)}`;

const validRows = (rows: unknown): rows is ManagedServerPathMapping[] =>
  Array.isArray(rows) &&
  rows.length <= 5000 &&
  rows.every(
    (row) =>
      row &&
      typeof row.serverPath === "string" &&
      row.serverPath.length <= 4096 &&
      typeof row.localPath === "string" &&
      row.localPath.length <= 4096,
  );
const copyRows = (rows: ManagedServerPathMapping[]) =>
  rows.map(({ serverPath, localPath }) => ({ serverPath, localPath }));

export function readMappingDraft(serverId: string): ManagedMappingDraft | undefined {
  try {
    const raw = localStorage.getItem(mappingDraftKey(serverId));

    if (!raw || raw.length > 2 * 1024 * 1024) return;
    const draft = JSON.parse(raw);

    if (
      draft?.version !== 1 ||
      typeof draft.pairedAt !== "string" ||
      !validRows(draft.mappings) ||
      !validRows(draft.baseMappings)
    )
      return;

    return {
      version: 1,
      pairedAt: draft.pairedAt,
      mappings: copyRows(draft.mappings),
      baseMappings: copyRows(draft.baseMappings),
    };
  } catch {
    return;
  }
}

export function writeMappingDraft(
  server: ManagedServer,
  mappings: ManagedServerPathMapping[],
  baseMappings: ManagedServerPathMapping[],
) {
  const draft: ManagedMappingDraft = {
    version: 1,
    pairedAt: server.pairedAt,
    mappings: copyRows(mappings),
    baseMappings: copyRows(baseMappings),
  };
  const raw = JSON.stringify(draft);

  if (!validRows(mappings) || !validRows(baseMappings) || raw.length > 2 * 1024 * 1024)
    throw new Error("Mapping draft exceeds the browser storage budget");
  localStorage.setItem(mappingDraftKey(server.serverId), raw);
}

export const clearMappingDraft = (serverId: string) =>
  localStorage.removeItem(mappingDraftKey(serverId));
export const sameMappings = (a: ManagedServerPathMapping[], b: ManagedServerPathMapping[]) =>
  JSON.stringify(a) === JSON.stringify(b);
