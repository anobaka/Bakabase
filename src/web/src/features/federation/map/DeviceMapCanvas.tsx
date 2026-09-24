import type { KeyboardEvent, ReactNode } from "react";
import type { DeviceGraph, MapEdge, MapEdgeKind, MapNode, MapNodeKind } from "./graph";
import type { EdgeGeometry, LaneGeometry, MapLayout, NodeBox } from "./layout";

import { Fragment, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { motion, useReducedMotion } from "framer-motion";

import { SELF_ID, edgesOf } from "./graph";
import { layoutDeviceMap } from "./layout";
import { cardLine, edgeLabel, nodeLabel, nodeName } from "./describe";
import { fitEnd, fitNames, SEMIBOLD } from "./text";
import { reveal } from "./reveal";
import { attributeValue, focusLost, focusMapItem, mapItemOf } from "./mapFocus";
import DeviceMapGrid from "./DeviceMapGrid";

/*
 * The device map itself: hand-drawn SVG in theme colour classes, so it follows light and
 * dark mode. Every device and every relationship is a focusable control — Tab walks them in
 * reading order (this device, then each other device followed by its relationships, then
 * the devices found nearby) and Enter or Space opens its details.
 */

export type MapSelection = { type: "node" | "edge"; id: string };

/** How a control was used: from the keyboard, the panel takes focus so it is read out. */
export type SelectVia = "pointer" | "keyboard";

/** Literal class names, so Tailwind sees them. */
export const edgeStyles: Record<
  MapEdgeKind,
  { stroke: string; fill: string; text: string; width: number }
> = {
  sharing: {
    stroke: "stroke-primary",
    fill: "fill-primary",
    text: "text-primary",
    width: 2,
  },
  management: {
    stroke: "stroke-warning",
    fill: "fill-warning",
    text: "text-warning-600 dark:text-warning",
    width: 2.5,
  },
  // Reserved for data sync. Nothing draws it yet.
  sync: {
    stroke: "stroke-secondary",
    fill: "fill-secondary",
    text: "text-secondary",
    width: 2,
  },
};

/**
 * What each kind means, drawn small enough to sit on a line: an eye for browsing a library,
 * a key for full control, two turning arrows for sync. Centred on (0, 0), about 12 across.
 */
export const KindBadgeGlyph = ({ kind, className }: { kind: MapEdgeKind; className: string }) => {
  const common = {
    className: `${className} fill-none`,
    strokeWidth: 1.5,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
  };

  if (kind === "sharing")
    return (
      <g aria-hidden>
        <path {...common} d="M-5.8 0 C-3.6 -3.6 3.6 -3.6 5.8 0 C3.6 3.6 -3.6 3.6 -5.8 0 Z" />
        <circle {...common} r={1.7} />
      </g>
    );
  if (kind === "management")
    return (
      <g aria-hidden>
        <circle {...common} cx={-2.8} r={2.6} />
        <path {...common} d="M-0.2 0 H5.6 M3.4 0 V2.4 M5.6 0 V2.4" />
      </g>
    );

  return (
    <g aria-hidden>
      <path {...common} d="M-4.5 -0.8 A4.5 4.5 0 0 1 3.9 -2.2 M4.5 0.8 A4.5 4.5 0 0 1 -3.9 2.2" />
      <path {...common} d="M4.4 -4.4 L3.9 -2.2 L1.8 -2.7 M-4.4 4.4 L-3.9 2.2 L-1.8 2.7" />
    </g>
  );
};

/** A relationship kind's badge on its own, as it sits on the map's lines: for headings, the legend. */
export const KindBadge = ({
  kind,
  className = "h-5 w-5",
}: {
  kind: MapEdgeKind;
  className?: string;
}) => (
  <svg aria-hidden className={`shrink-0 ${className}`} viewBox="-12 -12 24 24">
    <circle className={`fill-content1 ${edgeStyles[kind].stroke}`} r={10} strokeWidth={1.5} />
    <KindBadgeGlyph className={edgeStyles[kind].stroke} kind={kind} />
  </svg>
);

const PENDING_DASH = "6 5";

/** Dots, with round caps: set up but not working. Unlike a pending dash, not colour alone. */
export const ATTENTION_DASH = "0.5 5";
const ARROW = 9;

/**
 * A warning triangle, centred on (0, 0) about 11 across: what marks a relationship that does
 * not work right now, on its badge and in the legend. A shape, not just a colour, so it reads
 * on the amber of a management line too.
 */
export const AttentionMark = ({ x = 0, y = 0 }: { x?: number; y?: number }) => (
  <g aria-hidden data-attention-mark transform={`translate(${x} ${y})`}>
    <path
      className="fill-danger stroke-content1"
      d="M0 -5.8 L5.6 4.2 L-5.6 4.2 Z"
      strokeLinejoin="round"
      strokeWidth={1.4}
    />
    <path
      className="stroke-danger-foreground"
      d="M0 -2.4 V0.8 M0 2.5 V2.6"
      strokeLinecap="round"
      strokeWidth={1.4}
    />
  </g>
);

const presenceDot: Record<MapNode["presence"], string> = {
  online: "fill-success stroke-success",
  offline: "fill-default-400 stroke-default-400",
  unknown: "fill-content1 stroke-default-400",
};

/** The type size of a card's name. */
const nameSizeOf = (node: MapNode) => (node.self ? 15 : node.ghost ? 13 : 14);

/** How much of a card its name may take: the kind glyph on the left, the marks on the right. */
const nameRoomOf = (node: MapNode, box: NodeBox) =>
  box.w - (node.ghost ? 40 : 50) - (node.ghost ? 34 : node.issues.length ? 40 : 24);

const prefersReducedMotion = () =>
  typeof window !== "undefined" &&
  typeof window.matchMedia === "function" &&
  window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/** A small line drawing of what the device is, 28×24, centred on (0, 0). */
export const KindGlyph = ({ kind, className }: { kind: MapNodeKind; className: string }) => {
  const common = {
    className: `${className} fill-none`,
    strokeWidth: 1.8,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
  };

  return (
    <g aria-hidden transform="translate(-14 -12)">
      {kind === "desktop" && (
        <>
          <rect {...common} height={16} rx={2} width={26} x={1} y={1} />
          <path {...common} d="M14 17 V21 M8 22 H20" />
        </>
      )}
      {kind === "server" && (
        <>
          <rect {...common} height={22} rx={2.5} width={18} x={5} y={1} />
          <path {...common} d="M9 8 H19 M9 13 H19 M9 18 H19" />
        </>
      )}
      {kind === "mobile" && (
        <>
          <rect {...common} height={22} rx={3} width={13} x={7.5} y={1} />
          <path {...common} d="M12 19.5 H16" />
        </>
      )}
      {kind === "unknown" && (
        <>
          <rect {...common} height={20} rx={4} width={22} x={3} y={2} />
          <circle {...common} cx={14} cy={12} r={3.2} />
        </>
      )}
    </g>
  );
};

const onActivate = (activate: () => void) => (event: KeyboardEvent<SVGGElement>) => {
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    activate();
  }
};

