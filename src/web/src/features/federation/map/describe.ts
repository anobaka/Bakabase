import type { TFunction } from "i18next";
import type { DeviceGraph, MapEdge, MapNode } from "./graph";

import { remoteDevicePlatformLabelKey } from "@/core/remoteDevicePlatform";
import { parseServerTime } from "@/core/serverTime";

/*
 * The words for what the map draws, shared by the drawing's accessible names, the list that
 * says the same for screen readers, and the panel — so all three always agree.
 */

type T = TFunction;

/** The device's own name, or "This device" when it has none to show. */
export const nodeName = (t: T, node: MapNode) =>
  node.name || (node.self ? t("federation.thisDevice") : "");

export const kindLabel = (t: T, node: MapNode) =>
  node.kind === "unknown" ? undefined : t(`federation.map.kind.${node.kind}`);

export const platformLabel = (t: T, node: MapNode) =>
  node.platform === undefined ? undefined : t(remoteDevicePlatformLabelKey(node.platform));

export const presenceLabel = (t: T, node: MapNode) => t(`federation.map.presence.${node.presence}`);

export const issueLabel = (t: T, issue: MapNode["issues"][number]) =>
  t(`federation.map.issue.${issue}`);

/** "Online · Desktop app · macOS": what a node's card says under its name. */
export const nodeSummary = (t: T, node: MapNode) =>
  [
    node.self
      ? t("federation.thisDevice")
      : node.ghost
        ? t("federation.map.ghost")
        : node.unverified
          ? t("federation.map.unverified")
          : presenceLabel(t, node),
    kindLabel(t, node),
    platformLabel(t, node),
  ]
    .filter(Boolean)
    .join(" · ");

/**
 * The line under a card's name: what the device is when that is known — its presence has
 * its own dot — and how it answered otherwise. A device known only by its own request says
 * so, and one whose request ended says that, since neither has anything else to show.
 */
export const cardLine = (t: T, node: MapNode) => {
  const what = [kindLabel(t, node), platformLabel(t, node)].filter(Boolean).join(" · ");

  if (node.self) return what;
  if (node.ghost) return t("federation.map.ghost");
  if (node.unverified) return t("federation.map.unverified");
  if (what) return what;

  return node.issues.includes("requestEnded")
    ? issueLabel(t, "requestEnded")
    : presenceLabel(t, node);
};

/**
 * One sentence per direction the relationship has, e.g. "NAS can browse this device's
 * library" — and, for a direction that does not work right now, why.
 */
export const directionPhrases = (t: T, edge: MapEdge, name: string) => {
  if (edge.kind === "sync") return [t("federation.map.edge.sync")];
  const phrases: string[] = [];

  for (const direction of ["in", "out"] as const) {
    const status = edge[direction];

    if (status !== "none")
      phrases.push(t(`federation.map.direction.${edge.kind}.${direction}.${status}`, { name }));
  }
  if (edge.attention)
    phrases.push(
      t(`federation.map.attention.${edge.kind}.${edge.attention.direction}`, {
        name,
        issue: issueLabel(t, edge.attention.issue),
      }),
    );

  return phrases;
};

export const edgeKindLabel = (t: T, edge: Pick<MapEdge, "kind">) =>
  t(`federation.map.edge.${edge.kind}`);

export const edgeLabel = (t: T, graph: DeviceGraph, edge: MapEdge) => {
  const node = graph.nodes.find((item) => item.id === edge.nodeId);
  const name = node ? nodeName(t, node) : "";

  return t("federation.map.edge.label", {
    kind: edgeKindLabel(t, edge),
    name,
    summary: directionPhrases(t, edge, name).join("; "),
  });
};

/** Every relationship of one device, one sentence per kind, for the screen-reader list. */
export const relationshipSentences = (t: T, graph: DeviceGraph, node: MapNode) => {
  const name = nodeName(t, node);

  return graph.edges
    .filter((edge) => edge.nodeId === node.id)
    .map((edge) => `${edgeKindLabel(t, edge)}: ${directionPhrases(t, edge, name).join("; ")}`);
};

export const nodeLabel = (t: T, node: MapNode) => {
  const name = nodeName(t, node);
  const issues = node.issues.map((issue) => issueLabel(t, issue));
  const summary = [nodeSummary(t, node), ...issues].join(" · ");

  return t(node.ghost ? "federation.map.node.ghostLabel" : "federation.map.node.label", {
    name,
    summary,
  });
};

/** What another record of a device's name is to this device, most telling first. */
const namesakeRoles = {
  nearby: "federation.map.namesake.role.nearby",
  manager: "federation.map.namesake.role.manager",
  managed: "federation.map.namesake.role.managed",
  sharesWithThis: "federation.map.namesake.role.sharesWithThis",
  browsesThis: "federation.map.namesake.role.browsesThis",
  sharing: "federation.map.namesake.role.sharing",
  askedToManage: "federation.map.namesake.role.askedToManage",
  askedToBrowse: "federation.map.namesake.role.askedToBrowse",
  known: "federation.map.namesake.role.known",
} as const;

const namesakeRole = ({ ghost, sources }: MapNode): keyof typeof namesakeRoles => {
  if (ghost) return "nearby";
  if (sources.managers.length) return "manager";
  if (sources.server) return "managed";
  if (sources.peer?.outboundGrant) return "sharesWithThis";
  if (sources.peer?.inboundGrant) return "browsesThis";
  if (sources.peer) return "sharing";
  if (sources.managementRequestsOut.length) return "askedToManage";
  if (sources.sharingRequests.some((request) => request.direction === "outgoing"))
    return "askedToBrowse";

  return "known";
};

/**
 * What a device of the same name is, for telling records of one name apart: what it has to
 * do with this device — the device that manages this one, the one sharing its library, one
 * found nearby — and where it is, or what it runs on when no address is known. `precise` adds
 * when a device that manages this one was paired, for two such pairings that say the same.
 */
export const namesakeDescription = (t: T, node: MapNode, precise = false) => {
  const role = t(namesakeRoles[namesakeRole(node)]);
  const paired = precise ? parseServerTime(node.sources.managers[0]?.createdAt) : null;
  const detail = [
    node.address ?? platformLabel(t, node) ?? kindLabel(t, node),
    paired ? t("federation.map.namesake.paired", { time: paired.toLocaleString() }) : undefined,
  ]
    .filter(Boolean)
    .join(" · ");

  return detail ? t("federation.map.namesake.what", { role, detail }) : role;
};
