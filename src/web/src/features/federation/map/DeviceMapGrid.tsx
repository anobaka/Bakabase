import type { MouseEvent } from "react";
import type { DeviceGraph, MapEdge, MapNode } from "./graph";
import type { MapSelection, SelectVia } from "./DeviceMapCanvas";

import { useTranslation } from "react-i18next";

import { edgesOf } from "./graph";
import { ATTENTION_DASH, AttentionMark, edgeStyles, KindBadge, KindGlyph } from "./DeviceMapCanvas";
import { cardLine, edgeKindLabel, edgeLabel, issueLabel, nodeLabel, nodeName } from "./describe";

/*
 * The device map as a list, for more devices than the width can draw readably: every device a
 * card, every relationship spelt out on it with the map's own marks — its kind's badge, which
 * way it goes, pending dashed, broken dotted with the warning mark. Everything selects as on
 * the map and opens the same details; nothing is shrunk to fit.
 */

/** A keyboard press on a button fires `click` with no pointer behind it. */
const via = (event: MouseEvent): SelectVia => (event.detail === 0 ? "keyboard" : "pointer");

const presenceDot: Record<MapNode["presence"], string> = {
  online: "fill-success stroke-success",
  offline: "fill-default-400 stroke-default-400",
  unknown: "fill-content1 stroke-default-400",
};

/** One direction, drawn as on the map: an arrow towards the other device (out) or this one. */
function DirectionSwatch({ edge, direction }: { edge: MapEdge; direction: "in" | "out" }) {
  const style = edgeStyles[edge.kind];
  const status = edge[direction];
  const broken = edge.attention?.direction === direction;

  return (
    <svg aria-hidden className="mt-0.5 h-3 w-7 shrink-0" viewBox="0 0 28 12">
      <path
        className={style.stroke}
        d={direction === "out" ? "M2 6 H21" : "M26 6 H7"}
        strokeDasharray={status === "pending" ? "4 3" : broken ? ATTENTION_DASH : undefined}
        strokeLinecap="round"
        strokeWidth={2}
      />
      <path
        className={style.fill}
        d={direction === "out" ? "M20 2 L27 6 L20 10 z" : "M8 2 L1 6 L8 10 z"}
      />
    </svg>
  );
}

function Relationship({
  graph,
  edge,
  name,
  selected,
  onSelect,
}: {
  graph: DeviceGraph;
  edge: MapEdge;
  name: string;
  selected: boolean;
  onSelect: (via: SelectVia) => void;
}) {
  const { t } = useTranslation();
  const style = edgeStyles[edge.kind];

  return (
    <button
      aria-label={edgeLabel(t, graph, edge)}
      aria-pressed={selected}
      className={`flex w-full items-start gap-2 rounded-lg px-2 py-1.5 text-left text-xs outline-none hover:bg-default-100 focus-visible:ring-2 focus-visible:ring-focus ${selected ? "bg-default-100 ring-1 ring-primary/40" : ""}`}
      data-edge={edge.id}
      data-in={edge.in}
      data-kind={edge.kind}
      data-out={edge.out}
      type="button"
      onClick={(event) => onSelect(via(event))}
    >
      <KindBadge className="h-4 w-4" kind={edge.kind} />
      <span className="min-w-0 flex-1 space-y-1">
        <span className={`block font-medium ${style.text}`}>{edgeKindLabel(t, edge)}</span>
        {(["in", "out"] as const)
          .filter((direction) => edge[direction] !== "none")
          .map((direction) => (
            <span
              key={direction}
              className="flex items-start gap-1.5 text-default-600"
              data-direction={direction}
              data-status={edge[direction]}
            >
              <DirectionSwatch direction={direction} edge={edge} />
              <span className="min-w-0 flex-1">
                {t(`federation.map.direction.${edge.kind}.${direction}.${edge[direction]}`, {
                  name,
                })}
              </span>
            </span>
          ))}
        {edge.mode && (
          <span className="block pl-[2.125rem] text-default-500" data-mode={edge.mode}>
            {t(`federation.map.${edge.kind}.mode.${edge.mode}`)}
          </span>
        )}
        {edge.attention && (
          <span data-attention className="flex items-start gap-1.5 text-danger">
            <svg aria-hidden className="mt-0.5 h-3 w-7 shrink-0" viewBox="0 0 28 12">
              <AttentionMark x={14} y={6.5} />
            </svg>
            <span className="min-w-0 flex-1">
              {t(`federation.map.attention.${edge.kind}.${edge.attention.direction}`, {
                name,
                issue: issueLabel(t, edge.attention.issue),
              })}
            </span>
          </span>
        )}
      </span>
    </button>
  );
}