const spring = (animate: boolean) =>
  animate ? { type: "spring" as const, stiffness: 260, damping: 32 } : { duration: 0 };

function NodeCard({
  node,
  box,
  label,
  selected,
  dimmed,
  animate,
  onSelect,
}: {
  node: MapNode;
  box: NodeBox;
  /** Its name as it fits the card; the full name is its tooltip and accessible name. */
  label: string;
  selected: boolean;
  dimmed: boolean;
  animate: boolean;
  onSelect: (via: SelectVia) => void;
}) {
  const { t } = useTranslation();
  const x = -box.w / 2;
  const y = -box.h / 2;
  const nameSize = nameSizeOf(node);
  const textX = x + (node.ghost ? 40 : 50);
  const textRoom = nameRoomOf(node, box);
  const name = nodeName(t, node);
  const line = cardLine(t, node);
  const cardClass = node.self
    ? "fill-primary-50 stroke-primary"
    : node.ghost
      ? "fill-content1 stroke-default-400 group-hover:stroke-primary"
      : selected
        ? "fill-content1 stroke-primary"
        : node.unverified
          ? "fill-content1 stroke-warning/70 group-hover:stroke-warning"
          : "fill-content1 stroke-default-300 group-hover:stroke-default-500";

  return (
    <motion.g
      animate={{ x: box.cx, y: box.cy }}
      className={`transition-opacity duration-200 ${dimmed ? "opacity-35" : "opacity-100"}`}
      initial={false}
      transition={spring(animate)}
    >
      <g
        aria-label={nodeLabel(t, node)}
        aria-pressed={selected}
        className="group cursor-pointer outline-none"
        data-ghost={node.ghost || undefined}
        data-kind={node.kind}
        data-node={node.id}
        data-presence={node.presence}
        role="button"
        tabIndex={0}
        onClick={() => onSelect("pointer")}
        onKeyDown={onActivate(() => onSelect("keyboard"))}
      >
        <title>{name}</title>
        {/* Keyboard focus ring: the card's own border is too faint to carry it. */}
        <rect
          className="fill-none stroke-focus opacity-0 group-focus-visible:opacity-100"
          height={box.h + 10}
          rx={16}
          strokeWidth={2.5}
          width={box.w + 10}
          x={x - 5}
          y={y - 5}
        />
        {(selected || node.self) && (
          <rect
            className={selected ? "fill-none stroke-primary/35" : "fill-none stroke-primary/15"}
            height={box.h + 12}
            rx={17}
            strokeWidth={6}
            width={box.w + 12}
            x={x - 6}
            y={y - 6}
          />
        )}
        <rect
          className={cardClass}
          height={box.h}
          rx={12}
          strokeDasharray={node.ghost ? "5 4" : undefined}
          strokeWidth={node.self || selected ? 2 : 1.2}
          width={box.w}
          x={x}
          y={y}
        />
        <g transform={`translate(${x + (node.ghost ? 21 : 26)} 0)`}>
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
        </g>
        {node.self ? (
          <>
            <text
              className="fill-foreground"
              fontSize={nameSize}
              fontWeight={650}
              x={textX}
              y={line ? -9 : -3}
            >
              {label}
            </text>
            <text
              className="fill-primary-600 dark:fill-primary-400"
              fontSize={11}
              fontWeight={600}
              x={textX}
              y={line ? 8 : 14}
            >
              {t("federation.thisDevice")}
            </text>
            {line && (
              <text className="fill-default-500" fontSize={11} x={textX} y={24}>
                {fitEnd(line, textRoom, 11)}
              </text>
            )}
          </>
        ) : (
          <>
            <text
              className={node.ghost ? "fill-default-600" : "fill-foreground"}
              fontSize={nameSize}
              fontWeight={600}
              x={textX}
              y={-3}
            >
              {label}
            </text>
            <text className="fill-default-500" fontSize={11} x={textX} y={14}>
              {fitEnd(line, box.w - (textX - x) - (node.ghost ? 34 : 12), 11)}
            </text>
          </>
        )}
        {!node.self && !node.ghost && !node.unverified && (
          <circle
            className={presenceDot[node.presence]}
            cx={x + box.w - 14}
            cy={y + 14}
            data-presence-dot={node.presence}
            r={4.5}
            strokeWidth={1.5}
          />
        )}
        {node.unverified && (
          // Nothing checked who this is: a question mark, not a presence it does not have.
          <g aria-hidden data-unverified-mark transform={`translate(${x + box.w - 14} ${y + 14})`}>
            <circle className="fill-warning/15 stroke-warning" r={7} strokeWidth={1.3} />
            <text
              className="fill-warning-700 dark:fill-warning"
              dominantBaseline="central"
              fontSize={10}
              fontWeight={700}
              textAnchor="middle"
            >
              ?
            </text>
          </g>
        )}
        {node.issues.length > 0 && (
          <g aria-hidden transform={`translate(${x + box.w - 31} ${y + 14.5})`}>
            <path
              className="fill-warning stroke-warning"
              d="M0 -5.5 L5.5 4.5 L-5.5 4.5 Z"
              strokeLinejoin="round"
              strokeWidth={1.2}
            />
            <path
              className="stroke-warning-foreground"
              d="M0 -2 V1 M0 2.8 V3"
              strokeLinecap="round"
              strokeWidth={1.3}
            />
          </g>
        )}
        {node.ghost && (
          // The pair affordance: selecting the ghost opens its panel with the ways to connect.
          <g aria-hidden transform={`translate(${x + box.w - 19} 0)`}>
            <circle
              className="fill-primary/10 stroke-primary group-hover:fill-primary/25"
              r={10}
              strokeWidth={1.4}
            />
            <path
              className="stroke-primary"
              d="M-4.5 0 H4.5 M0 -4.5 V4.5"
              strokeLinecap="round"
              strokeWidth={1.8}
            />
          </g>
        )}
      </g>
    </motion.g>
  );
}

