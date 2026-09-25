/*
 * Where the data sync diagram puts its cards: this device in the middle, every other device
 * around it on an ellipse, each on its own spoke, with the spoke's mode in words beside its badge.
 * Pure and deterministic, so the same devices at the same width are always drawn the same way.
 *
 * Readable at any count, like the device map: no card within `GAP` of another, and no mode's
 * words over a card, a badge, a line or other words — each is set beside its badge, on the side
 * that is clear. The ellipse is made taller while that is not met, and — when no height up to
 * `MAX_HEIGHT` meets it, or the width is below `LIST_BELOW` — there is no drawing at all: the
 * devices are listed instead.
 */

export interface Box {
  cx: number;
  cy: number;
  w: number;
  h: number;
}

export interface Point {
  x: number;
  y: number;
}

export type DiagramLayout =
  | {
      mode: "drawing";
      width: number;
      height: number;
      self: Box;
      /** One per device, in the order given. */
      peers: Box[];
      /** Where each device's mode words go, centred; none for a device whose line shows none. */
      labels: (Box | undefined)[];
      /** The "sync with another device" card, when there is one. */
      add?: Box;
    }
  | { mode: "list" };

/** Below this width the diagram lists the devices, each with a small two-arrow drawing. */
export const LIST_BELOW = 640;
export const SELF_W = 240;
export const SELF_H = 88;
export const PEER_W = 176;
export const PEER_H = 64;
/** The least room between two cards. */
export const GAP = 12;
const MIN_HEIGHT = 200;

export const MAX_HEIGHT = 760;
/** The least length of a spoke between its two cards: room for both arrowheads and the badge. */
export const MIN_SPOKE = 48;
/** How far out the devices stand at most: a wide page keeps the drawing together. */
const MAX_RADIUS_X = 440;
const HEIGHT_STEP = 40;
const MARGIN = 8;

/** The mode's words on a spoke: their size, and the height of their line. */
export const LABEL_FONT_SIZE = 10;
export const LABEL_H = 12;
/** The least room between the mode's words and anything else drawn. */
export const LABEL_GAP = 4;
/** How far the two directions of a spoke run from its middle line. */
export const LANE_OFFSET = 5;
/** Half an arrowhead's width: how far an arrow reaches beside its line. */
const ARROW_HALF = 4.5;

/** A badge with its warning mark, around its centre: what the words keep clear of. */
export const BADGE_HALF = 14;

const apart = (a: Box, b: Box) =>
  Math.abs(a.cx - b.cx) >= (a.w + b.w) / 2 + GAP || Math.abs(a.cy - b.cy) >= (a.h + b.h) / 2 + GAP;

/** Every pair of cards is at least `GAP` apart. */
export const noOverlap = (boxes: Box[]) =>
  boxes.every((box, i) => boxes.slice(i + 1).every((other) => apart(box, other)));

/** Whether two boxes come within `gap` of each other. */
export const boxesMeet = (a: Box, b: Box, gap = 0) =>
  Math.abs(a.cx - b.cx) < (a.w + b.w) / 2 + gap && Math.abs(a.cy - b.cy) < (a.h + b.h) / 2 + gap;

/** Whether the segment from `a` to `b` comes within `pad` of the box (Liang–Barsky clipping). */
export const segmentMeetsBox = (a: Point, b: Point, box: Box, pad = 0) => {
  const [left, right] = [box.cx - box.w / 2 - pad, box.cx + box.w / 2 + pad];
  const [top, bottom] = [box.cy - box.h / 2 - pad, box.cy + box.h / 2 + pad];
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  let t0 = 0;
  let t1 = 1;

  for (const [p, q] of [
    [-dx, a.x - left],
    [dx, right - a.x],
    [-dy, a.y - top],
    [dy, bottom - a.y],
  ]) {
    if (p === 0) {
      if (q < 0) return false;
      continue;
    }
    const t = q / p;

    if (p < 0) t0 = Math.max(t0, t);
    else t1 = Math.min(t1, t);
    if (t0 > t1) return false;
  }

  return true;
};

/**
 * Lays the diagram out for `count` devices, `withAdd` the "sync with another device" card, and
 * `labelWidths` the measured width of each device's mode words (0 or none where its line shows
 * none).
 */
export function layoutSyncDiagram(
  width: number,
  count: number,
  withAdd: boolean,
  labelWidths: readonly number[] = [],
): DiagramLayout {
  if (width < LIST_BELOW) return { mode: "list" };
  const total = count + (withAdd ? 1 : 0);

  // From the left, clockwise: one device stands beside this one, two on either side — or, where
  // the width leaves no room beside it, turned half a step or a quarter, so none stands there.
  for (const turn of [0, 0.5, 0.25])
    for (let height = MIN_HEIGHT; height <= MAX_HEIGHT; height += HEIGHT_STEP) {
      const cx = width / 2;
      const cy = height / 2;
      const self: Box = { cx, cy, w: SELF_W, h: SELF_H };

      if (total === 0) return { mode: "drawing", width, height, self, peers: [], labels: [] };
      const rx = Math.min(width / 2 - PEER_W / 2 - MARGIN, MAX_RADIUS_X);
      const ry = height / 2 - PEER_H / 2 - MARGIN;
      const boxes: Box[] = Array.from({ length: total }, (_, i) => {
        const angle = Math.PI + (2 * Math.PI * (i + turn)) / total;

        return {
          cx: Math.round(cx + rx * Math.cos(angle)),
          cy: Math.round(cy + ry * Math.sin(angle)),
          w: PEER_W,
          h: PEER_H,
        };
      });

      if (!noOverlap([self, ...boxes])) continue;
      if (boxes.some((box) => spokeLength(self, box) < MIN_SPOKE)) continue;
      const peers = boxes.slice(0, count);
      const labels = placeLabels(
        width,
        height,
        self,
        boxes,
        peers.map((_, i) => labelWidths[i]),
      );

      if (!labels) continue;

      return {
        mode: "drawing",
        width,
        height,
        self,
        peers,
        labels,
        add: withAdd ? boxes[count] : undefined,
      };
    }

  return { mode: "list" };
}

