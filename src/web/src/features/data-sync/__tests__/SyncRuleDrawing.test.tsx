import type * as Api from "../api";
import type { SyncPeer } from "../viewModels";

import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import SyncRuleDrawing from "../components/SyncRuleDrawing";
import { dataSyncApi } from "../api";
import { syncPeerFromLink, syncPeerFromMapPeer } from "../viewModels";

import { link, mapPeer, NOW, recordingActions } from "./dataSyncFixtures";

import { DataSyncLinkMode, DataSyncLinkState, RemoteAccessMode } from "@/sdk/constants";

vi.mock("react-i18next", () => ({
  // Keys as text, followed by the interpolated values, so a test can see what was said.
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
vi.mock("../api", async (importOriginal) => ({
  ...(await importOriginal<typeof Api>()),
  dataSyncApi: {
    updateLink: vi.fn(async () => ({})),
    createLink: vi.fn(async () => ({})),
    setSharing: vi.fn(async () => undefined),
    revokeReader: vi.fn(async () => undefined),
    resumeLink: vi.fn(async () => ({})),
    pauseLink: vi.fn(async () => ({})),
    syncNow: vi.fn(async () => ({})),
    resetLink: vi.fn(async () => undefined),
    setAllPaused: vi.fn(async () => undefined),
    copyOnce: vi.fn(async () => ({ linkId: 21, copyOnce: true, linkMode: 0 })),
  },
}));

const nas = (patch: Parameters<typeof link>[3] = {}) =>
  syncPeerFromLink(link(1, "node-nas", "NAS", patch));

let recorded = recordingActions();

const draw = (
  peer: SyncPeer,
  options: {
    canManage?: boolean;
    sharingEnabled?: boolean;
    remoteAccessMode?: RemoteAccessMode;
    initialWidth?: number;
    onCreateCode?: () => void;
  } = {},
) =>
  render(
    <MemoryRouter>
      <SyncRuleDrawing
        actions={recorded.actions}
        canManage={options.canManage ?? true}
        initialWidth={options.initialWidth ?? 520}
        now={NOW}
        peer={peer}
        remoteAccessMode={options.remoteAccessMode ?? RemoteAccessMode.Enabled}
        selfName="This PC"
        sharingEnabled={options.sharingEnabled ?? true}
        onCreateCode={options.onCreateCode}
      />
    </MemoryRouter>,
  );

const receive = () => screen.getByTestId("data-sync-arrow-receive");
const read = () => screen.getByTestId("data-sync-arrow-read");
const lastConfirmation = () => recorded.confirmations[recorded.confirmations.length - 1];

beforeEach(() => {
  vi.clearAllMocks();
  recorded = recordingActions();
});
afterEach(cleanup);

describe("the rule editor's arrows", () => {
  it("are buttons that say whether they are on", () => {
    draw(nas());

    expect(receive().tagName).toBe("BUTTON");
    expect(receive()).toHaveAttribute("aria-pressed", "true");
    expect(read().tagName).toBe("BUTTON");
    expect(read()).toHaveAttribute("aria-pressed", "true");
    expect(receive()).toHaveAccessibleName(
      "federation.map.direction.sync.in.active NAS, dataSync.mode.twoWay",
    );
  });

  it("turns receiving off from the keyboard, asking first and saying the other still reads", () => {
    draw(nas());

    fireEvent.keyDown(receive(), { key: "Enter" });
    expect(recorded.actions.confirm).toHaveBeenCalledTimes(1);
    expect(lastConfirmation()).toMatchObject({
      title: "dataSync.off.title NAS",
      description: "dataSync.off.description NAS",
      warning: "dataSync.off.stillReads NAS",
      refresh: ["dataSync"],
    });
    // Nothing happens until it is confirmed, and then through the host.
    expect(dataSyncApi.updateLink).not.toHaveBeenCalled();
    void lastConfirmation().action();
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.Off });
  });

  it("turns a stopped link back on in its last mode with Space", async () => {
    draw(
      nas({
        mode: DataSyncLinkMode.Off,
        lastMode: DataSyncLinkMode.Follow,
        state: DataSyncLinkState.Stopped,
      }),
    );

    expect(receive()).toHaveAttribute("aria-pressed", "false");
    await act(async () => {
      fireEvent.keyDown(receive(), { key: " " });
    });
    // Resuming a link it already has creates no access: it just runs, through the host.
    expect(recorded.actions.run).toHaveBeenCalledTimes(1);
    expect(recorded.actions.confirm).not.toHaveBeenCalled();
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.Follow });
  });

  it("keeps the may-read arrow focusable when off, and never disabled", () => {
    draw(nas({ peerMayReadUs: false, peerModeTowardsUs: undefined }));

    expect(read()).toHaveAttribute("aria-disabled", "true");
    expect(read()).not.toBeDisabled();
    act(() => read().focus());
    expect(read()).toHaveFocus();
    fireEvent.keyDown(read(), { key: "Enter" });
    fireEvent.click(read());
    expect(recorded.actions.confirm).not.toHaveBeenCalled();
    expect(recorded.actions.run).not.toHaveBeenCalled();
  });

  it("stops the other device reading, asking first", () => {
    draw(nas());

    fireEvent.click(read());
    expect(lastConfirmation()).toMatchObject({ title: "dataSync.arrow.read.stopTitle NAS" });
    void lastConfirmation().action();
    expect(dataSyncApi.revokeReader).toHaveBeenCalledWith("node-nas");
  });

  it("offers ways to let the other device read, where this window may", () => {
    const onCreateCode = vi.fn();

    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
      {
        onCreateCode,
      },
    );
    fireEvent.click(screen.getByText("dataSync.invitation.createFor NAS"));
    expect(onCreateCode).toHaveBeenCalled();
    expect(screen.getAllByText("dataSync.mode.twoWay").length).toBeGreaterThan(0);
    cleanup();

    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
      {
        onCreateCode,
        canManage: false,
      },
    );
    expect(screen.queryByText("dataSync.invitation.createFor NAS")).toBeNull();
  });

  it("shows the kinds the other device receives on the may-read arrow", () => {
    draw(nas({ peerKinds: ["extensionGroup"] }));

    expect(
      within(screen.getByTestId("data-sync-peer-kinds")).getByText("dataSync.kind.extensionGroup"),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId("data-sync-peer-kinds")).queryByText(
        "dataSync.kind.customProperty",
      ),
    ).toBeNull();
  });
});

