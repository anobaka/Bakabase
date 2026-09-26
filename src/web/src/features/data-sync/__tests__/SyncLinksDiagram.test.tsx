import type { Box, Point } from "../components/diagramLayout";

import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import SyncLinksDiagram, { wrapLabel } from "../components/SyncLinksDiagram";
import {
  GAP,
  layoutSyncDiagram,
  LIST_BELOW,
  noOverlap,
  PEER_W,
  spokeGeometry,
} from "../components/diagramLayout";
import { lineMode, syncPeerFromLink, syncPeersOf } from "../viewModels";

import { link, mapView, NOW, outgoing, reader } from "./dataSyncFixtures";

import { SEMIBOLD, textWidth } from "@/features/federation/map/text";
import en from "@/locales/en/pages/dataSync.json";
import cn from "@/locales/cn/pages/dataSync.json";
import { DataSyncLinkMode, DataSyncLinkState } from "@/sdk/constants";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options
        ? [
            key,
            ...Object.entries(options)
              .filter(([name, value]) => name !== "defaultValue" && value !== undefined)
              .map(([, value]) => String(value)),
          ].join(" ")
        : key,
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => false },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));

const self = {
  name: "This PC",
  kinds: [
    { kind: "customProperty", count: 100 },
    { kind: "extensionGroup", count: 8 },
  ],
  sharingEnabled: true,
  headless: false,
};

/** A home office: a NAS in step both ways with decisions waiting there, a PC asked, a reader. */
const peers = () =>
  syncPeersOf({
    links: [
      link(1, "node-nas", "NAS", {
        openItems: 9,
        peerAttention: {
          headless: true,
          openDecisions: 2,
          pausedLinks: 0,
          restorePending: false,
          awaitingReview: 0,
        },
      }),
      link(2, "node-pc2", "PC-2", {
        mode: DataSyncLinkMode.Follow,
        state: DataSyncLinkState.AwaitingAccess,
        peerMayReadUs: false,
        peerModeTowardsUs: undefined,
      }),
      link(5, "node-htpc", "HTPC", {
        mode: DataSyncLinkMode.Follow,
        state: DataSyncLinkState.Paused,
        pausedReason: 1,
        peerMayReadUs: false,
      }),
    ],
    map: mapView({ outgoing: [outgoing(2, "node-pc2", "PC-2")] }),
    readers: [reader("node-reader", "Reader PC")],
  });

afterEach(cleanup);

