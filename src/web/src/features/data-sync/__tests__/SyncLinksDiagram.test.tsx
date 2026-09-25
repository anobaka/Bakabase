import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import SyncLinksDiagram from "../components/SyncLinksDiagram";
import {
  GAP,
  layoutSyncDiagram,
  LIST_BELOW,
  noOverlap,
  spokeGeometry,
} from "../components/diagramLayout";
import { syncPeerFromLink, syncPeersOf } from "../viewModels";

import { link, mapView, NOW, outgoing, reader } from "./dataSyncFixtures";

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
