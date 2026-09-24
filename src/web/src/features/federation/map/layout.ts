import type { DeviceGraph, MapDirectionStatus, MapEdge, MapEdgeKind, MapNode } from "./graph";

import { SELF_ID, mapEdgeKinds } from "./graph";
import { SEMIBOLD, textWidth } from "./text";

/*
 * Where everything on the device map goes. Deterministic and force-free: this device in the
 * middle, the others fanned out above and below it in the graph's order (connected devices by
 * name, clockwise from the top left), devices found nearby in rows underneath. The same graph
 * at the same width always gives the same picture, so a refresh that only changes a status
 * never moves anything.
 *
 * Every relationship has this device at one end, so each is a spoke. Devices are placed so
 * every spoke shows about the same length of line between the two cards — wide cards beside
 * each other would leave a horizontal spoke no room for its arrows and badge, which is why
 * nobody sits level with this device. Kinds get their own lane, a parallel line beside the
 * spoke, so a device that both shares and is managed shows two lines, never one line meaning
 * two things.
 *
 * Nothing is accepted where a line or a badge runs over a card other than its own two: spokes
 * grow longer until none does, and where the canvas is too narrow for that the map is laid out
 * wider and drawn scaled down. Cards grow with the names they carry, within reason.
 */

export interface NodeBox {
  id: string;
  cx: number;
  cy: number;
  w: number;
  h: number;
}

export interface Point {
  x: number;
  y: number;
}

/**
 * One drawn line. `both` carries an arrowhead at each end; `out` points at the other device,
 * `in` at this one.
 */
export interface LaneGeometry {
  direction: "both" | "out" | "in";
  status: Exclude<MapDirectionStatus, "none">;
  /** The direction exists but does not work right now: drawn in its own style. */
  attention: boolean;
  from: Point;
  to: Point;
}

export interface EdgeGeometry {
  id: string;
  kind: MapEdgeKind;
  lanes: LaneGeometry[];
  /** The spoke's own line for this kind, for hit-testing and the focus ring. */
  from: Point;
  to: Point;
  /** Where the kind's badge sits. */
  badge: Point;
}

export interface MapLayout {
  /** The drawing's own width: wider than asked for when it had to be drawn scaled down. */
  width: number;
  height: number;
  compact: boolean;
  boxes: Map<string, NodeBox>;
  edges: Map<string, EdgeGeometry>;
  /** Top of the rows of devices found nearby, for their caption. */
  ghostTop?: number;
  /**
   * Whether this is a picture anyone can read where it is shown: no line or badge over a card
   * other than its own two, drawn at {@link MIN_SCALE} or larger. When it is not — more devices
   * than the width can hold — the page shows them as a list instead of this drawing.
   */
  readable: boolean;
  /** How much this drawing is scaled where it is shown (1 when it fits as laid out). */
  scale: number;
  /** Devices sit at two distances from this device, every other one further out. */
  tiers: 1 | 2;
}

export const MIN_WIDTH = 520;
export const MAX_WIDTH = 1200;
/**
 * The smallest a map is ever drawn: its smallest type (a card's 11 px second line) stays
 * about 9 px, its names about 11 px. Below this the devices are listed instead.
 */
export const MIN_SCALE = 0.82;
const MARGIN = 20;
const LANE_GAP = 24;
const SPLIT_GAP = 4.5;
const END_GAP = 7;
const GHOST_GAP = 16;
const GHOST_CAPTION = 30;
const NODE_PADDING = 14;
/** How much line a spoke shows between the two cards, at least. */
const SPOKE = 124;
const SPOKE_COMPACT = 104;
/** How much longer spokes may grow at one width before the map is drawn scaled down. */
const MAX_LENGTHEN = 200;
/** How much wider than its canvas the map may be laid out, and in what steps. */
const MAX_ZOOM_OUT = 2.6;
const ZOOM_STEP = 1.08;
/**
 * How much further out the outer of two tiers sits: a card's height and room for the lines
 * of the tier behind it to pass between. Tried in turn; the shorter picture wins.
 */
const TIER_STAGGERS = [36, 76];
/** Fewer devices than this always fit one tier. */
const TIERS_FROM = 6;

/**
 * The tallest a map is drawn — for its width, and at all, in pixels as shown (its devices,
 * without the rows found nearby): taller, it is read a part at a time, like a list.
 */
export const MAX_ASPECT = 1.25;
export const MAX_SHOWN_HEIGHT = 1400;