describe("the kinds a link receives", () => {
  it("switches a kind through the host", async () => {
    draw(nas());
    const chips = screen.getAllByTestId("data-sync-kind-chip");

    await act(async () => {
      fireEvent.click(chips.find((chip) => chip.dataset.kind === "customProperty")!);
    });
    expect(recorded.actions.run).toHaveBeenCalledTimes(1);
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { kinds: ["extensionGroup"] });
  });

  it("never switches the last one off", () => {
    draw(nas({ kinds: ["extensionGroup"] }));
    const last = screen
      .getAllByTestId("data-sync-kind-chip")
      .find((chip) => chip.dataset.kind === "extensionGroup")!;

    expect(last).toHaveAttribute("aria-disabled", "true");
    expect(last).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(last);
    expect(recorded.actions.run).not.toHaveBeenCalled();
  });
});

describe("the mode buttons", () => {
  it("say the same as the arrow and its badge", () => {
    draw(nas({ mode: DataSyncLinkMode.Follow, lastMode: DataSyncLinkMode.Follow }));

    expect(screen.getByTestId("data-sync-mode-follow")).toBeChecked();
    expect(screen.getByTestId("data-sync-mode-badge")).toHaveAttribute("data-mode", "follow");
    expect(receive()).toHaveAttribute("aria-pressed", "true");
  });

  it("switch through the badge's menu exactly as through the buttons", async () => {
    draw(nas({ mode: DataSyncLinkMode.Follow, lastMode: DataSyncLinkMode.Follow }));

    fireEvent.click(screen.getByTestId("data-sync-mode-badge"));
    const menu = screen.getByRole("menu");

    expect(within(menu).getByRole("menuitemradio", { checked: true })).toHaveTextContent(
      "dataSync.mode.follow",
    );
    // Both ways, towards a device that already reads this one: nothing is widened.
    await act(async () => {
      fireEvent.click(within(menu).getByRole("menuitemradio", { name: "dataSync.mode.twoWay" }));
    });
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.TwoWay });
  });

  it("asks for the two-way consent and turns sharing and remote access on when either is off", async () => {
    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
      { sharingEnabled: false, remoteAccessMode: RemoteAccessMode.Disabled },
    );

    fireEvent.click(screen.getByTestId("data-sync-mode-twoWay"));
    expect(lastConfirmation()).toMatchObject({
      description: "dataSync.twoWay.consent NAS",
      warning: "dataSync.twoWay.turnsOnSharing dataSync.sharing.remoteAccess",
    });
    await lastConfirmation().action();
    expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
      enabled: true,
      enablePairedRemoteAccess: true,
    });
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.TwoWay });
  });

  it("says nothing about turning things on when both are on already", () => {
    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
    );

    fireEvent.click(screen.getByTestId("data-sync-mode-twoWay"));
    expect(lastConfirmation().description).toBe("dataSync.twoWay.consent NAS");
    expect(lastConfirmation().warning).toBeUndefined();
  });

  it("previews the request the other device will see before a new link sends it", async () => {
    const peer = syncPeerFromMapPeer(
      mapPeer("node-new", "New PC", {
        linkId: undefined,
        mode: DataSyncLinkMode.Off,
        lastMode: DataSyncLinkMode.Off,
        state: undefined,
        receiving: false,
        peerMayRead: false,
      }),
    );

    draw(peer);
    fireEvent.click(screen.getByTestId("data-sync-mode-follow"));
    expect(lastConfirmation()).toMatchObject({
      title: "dataSync.follow.title New PC",
      warning: "dataSync.request.follow This PC",
    });
    await lastConfirmation().action();
    expect(dataSyncApi.createLink).toHaveBeenCalledWith({
      peerNodeId: "node-new",
      mode: DataSyncLinkMode.Follow,
      kinds: ["customProperty", "extensionGroup"],
    });
  });

  it("does not offer what would create access where this window may not", () => {
    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
      {
        canManage: false,
      },
    );

    expect(screen.getByTestId("data-sync-mode-twoWay")).toBeDisabled();
    expect(screen.getByTestId("data-sync-mode-off")).not.toBeDisabled();
    expect(screen.getByTestId("data-sync-manage-elsewhere")).toBeInTheDocument();
  });

  it("copies once through a confirmation, then opens the review", async () => {
    draw(
      nas({
        mode: DataSyncLinkMode.Off,
        lastMode: DataSyncLinkMode.Off,
        state: DataSyncLinkState.Stopped,
      }),
    );

    fireEvent.click(screen.getByTestId("data-sync-copy-once"));
    await lastConfirmation().action();
    expect(dataSyncApi.copyOnce).toHaveBeenCalledWith({
      peerNodeId: "node-nas",
      kinds: ["customProperty", "extensionGroup"],
    });
  });
});