/** Whether the segment from `a` to `b` enters the box (Liang–Barsky clipping). */
const crosses = (a: Point, b: Point, box: Box) => {
  const [left, right] = [box.cx - box.w / 2, box.cx + box.w / 2];
  const [top, bottom] = [box.cy - box.h / 2, box.cy + box.h / 2];
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

describe("where the diagram puts its cards", () => {
  it("lists the devices below 640 px", () => {
    expect(layoutSyncDiagram(LIST_BELOW - 1, 3, true)).toEqual({ mode: "list" });
  });

  it.each([0, 1, 2, 3, 5, 8])(
    "draws %i devices without any card within the gap of another",
    (count) => {
      for (const width of [640, 820, 1100, 1400]) {
        const layout = layoutSyncDiagram(width, count, true);

        if (layout.mode === "list") continue;
        expect(layout.peers).toHaveLength(count);
        expect(noOverlap([layout.self, ...layout.peers, ...(layout.add ? [layout.add] : [])])).toBe(
          true,
        );
        // Every card inside the drawing.
        for (const box of [layout.self, ...layout.peers])
          expect(box.cx - box.w / 2).toBeGreaterThanOrEqual(0);
      }
    },
  );

  it("never runs a line over another device's card", () => {
    for (let count = 1; count <= 12; count += 1)
      for (let width = 640; width <= 1500; width += 20) {
        const layout = layoutSyncDiagram(width, count, true);

        if (layout.mode === "list") continue;
        const boxes = [...layout.peers, ...(layout.add ? [layout.add] : [])];

        boxes.forEach((box, index) => {
          const { from, to } = spokeGeometry(layout.self, box);

          boxes.forEach((other, at) => {
            if (at !== index) expect(crosses(from, to, other), `${count} at ${width}`).toBe(false);
          });
        });
      }
  });

  describe("the mode's words on each line", () => {
    /** "both ways" and "receive only" as the drawing measures them, and the Chinese 双向. */
    const words = [52, 63, 22];
    /** A box grown by `by` on every side. */
    const grown = (box: Box, by: number): Box => ({ ...box, w: box.w + 2 * by, h: box.h + 2 * by });
    const meet = (a: Box, b: Box) =>
      Math.abs(a.cx - b.cx) < (a.w + b.w) / 2 && Math.abs(a.cy - b.cy) < (a.h + b.h) / 2;
    /** How far either direction's arrows reach beside a spoke's middle line. */
    const lanes = 5 + 4.5;
    /** A badge with its warning mark. */
    const badge = (at: Point): Box => ({ cx: at.x, cy: at.y, w: 28, h: 28 });

    it("sets them clear of every card, badge, line and other words, at any count and width", () => {
      let drawn = 0;

      for (let count = 1; count <= 12; count += 1)
        for (let width = 640; width <= 1500; width += 20)
          for (const withAdd of [false, true]) {
            const widths = Array.from({ length: count }, (_, i) => words[i % words.length]);
            const layout = layoutSyncDiagram(width, count, withAdd, widths);
            const at = `${count}${withAdd ? " + add" : ""} at ${width}`;

            if (layout.mode === "list") continue;
            drawn += 1;
            const cards = [layout.self, ...layout.peers, ...(layout.add ? [layout.add] : [])];
            const spokes = cards.slice(1).map((card) => spokeGeometry(layout.self, card));

            expect(layout.labels, at).toHaveLength(count);
            layout.labels.forEach((label, index) => {
              expect(label, at).toBeDefined();
              if (!label) return;
              expect(label.w, at).toBe(widths[index]);
              // Inside the drawing.
              expect(label.cx - label.w / 2, at).toBeGreaterThanOrEqual(0);
              expect(label.cx + label.w / 2, at).toBeLessThanOrEqual(layout.width);
              expect(label.cy - label.h / 2, at).toBeGreaterThanOrEqual(0);
              expect(label.cy + label.h / 2, at).toBeLessThanOrEqual(layout.height);
              for (const card of cards) expect(meet(label, card), `card, ${at}`).toBe(false);
              for (const spoke of spokes) {
                expect(meet(label, badge(spoke.badge)), `badge, ${at}`).toBe(false);
                expect(crosses(spoke.from, spoke.to, grown(label, lanes)), `line, ${at}`).toBe(
                  false,
                );
              }
              layout.labels.forEach((other, at2) => {
                if (other && at2 !== index) expect(meet(label, other), `words, ${at}`).toBe(false);
              });
            });
          }

      // Nearly every case is still drawn: a list only where the words find no room.
      expect(drawn).toBeGreaterThan(900);
    });

    it("sets them beside a line that runs up and down, not across it", () => {
      // Three devices and the way to add one at 960 px: one device straight above this one.
      const layout = layoutSyncDiagram(960, 3, true, [52, 52, 52]);

      if (layout.mode !== "drawing") throw new Error("drawn");
      const top = layout.peers.findIndex((peer) => peer.cx === layout.self.cx);
      const label = layout.labels[top]!;

      expect(top).toBeGreaterThanOrEqual(0);
      expect(
        label.cx - label.w / 2 >= layout.self.cx + lanes ||
          label.cx + label.w / 2 <= layout.self.cx - lanes,
      ).toBe(true);
    });

    it("draws the words where the layout set them", () => {
      render(
        <SyncLinksDiagram
          initialWidth={1100}
          now={NOW}
          peers={peers()}
          self={self}
          onSelect={vi.fn()}
        />,
      );
      const layout = layoutSyncDiagram(
        1100,
        4,
        false,
        peers().map((peer) => {
          const mode = lineMode(peer);

          return mode ? Math.ceil(textWidth(`federation.map.sync.mode.${mode}`, 10) * SEMIBOLD) : 0;
        }),
      );

      if (layout.mode !== "drawing") throw new Error("drawn");
      const nas = screen
        .getAllByTestId("data-sync-spoke")
        .find((spoke) => spoke.getAttribute("data-sync-peer") === "node-nas")!;
      const text = within(nas).getByTestId("data-sync-spoke-mode");

      expect(Number(text.getAttribute("x"))).toBeCloseTo(layout.labels[1]!.cx);
      expect(Number(text.getAttribute("y"))).toBeCloseTo(layout.labels[1]!.cy);
    });
  });

  it("draws a few devices at a desktop width, and lists a crowd", () => {
    expect(layoutSyncDiagram(960, 4, true).mode).toBe("drawing");
    expect(layoutSyncDiagram(700, 30, true).mode).toBe("list");
  });

  it("is the same every time", () => {
    expect(layoutSyncDiagram(960, 5, true)).toEqual(layoutSyncDiagram(960, 5, true));
    expect(GAP).toBeGreaterThan(0);
  });

  it("runs the two directions side by side, from card edge to card edge", () => {
    const layout = layoutSyncDiagram(960, 1, false);

    if (layout.mode !== "drawing") throw new Error("drawn");
    const spoke = spokeGeometry(layout.self, layout.peers[0]);

    // The one device stands to the left: the line leaves this device's card on its left edge.
    expect(spoke.from.x).toBeLessThan(layout.self.cx - layout.self.w / 2);
    expect(spoke.to.x).toBeGreaterThan(layout.peers[0].cx + layout.peers[0].w / 2);
    expect(spoke.receive.from.y).not.toBe(spoke.read.from.y);
  });
});

describe("the diagram", () => {
  it("draws this device, every device on a spoke of its own, and the way to add one", () => {
    const onAdd = vi.fn();

    render(
      <SyncLinksDiagram
        initialWidth={1100}
        now={NOW}
        peers={peers()}
        self={self}
        onAdd={onAdd}
        onSelect={vi.fn()}
      />,
    );

    expect(screen.getByTestId("data-sync-diagram")).toHaveAttribute("data-mode", "drawing");
    expect(screen.getByTestId("data-sync-self")).toHaveTextContent("This PC");
    const cards = screen.getAllByTestId("data-sync-peer");

    expect(cards.map((card) => card.getAttribute("data-sync-peer"))).toEqual([
      "node-htpc",
      "node-nas",
      "node-pc2",
      "node-reader",
    ]);
    const spokes = screen.getAllByTestId("data-sync-spoke");

    expect(spokes).toHaveLength(4);
    const nas = spokes.find((spoke) => spoke.getAttribute("data-sync-peer") === "node-nas")!;

    expect(nas).toHaveAttribute("data-in", "active");
    expect(nas).toHaveAttribute("data-out", "active");
    expect(within(nas).getByTestId("data-sync-spoke-mode")).toHaveTextContent(
      "federation.map.sync.mode.twoWay",
    );
    const pc2 = spokes.find((spoke) => spoke.getAttribute("data-sync-peer") === "node-pc2")!;

    expect(pc2).toHaveAttribute("data-in", "pending");
    expect(pc2).toHaveAttribute("data-out", "none");
    const htpc = spokes.find((spoke) => spoke.getAttribute("data-sync-peer") === "node-htpc")!;

    // Paused: set up, not working — dotted, with the attention mark.
    expect(htpc.querySelector('[data-direction="receive"]')).toHaveAttribute(
      "data-attention",
      "true",
    );
    expect(htpc.querySelector("[data-attention-mark]")).not.toBeNull();
    const readerSpoke = spokes.find(
      (spoke) => spoke.getAttribute("data-sync-peer") === "node-reader",
    )!;

    expect(readerSpoke).toHaveAttribute("data-in", "none");
    expect(readerSpoke).toHaveAttribute("data-out", "active");

    fireEvent.click(screen.getByTestId("data-sync-add"));
    expect(onAdd).toHaveBeenCalled();
  });

  it("puts what needs you, and what waits there, on the card", () => {
    render(
      <SyncLinksDiagram
        initialWidth={1100}
        now={NOW}
        peers={peers()}
        self={self}
        onSelect={vi.fn()}
      />,
    );
    const nas = screen
      .getAllByTestId("data-sync-peer")
      .find((card) => card.getAttribute("data-sync-peer") === "node-nas")!;

    expect(within(nas).getByTestId("data-sync-open-items")).toHaveTextContent("9");
    expect(within(nas).getByTestId("data-sync-waits-there")).toHaveTextContent("2");
    expect(nas).toHaveAccessibleName(
      expect.stringContaining("dataSync.status.NeedsYouThere NAS 2"),
    );
    expect(screen.queryByTestId("data-sync-add")).toBeNull();
  });

  it("opens a device from its card or its spoke, by keyboard or pointer", () => {
    const onSelect = vi.fn();

    render(
      <SyncLinksDiagram
        initialWidth={1100}
        now={NOW}
        peers={peers()}
        self={self}
        onSelect={onSelect}
      />,
    );
    const card = screen.getAllByTestId("data-sync-peer")[1];
    const spoke = screen.getAllByTestId("data-sync-spoke")[1];

    expect(card).toHaveAttribute("role", "button");
    expect(card).toHaveAttribute("tabindex", "0");
    fireEvent.keyDown(card, { key: "Enter" });
    expect(onSelect).toHaveBeenLastCalledWith("node-nas", "keyboard", "card");
    fireEvent.keyDown(spoke, { key: " " });
    expect(onSelect).toHaveBeenLastCalledWith("node-nas", "keyboard", "spoke");
    fireEvent.click(spoke);
    expect(onSelect).toHaveBeenLastCalledWith("node-nas", "pointer", "spoke");
  });

  it("takes the keyboard to each device, then to its line, as the device map does", () => {
    render(
      <SyncLinksDiagram
        initialWidth={1100}
        now={NOW}
        peers={peers()}
        self={self}
        onAdd={vi.fn()}
        onSelect={vi.fn()}
      />,
    );
    const order = Array.from(
      screen.getByTestId("data-sync-diagram").querySelectorAll<HTMLElement>('[tabindex="0"]'),
    ).map((element) =>
      element.dataset.syncPeer
        ? `${element.dataset.syncPart}:${element.dataset.syncPeer}`
        : element.dataset.testid,
    );

    expect(order).toEqual([
      "card:node-htpc",
      "spoke:node-htpc",
      "card:node-nas",
      "spoke:node-nas",
      "card:node-pc2",
      "spoke:node-pc2",
      "card:node-reader",
      "spoke:node-reader",
      "data-sync-add",
    ]);
  });

  it("says every device, both ways, to a screen reader", () => {
    render(
      <SyncLinksDiagram
        initialWidth={1100}
        now={NOW}
        peers={peers()}
        self={self}
        onSelect={vi.fn()}
      />,
    );
    const items = within(screen.getByTestId("data-sync-diagram-summary")).getAllByRole("listitem");

    expect(items).toHaveLength(5);
    expect(items[0]).toHaveTextContent("dataSync.diagram.selfSummary This PC");
    expect(items[2]).toHaveTextContent(
      "federation.map.direction.sync.in.active NAS. federation.map.direction.sync.out.active NAS. federation.map.sync.mode.twoWay",
    );
  });

  it("lists the devices when it is narrow, each with its two directions", () => {
    const onSelect = vi.fn();

    render(
      <SyncLinksDiagram
        initialWidth={500}
        now={NOW}
        peers={peers()}
        self={self}
        onAdd={vi.fn()}
        onSelect={onSelect}
      />,
    );

    expect(screen.getByTestId("data-sync-diagram")).toHaveAttribute("data-mode", "list");
    const rows = screen.getAllByTestId("data-sync-peer-row");

    expect(rows).toHaveLength(4);
    expect(rows[1]).toHaveAttribute("data-sync-peer", "node-nas");
    expect(rows[1].querySelector("[data-in]")).toHaveAttribute("data-in", "active");
    fireEvent.click(rows[1], { detail: 1 });
    expect(onSelect).toHaveBeenLastCalledWith("node-nas", "pointer", "card");
    expect(screen.getByTestId("data-sync-add")).toBeInTheDocument();
  });

  it("names the modes on the line the way the map does: mutual Follow is both ways", () => {
    const mutual = syncPeerFromLink(
      link(3, "node-lap", "Laptop", { mode: DataSyncLinkMode.Follow, peerModeTowardsUs: "follow" }),
    );

    render(
      <SyncLinksDiagram
        initialWidth={1100}
        now={NOW}
        peers={[mutual]}
        self={self}
        onSelect={vi.fn()}
      />,
    );
    expect(screen.getByTestId("data-sync-spoke")).toHaveAttribute("data-mode", "twoWay");
  });
});

describe("the drawing's call to sync with another device", () => {
  // As wide as the add card leaves beside its plus, in its size.
  const room = PEER_W - 52;
  const size = 12 * SEMIBOLD;

  it("sets its English label on two lines rather than cutting it short", () => {
    const lines = wrapLabel(en["dataSync.wizard.open"], room, size);

    expect(lines.join(" ")).toBe(en["dataSync.wizard.open"]);
    expect(lines).toHaveLength(2);
    for (const line of lines) expect(textWidth(line, size)).toBeLessThanOrEqual(room);
  });

  it("keeps a label that fits on one line, and shortens a word that fits on none", () => {
    expect(wrapLabel(cn["dataSync.wizard.open"], room, size)).toEqual([cn["dataSync.wizard.open"]]);
    expect(wrapLabel("x".repeat(60), room, size)[0]).toMatch(/…$/);
    expect(wrapLabel("one two three four five six seven eight", 60, size)).toHaveLength(2);
  });
});
