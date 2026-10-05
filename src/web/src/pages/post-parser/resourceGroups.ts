import type { DownloadContentGroup, DownloadInfoData, DownloadResource } from "./results";
import type { DownloadResourceGroup } from "./resourceDeduplication";

import { groupDownloadResources } from "./resourceDeduplication";

export interface DownloadContentSection {
  key: string;
  group?: DownloadContentGroup;
  resources: DownloadResourceGroup[];
}

const kindOrder = ["main", "preview", "supplement", "related", "tool", "unknown"] as const;
const text = (value: unknown, limit: number) =>
  typeof value === "string" ? value.trim().slice(0, limit) : "";
const groupId = (value: unknown) => {
  if (typeof value !== "string") return undefined;
  const id = value.trim();

  return id && id.length <= 80 ? id : undefined;
};

/** Group IDs are opaque and local to a result; domains and URL similarity never imply shared content. */
export function normalizeDownloadContentGroups(
  value: unknown,
  resources: readonly DownloadResource[],
): DownloadContentGroup[] {
  if (!Array.isArray(value)) return [];
  const records = value.filter(
    (item): item is Record<string, unknown> =>
      item != null && typeof item === "object" && !Array.isArray(item),
  );
  const counts = new Map<string, number>();

  for (const record of records) {
    const id = groupId(record.id);

    if (id) counts.set(id, (counts.get(id) ?? 0) + 1);
  }
  const referenced = new Set(resources.map((resource) => groupId(resource.groupId)));
  const groups: DownloadContentGroup[] = [];

  for (const record of records) {
    const id = groupId(record.id);
    const title = text(record.title, 160);

    if (!id || counts.get(id) !== 1 || !title || !referenced.has(id)) continue;
    const kind = text(record.kind, 80).toLowerCase();

    groups.push({
      ...record,
      id,
      title,
      kind: kindOrder.includes(kind as DownloadContentGroup["kind"])
        ? (kind as DownloadContentGroup["kind"])
        : "unknown",
      summary: text(record.summary, 500) || undefined,
      evidence: Array.isArray(record.evidence)
        ? [...new Set(record.evidence.map((item) => text(item, 300)).filter(Boolean))].slice(0, 8)
        : [],
    });
    if (groups.length === 128) break;
  }

  return groups;
}

export function resolveDownloadGroupId(value: unknown, groups: readonly DownloadContentGroup[]) {
  const id = groupId(value);

  return groups.some((group) => group.id === id) ? id : undefined;
}

/** Display ordering must not change the original resource indices used by subsequent actions. */
export function getDownloadContentGroups(data: DownloadInfoData): DownloadContentSection[] {
  const resources = Array.isArray(data.resources) ? data.resources : [];
  const groups = normalizeDownloadContentGroups(data.groups, resources);
  const sections = new Map<string, DownloadContentSection>();
  const merged = groupDownloadResources(
    resources.map((resource) => ({
      ...resource,
      groupId: resolveDownloadGroupId(resource.groupId, groups),
    })),
  );

  for (const resource of merged) {
    const group = groups.find((item) => item.id === resource.resource.groupId);
    const key = group ? `group:${group.id}` : "ungrouped";
    const section = sections.get(key) ?? { key, group, resources: [] };

    section.resources.push(resource);
    sections.set(key, section);
  }

  return [...sections.values()].sort(
    (a, b) =>
      (a.group ? kindOrder.indexOf(a.group.kind) : kindOrder.length) -
      (b.group ? kindOrder.indexOf(b.group.kind) : kindOrder.length),
  );
}