/** The name's type size on a card, what the card needs beside it, and the widest a card gets. */
export const NAME_SIZE = 14;
const NAME_CHROME = 90;
/** The line under the name: its type size, and what the card needs beside it. */
const LINE_SIZE = 11;
const LINE_CHROME = 62;
const MAX_CARD = 280;

export const nodeSize = (compact: boolean) => ({ w: compact ? 168 : 188, h: 60 });
export const selfSize = (compact: boolean) => ({ w: compact ? 200 : 228, h: 78 });
export const ghostSize = (compact: boolean) => ({ w: compact ? 156 : 172, h: 52 });

/**
 * Directions for `n` devices, in degrees clockwise from the right: half fanned out above
 * this device from left to right, the rest below from right to left — clockwise all round.
 * A row of one sits straight above (or below); wider rows fan out further.
 */
export const spokeAngles = (n: number) => {
  if (n <= 0) return [];
  const above = Math.ceil(n / 2);
  const fan = (count: number, centre: number) => {
    if (count <= 0) return [];
    if (count === 1) return [centre];
    const half = Math.min(68, 28 + 12 * count);

    return Array.from({ length: count }, (_, i) => centre - half + ((2 * half) / (count - 1)) * i);
  };

  return [...fan(above, -90), ...fan(n - above, 90)];
};

const overlaps = (a: NodeBox, b: NodeBox, padding: number) =>
  Math.abs(a.cx - b.cx) < (a.w + b.w) / 2 + padding &&
  Math.abs(a.cy - b.cy) < (a.h + b.h) / 2 + padding;

/** How far from a box's centre the ray along `u` leaves it. */
const reach = (u: Point, w: number, h: number) =>
  Math.min(u.x ? w / 2 / Math.abs(u.x) : Infinity, u.y ? h / 2 / Math.abs(u.y) : Infinity);

/** How far from `p`, inside `box`, the ray along `u` leaves it. */
const exitDistance = (p: Point, u: Point, box: NodeBox) => {
  const hw = box.w / 2;
  const hh = box.h / 2;
  const tx = u.x > 0 ? (box.cx + hw - p.x) / u.x : u.x < 0 ? (box.cx - hw - p.x) / u.x : Infinity;
  const ty = u.y > 0 ? (box.cy + hh - p.y) / u.y : u.y < 0 ? (box.cy - hh - p.y) / u.y : Infinity;

  return Math.max(0, Math.min(tx, ty));
};

/** A line from `a` to `b`, shifted sideways by `offset`, cut where it leaves each box. */
const spoke = (a: NodeBox, b: NodeBox, offset: number) => {
  const dx = b.cx - a.cx;
  const dy = b.cy - a.cy;
  const length = Math.hypot(dx, dy) || 1;
  const u = { x: dx / length, y: dy / length };
  const normal = { x: -u.y, y: u.x };
  const start = { x: a.cx + normal.x * offset, y: a.cy + normal.y * offset };
  const end = { x: b.cx + normal.x * offset, y: b.cy + normal.y * offset };
  const leaveA = exitDistance(start, u, a) + END_GAP;
  const leaveB = exitDistance(end, { x: -u.x, y: -u.y }, b) + END_GAP;

  return {
    from: { x: start.x + u.x * leaveA, y: start.y + u.y * leaveA },
    to: { x: end.x - u.x * leaveB, y: end.y - u.y * leaveB },
  };
};

const round = (value: number) => Math.round(value * 10) / 10;
const roundPoint = (point: Point): Point => ({ x: round(point.x), y: round(point.y) });

const lanesOf = (edge: MapEdge, a: NodeBox, b: NodeBox, offset: number): LaneGeometry[] => {
  const line = (
    direction: LaneGeometry["direction"],
    status: LaneGeometry["status"],
    shift = 0,
  ) => {
    const { from, to } = spoke(a, b, offset + shift);

    return {
      direction,
      status,
      attention: direction === edge.attention?.direction,
      from: roundPoint(from),
      to: roundPoint(to),
    };
  };

  if (edge.out !== "none" && edge.in !== "none") {
    // Both ways in the same state read best as one line with two arrowheads; two states —
    // or one way working and the other not — need two lines, or a line's style would have
    // to mean one direction only.
    return edge.out === edge.in && !edge.attention
      ? [line("both", edge.out)]
      : [line("out", edge.out, -SPLIT_GAP), line("in", edge.in, SPLIT_GAP)];
  }
  if (edge.out !== "none") return [line("out", edge.out)];
  if (edge.in !== "none") return [line("in", edge.in)];

  return [];
};