function DeviceTile({
  graph,
  node,
  selection,
  onSelect,
}: {
  graph: DeviceGraph;
  node: MapNode;
  selection?: MapSelection;
  onSelect: (selection: MapSelection, via: SelectVia) => void;
}) {
  const { t } = useTranslation();
  const name = nodeName(t, node);
  const selected = selection?.type === "node" && selection.id === node.id;
  const edges = edgesOf(graph, node.id);

  return (
    <li
      className={`space-y-1 rounded-xl border bg-content1 p-2 ${
        node.self
          ? "border-primary bg-primary-50"
          : selected
            ? "border-primary"
            : node.ghost
              ? "border-dashed border-default-400"
              : node.unverified
                ? "border-warning/70"
                : "border-default-200"
      }`}
      data-tile={node.id}
    >
      <button
        aria-label={nodeLabel(t, node)}
        aria-pressed={selected}
        className="flex w-full items-start gap-3 rounded-lg p-1.5 text-left outline-none hover:bg-default-100 focus-visible:ring-2 focus-visible:ring-focus"
        data-ghost={node.ghost || undefined}
        data-kind={node.kind}
        data-node={node.id}
        data-presence={node.presence}
        title={name}
        type="button"
        onClick={(event) => onSelect({ type: "node", id: node.id }, via(event))}
      >
        <svg aria-hidden className="mt-0.5 h-7 w-8 shrink-0" viewBox="-16 -14 32 28">
          <KindGlyph
            className={
              node.self
                ? "stroke-primary"
                : node.ghost
                  ? "stroke-default-400"
                  : "stroke-default-600"
            }
            kind={node.kind}
          />
        </svg>
        <span className="min-w-0 flex-1">
          <span className="block break-words text-sm font-semibold">{name}</span>
          {node.self && (
            <span className="block text-xs font-semibold text-primary-600 dark:text-primary-400">
              {t("federation.thisDevice")}
            </span>
          )}
          <span className="block break-words text-xs text-default-500">{cardLine(t, node)}</span>
        </span>
        <span className="flex shrink-0 items-center gap-1.5 pt-1">
          {node.issues.length > 0 && (
            <svg aria-hidden className="h-3.5 w-3.5" viewBox="-7 -7 14 14">
              <path
                className="fill-warning stroke-warning"
                d="M0 -5.5 L5.5 4.5 L-5.5 4.5 Z"
                strokeLinejoin="round"
                strokeWidth={1.2}
              />
            </svg>
          )}
          {node.unverified ? (
            <span
              aria-hidden
              className="flex h-3.5 w-3.5 items-center justify-center rounded-full border border-warning bg-warning/15 text-[10px] font-bold text-warning-700 dark:text-warning"
            >
              ?
            </span>
          ) : node.ghost ? (
            <span
              aria-hidden
              className="flex h-5 w-5 items-center justify-center rounded-full border border-primary bg-primary/10 text-primary"
            >
              +
            </span>
          ) : (
            !node.self && (
              <svg aria-hidden className="h-2.5 w-2.5" viewBox="0 0 10 10">
                <circle
                  className={presenceDot[node.presence]}
                  cx={5}
                  cy={5}
                  r={4}
                  strokeWidth={1.5}
                />
              </svg>
            )
          )}
        </span>
      </button>
      {edges.length > 0 && (
        <ul className="space-y-0.5 border-t border-default-200 pt-1">
          {edges.map((edge) => (
            <li key={edge.id}>
              <Relationship
                edge={edge}
                graph={graph}
                name={name}
                selected={selection?.type === "edge" && selection.id === edge.id}
                onSelect={(how) => onSelect({ type: "edge", id: edge.id }, how)}
              />
            </li>
          ))}
        </ul>
      )}
    </li>
  );
}

export default function DeviceMapGrid({
  graph,
  selection,
  onSelect,
}: {
  graph: DeviceGraph;
  selection?: MapSelection;
  onSelect: (selection: MapSelection, via: SelectVia) => void;
}) {
  const { t } = useTranslation();
  const others = graph.nodes.filter((node) => !node.ghost);
  const ghosts = graph.nodes.filter((node) => node.ghost);
  const grid = "grid grid-cols-[repeat(auto-fill,minmax(260px,1fr))] items-start gap-2";

  return (
    <div className="space-y-3 p-1" data-testid="device-map-grid">
      <p className="rounded-lg bg-default-50 px-3 py-2 text-xs text-default-500" role="note">
        {t("federation.map.large.note", { count: graph.nodes.length })}
      </p>
      <ul aria-label={t("federation.map.canvasLabel")} className={grid}>
        {[graph.self, ...others].map((node) => (
          <DeviceTile
            key={node.id}
            graph={graph}
            node={node}
            selection={selection}
            onSelect={onSelect}
          />
        ))}
      </ul>
      {ghosts.length > 0 && (
        <section className="space-y-2">
          <h3 className="px-1 text-xs text-default-500">{t("federation.map.nearby")}</h3>
          <ul className={grid}>
            {ghosts.map((node) => (
              <DeviceTile
                key={node.id}
                graph={graph}
                node={node}
                selection={selection}
                onSelect={onSelect}
              />
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
