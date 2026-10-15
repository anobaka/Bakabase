import type { DeviceGraph } from "../map/graph";
import type { MapLayout, NodeBox } from "../map/layout";

import { describe, expect, it } from "vitest";

import { buildDeviceGraph, SELF_ID } from "../map/graph";
import {
  layoutDeviceMap,
  MAX_ASPECT,
  MAX_SHOWN_HEIGHT,
  MAX_WIDTH,
  MIN_SCALE,
  MIN_WIDTH,
  spokeAngles,
} from "../map/layout";

import { manager, server, servers, access, status } from "./deviceMapFixtures";

import { ManagedServerState } from "@/sdk/constants";

/** `n` other devices, each connected some way, a few found nearby as well. */
const graphOf = (n: number, ghosts = 0) =>
  buildDeviceGraph({
    status: status(),
    servers: servers({
      // The same installs, managed from here too: one device each.
      servers: Array.from({ length: n }, (_, i) =>
        server(`p${i}`, {
          name: `Device ${String.fromCharCode(65 + i)}`,
          address: `http://10.0.0.${i + 1}:34567`,
        }),
      ),
    }),
    discovery: {
      management: Array.from({ length: ghosts }, (_, i) => ({
        serverId: `g${i}`,
        appVersion: "2.4.0",
        alreadyManaged: false,
        name: `Nearby ${i}`,
        address: `http://10.9.0.${i + 1}:34567`,
      })),
    },
  });

const overlap = (a: NodeBox, b: NodeBox) =>
  Math.abs(a.cx - b.cx) < (a.w + b.w) / 2 && Math.abs(a.cy - b.cy) < (a.h + b.h) / 2;

const widths = [MIN_WIDTH, 640, 760, 1100];