/** One device's relationships, in the order their lanes go. */
const kindsOf = (graph: DeviceGraph, nodeId: string) =>
  graph.edges
    .filter((edge) => edge.nodeId === nodeId)
    .sort((a, b) => mapEdgeKinds.indexOf(a.kind) - mapEdgeKinds.indexOf(b.kind));

/** How far beside the spoke a kind's lane runs. */
const laneOffset = (index: number, count: number) => (index - (count - 1) / 2) * LANE_GAP;

/** The lines drawn for one device's relationships, each kind on its lane, and their badges. */
const geometryOf = (graph: DeviceGraph, self: NodeBox, box: NodeBox): EdgeGeometry[] => {
  const kinds = kindsOf(graph, box.id);

  return kinds.map((edge, index) => {
    const offset = laneOffset(index, kinds.length);
    const center = spoke(self, box, offset);

    return {
      id: edge.id,
      kind: edge.kind,
      lanes: lanesOf(edge, self, box, offset),
      from: roundPoint(center.from),
      to: roundPoint(center.to),
      badge: roundPoint({
        x: (center.from.x + center.to.x) / 2,
        y: (center.from.y + center.to.y) / 2,
      }),
    };
  });
};

/** Whether the segment from `p` to `q` passes through `box` grown by `pad` (Liang–Barsky). */
const crossesBox = (p: Point, q: Point, box: NodeBox, pad: number) => {
  const dx = q.x - p.x;
  const dy = q.y - p.y;
  let enter = 0;
  let leave = 1;
  const clip = (denominator: number, numerator: number) => {
    if (denominator === 0) return numerator >= 0;
    const t = numerator / denominator;

    if (denominator < 0) {
      if (t > leave) return false;
      enter = Math.max(enter, t);
    } else {
      if (t < enter) return false;
      leave = Math.min(leave, t);
    }

    return true;
  };

  return (
    clip(-dx, p.x - (box.cx - box.w / 2 - pad)) &&
    clip(dx, box.cx + box.w / 2 + pad - p.x) &&
    clip(-dy, p.y - (box.cy - box.h / 2 - pad)) &&
    clip(dy, box.cy + box.h / 2 + pad - p.y) &&
    enter < leave
  );
};

const circleHitsBox = (centre: Point, radius: number, box: NodeBox) => {
  const x = Math.max(box.cx - box.w / 2, Math.min(centre.x, box.cx + box.w / 2));
  const y = Math.max(box.cy - box.h / 2, Math.min(centre.y, box.cy + box.h / 2));

  return Math.hypot(centre.x - x, centre.y - y) < radius;
};

/** A line's badge, with room for the warning mark it can carry. */
const BADGE_RADIUS = 16;
/** How far a line or a badge keeps from a card that is not one of its ends. */
const CLEARANCE = 6;

/**
 * Every line or badge that runs over a card other than the two it joins — what the layout
 * never accepts. Empty for a readable map. `boxes` is this device first, then the others.
 */
export const crossings = (graph: DeviceGraph, boxes: NodeBox[], clearance = CLEARANCE) => {
  const [self, ...others] = boxes;
  const problems: string[] = [];

  for (const box of others) {
    for (const geometry of geometryOf(graph, self, box)) {
      // Where either of its lines can be: a relationship splits into two when one direction
      // changes state, and a change of state must never move a card.
      const index = kindsOf(graph, box.id).findIndex((edge) => edge.id === geometry.id);
      const offset = laneOffset(index, kindsOf(graph, box.id).length);
      const extents = [-SPLIT_GAP, SPLIT_GAP].map((shift) => spoke(self, box, offset + shift));

      for (const other of others) {
        if (other.id === box.id) continue;
        if (extents.some((line) => crossesBox(line.from, line.to, other, clearance)))
          problems.push(`line ${geometry.id} crosses ${other.id}`);
        if (circleHitsBox(geometry.badge, BADGE_RADIUS + clearance, other))
          problems.push(`badge of ${geometry.id} on ${other.id}`);
      }
    }
  }

  return problems;
};