function Lane({
  lane,
  kind,
  markerId,
  emphasised,
  animate,
}: {
  lane: LaneGeometry;
  kind: MapEdgeKind;
  markerId: (kind: MapEdgeKind) => string;
  emphasised: boolean;
  animate: boolean;
}) {
  const style = edgeStyles[kind];
  const pending = lane.status === "pending";
  const marker = `url(#${markerId(kind)})`;

  return (
    <motion.line
      animate={{ x1: lane.from.x, y1: lane.from.y, x2: lane.to.x, y2: lane.to.y }}
      className={style.stroke}
      data-attention={lane.attention || undefined}
      data-direction={lane.direction}
      data-status={lane.status}
      initial={false}
      markerEnd={lane.direction !== "in" ? marker : undefined}
      markerStart={lane.direction !== "out" ? marker : undefined}
      strokeDasharray={pending ? PENDING_DASH : lane.attention ? ATTENTION_DASH : undefined}
      strokeLinecap="round"
      strokeOpacity={pending ? 0.85 : 1}
      strokeWidth={style.width + (emphasised ? 1 : 0)}
      transition={spring(animate)}
    >
      {pending && animate && (
        <animate
          attributeName="stroke-dashoffset"
          dur="1.4s"
          from="22"
          repeatCount="indefinite"
          to="0"
        />
      )}
    </motion.line>
  );
}