describe("device map layout", () => {
  it.each([0, 1, 2, 3, 5, 8, 12])(
    "keeps %i devices apart and on the canvas at every width",
    (n) => {
      for (const width of widths) {
        const layout = layoutDeviceMap(graphOf(n, n ? 3 : 0), width);
        const boxes = [...layout.boxes.values()];

        expect(boxes).toHaveLength(n + 1 + (n ? 3 : 0));
        for (const box of boxes) {
          expect(box.cx - box.w / 2, `${box.id} @${width}`).toBeGreaterThanOrEqual(0);
          expect(box.cx + box.w / 2, `${box.id} @${width}`).toBeLessThanOrEqual(layout.width);
          expect(box.cy - box.h / 2, `${box.id} @${width}`).toBeGreaterThanOrEqual(0);
          expect(box.cy + box.h / 2, `${box.id} @${width}`).toBeLessThanOrEqual(layout.height);
        }
        boxes.forEach((a, i) =>
          boxes
            .slice(i + 1)
            .forEach((b) => expect(overlap(a, b), `${a.id} × ${b.id} @${width}`).toBe(false)),
        );
      }
    },
  );

  // Checked apart from the layout's own test of it: points along every drawn line, and round
  // every badge, against every card that is not one of the line's two ends.
  const runsOver = (layout: MapLayout, graph: DeviceGraph) => {
    const problems: string[] = [];
    const inside = (x: number, y: number, box: NodeBox, pad: number) =>
      Math.abs(x - box.cx) < box.w / 2 + pad && Math.abs(y - box.cy) < box.h / 2 + pad;

    for (const edge of graph.edges) {
      const geometry = layout.edges.get(edge.id)!;
      const others = [...layout.boxes.values()].filter(
        (box) => box.id !== edge.nodeId && box.id !== SELF_ID,
      );

      for (const box of others) {
        for (const lane of geometry.lanes) {
          const steps = Math.ceil(Math.hypot(lane.to.x - lane.from.x, lane.to.y - lane.from.y));

          for (let step = 0; step <= steps; step++) {
            const x = lane.from.x + ((lane.to.x - lane.from.x) * step) / steps;
            const y = lane.from.y + ((lane.to.y - lane.from.y) * step) / steps;

            if (inside(x, y, box, 2)) {
              problems.push(`line ${edge.id} over ${box.id}`);
              break;
            }
          }
        }
        for (let angle = 0; angle < 360; angle += 15) {
          const radians = (angle * Math.PI) / 180;

          if (
            inside(
              geometry.badge.x + 10 * Math.cos(radians),
              geometry.badge.y + 10 * Math.sin(radians),
              box,
              2,
            )
          ) {
            problems.push(`badge of ${edge.id} over ${box.id}`);
            break;
          }
        }
      }
    }

    return [...new Set(problems)];
  };

  /** How tall the devices and their lines are drawn, as shown — the rows found nearby apart. */
  const shownHeight = (layout: MapLayout) => {
    const ring = [...layout.boxes.values()].filter((box) => !box.id.startsWith("ghost:"));

    return (
      (Math.max(...ring.map((box) => box.cy + box.h / 2)) -
        Math.min(...ring.map((box) => box.cy - box.h / 2))) *
      layout.scale
    );
  };
  /** What every drawn map is: nothing over a card, nothing smaller or taller than it reads. */
  const expectReadable = (layout: MapLayout, graph: DeviceGraph, what: string) => {
    expect(runsOver(layout, graph), what).toEqual([]);
    expect(layout.scale, what).toBeGreaterThanOrEqual(MIN_SCALE);
    expect(shownHeight(layout), what).toBeLessThanOrEqual(MAX_SHOWN_HEIGHT);
    expect(shownHeight(layout) / (layout.width * layout.scale), what).toBeLessThanOrEqual(
      MAX_ASPECT,
    );
  };
  const allWidths = [MIN_WIDTH, 600, 640, 760, 880, 1100, MAX_WIDTH, 1280, 1440];

  it("never runs a line or a badge over another card, nor draws a map smaller or taller than it reads", () => {
    for (let n = 1; n <= 30; n++) {
      for (const width of allWidths) {
        const graph = graphOf(n, 3);
        const layout = layoutDeviceMap(graph, width);

        // Not drawn at all otherwise: the page lists the devices instead.
        if (layout.readable) expectReadable(layout, graph, `${n} devices @${width}`);
      }
    }
  });

  it("draws a handful of devices at any width, and a dozen wherever there is room", () => {
    for (let n = 1; n <= 14; n++) {
      for (const width of allWidths) {
        const needs = n <= 8 ? MIN_WIDTH : n <= 10 ? 760 : 880;

        if (width >= needs)
          expect(layoutDeviceMap(graphOf(n, 3), width).readable, `${n} devices @${width}`).toBe(
            true,
          );
      }
    }
  });

  // Machine names, as most devices have, and what they are under them.
  const namedGraph = (n: number) =>
    buildDeviceGraph({
      servers: servers({
        servers: Array.from({ length: n }, (_, i) =>
          server(`p${i}`, {
            name: `DESKTOP-${String(1000 + i)}QH`,
            address: `http://10.0.0.${i + 1}:34567`,
          }),
        ),
      }),
    });

  it.each([20, 24])(
    "with %i devices at 1100–1440 px, draws a map only where it reads, and lists them otherwise",
    (n) => {
      for (const width of [1100, 1180, 1280, 1366, 1440]) {
        for (const graph of [graphOf(n, 3), namedGraph(n)]) {
          const layout = layoutDeviceMap(graph, width, () => "Desktop app · Windows");

          if (layout.readable) expectReadable(layout, graph, `${n} devices @${width}`);
        }
      }
      // Machine names at the desktop app's smallest window: a list, never a column of shrunk
      // cards.
      expect(layoutDeviceMap(namedGraph(n), 1100, () => "Desktop app · Windows").readable).toBe(
        false,
      );
    },
  );

  it("gives every spoke room for its arrows and badge", () => {
    for (const n of [1, 3, 6, 8]) {
      const layout = layoutDeviceMap(graphOf(n), 760);

      for (const edge of layout.edges.values()) {
        const length = Math.hypot(edge.to.x - edge.from.x, edge.to.y - edge.from.y);

        expect(length, `${edge.id} of ${n}`).toBeGreaterThan(60);
      }
    }
  });

  it("puts nobody level with this device, where wide cards would leave no line between", () => {
    for (let n = 1; n <= 12; n++) {
      for (const angle of spokeAngles(n)) {
        expect(Math.abs(Math.sin((angle * Math.PI) / 180)), `${angle} of ${n}`).toBeGreaterThan(
          0.3,
        );
      }
    }
    expect(spokeAngles(1)).toEqual([-90]);
    expect(spokeAngles(2)).toEqual([-90, 90]);
  });

  it("does not move anything when only a status changes", () => {
    const before = buildDeviceGraph({
      servers: servers({ servers: [server("a"), server("b"), server("c")] }),
      access: access({ devices: [manager("d1", "Phone")] }),
    });
    const after = buildDeviceGraph({
      servers: servers({
        servers: [
          server("a", { state: ManagedServerState.Offline }),
          server("b", { state: ManagedServerState.WrongServer }),
          server("c"),
        ],
      }),
      access: access({ devices: [manager("d1", "Phone")] }),
    });

    expect(layoutDeviceMap(after, 800).boxes).toEqual(layoutDeviceMap(before, 800).boxes);
  });

  it("puts this device in the middle and the devices found nearby in rows underneath", () => {
    const layout = layoutDeviceMap(graphOf(4, 6), 760);
    const self = layout.boxes.get(SELF_ID)!;
    const ghosts = [...layout.boxes.values()].filter((box) => box.id.startsWith("ghost:"));
    const connected = [...layout.boxes.values()].filter((box) => box.id.startsWith("server:"));

    expect(self.cx).toBe(layout.width / 2);
    expect(ghosts).toHaveLength(6);
    const lowestConnected = Math.max(...connected.map((box) => box.cy + box.h / 2));

    for (const ghost of ghosts) expect(ghost.cy - ghost.h / 2).toBeGreaterThan(lowestConnected);
    expect(layout.ghostTop).toBeGreaterThan(lowestConnected);
  });

  it("clamps the width it lays out for", () => {
    expect(layoutDeviceMap(graphOf(2), 100).width).toBe(MIN_WIDTH);
    expect(layoutDeviceMap(graphOf(2), 5000).width).toBe(MAX_WIDTH);
    expect(layoutDeviceMap(graphOf(2), 600).compact).toBe(true);
    expect(layoutDeviceMap(graphOf(2), 900).compact).toBe(false);
  });
});