/** How wide the other devices' cards are: wide enough for their names, within reason. */
export const cardWidth = (
  graph: DeviceGraph,
  width: number,
  compact: boolean,
  lineOf?: (node: MapNode) => string,
) => {
  const base = nodeSize(compact).w;
  // Room for a warning mark as well, so a status change never cuts a name shorter; and for
  // what the device is ("Headless server · Windows"), which changes only when it says so.
  const widest = Math.max(
    0,
    ...graph.nodes
      .filter((item) => !item.ghost)
      .map((item) =>
        Math.max(
          textWidth(item.name, NAME_SIZE * SEMIBOLD) + NAME_CHROME,
          lineOf ? textWidth(lineOf(item), LINE_SIZE) + LINE_CHROME : 0,
        ),
      ),
  );
  const cap = Math.max(base, Math.min(MAX_CARD, Math.round(width * 0.34)));

  // Up, never down: a name that fits by a fraction of a pixel must not be shortened.
  return Math.ceil(Math.min(cap, Math.max(base, widest)));
};

/**
 * @param requestedWidth The width the map has to be shown in. It is laid out for at most
 * {@link MAX_WIDTH} and at least {@link MIN_WIDTH}.
 * @param lineOf What a card says under its name, when the caller can word it — the layout
 * makes room for it as for the name.
 */