/**
 * Sets each device's mode words beside its badge, on the preferred side of its spoke or else the
 * other one — wherever they keep clear of every card, badge, line and other words, inside the
 * drawing. Undefined when some device's words fit on neither side.
 */
const placeLabels = (
  width: number,
  height: number,
  self: Box,
  boxes: Box[],
  labelWidths: (number | undefined)[],
): (Box | undefined)[] | undefined => {
  const spokes = boxes.map((box) => spokeGeometry(self, box));
  const placed: Box[] = [];
  const labels: (Box | undefined)[] = [];

  const clear = (label: Box) =>
    label.cx - label.w / 2 >= LABEL_GAP &&
    label.cx + label.w / 2 <= width - LABEL_GAP &&
    label.cy - label.h / 2 >= LABEL_GAP &&
    label.cy + label.h / 2 <= height - LABEL_GAP &&
    [self, ...boxes, ...placed].every((box) => !boxesMeet(label, box, LABEL_GAP)) &&
    spokes.every(
      (spoke) =>
        !boxesMeet(label, { cx: spoke.badge.x, cy: spoke.badge.y, w: 0, h: 0 }, BADGE_HALF) &&
        !segmentMeetsBox(spoke.from, spoke.to, label, LANE_OFFSET + ARROW_HALF),
    );

  for (const [index, labelWidth] of labelWidths.entries()) {
    if (!labelWidth) {
      labels.push(undefined);
      continue;
    }
    const spoke = spokes[index];
    const preferred = spoke.normal.y >= 0 ? 1 : -1;
    const label = [preferred, -preferred]
      .map((side) => labelBox(spoke, labelWidth, side))
      .find(clear);

    if (!label) return undefined;
    placed.push(label);
    labels.push(label);
  }

  return labels;
};

/**
 * The mode's words beside a spoke's badge, on one `side` of it: far enough along the spoke's
 * normal that, measured along it, they clear the badge with its mark and both directions' arrows
 * — set off by half their width where the spoke runs up and down, by half their height where it
 * runs across.
 */
export const labelBox = (spoke: SpokeGeometry, labelWidth: number, side: number): Box => {
  const { x: nx, y: ny } = spoke.normal;
  const along = (halfWidth: number, halfHeight: number) =>
    halfWidth * Math.abs(nx) + halfHeight * Math.abs(ny);
  const distance =
    Math.max(along(BADGE_HALF, BADGE_HALF), LANE_OFFSET + ARROW_HALF) +
    along(labelWidth / 2, LABEL_H / 2) +
    LABEL_GAP / 2;

  return {
    cx: spoke.badge.x + nx * side * distance,
    cy: spoke.badge.y + ny * side * distance,
    w: labelWidth,
    h: LABEL_H,
  };
};

/** How long a spoke is between the edges of its two cards. */
export const spokeLength = (self: Box, peer: Box) => {
  const { from, to } = spokeGeometry(self, peer);

  return Math.hypot(to.x - from.x, to.y - from.y);
};

/** Where the line from `from` towards `to` leaves `box`, `margin` outside its edge. */
export const exitPoint = (box: Box, to: Point, margin = 4): Point => {
  const dx = to.x - box.cx;
  const dy = to.y - box.cy;

  if (dx === 0 && dy === 0) return { x: box.cx, y: box.cy };
  const sx = dx === 0 ? Infinity : (box.w / 2 + margin) / Math.abs(dx);
  const sy = dy === 0 ? Infinity : (box.h / 2 + margin) / Math.abs(dy);
  const s = Math.min(sx, sy);

  return { x: box.cx + dx * s, y: box.cy + dy * s };
};

export interface SpokeGeometry {
  /** At the edge of this device's card. */
  from: Point;
  /** At the edge of the other device's card. */
  to: Point;
  /** The receive direction (the other device → this one), set off to one side. */
  receive: { from: Point; to: Point };
  /** The may-read direction (this device → the other one), set off to the other side. */
  read: { from: Point; to: Point };
  badge: Point;
  /** A unit normal to the spoke: the two directions run side by side along it. */
  normal: Point;
}

export const spokeGeometry = (self: Box, peer: Box): SpokeGeometry => {
  const from = exitPoint(self, { x: peer.cx, y: peer.cy });
  const to = exitPoint(peer, { x: self.cx, y: self.cy });
  const length = Math.hypot(to.x - from.x, to.y - from.y) || 1;
  const nx = -(to.y - from.y) / length;
  const ny = (to.x - from.x) / length;
  const shift = (point: Point, by: number): Point => ({
    x: point.x + nx * by,
    y: point.y + ny * by,
  });

  return {
    from,
    to,
    receive: { from: shift(to, -LANE_OFFSET), to: shift(from, -LANE_OFFSET) },
    read: { from: shift(from, LANE_OFFSET), to: shift(to, LANE_OFFSET) },
    badge: { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 },
    normal: { x: nx, y: ny },
  };
};