function EdgeShape({
  edge,
  geometry,
  label,
  selected,
  dimmed,
  animate,
  markerId,
  onSelect,
}: {
  edge: MapEdge;
  geometry: EdgeGeometry;
  label: string;
  selected: boolean;
  dimmed: boolean;
  animate: boolean;
  markerId: (kind: MapEdgeKind) => string;
  onSelect: (via: SelectVia) => void;
}) {
  const style = edgeStyles[edge.kind];

  return (
    <g
      aria-label={label}
      aria-pressed={selected}
      className={`group cursor-pointer outline-none transition-opacity duration-200 ${dimmed ? "opacity-25" : "opacity-100"}`}
      data-edge={edge.id}
      data-in={edge.in}
      data-kind={edge.kind}
      data-out={edge.out}
      role="button"
      tabIndex={0}
      onClick={() => onSelect("pointer")}
      onKeyDown={onActivate(() => onSelect("keyboard"))}
    >
      <title>{label}</title>
      {/* A wide invisible line, so the thin one is easy to hit. */}
      <line
        stroke="transparent"
        strokeLinecap="round"
        strokeWidth={18}
        x1={geometry.from.x}
        x2={geometry.to.x}
        y1={geometry.from.y}
        y2={geometry.to.y}
      />
      <line
        className="stroke-focus opacity-0 group-focus-visible:opacity-60"
        strokeLinecap="round"
        strokeWidth={10}
        x1={geometry.from.x}
        x2={geometry.to.x}
        y1={geometry.from.y}
        y2={geometry.to.y}
      />
      {geometry.lanes.map((lane) => (
        <Lane
          key={lane.direction}
          animate={animate}
          emphasised={selected}
          kind={edge.kind}
          lane={lane}
          markerId={markerId}
        />
      ))}
      <motion.g
        animate={{ x: geometry.badge.x, y: geometry.badge.y }}
        initial={false}
        transition={spring(animate)}
      >
        <circle
          className={`${selected ? style.fill : "fill-content1"} ${style.stroke}`}
          r={10}
          strokeWidth={selected ? 2 : 1.5}
        />
        <KindBadgeGlyph
          className={selected ? "stroke-background" : style.stroke}
          kind={edge.kind}
        />
        {edge.attention && <AttentionMark x={8} y={-8} />}
      </motion.g>
    </g>
  );
}