export function layoutDeviceMap(
  graph: DeviceGraph,
  requestedWidth: number,
  lineOf?: (node: MapNode) => string,
): MapLayout {
  const width = Math.round(Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, requestedWidth)));
  // What it is shown at: a wider page centres the drawing rather than stretch it (and its
  // text) past the width it was laid out for; a phone shrinks the narrowest one like a picture.
  const shown = width;
  const compact = width < 640;
  const nodeHeight = nodeSize(compact).h;
  const selfCard = selfSize(compact);
  const ghostCard = ghostSize(compact);
  const around = graph.nodes.filter((item) => !item.ghost);
  const ghosts = graph.nodes.filter((item) => item.ghost);
  const angles = spokeAngles(around.length);
  const shortest = compact ? SPOKE_COMPACT : SPOKE;
  const above = Math.ceil(around.length / 2);
  // Every other device in each fan sits in the outer tier, when there are two.
  const outer = around.map((_, index) => (index < above ? index : index - above) % 2 === 1);

  interface Shape {
    tiers: 1 | 2;
    stagger: number;
  }
  // Each device goes out along its direction until the spoke to it shows `length` of line
  // (and, in the outer tier, `stagger` more); one that would leave the canvas sideways is
  // kept inside, a little steeper.
  const place = (canvas: number, node: { w: number; h: number }, shape: Shape, length: number) => {
    const cx = canvas / 2;
    const minX = MARGIN + node.w / 2;
    const maxX = canvas - MARGIN - node.w / 2;
    const self: NodeBox = { id: SELF_ID, cx, cy: 0, ...selfCard };
    const others = around.map((item, index) => {
      const radians = (angles[index] * Math.PI) / 180;
      const u = { x: Math.cos(radians), y: Math.sin(radians) };
      const further = shape.tiers === 2 && outer[index] ? node.h + shape.stagger : 0;
      const distance =
        reach(u, selfCard.w, selfCard.h) + length + further + reach(u, node.w, node.h);

      return {
        id: item.id,
        cx: Math.min(maxX, Math.max(minX, cx + u.x * distance)),
        cy: u.y * distance,
        ...node,
      };
    });

    return { canvas, self, others, tiers: shape.tiers };
  };

  type Placement = ReturnType<typeof place>;
  const overlapping = ({ self, others }: Placement) => {
    const all = [self, ...others];

    return all.some((a, i) => all.some((b, j) => j > i && overlaps(a, b, NODE_PADDING)));
  };
  const extent = ({ self, others }: Placement) =>
    Math.max(...[self, ...others].map((box) => box.cy + box.h / 2)) -
    Math.min(...[self, ...others].map((box) => box.cy - box.h / 2));
  const readable = (placed: Placement) =>
    !overlapping(placed) &&
    // A map many screens tall is no longer a picture of anything: it is read a part at a time.
    extent(placed) + 2 * MARGIN <=
      Math.min(MAX_ASPECT * placed.canvas, (MAX_SHOWN_HEIGHT * placed.canvas) / shown) &&
    crossings(graph, [placed.self, ...placed.others]).length === 0;
  /** The shortest spokes that make this shape readable on this canvas, if any do. */
  const settle = (canvas: number, node: { w: number; h: number }, shape: Shape) => {
    // First as long as it takes for no card to touch another — many devices need that — then
    // a little longer at most, before trying wider.
    let length = shortest;

    for (let step = 0; step < 300 && overlapping(place(canvas, node, shape, length)); step++)
      length += 8;
    for (const longest = length + MAX_LENGTHEN; length <= longest; length += 8) {
      const candidate = place(canvas, node, shape, length);

      if (readable(candidate)) return candidate;
    }

    return undefined;
  };
  const shapes: Shape[] = [
    { tiers: 1, stagger: 0 },
    ...(around.length >= TIERS_FROM
      ? TIER_STAGGERS.map((stagger) => ({ tiers: 2 as const, stagger }))
      : []),
  ];

  // Longer spokes until no card touches another and no line or badge runs over a card; many
  // devices in two tiers when that is the shorter picture. A device kept inside a narrow
  // canvas bends its spoke across its neighbours, and no length undoes that; then the map is
  // laid out wider and drawn scaled to the width it has — but never smaller than a reader
  // can read, and never with a line across a card.
  let placed: Placement | undefined;
  const widest = Math.min(width * MAX_ZOOM_OUT, shown / MIN_SCALE);

  for (let canvas = width; canvas <= widest && !placed; canvas = Math.round(canvas * ZOOM_STEP)) {
    // Cards as wide as their names on the canvas actually laid out: a map drawn smaller has
    // room for wider cards, whose text is drawn smaller with them.
    const node = { w: cardWidth(graph, canvas, compact, lineOf), h: nodeHeight };

    for (const shape of shapes) {
      const candidate = settle(canvas, node, shape);

      if (candidate && (!placed || extent(candidate) < extent(placed))) placed = candidate;
    }
  }
  const found = !!placed;

  if (!placed) {
    // More devices than any of that makes readable, and nobody will be shown this drawing —
    // the page lists them instead. Still laid out, cards apart, for whoever asks.
    const node = { w: cardWidth(graph, width, compact, lineOf), h: nodeHeight };
    const shape: Shape = { tiers: 1, stagger: 0 };
    let length = shortest;

    placed = place(width, node, shape, length);
    for (let step = 0; step < 300 && overlapping(placed); step++) {
      length += 8;
      placed = place(width, node, shape, length);
    }
  }

  const canvas = placed.canvas;

  // Shift everything down so the topmost card clears the margin.
  const all = [placed.self, ...placed.others];
  const top = Math.min(...all.map((box) => box.cy - box.h / 2));
  const bottom = Math.max(...all.map((box) => box.cy + box.h / 2));
  const shift = MARGIN + (around.length ? 0 : 24) - top;
  const boxes = new Map<string, NodeBox>();

  for (const box of all)
    boxes.set(box.id, { ...box, cx: round(box.cx), cy: round(box.cy + shift) });

  let height = bottom + shift + MARGIN + (around.length ? 0 : 24);
  let ghostTop: number | undefined;

  if (ghosts.length) {
    ghostTop = height - MARGIN + 8;
    const perRow = Math.max(
      1,
      Math.floor((canvas - 2 * MARGIN + GHOST_GAP) / (ghostCard.w + GHOST_GAP)),
    );
    const rows = Math.ceil(ghosts.length / perRow);

    ghosts.forEach((ghost, index) => {
      const row = Math.floor(index / perRow);
      const inRow = Math.min(perRow, ghosts.length - row * perRow);
      const column = index - row * perRow;
      const rowWidth = inRow * ghostCard.w + (inRow - 1) * GHOST_GAP;
      const left = (canvas - rowWidth) / 2;

      boxes.set(ghost.id, {
        id: ghost.id,
        cx: round(left + column * (ghostCard.w + GHOST_GAP) + ghostCard.w / 2),
        cy: round(ghostTop! + GHOST_CAPTION + row * (ghostCard.h + GHOST_GAP) + ghostCard.h / 2),
        ...ghostCard,
      });
    });
    height = ghostTop + GHOST_CAPTION + rows * (ghostCard.h + GHOST_GAP) - GHOST_GAP + MARGIN;
  }

  const selfBox = boxes.get(SELF_ID)!;
  const edges = new Map<string, EdgeGeometry>();

  for (const item of around)
    for (const geometry of geometryOf(graph, selfBox, boxes.get(item.id)!))
      edges.set(geometry.id, geometry);

  return {
    width: canvas,
    height: Math.round(height),
    compact,
    boxes,
    edges,
    ghostTop: ghostTop === undefined ? undefined : round(ghostTop),
    readable: found,
    scale: Math.round((shown / canvas) * 1000) / 1000,
    tiers: placed.tiers,
  };
}
