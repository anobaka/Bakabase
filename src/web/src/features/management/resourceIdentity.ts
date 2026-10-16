import type { Resource } from "@/core/models/Resource";
import type { ManagementSource } from "./api";

export type ResourceSelectionKey = number | string;

export function resourceSelectionKey(
  resource: Pick<Resource, "id" | "owner">,
): ResourceSelectionKey {
  return resource.owner && !resource.owner.isLocal
    ? JSON.stringify([resource.owner.serverId, resource.owner.libraryEpoch, resource.id])
    : resource.id;
}

export function sameResourceOwner(a?: ManagementSource, b?: ManagementSource) {
  if ((!a || a.isLocal) && (!b || b.isLocal)) return true;
  if (!a || !b) return !a && !b;

  return a.serverId === b.serverId && a.libraryEpoch === b.libraryEpoch;
}

export function deviceColor(serverId: string, colors?: Record<string, string> | null) {
  const configured = colors?.[serverId];

  if (configured && /^#[\da-f]{6}$/i.test(configured)) return configured;
  // Stable across source ordering, reconnects and pages; independent of another device's IDs.
  let hash = 2166136261;

  for (const character of serverId) hash = Math.imul(hash ^ character.charCodeAt(0), 16777619);
  // Keep the original HSL palette, expressed as HEX so native color inputs and
  // device dots can share the same resolved value (color inputs do not accept HSL).
  const hue = ((hash >>> 0) % 360) / 60;
  const chroma = (1 - Math.abs(2 * 0.58 - 1)) * 0.7;
  const secondary = chroma * (1 - Math.abs((hue % 2) - 1));
  const base = 0.58 - chroma / 2;
  const channels = [
    [chroma, secondary, 0],
    [secondary, chroma, 0],
    [0, chroma, secondary],
    [0, secondary, chroma],
    [secondary, 0, chroma],
    [chroma, 0, secondary],
  ][Math.floor(hue)];

  return `#${channels
    .map((value) =>
      Math.round((value + base) * 255)
        .toString(16)
        .padStart(2, "0"),
    )
    .join("")}`;
}