export interface DeviceMapCanvasProps {
  graph: DeviceGraph;
  selection?: MapSelection;
  onSelect: (selection: MapSelection, via: SelectVia) => void;
  /** Shown under the drawing, e.g. what to do when this device is alone. */
  footer?: ReactNode;
  /** A width to lay out for before the container can be measured (tests, first paint). */
  initialWidth?: number;
}

/** Room around a card for its selection ring. */
const RING = 8;

const nextFrame = (run: () => void) => {
  if (typeof requestAnimationFrame !== "function") {
    const timer = setTimeout(run, 16);

    return () => clearTimeout(timer);
  }
  const frame = requestAnimationFrame(run);

  return () => cancelAnimationFrame(frame);
};

/** Follows the width the map is given, so its text stays at reading size at any width. */
const useWidth = (initial: number) => {
  const ref = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(initial);

  useLayoutEffect(() => {
    const element = ref.current;

    if (!element) return;
    const measure = () => {
      const measured = element.getBoundingClientRect().width;

      if (measured > 0) setWidth(Math.round(measured));
    };

    measure();
    if (typeof ResizeObserver !== "function") return;
    const observer = new ResizeObserver(measure);

    observer.observe(element);

    return () => observer.disconnect();
  }, []);

  return { ref, width };
};