describe("the status line", () => {
  it("offers what a pause asks for", () => {
    draw(nas({ state: DataSyncLinkState.Paused, pausedReason: 5, pausedDetail: "deletions=40" }));

    expect(screen.getByText("dataSync.status.paused.MassDeletion NAS 40")).toBeInTheDocument();
    expect(screen.getByText("dataSync.pause.applyAsUsual")).toBeInTheDocument();
    expect(screen.getByText("dataSync.pause.reviewDeletions")).toBeInTheDocument();
  });

  it("asks the other device to keep in step when it declined to read back", async () => {
    draw(nas({ readBackDeclined: true, peerMayReadUs: false, peerModeTowardsUs: undefined }));

    expect(screen.getByText("dataSync.status.ReadBackDeclined NAS")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByText("dataSync.link.askKeepInStep NAS"));
    });
    expect(dataSyncApi.resumeLink).toHaveBeenCalledWith(1, 4);
  });

  it("says mutual Follow works as both ways", () => {
    draw(nas({ mode: DataSyncLinkMode.Follow, peerModeTowardsUs: "follow" }));

    expect(screen.getByText("dataSync.status.MutualFollow")).toBeInTheDocument();
  });

  it("says what waits for a decision on the other device", () => {
    draw(
      nas({
        peerAttention: {
          headless: true,
          openDecisions: 2,
          pausedLinks: 0,
          restorePending: false,
          awaitingReview: 0,
        },
      }),
    );

    expect(screen.getByText("dataSync.status.NeedsYouThere NAS 2")).toBeInTheDocument();
    expect(screen.getByText("dataSync.link.decideThere NAS")).toBeInTheDocument();
  });

  it("offers the review while it waits", () => {
    draw(nas({ state: DataSyncLinkState.AwaitingReview }));

    expect(screen.getByText("dataSync.link.review").closest("a")).toHaveAttribute(
      "href",
      "/data-sync?link=1&review=1",
    );
  });
});

describe("the drawing's layout", () => {
  it("stands horizontally at 420 px and wider", () => {
    draw(nas(), { initialWidth: 420 });

    expect(screen.getByTestId("data-sync-rule-drawing")).toHaveAttribute(
      "data-orientation",
      "horizontal",
    );
  });

  it("stands vertically below 420 px, as its container is measured", () => {
    const measure = vi
      .spyOn(HTMLElement.prototype, "getBoundingClientRect")
      .mockImplementation(function (this: HTMLElement) {
        const width = this.dataset.testid === "data-sync-rule-drawing" ? 380 : 0;

        return {
          width,
          height: 300,
          top: 0,
          left: 0,
          right: width,
          bottom: 300,
          x: 0,
          y: 0,
        } as DOMRect;
      });

    try {
      draw(nas(), { initialWidth: 1000 });
      expect(screen.getByTestId("data-sync-rule-drawing")).toHaveAttribute(
        "data-orientation",
        "vertical",
      );
      // Vertical: the arrows point up and down.
      expect(receive().querySelector("[data-arrow]")).toHaveAttribute("data-arrow", "down");
      expect(read().querySelector("[data-arrow]")).toHaveAttribute("data-arrow", "up");
    } finally {
      measure.mockRestore();
    }
  });

  it("says both directions, as a list, to a screen reader", () => {
    draw(nas({ peerKinds: ["customProperty"] }));
    const items = within(screen.getByTestId("data-sync-rule-list")).getAllByRole("listitem");

    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent(
      "federation.map.direction.sync.in.active NAS, dataSync.mode.twoWay",
    );
    expect(items[1]).toHaveTextContent(
      "federation.map.direction.sync.out.active NAS: dataSync.kind.customProperty",
    );
  });
});
