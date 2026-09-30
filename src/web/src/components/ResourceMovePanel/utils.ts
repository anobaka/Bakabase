import type { MoveDestination, MovePayload } from "./types";

import { isMoveSourceContext } from "./sourceContext";

import { normalizedMovePath } from "@/stores/resourceMovePanel";

export function parseMovePayload(value: string): MovePayload | undefined {
  try {
    const data = JSON.parse(value);

    if (
      !data ||
      !isMoveSourceContext(data.sourceContext) ||
      !Array.isArray(data.resources) ||
      !data.resources.length ||
      data.resources.some(
        (r: unknown) =>
          !r ||
          typeof r !== "object" ||
          "ref" in r ||
          "resourceRef" in r ||
          "nodeId" in r ||
          "libraryEpoch" in r ||
          !Number.isInteger((r as { id: number }).id) ||
          (r as { id: number }).id <= 0,
      )
    )
      return;

    return {
      sourceContext: {
        nodeId: data.sourceContext.nodeId,
        libraryEpoch: data.sourceContext.libraryEpoch,
      },
      resources: data.resources.map((r: { id: number; path?: string; displayName?: string }) => ({
        id: r.id,
        path: typeof r.path === "string" ? r.path : undefined,
        displayName: typeof r.displayName === "string" ? r.displayName : undefined,
      })),
      sourceTabId: typeof data.sourceTabId === "string" ? data.sourceTabId : undefined,
      sourceTabName: typeof data.sourceTabName === "string" ? data.sourceTabName : undefined,
    };
  } catch {
    return;
  }
}
export type DestinationGroup = { prefix?: string; destinations: MoveDestination[] };
/** Group adjacent peers only: a user reorder must never be undone by grouping. */
export function groupDestinations(
  destinations: MoveDestination[],
  grouped: boolean,
): DestinationGroup[] {
  const groups: DestinationGroup[] = [];

  for (const destination of destinations) {
    const path = normalizedMovePath(destination.path);
    const prefix = path.slice(0, path.lastIndexOf("/"));
    const previous = groups.at(-1);

    if (grouped && prefix && prefix !== "/" && previous && previous.prefix === prefix)
      previous.destinations.push(destination);
    else
      groups.push({ prefix: grouped && prefix ? prefix : undefined, destinations: [destination] });
  }

  return groups.map((g) => (g.destinations.length > 1 ? g : { destinations: g.destinations }));
}
export function reorderDestinations(
  destinations: MoveDestination[],
  draggedId: string,
  targetId: string,
): MoveDestination[] {
  const dragged = destinations.find((d) => d.id === draggedId);
  const target = destinations.find((d) => d.id === targetId);

  if (
    !dragged ||
    !target ||
    dragged.scope !== target.scope ||
    dragged.tabId !== target.tabId ||
    dragged.id === target.id
  )
    return destinations;
  const peers = destinations
    .filter((d) => !d.isDeleted && d.scope === dragged.scope && d.tabId === dragged.tabId)
    .sort((a, b) => a.order - b.order);
  const from = peers.findIndex((d) => d.id === draggedId),
    to = peers.findIndex((d) => d.id === targetId);

  peers.splice(to, 0, ...peers.splice(from, 1));
  const order = new Map(peers.map((d, index) => [d.id, index]));

  return destinations.map((d) => (order.has(d.id) ? { ...d, order: order.get(d.id)! } : d));
}
export function clampGeometry(
  g: { x: number; y: number; width: number; height: number },
  width: number,
  height: number,
) {
  const w = Math.min(Math.max(300, g.width), Math.max(200, width - 16));
  const h = Math.min(Math.max(300, g.height), Math.max(200, height - 16));

  return {
    width: w,
    height: h,
    x: Math.min(Math.max(8, g.x), Math.max(8, width - w - 8)),
    y: Math.min(Math.max(8, g.y), Math.max(8, height - h - 8)),
  };
}