export default function DeviceMapCanvas({
  graph,
  selection,
  onSelect,
  footer,
  initialWidth = 880,
}: DeviceMapCanvasProps) {
  const { t } = useTranslation();
  const { ref, width } = useWidth(initialWidth);
  // Cards make room for their second line as they draw it — what the device is, or for one
  // known only by a request, what became of it.
  const layout: MapLayout = useMemo(
    () => layoutDeviceMap(graph, width, (node) => cardLine(t, node)),
    [graph, width, t],
  );
  // Names as they fit their cards — never two different names shortened alike.
  const labels = useMemo(
    () =>
      fitNames(
        [graph.self, ...graph.nodes].map((node) => ({
          id: node.id,
          name: nodeName(t, node),
          maxWidth: nameRoomOf(node, layout.boxes.get(node.id)!),
          // Measured as set: semibold.
          fontSize: nameSizeOf(node) * SEMIBOLD,
        })),
      ),
    [graph, layout, t],
  );
  const reducedMotion = useReducedMotion();
  const animate = useMemo(() => !prefersReducedMotion(), []) && !reducedMotion;
  // Marker and pattern ids are document-global.
  const uid = useId().replace(/[^a-zA-Z0-9_-]/g, "");
  const markerId = (kind: MapEdgeKind) => `dm-${uid}-arrow-${kind}`;
  const titleId = `dm-${uid}-title`;
  const ring = graph.nodes.filter((node) => !node.ghost);
  const ghosts = graph.nodes.filter((node) => node.ghost);

  // What stays bright while something is selected: the selection, and what it touches.
  const related = useMemo(() => {
    if (!selection) return undefined;
    const ids = new Set<string>([SELF_ID]);

    if (selection.type === "node") {
      ids.add(selection.id);
      if (selection.id === SELF_ID) graph.edges.forEach((edge) => ids.add(edge.id));
      else edgesOf(graph, selection.id).forEach((edge) => ids.add(edge.id));
    } else {
      const edge = graph.edges.find((item) => item.id === selection.id);

      ids.add(selection.id);
      if (edge) ids.add(edge.nodeId);
    }

    return ids;
  }, [graph, selection]);
  const dimmed = (id: string) => !!related && !related.has(id);
  const svg = useRef<SVGSVGElement>(null);
  const selectedNodeId = !selection
    ? undefined
    : selection.type === "node"
      ? selection.id
      : graph.edges.find((edge) => edge.id === selection.id)?.nodeId;

  // The selected device and its lines are in view whenever something is selected: when it is
  // chosen, and each time the map is laid out again — for the width the details leave it, when
  // they open beside it. Positions come from the layout, not from cards still moving there.
  useEffect(() => {
    if (!selection || !selectedNodeId) return;

    return nextFrame(() => {
      if (!layout.readable) {
        const tile = ref.current?.querySelector(`[data-tile="${attributeValue(selectedNodeId)}"]`);

        if (tile) reveal(tile, tile.getBoundingClientRect(), undefined, animate);

        return;
      }
      const drawing = svg.current;
      const shown = drawing?.getBoundingClientRect();
      const box = layout.boxes.get(selectedNodeId);

      if (!drawing || !shown?.width || !box) return;
      const scale = shown.width / layout.width;
      const ys = [box.cy - box.h / 2 - RING, box.cy + box.h / 2 + RING];
      const lines =
        selection.type === "edge"
          ? [layout.edges.get(selection.id)]
          : selectedNodeId === SELF_ID
            ? []
            : edgesOf(graph, selectedNodeId).map((edge) => layout.edges.get(edge.id));

      for (const line of lines)
        if (line) ys.push(line.from.y, line.to.y, line.badge.y - RING, line.badge.y + RING);
      const at = (y: number) => shown.top + y * scale;

      reveal(
        drawing,
        { top: at(Math.min(...ys)), bottom: at(Math.max(...ys)) },
        { top: at(ys[0]), bottom: at(ys[1]) },
        animate,
      );
    });
    // Again when the map is laid out for another width, never on a refresh that moves nothing.
  }, [selection?.type, selection?.id, selectedNodeId, width, layout.readable]);

  // Where the keyboard is on the map, by what it is on: the drawing and the list replace each
  // other's controls when the width the map is given changes — the details opening or closing
  // beside it — and the control that had the keyboard goes with them, to the page's body. The
  // keyboard is then given to the same device or relationship in the rendering that replaced
  // it. Only where the reader left it: a pointer pressed, or focus moved, anywhere else lets go.
  const keyboardAt = useRef<MapSelection>();

  useEffect(() => {
    const follow = (event: Event) => {
      keyboardAt.current = mapItemOf(event.target, ref.current);
    };

    document.addEventListener("focusin", follow, true);
    document.addEventListener("pointerdown", follow, true);

    return () => {
      document.removeEventListener("focusin", follow, true);
      document.removeEventListener("pointerdown", follow, true);
    };
  }, [ref]);
  const rendering = layout.readable ? "map" : "list";
  const shown = useRef(rendering);

  useLayoutEffect(() => {
    if (shown.current === rendering) return;
    shown.current = rendering;
    const item = keyboardAt.current;

    if (item && focusLost()) focusMapItem(ref.current, item);
  }, [rendering]);

  const isSelected = (type: MapSelection["type"], id: string) =>
    selection?.type === type && selection.id === id;
  const selectNode = (id: string) => (via: SelectVia) => onSelect({ type: "node", id }, via);
  const selectEdge = (id: string) => (via: SelectVia) => onSelect({ type: "edge", id }, via);

  if (!layout.readable)
    // More devices than this width draws readably: listed, with every relationship, instead.
    return (
      <div ref={ref} className="relative w-full" data-mode="list" data-testid="device-map-canvas">
        <DeviceMapGrid graph={graph} selection={selection} onSelect={onSelect} />
        {footer}
      </div>
    );

  return (
    <div
      ref={ref}
      className="relative w-full"
      data-height={layout.height}
      data-mode="map"
      data-scale={layout.scale}
      data-testid="device-map-canvas"
      data-tiers={layout.tiers}
      data-width={layout.width}
    >
      <svg
        ref={svg}
        aria-labelledby={titleId}
        className="mx-auto block h-auto w-full select-none"
        role="group"
        // Never stretched past the width it was laid out for: its text stays at its size.
        style={{ maxWidth: layout.width }}
        viewBox={`0 0 ${layout.width} ${layout.height}`}
      >
        <title id={titleId}>{t("federation.map.canvasLabel")}</title>
        <defs>
          {(Object.keys(edgeStyles) as MapEdgeKind[]).map((kind) => (
            <marker
              key={kind}
              id={markerId(kind)}
              markerHeight={ARROW}
              markerUnits="userSpaceOnUse"
              markerWidth={ARROW}
              orient="auto-start-reverse"
              refX={9}
              refY={5}
              viewBox="0 0 10 10"
            >
              <path className={edgeStyles[kind].fill} d="M0 0.5 L10 5 L0 9.5 L2.5 5 z" />
            </marker>
          ))}
          <pattern height={22} id={`dm-${uid}-dots`} patternUnits="userSpaceOnUse" width={22}>
            <circle className="fill-default-200" cx={1.5} cy={1.5} r={1.1} />
          </pattern>
        </defs>
        <rect fill={`url(#dm-${uid}-dots)`} height={layout.height} width={layout.width} />
        {layout.ghostTop !== undefined && (
          <g aria-hidden>
            <line
              className="stroke-default-200"
              strokeDasharray="3 5"
              x1={20}
              x2={layout.width - 20}
              y1={layout.ghostTop}
              y2={layout.ghostTop}
            />
            <text className="fill-default-500" fontSize={11.5} x={20} y={layout.ghostTop + 18}>
              {t("federation.map.nearby")}
            </text>
          </g>
        )}

        <NodeCard
          animate={animate}
          box={layout.boxes.get(SELF_ID)!}
          dimmed={false}
          label={labels.get(SELF_ID) ?? ""}
          node={graph.self}
          selected={isSelected("node", SELF_ID)}
          onSelect={selectNode(SELF_ID)}
        />
        {ring.map((node) => (
          <Fragment key={node.id}>
            <NodeCard
              animate={animate}
              box={layout.boxes.get(node.id)!}
              dimmed={dimmed(node.id)}
              label={labels.get(node.id) ?? ""}
              node={node}
              selected={isSelected("node", node.id)}
              onSelect={selectNode(node.id)}
            />
            {edgesOf(graph, node.id).map((edge) => {
              const geometry = layout.edges.get(edge.id);

              return geometry ? (
                <EdgeShape
                  key={edge.id}
                  animate={animate}
                  dimmed={dimmed(edge.id)}
                  edge={edge}
                  geometry={geometry}
                  label={edgeLabel(t, graph, edge)}
                  markerId={markerId}
                  selected={isSelected("edge", edge.id)}
                  onSelect={selectEdge(edge.id)}
                />
              ) : null;
            })}
          </Fragment>
        ))}
        {ghosts.map((node) => (
          <NodeCard
            key={node.id}
            animate={animate}
            box={layout.boxes.get(node.id)!}
            dimmed={dimmed(node.id)}
            label={labels.get(node.id) ?? ""}
            node={node}
            selected={isSelected("node", node.id)}
            onSelect={selectNode(node.id)}
          />
        ))}
      </svg>
      {footer}
    </div>
  );
}
