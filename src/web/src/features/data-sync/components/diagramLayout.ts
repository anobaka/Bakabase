/*
 * Where the data sync diagram puts its cards: this device in the middle, every other device
 * around it on an ellipse, each on its own spoke. Pure and deterministic, so the same devices
 * at the same width are always drawn the same way.
 *
 * Readable at any count, like the device map: no card within `GAP` of another, the ellipse
 * made taller while that is not met, and — when no height up to `MAX_HEIGHT` meets it, or the
 * width is below `LIST_BELOW` — no drawing at all: the devices are listed instead.
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

const apart = (a: Box, b: Box) =>
  Math.abs(a.cx - b.cx) >= (a.w + b.w) / 2 + GAP || Math.abs(a.cy - b.cy) >= (a.h + b.h) / 2 + GAP;

/** Every pair of cards is at least `GAP` apart. */
export const noOverlap = (boxes: Box[]) =>
  boxes.every((box, i) => boxes.slice(i + 1).every((other) => apart(box, other)));

export function layoutSyncDiagram(width: number, count: number, withAdd: boolean): DiagramLayout {
  if (width < LIST_BELOW) return { mode: "list" };
  const total = count + (withAdd ? 1 : 0);

  // From the left, clockwise: one device stands beside this one, two on either side — or, where
  // the width leaves no room beside it, turned half a step or a quarter, so none stands there.
  for (const turn of [0, 0.5, 0.25])
    for (let height = MIN_HEIGHT; height <= MAX_HEIGHT; height += HEIGHT_STEP) {
      const cx = width / 2;
      const cy = height / 2;
      const self: Box = { cx, cy, w: SELF_W, h: SELF_H };

      if (total === 0) return { mode: "drawing", width, height, self, peers: [] };
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

      return {
        mode: "drawing",
        width,
        height,
        self,
        peers: boxes.slice(0, count),
        add: withAdd ? boxes[count] : undefined,
      };
    }

  return { mode: "list" };
}

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
  /** Where the mode's words go, beside the badge. */
  label: Point;
}

const LANE_OFFSET = 5;

export const spokeGeometry = (self: Box, peer: Box): SpokeGeometry => {
  const from = exitPoint(self, { x: peer.cx, y: peer.cy });
  const to = exitPoint(peer, { x: self.cx, y: self.cy });
  const length = Math.hypot(to.x - from.x, to.y - from.y) || 1;
  // A unit normal: the two directions run side by side along it.
  const nx = -(to.y - from.y) / length;
  const ny = (to.x - from.x) / length;
  const shift = (point: Point, by: number): Point => ({
    x: point.x + nx * by,
    y: point.y + ny * by,
  });
  const middle = { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 };
  const side = ny >= 0 ? 1 : -1;

  return {
    from,
    to,
    receive: { from: shift(to, -LANE_OFFSET), to: shift(from, -LANE_OFFSET) },
    read: { from: shift(from, LANE_OFFSET), to: shift(to, LANE_OFFSET) },
    badge: middle,
    // Below the line where it runs across, beside it where it runs up and down.
    label: shift(middle, side * 20),
  };
};
